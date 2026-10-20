using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Walkers.Config;
using Walkers.Director;
using Walkers.Stalker;

namespace Walkers.Harassment.Acts;

/// <summary>
/// Breaking and entering when enraged (the owner's answer 9), stage 5, at night. Enraged means a
/// player has hurt it during this haunt, or it came back after an improper kill. The closed, unlocked
/// door nearest the victim (within 16 blocks, where it may touch) bursts open with a crack of wood,
/// and inside, within harass.breakIn.reach of the door, up to two lids pop and up to two torches go
/// out. Nothing is broken, taken or placed. The door goes on the restore list; doused torches stay
/// out, as with the torch act. The body stays outside: at most the stalker AI's doorway lunge goes a
/// step in.
///
/// What keeps it out: a barred door, a padlock, a block set behind the door, a story lock, or a door
/// on land the claim rule forbids. Kept out, it scrapes at the boards and the door creaks. It never
/// opens a door while its victim is in bed, near animals that could get out, or near another hostile
/// creature, and only harass.breakIn.perNight times a night and perHaunt times a haunt.
/// </summary>
public sealed class BreakInAct : HarassAct
{
    public const string ActCode = "breakIn";

    public override string Code => ActCode;

    public override int MinStage => 5;

    public override bool Enabled(HarassConfig config) => config.BreakIn.Enabled;

    public override int Cap(HarassConfig config) => config.BreakIn.PerNight;

    /// <summary>A player hurt it this haunt, or it is a walker that came back.</summary>
    public static bool IsEnraged(HarassContext ctx) =>
        ctx.Brain.Ai.GetBool(StalkerMemory.Provoked) || ctx.Memory.GetInt(WalkerMemory.Returns) > 0;

    public override double Weight(HarassContext ctx) => IsEnraged(ctx) ? 2 : 0;

    public override ActResult Run(HarassContext ctx)
    {
        if (!IsEnraged(ctx))
        {
            return ActResult.Skip("not enraged");
        }
        if (ctx.Harass.GetInt(HarassMemory.BreakIns) >= ctx.Config.BreakIn.PerHaunt)
        {
            return ActResult.Skip("this haunt's break-ins are spent");
        }
        if (StalkerModSystem.IsAsleep(ctx.Victim))
        {
            return ActResult.Skip("the victim is in bed: it opens nothing");
        }
        var world = ctx.Api.World;
        var door = BlockRules.FindNear(world, ctx.Victim, BlockRules.VictimReach, be => Doors.From(be))
            .Select(d => d.Found)
            .FirstOrDefault(d => !d.Opened && d.Door != null && BlockRules.ClaimsAllow(world, ctx.Config.RespectClaims, d.Pos, ctx.Victim));
        if (door == null)
        {
            return ActResult.Skip("no closed door near the victim");
        }
        var pos = door.Pos;
        if (!Doors.MayMove(world, door) || Blocked(ctx, pos))
        {
            Scrape(ctx, pos);
            return ActResult.Ok($"kept out at {door.Name} at {pos}: it scrapes at the boards");
        }
        if (BlockRules.AnimalsNear(world, pos, 16) || BlockRules.HostilesNear(world, pos, 24))
        {
            return ActResult.Skip("animals or another hostile creature near the door");
        }

        // The door bursts in.
        var center = new Vec3d(pos.X + 0.5, pos.Y + 1, pos.Z + 0.5);
        HarassSounds.At(world, HarassSounds.LargeDoorClose, center, 1.25f, 32, 1f);
        HarassSounds.At(world, HarassSounds.KnockBody, center, 0.6f, 32, 1f);
        DoorAct.Open(ctx, door);
        ctx.Api.Event.RegisterCallback(_ => HarassSounds.At(world, HarassSounds.WoodCreak, center, 0.7f, 24, 1f), 600);

        // Inside: lids and torches within reach of the door, one after another.
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
                if (world.BlockAccessor.GetBlock(torchPos) is Vintagestory.GameContent.BlockTorch t && DouseTorchAct.IsLit(t))
                {
                    DouseTorchAct.Douse(ctx, torchPos, t);
                }
            }, delay);
            delay += 1200 + ctx.Rand.Next(800);
        }
        if (ctx.Victim.Pos.DistanceTo(center) <= 8)
        {
            ScreenShake.Send(ctx, 0.15f);
        }
        ctx.Harass.SetInt(HarassMemory.BreakIns, ctx.Harass.GetInt(HarassMemory.BreakIns) + 1);
        return ActResult.Ok($"{door.Name} at {pos} forced open; {lids.Count} lid(s), {torches.Count} torch(es) inside");
    }

    /// <summary>A solid block set right behind the door, on the victim's side, keeps it shut.</summary>
    private static bool Blocked(HarassContext ctx, BlockPos pos)
    {
        var accessor = ctx.Api.World.BlockAccessor;
        double dx = ctx.Victim.Pos.X - (pos.X + 0.5), dz = ctx.Victim.Pos.Z - (pos.Z + 0.5);
        var face = Math.Abs(dx) > Math.Abs(dz) ? (dx > 0 ? BlockFacing.EAST : BlockFacing.WEST) : (dz > 0 ? BlockFacing.SOUTH : BlockFacing.NORTH);
        for (int dy = 0; dy < 2; dy++)
        {
            var behind = pos.AddCopy(face.Normali.X, dy, face.Normali.Z);
            var block = accessor.GetBlock(behind);
            if (block.SideSolid[face.Opposite.Index])
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
