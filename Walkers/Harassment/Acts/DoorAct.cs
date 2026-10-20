using Vintagestory.API.MathTools;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using Walkers.Config;
using Walkers.Director;
using Walkers.Stalker;

namespace Walkers.Harassment.Acts;

/// <summary>
/// Doors and trapdoors, stages 4 and 5, at night: a door or trapdoor near the victim swings open while
/// they are not looking, or one they left open (a fence gate included) swings shut. It never opens a
/// fence gate. It opens one only when no animal that could get out is within 16 blocks of it, no other
/// hostile creature within 24, and the victim is not in bed. A door it opened goes on the restore list
/// and is closed within harass.doors.maxOpenHours, when it withdraws, and after a restart.
/// </summary>
public sealed class DoorAct : HarassAct
{
    public const string ActCode = "doors";

    private const int GateSearch = 8;

    public override string Code => ActCode;

    public override int MinStage => 4;

    public override bool Enabled(HarassConfig config) => config.Doors.Enabled;

    public override int Cap(HarassConfig config) => config.Doors.PerNight;

    public override ActResult Run(HarassContext ctx)
    {
        var world = ctx.Api.World;
        var doors = BlockRules.FindNear(world, ctx.Victim, BlockRules.VictimReach, be => Doors.From(be))
            .Where(d => BlockRules.MayTouch(world, ctx.Config, d.Pos, ctx.Victim) && Doors.MayMove(world, d.Found) && !BlockRules.VictimSees(world, ctx.Victim, d.Pos))
            .Select(d => d.Found)
            .ToList();
        var openOnes = doors.Where(d => d.Opened && !Doors.SomeoneInDoorway(world, d.Pos)).ToList();
        var closedOnes = doors.Where(d => !d.Opened).ToList();
        var gates = OpenGatesNear(ctx);

        bool asleep = StalkerModSystem.IsAsleep(ctx.Victim);
        bool shut = (openOnes.Count > 0 || gates.Count > 0) && (closedOnes.Count == 0 || asleep || ctx.Rand.NextDouble() < 0.4);
        if (shut)
        {
            int pick = ctx.Rand.Next(openOnes.Count + gates.Count);
            if (pick < openOnes.Count)
            {
                var door = openOnes[pick];
                door.Set(false);
                return ActResult.Ok($"{door.Name} at {door.Pos} shut");
            }
            var (gatePos, gate) = gates[pick - openOnes.Count];
            ShutGate(ctx, gatePos, gate);
            return ActResult.Ok($"{gate.Code.ToShortString()} at {gatePos} shut");
        }
        if (asleep)
        {
            return ActResult.Skip("the victim is in bed: it opens nothing");
        }
        foreach (var door in closedOnes.OrderBy(_ => ctx.Rand.Next()))
        {
            if (BlockRules.AnimalsNear(world, door.Pos, 16) || BlockRules.HostilesNear(world, door.Pos, 24))
            {
                continue;
            }
            Open(ctx, door);
            return ActResult.Ok($"{door.Name} at {door.Pos} opened");
        }
        return ActResult.Skip(doors.Count == 0 ? "no door it may touch out of the victim's sight" : "animals or hostile creatures near every closed door");
    }

    /// <summary>Opens a door and enters it on the restore list, due in harass.doors.maxOpenHours.</summary>
    public static void Open(HarassContext ctx, Doors.Handle door)
    {
        var world = ctx.Api.World;
        door.Set(true);
        double due = world.Calendar.TotalHours + ctx.Config.Doors.MaxOpenHours;
        ctx.System.Restore.Add(ctx, new RestoreEntry(Doors.RestoreKind, door.Pos.Copy(), "closed", "open", ctx.System.WalkerIdOf(ctx), due, UndoOnWithdraw: true, UndoAtDawn: true));
    }

    /// <summary>Open fence gates within 8 blocks of the victim that it may touch, out of their sight.</summary>
    private static List<(BlockPos, BlockFenceGate)> OpenGatesNear(HarassContext ctx)
    {
        var world = ctx.Api.World;
        var found = new List<(BlockPos, BlockFenceGate)>();
        var center = ctx.Victim.Pos.AsBlockPos;
        var min = center.AddCopy(-GateSearch, -3, -GateSearch);
        var max = center.AddCopy(GateSearch, 3, GateSearch);
        world.BlockAccessor.WalkBlocks(min, max, (block, x, y, z) =>
        {
            if (block is BlockFenceGate gate && gate.IsOpened())
            {
                var pos = new BlockPos(x, y, z, center.dimension);
                if (BlockRules.MayTouch(world, ctx.Config, pos, ctx.Victim) && !BlockRules.VictimSees(world, ctx.Victim, pos) && !Doors.SomeoneInDoorway(world, pos))
                {
                    found.Add((pos, gate));
                }
            }
        });
        return found;
    }

    /// <summary>Activate on a gate swaps the block silently, so the gate's own sound is played here.</summary>
    private static void ShutGate(HarassContext ctx, BlockPos pos, BlockFenceGate gate)
    {
        var world = ctx.Api.World;
        var args = new Vintagestory.API.Datastructures.TreeAttribute();
        args.SetBool("opened", false);
        gate.Activate(world, new Caller { Type = EnumCallerType.Entity, Entity = ctx.Body, Pos = ctx.Body.Pos.XYZ }, new BlockSelection { Position = pos }, args);
        var sound = AssetLocation.Create(gate.Attributes?["triggerSound"].AsString("sounds/block/door") ?? "sounds/block/door", gate.Code.Domain);
        world.PlaySoundAt(sound, pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5, null, 0.9f, 32, 1f);
    }
}
