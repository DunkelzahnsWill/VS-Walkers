using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Walkers.Config;
using Walkers.Director;

namespace Walkers.Forms;

/// <summary>A burn tick of ours, so the damage filter tells it from the vanilla one (Entity.cs:1128), which it replaces.</summary>
public class WalkersBurnDamageSource : DamageSource
{
}

/// <summary>A damage source that names its weapon itself; only the forms self-test makes these.</summary>
public class WalkersTestDamageSource : DamageSource
{
    public ItemStack? Weapon;
}

/// <summary>
/// The kill ritual (creature bible, section 7): which blows are silver, and what a banishing does.
/// </summary>
public static class WalkerRitual
{
    /// <summary>Counting players within this many blocks see the banishing line.</summary>
    public const double BanishLineRange = 32;

    public const string CauseSilverArrow = "silver arrow";
    public const string CauseSilverWeapon = "silver weapon";
    public const string CauseFire = "fire";
    public const string CauseBurnedBody = "burned body";

    /// <summary>The lore page that lies beside the eyes (lore bible: Later Leaves 2).</summary>
    private const string PageDiscoveryCode = "walkers-pennlate";
    private const int PageChapter = 1;

    /// <summary>True for an item that counts as silver: listed in ritual.silverItems, or carrying the walkersBane attribute.</summary>
    public static bool IsSilver(ItemStack? stack, RitualConfig config)
    {
        var collectible = stack?.Collectible;
        if (collectible?.Code == null)
        {
            return false;
        }
        if (collectible.Attributes?["walkersBane"].AsBool(false) == true)
        {
            return true;
        }
        var code = collectible.Code.ToString();
        return config.SilverItems.Any(s => string.Equals(s, code, StringComparison.OrdinalIgnoreCase) ||
                                           (!s.Contains(':') && string.Equals("game:" + s, code, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// The item a blow was struck with: the projectile's stack for arrows and thrown spears, otherwise the
    /// attacker's right hand, or the stack it held when the attack began (a blow that breaks the weapon
    /// empties the hand before the damage lands).
    /// </summary>
    public static ItemStack? WeaponOf(DamageSource? source, IWorldAccessor world, ItemStack? attackSnapshot)
    {
        if (source == null)
        {
            return null;
        }
        if (source is WalkersTestDamageSource test)
        {
            return test.Weapon;
        }
        if (source.SourceEntity is IProjectile projectile)
        {
            var stack = projectile.ProjectileStack;
            if (stack != null && stack.Collectible == null)
            {
                stack.ResolveBlockOrItem(world);
            }
            return stack;
        }
        if (source.SourceEntity is EntityAgent agent)
        {
            return agent.RightHandItemSlot?.Itemstack ?? attackSnapshot;
        }
        return null;
    }

    /// <summary>The player behind a blow or a burn: the attacker, the archer, or whoever lit the fire.</summary>
    public static IPlayer? PlayerOf(DamageSource? source)
    {
        var cause = source?.GetCauseEntity() ?? source?.SourceEntity;
        return (cause as EntityPlayer)?.Player;
    }

    /// <summary>
    /// The walker is banished: ash and bark, the eyes and a page beside them, stability back for the
    /// banisher, the banishing line for everyone near, and the director retires the walker.
    /// </summary>
    public static void Banish(FormsModSystem forms, Entity body, IPlayer? byPlayer, string cause)
    {
        var world = body.World;
        var pos = body.Pos.XYZ;
        SpawnAsh(world, pos, 60);
        world.PlaySoundAt(new AssetLocation("game", "sounds/effect/extinguish"), pos.X, pos.Y, pos.Z, null, true, 32, 1f);

        var drop = pos.AddCopy(0, 0.5, 0);
        if (world.GetItem(new AssetLocation(Core.WalkersModSystem.ModId, "walkereyes")) is Item eyes)
        {
            world.SpawnItemEntity(new ItemStack(eyes), drop);
        }
        if (world.GetItem(new AssetLocation("game", "lore-paper")) is Item paper)
        {
            var page = new ItemStack(paper);
            page.Attributes.SetString("category", "walkers-step");
            page.Attributes.SetString("discoveryCode", PageDiscoveryCode);
            page.Attributes["chapterIds"] = new IntArrayAttribute(new[] { PageChapter });
            page.Attributes.SetString("title", Lang.Get(Core.WalkersModSystem.ModId + ":pagename-knotleaf"));
            world.SpawnItemEntity(page, drop);
        }

        var config = forms.Config;
        if (byPlayer?.Entity != null && config.Ritual.BanishStabilityRecovery > 0 && world.Config.GetBool("temporalStability", true))
        {
            var attributes = byPlayer.Entity.WatchedAttributes;
            attributes.SetDouble("temporalStability", Math.Min(1, attributes.GetDouble("temporalStability", 1) + config.Ritual.BanishStabilityRecovery));
        }

        foreach (var player in forms.CountingPlayersNear(body, BanishLineRange))
        {
            forms.Sapi.SendIngameDiscovery(player, "walkers-banish", null);
        }

        var director = forms.Director;
        if (director != null && director.TryGetWalkerId(body, out var walkerId))
        {
            director.Banish(walkerId, byPlayer, cause);
        }
        forms.Mod.Logger.Notification("Forms: walker body #{0} banished ({1}{2})", body.EntityId, cause, byPlayer == null ? "" : ", by " + byPlayer.PlayerName);
    }

    /// <summary>Reports an outcome that is not a banishing to the director, and logs it.</summary>
    public static void Release(FormsModSystem forms, Entity body, WalkerRelease reason, bool fursTaken)
    {
        var director = forms.Director;
        if (director != null && director.TryGetWalkerId(body, out var walkerId))
        {
            director.Release(walkerId, reason, fursTaken);
        }
        forms.Mod.Logger.Notification("Forms: walker body #{0} released ({1}); the walker will return", body.EntityId, reason);
    }

    /// <summary>Bursts of bark and ash, as at a shift or a banishing. Clients receive them from the server.</summary>
    public static void SpawnAsh(IWorldAccessor world, Vec3d pos, int quantity)
    {
        var particles = new SimpleParticleProperties(
            quantity / 2f, quantity,
            ColorUtil.ToRgba(255, 40, 30, 24),
            pos.AddCopy(-0.4, 0, -0.4), pos.AddCopy(0.4, 1.6, 0.4),
            new Vec3f(-0.3f, 0.1f, -0.3f), new Vec3f(0.3f, 0.6f, 0.3f),
            1.5f, 0.3f, 0.4f, 1f, EnumParticleModel.Cube);
        particles.SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -0.3f);
        world.SpawnParticles(particles, null);
    }
}
