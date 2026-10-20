using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Walkers.Forms;

/// <summary>
/// Unnatural fur ("walkers:walkerfur", item class "walkers-fur", both sides): each fur carries the id of
/// the walker it was skinned from, and stays warm until that walker is banished (creature bible,
/// section 7, "Unnatural furs"). Fire ends it: dropped into fire or lava it burns up, and in a firepit's
/// cooking slot it cooks to a pinch of charcoal. Either way forms reports the burn; the director owns
/// what the furs mean for whoever carries them.
/// </summary>
public class ItemWalkerFur : Item
{
    public const string ClassCode = Core.WalkersModSystem.ModId + "-fur";

    /// <summary>Stack attribute: the walker the fur came from. Furs without it (creative, the handbook) mean nothing.</summary>
    public const string WalkerIdKey = "walkersWalkerId";

    /// <summary>Stack attribute: set once the walker is banished.</summary>
    public const string ColdKey = "walkersCold";

    /// <summary>A dropped fur asks the director whether its walker is banished at most this often.</summary>
    private const long GroundCheckMs = 5000;

    private const string GroundCheckKey = "walkersFurCheckMs";

    public static int WalkerIdOf(ItemStack? stack) => stack?.Attributes.GetInt(WalkerIdKey, -1) ?? -1;

    /// <summary>Stamps the walker's id on furs just skinned from its body.</summary>
    public static void Stamp(ItemStack stack, int walkerId) => stack.Attributes.SetInt(WalkerIdKey, walkerId);

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        var stack = inSlot.Itemstack;
        if (WalkerIdOf(stack) >= 0)
        {
            dsc.AppendLine(Lang.Get(stack!.Attributes.GetBool(ColdKey) ? "walkers:walkerfur-cold" : "walkers:walkerfur-warm"));
        }
    }

    public override void OnGroundIdle(EntityItem entityItem)
    {
        base.OnGroundIdle(entityItem);
        if (entityItem.World.Side != EnumAppSide.Server || !entityItem.Alive)
        {
            return;
        }
        var stack = entityItem.Itemstack;
        if (entityItem.IsOnFire || entityItem.InLava)
        {
            WalkerRitual.SpawnAsh(entityItem.World, entityItem.Pos.XYZ, 8);
            Forms(entityItem.World)?.ReportFursBurned(WalkerIdOf(stack), entityItem.ByPlayerUid, stack?.StackSize ?? 0, "fire");
            entityItem.Die(EnumDespawnReason.Combusted);
            return;
        }
        long now = entityItem.World.ElapsedMilliseconds;
        if (WalkerIdOf(stack) >= 0 && !stack!.Attributes.GetBool(ColdKey) && now - entityItem.Attributes.GetLong(GroundCheckKey) >= GroundCheckMs)
        {
            entityItem.Attributes.SetLong(GroundCheckKey, now);
            if (Forms(entityItem.World)?.IsBanished(WalkerIdOf(stack)) == true)
            {
                stack.Attributes.SetBool(ColdKey, true);
                entityItem.WatchedAttributes.MarkAllDirty();
            }
        }
    }

    public override void OnModifiedInInventorySlot(IWorldAccessor world, ItemSlot slot, ItemStack? extractedStack = null)
    {
        base.OnModifiedInInventorySlot(world, slot, extractedStack);
        // Furs that lay in a chest while their walker was banished turn cold when next moved.
        if (world.Side == EnumAppSide.Server && slot.Itemstack is { } stack && WalkerIdOf(stack) >= 0 && !stack.Attributes.GetBool(ColdKey) &&
            Forms(world)?.IsBanished(WalkerIdOf(stack)) == true)
        {
            stack.Attributes.SetBool(ColdKey, true);
            slot.MarkDirty();
        }
    }

    public override void DoSmelt(IWorldAccessor world, ISlotProvider cookingSlotsProvider, ItemSlot inputSlot, ItemSlot outputSlot)
    {
        var stack = inputSlot.Itemstack;
        bool smelts = world.Side == EnumAppSide.Server && stack != null && CanSmelt(world, cookingSlotsProvider, stack, outputSlot.Itemstack);
        int walkerId = WalkerIdOf(stack);
        int before = stack?.StackSize ?? 0;
        base.DoSmelt(world, cookingSlotsProvider, inputSlot, outputSlot);
        if (smelts)
        {
            Forms(world)?.ReportFursBurned(walkerId, null, before - (inputSlot.Itemstack?.StackSize ?? 0), "firepit");
        }
    }

    private static FormsModSystem? Forms(IWorldAccessor world) =>
        world.Api is ICoreServerAPI sapi ? sapi.ModLoader.GetModSystem<FormsModSystem>() : null;
}
