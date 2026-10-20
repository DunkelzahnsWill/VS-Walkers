using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Walkers.Forms;

/// <summary>
/// The true form's body as a knife sees it ("walkers-husk", on both sides): after an ordinary death its
/// victim or its killer can skin it for 2 to ritual.huskFurCount unnatural furs; anyone else gets nothing (creature bible,
/// section 7). The knife looks for <see cref="IHarvestable"/> on the entity (ItemKnife.cs:227), so this
/// stands in for vanilla's harvestable, which the true form does not have. The permitted players travel
/// to clients in watched attributes, so the client only starts a cut the server will accept.
/// </summary>
public class EntityBehaviorWalkerHusk : EntityBehavior, IHarvestable
{
    public const string Code = Core.WalkersModSystem.ModId + "-husk";

    private const string SkinnersKey = "walkersSkinners";
    private const string SkinnedKey = "walkersSkinned";
    private const int MinFurs = 2;
    private const float HarvestSeconds = 5;

    public EntityBehaviorWalkerHusk(Entity entity) : base(entity)
    {
    }

    public override string PropertyName() => Code;

    /// <summary>Opens the body to skinning by these players (server).</summary>
    public static void Open(Entity body, IEnumerable<string> playerUids, bool allowed)
    {
        body.WatchedAttributes.SetString(SkinnersKey, allowed ? string.Join(",", playerUids) : "");
        body.WatchedAttributes.SetBool(SkinnedKey, false);
    }

    public bool IsHarvestable(ItemSlot slot, Entity forEntity)
    {
        if (entity.Alive || entity.WatchedAttributes.GetBool(SkinnedKey) || forEntity is not EntityPlayer player)
        {
            return false;
        }
        var skinners = entity.WatchedAttributes.GetString(SkinnersKey);
        return !string.IsNullOrEmpty(skinners) && skinners!.Split(',').Contains(player.PlayerUID);
    }

    public float GetHarvestDuration(ItemSlot slot, Entity forEntity) =>
        HarvestSeconds * forEntity.Stats.GetBlended("animalHarvestingTime");

    public void SetHarvested(IPlayer byPlayer, float dropQuantityMultiplier = 1f)
    {
        if (entity.World.Side != EnumAppSide.Server || byPlayer?.Entity == null || !IsHarvestable(byPlayer.InventoryManager.ActiveHotbarSlot, byPlayer.Entity))
        {
            return;
        }
        entity.WatchedAttributes.SetBool(SkinnedKey, true);
        if (entity.World.GetItem(new AssetLocation(Core.WalkersModSystem.ModId, "walkerfur")) is Item fur)
        {
            int most = entity.Api.ModLoader.GetModSystem<FormsModSystem>().Config.Ritual.HuskFurCount;
            var stack = new ItemStack(fur, entity.World.Rand.Next(MinFurs, Math.Max(MinFurs, most) + 1));
            if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true))
            {
                entity.World.SpawnItemEntity(stack, entity.Pos.XYZ.Add(0, 0.5, 0));
            }
        }
        entity.GetBehavior<EntityBehaviorWalkerForm>()?.OnSkinned(byPlayer);
    }
}
