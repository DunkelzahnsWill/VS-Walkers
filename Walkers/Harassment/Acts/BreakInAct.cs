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
/// Breaking and entering when enraged (the owner's answer 9; creature bible, section 7, "Enraged"):
/// stage 5, at night, at most enraged.entriesPerNight and enraged.entriesPerHaunt times, only while the
/// victim is awake, out of bed, more than 10 seconds past a wake and alone. The closed door nearest the
/// victim (within 16 blocks) bursts open with a crack of wood, or, from enraged.tearFromReturn and
/// once a haunt, is torn off its hinges (<see cref="TornDoor"/>). Inside, within harass.breakIn.reach
/// of the door, up to two lids pop and up to two torches go out. Then the kit opens the way in
/// (walkers/harass entryX/Y/Z, entryUntilMs, entryTorn) and the stalker AI's hunt comes through it
/// for at most enraged.maxIndoorSeconds and enraged.maxIndoorBlocks. Nothing else is broken, taken or
/// placed; an opened door goes on the restore list, doused torches stay out as with the torch act.
///
/// What keeps it out, enraged or not: a padlock, a door bar, plumb-and-square reinforcement on either
/// cell of the door, anything with a collision box in the two cells behind it, a metal, heavy, gate or
/// double door, a story lock, and a claim the victim neither owns nor shares. Kept out, it scrapes at
/// the boards and the door creaks. It never comes near animals that could get out or another hostile
/// creature.
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
        if (HarassMemory.Count(ctx.Harass, Code) >= enraged.EntriesPerNight)
        {
            return ActResult.Skip($"tonight's entries ({enraged.EntriesPerNight}) are spent");
        }
        if (ctx.Harass.GetInt(HarassMemory.BreakIns) >= enraged.EntriesPerHaunt)
        {
            return ActResult.Skip("this haunt's entries are spent");
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
        if (ctx.System.Stalker.CountingNear(ctx.Victim.Pos.XYZ, ctx.Victim.Pos.Dimension, CompanyRange).Any(e => e.EntityId != ctx.Victim.EntityId))
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
        string? keptOut = KeptOut(ctx, door);
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
        if (MayTear(ctx, door))
        {
            ctx.Harass.SetInt(HarassMemory.Tears, ctx.Harass.GetInt(HarassMemory.Tears) + 1);
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
        if (!Doors.MayMove(world, new Doors.Handle { Door = door }))
        {
            // The crude door: moving it without a player may break it, and tearing waits for a later return.
            Scrape(ctx, pos);
            return ActResult.Ok($"kept out at {Name(door)} at {pos} (it will not force this door yet): it scrapes at the boards");
        }

        // The door bursts in.
        var center = new Vec3d(pos.X + 0.5, pos.Y + 1, pos.Z + 0.5);
        HarassSounds.At(world, HarassSounds.LargeDoorClose, center, 1.25f, 32, 1f);
        HarassSounds.At(world, HarassSounds.KnockBody, center, 0.6f, 32, 1f);
        DoorAct.Open(ctx, new Doors.Handle { Door = door });
        ctx.Api.Event.RegisterCallback(_ => HarassSounds.At(world, HarassSounds.WoodCreak, center, 0.7f, 24, 1f), 600);
        var (lids, torches) = Inside(ctx, pos, torn: false);
        if (ctx.Victim.Pos.DistanceTo(center) <= 8)
        {
            ScreenShake.Send(ctx, 0.15f);
        }
        return ActResult.Ok($"{Name(door)} at {pos} forced open; {lids} lid(s), {torches} torch(es) inside");
    }

    /// <summary>Why this door keeps an enraged walker out, or null.</summary>
    public static string? KeptOut(HarassContext ctx, BEBehaviorDoor door)
    {
        var world = ctx.Api.World;
        if (Doors.IsLockedShut(world, door))
        {
            return "barred, padlocked, reinforced or story-locked";
        }
        if (!TornDoor.IsPlainWooden(door))
        {
            return "not a plain single wooden door";
        }
        if (SomethingBehind(ctx, door.Pos))
        {
            return "something stands behind the door";
        }
        return null;
    }

    /// <summary>From enraged.tearFromReturn, once per haunt (enraged.tearsPerHaunt).</summary>
    public static bool MayTear(HarassContext ctx, BEBehaviorDoor door)
    {
        var enraged = ctx.System.Config.Enraged;
        return Enraged.Level(ctx) >= enraged.TearFromReturn &&
               ctx.Harass.GetInt(HarassMemory.Tears) < enraged.TearsPerHaunt && TornDoor.IsPlainWooden(door);
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
                    DouseTorchAct.Douse(ctx, torchPos, t);
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
