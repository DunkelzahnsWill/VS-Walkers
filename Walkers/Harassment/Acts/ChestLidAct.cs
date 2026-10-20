using Vintagestory.API.MathTools;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;
using Walkers.Config;

namespace Walkers.Harassment.Acts;

/// <summary>
/// Chest lids, stages 4 and 5, day or night: a chest or trunk near the victim creaks open and shut on
/// its own. The container's own Activate with the walker as caller and a 1.5 s close sends only the lid
/// animation and its sounds; no inventory opens and nothing inside moves. Same claim, padlock and sight
/// rules as every block act, and never a container a player has open.
/// </summary>
public sealed class ChestLidAct : HarassAct
{
    public const string ActCode = "chestLids";

    private const int CloseMs = 1500;

    public override string Code => ActCode;

    public override int MinStage => 4;

    public override bool ByDay => true;

    public override bool Enabled(HarassConfig config) => config.ChestLids.Enabled;

    public override int Cap(HarassConfig config) => config.ChestLids.PerNight;

    public override ActResult Run(HarassContext ctx)
    {
        var world = ctx.Api.World;
        var chests = LidsNear(ctx, BlockRules.VictimReach)
            .Where(c => !BlockRules.VictimSees(world, ctx.Victim, c.Pos))
            .Take(6)
            .ToList();
        if (chests.Count == 0)
        {
            return ActResult.Skip("no chest it may touch out of the victim's sight");
        }
        var (pos, container) = chests[ctx.Rand.Next(chests.Count)];
        Pop(ctx, pos, container);
        return ActResult.Ok($"{container.Block.Code.ToShortString()} at {pos} popped");
    }

    /// <summary>Containers with a lid (the Container block behavior) within reach that pass the rules and that no player has open.</summary>
    public static System.Collections.Generic.List<(BlockPos Pos, BlockEntityOpenableContainer Container)> LidsNear(HarassContext ctx, int radius)
    {
        var world = ctx.Api.World;
        return BlockRules.FindNear(world, ctx.Victim, radius, be =>
                be is BlockEntityOpenableContainer c && c.Block?.GetBehavior<BlockBehaviorContainer>() != null ? c : null)
            .Where(c => BlockRules.MayTouch(world, ctx.Config, c.Pos, ctx.Victim) &&
                        !world.AllOnlinePlayers.Any(p => p.InventoryManager?.HasInventory(c.Found.Inventory) == true))
            .ToList();
    }

    public static void Pop(HarassContext ctx, BlockPos pos, BlockEntityOpenableContainer container)
    {
        var args = new TreeAttribute();
        args.SetLong("close", CloseMs);
        container.Block.Activate(ctx.Api.World, new Caller { Type = EnumCallerType.Entity, Entity = ctx.Body, Pos = ctx.Body.Pos.XYZ },
            new BlockSelection { Position = pos.Copy() }, args);
    }
}
