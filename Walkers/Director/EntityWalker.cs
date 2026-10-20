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

    public override bool StoreWithChunk => false;
}
