using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Walkers.Forms;

/// <summary>
/// Unnatural fur ("walkers:walkerfur", item class "walkers-fur", both sides; creature bible, section 7,
/// "Unnatural furs"). The furs carry no walker of their own: the director decides what carrying them
/// means. Fire ends them: dropped into fire or lava they burn up, and in a firepit's cooking slot they
/// cook to a pinch of charcoal. Either way forms reports the burn (<see cref="FormsModSystem.FursBurned"/>).
/// </summary>
public class ItemWalkerFur : Item
{
    public const string ClassCode = Core.WalkersModSystem.ModId + "-fur";

    public override void OnGroundIdle(EntityItem entityItem)
    {
        base.OnGroundIdle(entityItem);
        if (entityItem.World.Side != EnumAppSide.Server || !entityItem.Alive || !(entityItem.IsOnFire || entityItem.InLava))
        {
            return;
        }
        WalkerRitual.SpawnAsh(entityItem.World, entityItem.Pos.XYZ, 8);
        Forms(entityItem.World)?.ReportFursBurned(entityItem.ByPlayerUid, entityItem.Itemstack?.StackSize ?? 0, "fire");
        entityItem.Die(EnumDespawnReason.Combusted);
    }

    public override void DoSmelt(IWorldAccessor world, ISlotProvider cookingSlotsProvider, ItemSlot inputSlot, ItemSlot outputSlot)
    {
        var stack = inputSlot.Itemstack;
        bool smelts = world.Side == EnumAppSide.Server && stack != null && CanSmelt(world, cookingSlotsProvider, stack, outputSlot.Itemstack);
        int before = stack?.StackSize ?? 0;
        base.DoSmelt(world, cookingSlotsProvider, inputSlot, outputSlot);
        if (smelts)
        {
            Forms(world)?.ReportFursBurned(null, before - (inputSlot.Itemstack?.StackSize ?? 0), "firepit");
        }
    }

    private static FormsModSystem? Forms(IWorldAccessor world) =>
        world.Api is ICoreServerAPI sapi ? sapi.ModLoader.GetModSystem<FormsModSystem>() : null;
}
