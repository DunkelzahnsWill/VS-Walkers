using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Walkers.Config;
using Walkers.Director;
using Walkers.Harassment.Acts;
using Walkers.Stalker;
using Walkers.Stalker.Tasks;

namespace Walkers.Harassment;

/// <summary>
/// <c>/walkers harass selftest</c>: checks the harassment kit on a headless server, where nobody can
/// see or hear anything (creature bible, section 9: testing without a client). It builds a small test
/// house on a stone platform high above the world spawn (a door, a trapdoor, a lit torch and a chest
/// around a playerbot stand-in), places a true form 20 blocks off with the stand-in as its victim,
/// and runs each act directly under its own rules. Every check prints "pass" or "FAIL" with its name:
/// <list type="bullet">
/// <item>Rules on numbers: the drain's base rule, the whisper pools, the page stacks the engine needs.</item>
/// <item>Each act fires under its trigger: echoes (footsteps), wrong calls, whispers, a knock (with its
/// page), torches, doors, chest lids, and the enraged break-in.</item>
/// <item>Each respects its switch, its nightly cap, the victim's sight, the claim rule and a barred
/// way in; doors are put back by the restore list, torches stay out.</item>
/// </list>
/// Everything it builds and spawns is removed again, and every setting it touches is put back.
/// </summary>
public sealed class HarassSelfTest
{
    private const string StandInCode = "game:playerbot";
    private const string TrueFormCode = "walkers:skinwalker-true";

    private readonly ICoreServerAPI api;
    private readonly HarassModSystem system;
    private readonly List<Entity> spawned = new();
    private readonly List<BlockPos> placed = new();
    private LandClaim? claim;

    public HarassSelfTest(ICoreServerAPI api, HarassModSystem system)
    {
        this.api = api;
        this.system = system;
    }

    public string Run()
    {
        var lines = new List<string> { "Harassment self-test:" };
        void Check(string name, bool ok, string detail = "")
        {
            lines.Add($"  {name}: {(ok ? "pass" : "FAIL")}{(detail.Length > 0 && !ok ? " (" + detail + ")" : "")}");
        }

        try
        {
            RuleChecks(Check);
            LiveChecks(Check);
        }
        catch (Exception e)
        {
            Check("no exception", false, e.Message);
            system.Mod.Logger.Error("Harassment self-test failed with an exception: {0}", e);
        }
        finally
        {
            Cleanup();
        }
        int failed = lines.Count(l => l.EndsWith("FAIL", StringComparison.Ordinal) || l.Contains(": FAIL ("));
        lines.Add(failed == 0 ? "Harassment self-test: all checks passed" : $"Harassment self-test: {failed} check(s) failed");
        return string.Join("\n", lines);
    }

    private void RuleChecks(Action<string, bool, string> check)
    {
        // The drain's base rule (creature bible, section 6).
        double step = 0.05 / 60;
        double regen = StabilityDrain.NextValue(0.61, 0.60, tracking: true, target: 0.5, step);
        double gain = StabilityDrain.NextValue(0.70, 0.60, tracking: true, target: 0.5, step);
        double atTarget = StabilityDrain.NextValue(0.50, 0.50, tracking: true, target: 0.5, step);
        double lost = StabilityDrain.NextValue(0.80, 0.60, tracking: false, target: 0.5, step);
        check("drain: regeneration between writes is undone", Math.Abs(regen - (0.60 - step)) < 1e-9, $"{regen}");
        check("drain: a gain from elsewhere is kept", Math.Abs(gain - (0.70 - step)) < 1e-9, $"{gain}");
        check("drain: never below its target, never raises", atTarget >= 0.5 && atTarget <= 0.5 && lost < 0.80, $"{atTarget} {lost}");

        // The whisper pools (lore bible): W5 needs the name, W6 is never drawn.
        var stage4 = WhisperAct.Pool(4, allowName: true);
        var noName = WhisperAct.Pool(4, allowName: false);
        var stage3 = WhisperAct.Pool(3, allowName: true);
        check("whispers: W5 only with the name, W6 never, W2 not in stage 3",
            stage4.Contains(5) && !noName.Contains(5) && !stage4.Contains(6) && !stage3.Contains(2) && stage3.Contains(3), string.Join(",", stage4));

        // Page stacks: the attributes the journal needs, and refusal of a chapter that does not exist.
        var maud = system.Pages.MakeStack(new PagesAct.Page { Journal = PagesAct.Maud, Chapter = 5, NameKey = "walkers:pagename-letter" });
        var wat = system.Pages.MakeStack(new PagesAct.Page { Journal = PagesAct.Wat, Chapter = 2, NameKey = "walkers:pagename-scrap" });
        var bad = system.Pages.MakeStack(new PagesAct.Page { Journal = PagesAct.PennLate, Chapter = 2 }, quiet: true);
        var fallback = system.Pages.MakeStack(new PagesAct.Page { Journal = PagesAct.Fallback });
        bool maudOk = maud != null && maud.Collectible.Code.Path == "envelope-normal-blue" && maud.Attributes.GetString("category") == "walkers-step" &&
                      maud.Attributes.GetString("discoveryCode") == PagesAct.Maud && maud.Attributes["chapterIds"] is IntArrayAttribute ids && ids.value.SequenceEqual(new[] { 5 }) &&
                      maud.Attributes.GetString("title") == "A letter, its seal unbroken";
        check("pages: a Maud letter carries what the journal needs", maudOk, maud == null ? "no stack" : maud.Attributes.ToJsonToken());
        check("pages: Wat on lore paper, a chapter out of range refused", wat?.Collectible.Code.Path == "lore-paper" && bad == null, "");
        check("pages: the fallback letter is a signed parchment", fallback?.Attributes.GetString("signedby") == "" && fallback.Attributes["textCodes"] is StringArrayAttribute, "");
    }

    private void LiveChecks(Action<string, bool, string> check)
    {
        var world = api.World;
        var accessor = world.BlockAccessor;
        var spawn = world.DefaultSpawnPosition.AsBlockPos;
        if (accessor.GetChunkAtBlockPos(spawn) == null)
        {
            check("world spawn is loaded", false, "chunk not loaded");
            return;
        }
        int ground = accessor.GetTerrainMapheightAt(spawn);
        int y = Math.Min(accessor.MapSizeY - 20, ground + 30);
        var origin = new BlockPos(spawn.X, y, spawn.Z, 0);
        if (!BuildHouse(origin, out string missing))
        {
            check("test house built", false, missing);
            return;
        }
        check("test house built", true, "");

        var standInType = world.GetEntityType(new AssetLocation(StandInCode));
        var trueType = world.GetEntityType(new AssetLocation(TrueFormCode));
        if (standInType == null || trueType == null)
        {
            check("entity types loaded", false, $"{StandInCode} {standInType != null}, {TrueFormCode} {trueType != null}");
            return;
        }
        // The stand-in looks along +Z, at the chest; the door (+X), the torch (-X) and the trapdoor (-Z) are out of its sight.
        var standIn = Spawn(standInType, new Vec3d(origin.X + 0.5, y, origin.Z + 0.5), 0);
        var body = Spawn(trueType, new Vec3d(origin.X + 0.5, y, origin.Z + 20.5), GameMath.PI);
        var brain = StalkerModSystem.BrainOf(body);
        if (brain == null)
        {
            check("the walker has a brain", false, "");
            return;
        }
        system.Stalker.SetStandIn(brain, standIn, 3);
        brain.RunGazeCheck();
        HarassContext? Ctx(int stage)
        {
            system.Stalker.SetStandIn(brain, standIn, stage);
            brain.RunGazeCheck();
            return system.ContextOf(brain, night: true);
        }
        var ctx = Ctx(3);
        check("a context for the walker and its stand-in", ctx != null && ctx.Victim.EntityId == standIn.EntityId, "");
        if (ctx == null)
        {
            return;
        }
        var config = system.Config.Harass;
        HarassAct Act(string code) => system.Acts.First(a => a.Code == code);

        // Stage 3: echoes, calls, whispers, a knock, torches.
        var echo = system.RunNow(ctx, Act(EchoAct.ActCode));
        check("echoes: footsteps behind a stand-in", echo.Done, echo.Detail);
        var call = system.RunNow(ctx, Act(WrongCallAct.ActCode));
        check("wrong calls: a call from where it is not", call.Done, call.Detail);
        var whisper = system.RunNow(ctx, Act(WhisperAct.ActCode));
        check("whispers: a line (logged for a stand-in)", whisper.Done, whisper.Detail);

        bool wasOn = config.Whispers.Enabled;
        config.Whispers.Enabled = false;
        var off = system.RunNow(ctx, Act(WhisperAct.ActCode));
        config.Whispers.Enabled = wasOn;
        check("whispers: switched off, nothing happens", !off.Done && off.Detail == "switched off", off.Detail);

        for (int i = HarassMemory.Count(ctx.Harass, WhisperAct.ActCode); i < config.Whispers.PerNight; i++)
        {
            HarassMemory.AddCount(ctx.Harass, WhisperAct.ActCode);
        }
        var capped = system.RunNow(ctx, Act(WhisperAct.ActCode));
        check("whispers: tonight's cap holds", !capped.Done && capped.Detail.Contains("cap"), capped.Detail);

        var knock = system.RunNow(ctx, Act(KnockAct.ActCode));
        check("knocks: one rap on the door", knock.Done && knock.Detail.StartsWith("1 rap", StringComparison.Ordinal), knock.Detail);
        int pages = world.GetEntitiesAround(standIn.Pos.XYZ, 20, 10, e => e is EntityItem item && item.Itemstack?.Attributes.GetString("category") == "walkers-step").Length;
        check("knocks: the K1 page lies by the door", pages >= 1, $"{pages} page(s)");
        var second = system.RunNow(ctx, Act(KnockAct.ActCode));
        check("knocks: one knock a night", !second.Done, second.Detail);

        var torchPos = origin.AddCopy(-3, 0, 0);
        var torch = system.RunNow(ctx, Act(DouseTorchAct.ActCode));
        bool extinct = accessor.GetBlock(torchPos) is BlockTorch t && t.IsExtinct;
        check("torches: the torch out of sight goes out", torch.Done && extinct, torch.Detail);
        system.Restore.UndoAllLocal(world);
        check("torches: it stays out until a player relights it", accessor.GetBlock(torchPos) is BlockTorch still && still.IsExtinct, accessor.GetBlock(torchPos).Code?.ToShortString() ?? "");
        // Relit by hand, so the claim check below has a lit torch to refuse.
        accessor.ExchangeBlock(world.GetBlock(new AssetLocation("game", "torch-basic-lit-up"))!.Id, torchPos);

        // Stage 4 at night: doors, the claim rule, chest lids and the victim's sight.
        ctx = Ctx(4)!;
        var doorPos = origin.AddCopy(3, 0, 0);
        claim = new LandClaim { OwnedByPlayerUid = "walkers-selftest", LastKnownOwnerName = "selftest", ProtectionLevel = 1 };
        claim.Areas.Add(new Cuboidi(origin.X - 6, y - 1, origin.Z - 6, origin.X + 6, y + 4, origin.Z + 6));
        world.Claims.Add(claim);
        var claimed = system.RunNow(ctx, Act(DoorAct.ActCode));
        var claimedTorch = system.RunNow(ctx, Act(DouseTorchAct.ActCode));
        world.Claims.Remove(claim);
        claim = null;
        check("claims: nothing on someone else's claim", !claimed.Done && !claimedTorch.Done, claimed.Detail);

        var door = system.RunNow(ctx, Act(DoorAct.ActCode));
        bool anyOpen = Doors.At(world, doorPos)?.Opened == true || Doors.At(world, origin.AddCopy(0, 0, -3))?.Opened == true;
        check("doors: a door or trapdoor out of sight swings open", door.Done && anyOpen, door.Detail);
        system.Restore.UndoAllLocal(world);
        bool closed = Doors.At(world, doorPos)?.Opened == false && Doors.At(world, origin.AddCopy(0, 0, -3))?.Opened == false;
        check("doors: the restore list closes it", closed, "");

        var seen = system.RunNow(ctx, Act(ChestLidAct.ActCode));
        check("chest lids: not while the victim looks at it", !seen.Done, seen.Detail);
        standIn.Pos.Yaw = GameMath.PI;
        ctx = Ctx(4)!;
        var lid = system.RunNow(ctx, Act(ChestLidAct.ActCode));
        check("chest lids: out of sight, the lid pops", lid.Done, lid.Detail);
        standIn.Pos.Yaw = 0;

        var sleep = system.RunNow(ctx, Act(SleepDisturbAct.ActCode));
        check("broken sleep: nothing while the victim is awake", !sleep.Done, sleep.Detail);

        // Stage 5: the break-in, only when enraged (creature bible, section 7, "Enraged").
        ctx = Ctx(5)!;
        var memory = ctx.Memory;
        int returnsBefore = memory.GetInt(WalkerMemory.Returns);
        bool wasProvoked = brain.Ai.GetBool(StalkerMemory.Provoked);
        var breakInAct = Act(BreakInAct.ActCode);
        try
        {
            memory.SetInt(WalkerMemory.Returns, 0);
            brain.Ai.SetBool(StalkerMemory.Provoked, true);
            var calm = breakInAct.Run(ctx);
            check("enraged: a hit alone does not enrage it (onProvoked off)", !calm.Done && calm.Detail == "not enraged", calm.Detail);
            brain.Ai.SetBool(StalkerMemory.Provoked, wasProvoked);
            memory.SetInt(WalkerMemory.Returns, 1);
            check("enraged: a walker that came back is enraged", Enraged.Is(ctx), "");
            string hostility = world.Config.GetString("creatureHostility", "aggressive");
            world.Config.SetString("creatureHostility", "off");
            bool offCalm = !Enraged.Is(ctx);
            world.Config.SetString("creatureHostility", hostility);
            check("enraged: never while creature hostility is off", offCalm, "");

            var stone = world.GetBlock(new AssetLocation("game", "rock-granite"));
            var behind = doorPos.AddCopy(-1, 0, 0);
            accessor.SetBlock(stone!.Id, behind);
            placed.Add(behind);
            var barred = system.RunNow(ctx, breakInAct);
            check("break-in: a block behind the door keeps it out", barred.Done && barred.Detail.StartsWith("kept out", StringComparison.Ordinal) && Doors.At(world, doorPos)?.Opened == false, barred.Detail);
            accessor.SetBlock(0, behind);

            HarassMemory.NewNight(ctx.Harass, world.Calendar.TotalHours);
            ctx.State.LastWakeMs = world.ElapsedMilliseconds;
            var woke = system.RunNow(ctx, breakInAct);
            ctx.State.LastWakeMs = -100000;
            check("break-in: not within 10 seconds of a wake", !woke.Done && woke.Detail.Contains("woke"), woke.Detail);

            var breakIn = system.RunNow(ctx, breakInAct);
            var harass = ctx.Harass;
            bool entry = harass.GetLong(HarassMemory.EntryUntilMs) > world.ElapsedMilliseconds && harass.GetInt(HarassMemory.EntryX) == doorPos.X && !harass.GetBool(HarassMemory.EntryTorn);
            check("break-in: enraged, the unlocked door bursts open", breakIn.Done && breakIn.Detail.Contains("forced open") && Doors.At(world, doorPos)?.Opened == true, breakIn.Detail);
            check("break-in: the way in opens for the stalker AI", entry, $"until {harass.GetLong(HarassMemory.EntryUntilMs)}");
            var again = system.RunNow(ctx, breakInAct);
            check("break-in: one entry a night", !again.Done, again.Detail);
            system.Restore.UndoAllLocal(world);
            check("break-in: the restore list closes the door", Doors.At(world, doorPos)?.Opened == false, "");

            HarassMemory.NewNight(ctx.Harass, world.Calendar.TotalHours);
            var tell = system.Knocks.RunDusk(ctx);
            check("knocks: an enraged dusk ends in a long scrape", tell.Done && tell.Detail.Contains("long scrape"), tell.Detail);

            // The torn door: only from the second return, once a haunt; hung back by the restore list.
            var doorBh = world.BlockAccessor.GetBlockEntity(doorPos)?.GetBehavior<BEBehaviorDoor>();
            check("torn door: not on the first return", doorBh != null && !BreakInAct.MayTear(ctx, doorBh), "");
            memory.SetInt(WalkerMemory.Returns, 2);
            bool mayTear = doorBh != null && BreakInAct.MayTear(ctx, doorBh);
            bool torn = mayTear && TornDoor.TearNow(ctx, doorPos);
            var doorItem = world.GetEntitiesAround(doorPos.ToVec3d().Add(0.5, 0.5, 0.5), 3, 3, e => e is EntityItem i && i.Itemstack?.Block?.Code?.Path == "door-solid-oak").FirstOrDefault();
            check("torn door: from the second return it comes off whole, as an item", torn && accessor.GetBlock(doorPos).Id == 0 && doorItem != null, $"may {mayTear}, torn {torn}");
            placed.Add(doorPos.UpCopy());
            system.Restore.UndoAllLocal(world);
            bool rehung = world.BlockAccessor.GetBlockEntity(doorPos)?.GetBehavior<BEBehaviorDoor>() is { Opened: false } && doorItem is { Alive: false };
            check("torn door: hung back at dawn while its item lies there", rehung, accessor.GetBlock(doorPos).Code?.ToShortString() ?? "air");
            memory.SetInt(WalkerMemory.Returns, 1);

            EscalationChecks(check, ctx, standIn, origin, ground);
        }
        finally
        {
            memory.SetInt(WalkerMemory.Returns, returnsBefore);
            brain.Ai.SetBool(StalkerMemory.Provoked, wasProvoked);
        }
    }

    /// <summary>The rotting herd and the eaten crops, on the first return.</summary>
    private void EscalationChecks(Action<string, bool, string> check, HarassContext ctx, Entity standIn, BlockPos origin, int ground)
    {
        var world = api.World;
        var accessor = world.BlockAccessor;
        var escalation = system.Escalation;
        var harass = ctx.Harass;

        // The herd lies 16 to 24 blocks from the door, never while a player is within 32.
        harass.SetDouble("herdHomeX", origin.X + 0.5);
        harass.SetDouble("herdHomeY", ground + 1);
        harass.SetDouble("herdHomeZ", origin.Z + 0.5);
        var victimAt = standIn.Pos.XYZ;
        standIn.Pos.SetPos(origin.X + 0.5, ground + 1, origin.Z + 0.5);
        var watched = escalation.LayHerd(ctx);
        check("rotting herd: not while someone is within 32 blocks", !watched.Done, watched.Detail);
        standIn.Pos.SetPos(victimAt.X + 80, victimAt.Y, victimAt.Z);
        // A home far enough from anyone else counting (the director's smoke stand-ins stand at the spawn).
        foreach (var (hx, hz) in new[] { (64, 0), (-64, 0), (0, 64), (0, -64), (48, 48), (-48, -48) })
        {
            var home = new BlockPos(origin.X + hx, ground, origin.Z + hz, 0);
            if (accessor.GetChunkAtBlockPos(home) != null && !system.Stalker.CountingNear(home.ToVec3d(), 0, 56).Any(e => e.EntityId != standIn.EntityId))
            {
                harass.SetDouble("herdHomeX", home.X + 0.5);
                harass.SetDouble("herdHomeY", accessor.GetTerrainMapheightAt(home) + 1);
                harass.SetDouble("herdHomeZ", home.Z + 0.5);
                break;
            }
        }
        var herd = escalation.LayHerd(ctx);
        standIn.Pos.SetPos(victimAt);
        var ids = (harass["herdIds"] as LongArrayAttribute)?.value ?? Array.Empty<long>();
        check("rotting herd: two does where nobody is", herd.Done && ids.Length == system.Config.Escalation.Herd.WildCarcasses, herd.Detail);
        escalation.RemoveHerdNow(ctx);
        check("rotting herd: removed when its time is up", ids.All(id => world.GetEntityById(id) is not { Alive: true }) && !harass.HasAttribute("herdIds"), "");

        // Eaten crops: a line of ripe crops set back two stages; on a dedicated server never on unclaimed land by default.
        var carrot = world.GetBlock(new AssetLocation("game", "crop-carrot-7"));
        if (carrot == null)
        {
            check("eaten crops: the carrot crop exists", false, "");
            return;
        }
        var crops = Enumerable.Range(-2, 4).Select(dx => origin.AddCopy(dx, 0, -5)).ToList();
        foreach (var pos in crops)
        {
            Place(carrot, pos);
        }
        var cropConfig = system.Config.Escalation.Crops;
        var rule = cropConfig.ClaimRule;
        float share = cropConfig.MaxShare;
        try
        {
            var unclaimed = escalation.EatCrops(ctx);
            check("eaten crops: not on unclaimed land by default (dedicated server)", !unclaimed.Done || !api.Server.IsDedicated, unclaimed.Detail);
            cropConfig.ClaimRule = CropClaimRule.VictimAccess;
            cropConfig.MaxShare = 1;
            var eaten = escalation.EatCrops(ctx);
            int younger = crops.Count(p => accessor.GetBlock(p) is BlockCrop c && c.CurrentStage() == 5);
            check("eaten crops: a line of four set back two stages", eaten.Done && younger == 4, $"{eaten.Detail}; {younger} at stage 5");
            var more = escalation.EatCrops(ctx);
            check("eaten crops: no ripe crop left, nothing more", !more.Done, more.Detail);
        }
        finally
        {
            cropConfig.ClaimRule = rule;
            cropConfig.MaxShare = share;
        }
    }

    /// <summary>A 13 by 13 stone platform with a door (+X), a trapdoor (-Z), a lit torch (-X) and a chest (+Z) around the centre.</summary>
    private bool BuildHouse(BlockPos origin, out string missing)
    {
        var world = api.World;
        var accessor = world.BlockAccessor;
        Block? Get(string code) => world.GetBlock(new AssetLocation("game", code));
        var stone = Get("rock-granite");
        var door = Get("door-solid-oak");
        var trapdoor = Get("trapdoor-solid-oak-1");
        var torch = Get("torch-basic-lit-up");
        var chest = Get("chest-east");
        missing = string.Join(", ", new[] { ("rock-granite", stone), ("door-solid-oak", door), ("trapdoor-solid-oak-1", trapdoor), ("torch-basic-lit-up", torch), ("chest-east", chest) }
            .Where(b => b.Item2 == null).Select(b => b.Item1));
        if (missing.Length > 0)
        {
            return false;
        }
        for (int dx = -6; dx <= 6; dx++)
        {
            for (int dz = -6; dz <= 6; dz++)
            {
                Place(stone!, origin.AddCopy(dx, -1, dz));
            }
        }
        Place(door!, origin.AddCopy(3, 0, 0));
        Place(trapdoor!, origin.AddCopy(0, 0, -3));
        Place(torch!, origin.AddCopy(-3, 0, 0));
        Place(chest!, origin.AddCopy(0, 0, 3));
        return true;
    }

    private void Place(Block block, BlockPos pos)
    {
        api.World.BlockAccessor.SetBlock(block.Id, pos);
        placed.Add(pos);
    }

    private Entity Spawn(EntityProperties type, Vec3d pos, float yaw)
    {
        var entity = api.World.ClassRegistry.CreateEntity(type);
        entity.Pos.SetPos(pos);
        entity.Pos.Yaw = yaw;
        entity.Pos.Pitch = 0;
        api.World.SpawnEntity(entity);
        spawned.Add(entity);
        return entity;
    }

    private void Cleanup()
    {
        var world = api.World;
        system.Restore.UndoAllLocal(world);
        if (claim != null)
        {
            world.Claims.Remove(claim);
            claim = null;
        }
        Vec3d? near = spawned.FirstOrDefault()?.Pos.XYZ;
        foreach (var entity in spawned)
        {
            if (entity.Alive)
            {
                entity.Die(EnumDespawnReason.Removed);
            }
        }
        spawned.Clear();
        if (near != null)
        {
            foreach (var item in world.GetEntitiesAround(near, 24, 24, e => e is EntityItem))
            {
                item.Die(EnumDespawnReason.Removed);
            }
        }
        foreach (var pos in placed)
        {
            world.BlockAccessor.SetBlock(0, pos);
        }
        placed.Clear();
    }
}
