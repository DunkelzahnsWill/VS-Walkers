using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;
using Walkers.Stalker;

namespace Walkers.Harassment.Acts;

/// <summary>
/// Livestock panic, stages 2 to 5, ambient (at most once every 3 seconds while it passes, never drawn
/// by the scheduler): animals within harass.livestockPanic.radius of the walker bolt straight away from
/// it through their own flee task (InstaFleeFrom), before anyone sees why. Never while a door,
/// trapdoor or gate near them stands open, so a panic never sends a herd out of its pen.
/// </summary>
public sealed class LivestockPanic
{
    public const string ActCode = "livestockPanic";

    public const long EveryMs = 3000;

    /// <summary>Returns how many animals bolted.</summary>
    public int Tick(HarassContext ctx)
    {
        var config = ctx.Config.LivestockPanic;
        if (!config.Enabled || ctx.Stage < 2)
        {
            return 0;
        }
        long now = ctx.Api.World.ElapsedMilliseconds;
        if (now - ctx.State.LastPanicMs < EveryMs)
        {
            return 0;
        }
        ctx.State.LastPanicMs = now;
        var world = ctx.Api.World;
        var body = ctx.Body;
        var animals = world.GetEntitiesAround(body.Pos.XYZ, config.Radius, 4, e =>
            e is EntityAgent && e is not EntityPlayer && e.Alive && !StalkerModSystem.IsWalkerBody(e) &&
            e.EntityId != ctx.Victim.EntityId && e.Pos.DistanceTo(body.Pos) <= config.Radius);
        int bolted = 0;
        foreach (var animal in animals)
        {
            var flee = animal.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.AllTasks.OfType<AiTaskFleeEntity>().FirstOrDefault();
            if (flee == null || OpenWayOutNear(world, animal))
            {
                continue;
            }
            flee.InstaFleeFrom(body);
            bolted++;
        }
        return bolted;
    }

    private static bool OpenWayOutNear(IWorldAccessor world, Entity animal)
    {
        var pos = animal.Pos.AsBlockPos;
        if (BlockRules.FindNear(world, pos, 16, be => Doors.From(be)).Any(d => d.Found.Opened))
        {
            return true;
        }
        bool gate = false;
        world.BlockAccessor.WalkBlocks(pos.AddCopy(-6, -2, -6), pos.AddCopy(6, 2, 6), (block, _, _, _) =>
        {
            if (block is BlockFenceGate g && g.IsOpened())
            {
                gate = true;
            }
        });
        return gate;
    }
}
