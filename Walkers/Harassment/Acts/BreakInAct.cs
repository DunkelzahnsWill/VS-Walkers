using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Walkers.Config;
using Walkers.Director;
using Walkers.Stalker;

namespace Walkers.Harassment.Acts;

/// <summary>
/// Breaking and entering when enraged (the owner's answer 9 and ladder of 2026-10-20; creature bible,
/// section 7, "Enraged"): on hunt nights (stage 5), with the escalation level's breakInChance rolled
/// once a night, at most enraged.entriesPerNight a night and, per haunt, the level's door budget
/// (woodenDoorsPerHaunt plus otherDoorsPerHaunt). Only while the victim is awake, out of bed and more
/// than 10 seconds past a wake, and below Wrath (ignoresCompany) only while they are alone. The closed
/// door nearest the victim (within 16 blocks) is torn off its hinges (<see cref="TornDoor"/>) and
/// spends the budget of its material. Inside, within harass.breakIn.reach of the door, up to two lids
/// pop and up to two torches go out (eaten from Hate). Then the kit opens the way in (walkers/harass
/// entryX/Y/Z, entryUntilMs, entryTorn) and the stalker AI's hunt comes through it for at most
/// enraged.maxIndoorSeconds and enraged.maxIndoorBlocks. Nothing else is broken, taken or placed.
///
/// What keeps it out at every level: a padlock, a door bar, plumb-and-square reinforcement on either
/// cell of the door, anything with a collision box in the two cells behind it, a gate or double door,
/// a story lock, and a claim the victim neither owns nor shares; a door whose budget is spent also
/// holds. Kept out, it scrapes at the boards and the door creaks. It never comes near animals that
/// could get out or another hostile creature.
/// </summary>
public sealed class BreakInAct : HarassAct
{
    public const string ActCode = "breakIn";

    /// <summary>No entry this soon after the victim woke (real milliseconds).</summary>
    public const long WakeGraceMs = 10000;

    private const double CompanyRange = 24;

    public override string Code => ActCode;

    public override int MinStage => 5;

    public override bool Enabled(HarassConfig config) => config.BreakIn.Enabled;

    // The real cap is enraged.entriesPerNight, which Run checks: the cap here only sees the harass section.
    public override int Cap(HarassConfig config) => 3;

    public override double Weight(HarassContext ctx) => Enraged.Is(ctx) ? 2 : 0;

    public override ActResult Run(HarassContext ctx)
    {
        var enraged = ctx.System.Config.Enraged;
        if (!Enraged.Is(ctx))
        {
            return ActResult.Skip("not enraged");
        }
        var rules = Enraged.BreakInRules(ctx);
        if (HarassMemory.Count(ctx.Harass, Code) >= enraged.EntriesPerNight)
        {
            return ActResult.Skip($"tonight's entries ({enraged.EntriesPerNight}) are spent");
        }
        if (ctx.Harass.GetInt(HarassMemory.BreakIns) >= rules.WoodenDoorsPerHaunt + rules.OtherDoorsPerHaunt)
        {
            return ActResult.Skip("this haunt's doors are spent");
        }
        if (!Tonight(ctx, rules))
        {
            return ActResult.Skip($"not tonight (a {rules.BreakInChance:0.##} chance at escalation level {Enraged.Level(ctx)})");
        }
        if (StalkerModSystem.IsAsleep(ctx.Victim))
        {
            return ActResult.Skip("the victim is in bed: it opens nothing");
        }
        long now = ctx.Api.World.ElapsedMilliseconds;
        if (now - ctx.State.LastWakeMs < WakeGraceMs)
        {
            return ActResult.Skip("the victim woke moments ago");
        }
        if (!rules.IgnoresCompany && ctx.System.Stalker.CountingNear(ctx.Victim.Pos.XYZ, ctx.Victim.Pos.Dimension, CompanyRange).Any(e => e.EntityId != ctx.Victim.EntityId))
        {
            return ActResult.Skip("the victim has company");
        }
        var world = ctx.Api.World;
        var door = BlockRules.FindNear(world, ctx.Victim, BlockRules.VictimReach, be => be.GetBehavior<BEBehaviorDoor>())
            .Select(d => d.Found)
            .FirstOrDefault(d => !d.Opened);
        if (door == null)
        {
            return ActResult.Skip("no closed door near the victim");
        }
        var pos = door.Pos;
        // A claim the victim neither owns nor shares keeps it out even when the server ignores claims.
        var rule = ctx.Config.RespectClaims == ClaimRespect.Off ? ClaimRespect.VictimAccess : ctx.Config.RespectClaims;
        if (!BlockRules.ClaimsAllow(world, rule, pos, ctx.Victim))
        {
            return ActResult.Skip("the door is on a claim the victim neither owns nor shares");
        }
        string? keptOut = KeptOut(ctx, door) ?? BudgetSpent(ctx, door, rules);
        if (keptOut != null)
        {
            Scrape(ctx, pos);
            return ActResult.Ok($"kept out at {Name(door)} at {pos} ({keptOut}): it scrapes at the boards");
        }
        if (BlockRules.AnimalsNear(world, pos, 16) || BlockRules.HostilesNear(world, pos, 24))
        {
            return ActResult.Skip("animals or another hostile creature near the door");
        }

        ctx.Harass.SetInt(HarassMemory.BreakIns, ctx.Harass.GetInt(HarassMemory.BreakIns) + 1);
        string counter = TornDoor.MaterialOf(door) == DoorMaterial.Wooden ? HarassMemory.Tears : HarassMemory.OtherTears;
        ctx.Harass.SetInt(counter, ctx.Harass.GetInt(counter) + 1);
        string name = Name(door);
        TornDoor.Tear(ctx, door, torn =>
        {
            if (torn)
            {
                Inside(ctx, pos, torn: true);
                ctx.System.Pages.OnLoss(ctx, PagesAct.LossDoor);
            }
        });
        return ActResult.Ok($"{name} at {pos}: heavy blows, then it tears the door off its hinges");
    }

    /// <summary>The night's roll of the level's breakInChance, made once at the first try of the night.</summary>
    private static bool Tonight(HarassContext ctx, EscalationLevelConfig rules)
    {
        var harass = ctx.Harass;
        double dusk = harass.GetDouble(HarassMemory.Dusk);
        if (harass.GetDouble(HarassMemory.BreakInNight, -1) != dusk)
        {
            harass.SetDouble(HarassMemory.BreakInNight, dusk);
            harass.SetBool(HarassMemory.BreakInTonight, ctx.Rand.NextDouble() < rules.BreakInChance);
        }
        return harass.GetBool(HarassMemory.BreakInTonight);
    }

    /// <summary>Why this door keeps an enraged walker out at any level, or null.</summary>
    public static string? KeptOut(HarassContext ctx, BEBehaviorDoor door)
    {
        var world = ctx.Api.World;
        if (Doors.IsLockedShut(world, door))
        {
            return "barred, padlocked, reinforced or story-locked";
        }
        if (TornDoor.MaterialOf(door) == DoorMaterial.None)
        {
            return "a gate, double door or door it cannot tear";
        }
        if (SomethingBehind(ctx, door.Pos))
        {
            return "something stands behind the door";
        }
        return null;
    }

    /// <summary>The door's budget in this haunt is spent, or null.</summary>
    public static string? BudgetSpent(HarassContext ctx, BEBehaviorDoor door, EscalationLevelConfig rules)
    {
        bool wooden = TornDoor.MaterialOf(door) == DoorMaterial.Wooden;
        int torn = ctx.Harass.GetInt(wooden ? HarassMemory.Tears : HarassMemory.OtherTears);
        int budget = wooden ? rules.WoodenDoorsPerHaunt : rules.OtherDoorsPerHaunt;
        return torn >= budget ? $"this haunt's {(wooden ? "wooden" : "iron and heavy")} doors ({budget}) are spent" : null;
    }

    /// <summary>Lids and torches inside, one after another, then the way in opens for the stalker AI.</summary>
    private static (int Lids, int Torches) Inside(HarassContext ctx, BlockPos pos, bool torn)
    {
        var world = ctx.Api.World;
        int reach = ctx.Config.BreakIn.Reach;
        var lids = ChestLidAct.LidsNear(ctx, BlockRules.VictimReach).Where(c => c.Pos.DistanceTo(pos) <= reach).Take(2).ToList();
        var torches = DouseTorchAct.LitTorchesNear(ctx, BlockRules.VictimReach).Where(t => t.Pos.DistanceTo(pos) <= reach).Take(2).ToList();
        int delay = 1500;
        foreach (var (lidPos, container) in lids)
        {
            ctx.Api.Event.RegisterCallback(_ => ChestLidAct.Pop(ctx, lidPos, container), delay);
            delay += 1200 + ctx.Rand.Next(800);
        }
        foreach (var (torchPos, torch) in torches)
        {
            ctx.Api.Event.RegisterCallback(_ =>
            {
                if (world.BlockAccessor.GetBlock(torchPos) is BlockTorch t && DouseTorchAct.IsLit(t))
                {
                    DouseTorchAct.Snuff(ctx, torchPos, t);
                }
            }, delay);
            delay += 1200 + ctx.Rand.Next(800);
        }
        OpenEntry(ctx, pos, torn);
        return (lids.Count, torches.Count);
    }

    /// <summary>The hand-off to the stalker AI: it may come in through this door until the time runs out.</summary>
    public static void OpenEntry(HarassContext ctx, BlockPos pos, bool torn)
    {
        var harass = ctx.Harass;
        harass.SetInt(HarassMemory.EntryX, pos.X);
        harass.SetInt(HarassMemory.EntryY, pos.Y);
        harass.SetInt(HarassMemory.EntryZ, pos.Z);
        harass.SetBool(HarassMemory.EntryTorn, torn);
        harass.SetLong(HarassMemory.EntryUntilMs, ctx.Api.World.ElapsedMilliseconds + ctx.System.Config.Enraged.MaxIndoorSeconds * 1000L);
    }

    private static string Name(BEBehaviorDoor door) => door.Block.Code?.ToShortString() ?? "door";

    /// <summary>Anything with a collision box in the two cells right behind the door, on the victim's side.</summary>
    private static bool SomethingBehind(HarassContext ctx, BlockPos pos)
    {
        var accessor = ctx.Api.World.BlockAccessor;
        double dx = ctx.Victim.Pos.X - (pos.X + 0.5), dz = ctx.Victim.Pos.Z - (pos.Z + 0.5);
        var face = Math.Abs(dx) > Math.Abs(dz) ? (dx > 0 ? BlockFacing.EAST : BlockFacing.WEST) : (dz > 0 ? BlockFacing.SOUTH : BlockFacing.NORTH);
        for (int dy = 0; dy < 2; dy++)
        {
            var behind = pos.AddCopy(face.Normali.X, dy, face.Normali.Z);
            var boxes = accessor.GetBlock(behind).GetCollisionBoxes(accessor, behind);
            if (boxes != null && boxes.Length > 0)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Kept out: a scrape at the boards, then the door straining (sound list, rows 61 and 62).</summary>
    private static void Scrape(HarassContext ctx, BlockPos pos)
    {
        var world = ctx.Api.World;
        double dx = pos.X + 0.5 - ctx.Victim.Pos.X, dz = pos.Z + 0.5 - ctx.Victim.Pos.Z;
        double len = Math.Max(0.001, Math.Sqrt(dx * dx + dz * dz));
        var outside = new Vec3d(pos.X + 0.5 + dx / len * 0.6, pos.Y + 1, pos.Z + 0.5 + dz / len * 0.6);
        for (int i = 0; i < 3; i++)
        {
            ctx.Api.Event.RegisterCallback(_ => HarassSounds.At(world, HarassSounds.Scrape, outside, 0.55f + (float)ctx.Rand.NextDouble() * 0.1f, 16, 1f), 1 + i * 700);
        }
        ctx.Api.Event.RegisterCallback(_ => HarassSounds.At(world, HarassSounds.WoodCreak, outside, 0.75f, 16, 0.9f), 700);
        if (ctx.Victim.Pos.DistanceTo(outside) <= 3)
        {
            ScreenShake.Send(ctx, 0.1f);
        }
    }
}
