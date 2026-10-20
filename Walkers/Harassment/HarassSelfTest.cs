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
/// way in; torches and doors are put back by the restore list.</item>
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
        check("torches: the restore list relights it", accessor.GetBlock(torchPos) is BlockTorch lit && DouseTorchAct.IsLit(lit), accessor.GetBlock(torchPos).Code?.ToShortString() ?? "");

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

        // Stage 5: the break-in, only when enraged; a block behind the door keeps it out.
        ctx = Ctx(5)!;
        var calm = Act(BreakInAct.ActCode).Run(ctx);
        check("break-in: not without rage", !calm.Done, calm.Detail);
        bool wasProvoked = brain.Ai.GetBool(StalkerMemory.Provoked);
        brain.Ai.SetBool(StalkerMemory.Provoked, true);
        var stone = world.GetBlock(new AssetLocation("game", "rock-granite"));
        var behind = doorPos.AddCopy(-1, 0, 0);
        accessor.SetBlock(stone!.Id, behind);
        placed.Add(behind);
        var barred = system.RunNow(ctx, Act(BreakInAct.ActCode));
        check("break-in: a block behind the door keeps it out", barred.Done && barred.Detail.StartsWith("kept out", StringComparison.Ordinal) && Doors.At(world, doorPos)?.Opened == false, barred.Detail);
        accessor.SetBlock(0, behind);
        HarassMemory.NewNight(ctx.Harass, world.Calendar.TotalHours);
        var breakIn = system.RunNow(ctx, Act(BreakInAct.ActCode));
        check("break-in: enraged, the unlocked door bursts open", breakIn.Done && breakIn.Detail.Contains("forced open") && Doors.At(world, doorPos)?.Opened == true, breakIn.Detail);
        brain.Ai.SetBool(StalkerMemory.Provoked, wasProvoked);
        system.Restore.UndoAllLocal(world);
        check("break-in: the restore list closes the door", Doors.At(world, doorPos)?.Opened == false, "");
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
