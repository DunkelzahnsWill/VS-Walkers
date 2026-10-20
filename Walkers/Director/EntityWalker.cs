using Vintagestory.API.Common;

namespace Walkers.Director;

/// <summary>
/// The entity class of both walker forms ("class": "walkers-walker" in the entity JSON). A walker body
/// is never saved with its chunk: an unloaded chunk or a server stop simply removes it, and the
/// director places it again from the walker's record (creature bible, section 8; vanilla's Erel boss
/// does the same, EntityErel.cs:44). It stays an EntityAgent, so a dead body lies in the world through
/// deaddecay. Registered on both sides, because entity classes are created on clients too.
/// </summary>
public class EntityWalker : EntityAgent
{
    public const string ClassCode = Core.WalkersModSystem.ModId + "-walker";

    /// <summary>
    /// Asked before a body dies; true refuses the death and the body lives on. Forms uses it so no
    /// player kills the guise (creature bible, section 4). Never asked for a death without a cause.
    /// </summary>
    public static System.Func<EntityWalker, EnumDespawnReason, DamageSource, bool>? RefuseDeath;

    public override bool StoreWithChunk => false;

    public override void Die(EnumDespawnReason reason = EnumDespawnReason.Death, DamageSource? damageSourceForDeath = null)
    {
        if (Alive && reason == EnumDespawnReason.Death && damageSourceForDeath != null && World?.Side == EnumAppSide.Server &&
            RefuseDeath?.Invoke(this, reason, damageSourceForDeath) == true)
        {
            return;
        }
        base.Die(reason, damageSourceForDeath);
    }
}
