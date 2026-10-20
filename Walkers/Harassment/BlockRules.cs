using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Walkers.Config;
using Walkers.Stalker;

namespace Walkers.Harassment;

/// <summary>
/// The rules for any act that touches a block (creature bible, section 6, "How it picks an act"): the
/// block lies within 16 blocks of the victim, out of the victim's sight at that moment, where
/// harass.respectClaims allows, and carries no padlock (whoever holds the key) and no story lock.
/// Doors, torches and lids check none of this themselves, so every act asks here first.
/// </summary>
public static class BlockRules
{
    /// <summary>An act touches a block only this close to its victim.</summary>
    public const int VictimReach = 16;

    /// <summary>Full width of the victim's view cone, in degrees, for "out of sight".</summary>
    private const float SightConeDegrees = 120;

    private static readonly float CosHalfSightCone = MathF.Cos(SightConeDegrees * GameMath.DEG2RAD / 2);

    /// <summary>
    /// The claim rule. victimAccess: unclaimed land, or a claim the victim owns or shares through the
    /// claim's owning group; never a claim where the victim is only a permitted player, nor one open
    /// to everyone. A stand-in victim owns nothing, so only unclaimed land. anyClaim: unclaimed land
    /// only. off: anywhere.
    /// </summary>
    public static bool ClaimsAllow(IWorldAccessor world, ClaimRespect rule, BlockPos pos, Entity victim)
    {
        if (rule == ClaimRespect.Off)
        {
            return true;
        }
        var claims = world.Claims.Get(pos);
        if (claims == null || claims.Length == 0)
        {
            return true;
        }
        if (rule == ClaimRespect.AnyClaim || victim is not EntityPlayer { Player: IPlayer player })
        {
            return false;
        }
        foreach (var claim in claims)
        {
            bool owns = claim.OwnedByPlayerUid == player.PlayerUID;
            bool sharesGroup = claim.OwnedByPlayerGroupUid != 0 && player.GetGroup((int)claim.OwnedByPlayerGroupUid) != null;
            if (!owns && !sharesGroup)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>A padlock on the block, whoever holds the key (not IsLockedForInteract, which lets the owner through).</summary>
    public static bool IsPadlocked(IWorldAccessor world, BlockPos pos) =>
        world.Api.ModLoader.GetModSystem<ModSystemBlockReinforcement>()?.GetReinforcment(pos)?.Locked == true;

    /// <summary>Every rule but the sight check: reach, claims, padlock.</summary>
    public static bool MayTouch(IWorldAccessor world, HarassConfig config, BlockPos pos, Entity victim)
    {
        if (victim.Pos.Dimension != pos.dimension || victim.Pos.SquareDistanceTo(pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5) > VictimReach * VictimReach)
        {
            return false;
        }
        return ClaimsAllow(world, config.RespectClaims, pos, victim) && !IsPadlocked(world, pos);
    }

    /// <summary>
    /// Whether the victim can see the block's centre right now: inside a 120-degree cone around where
    /// they look, with no solid block in between. A stand-in looks along its yaw.
    /// </summary>
    public static bool VictimSees(IWorldAccessor world, Entity victim, BlockPos pos)
    {
        var eye = WalkerGaze.EyePos(victim);
        var target = new Vec3d(pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5);
        if (!WalkerGaze.InCone(eye, WalkerGaze.ViewVector(victim), target, CosHalfSightCone))
        {
            return false;
        }
        BlockSelection? blockSel = null;
        EntitySelection? entitySel = null;
        world.RayTraceForSelection(eye, target, ref blockSel, ref entitySel, (bp, _) => !bp.Equals(pos), _ => false);
        return blockSel == null;
    }

    /// <summary>
    /// Every loaded block entity that <paramref name="pick"/> turns into a <typeparamref name="T"/> (a
    /// block entity class or one of its behaviors) within <paramref name="radius"/> blocks of the victim, nearest first.
    /// Walks the block entities of the chunks in range, never every block.
    /// </summary>
    public static List<(BlockPos Pos, T Found)> FindNear<T>(IWorldAccessor world, Entity victim, int radius, System.Func<BlockEntity, T?> pick) where T : class =>
        FindNear(world, victim.Pos.AsBlockPos, radius, pick);

    /// <summary>As above, around a block position.</summary>
    public static List<(BlockPos Pos, T Found)> FindNear<T>(IWorldAccessor world, BlockPos center, int radius, System.Func<BlockEntity, T?> pick) where T : class
    {
        var found = new List<(BlockPos, T)>();
        var accessor = world.BlockAccessor;
        int size = Vintagestory.API.Config.GlobalConstants.ChunkSize;
        int minCx = (center.X - radius) / size, maxCx = (center.X + radius) / size;
        int minCy = Math.Max(0, center.Y - radius) / size, maxCy = (center.Y + radius) / size;
        int minCz = (center.Z - radius) / size, maxCz = (center.Z + radius) / size;
        int radiusSq = radius * radius;
        for (int cx = minCx; cx <= maxCx; cx++)
        {
            for (int cy = minCy; cy <= maxCy; cy++)
            {
                for (int cz = minCz; cz <= maxCz; cz++)
                {
                    var chunk = accessor.GetChunk(cx, cy + center.dimension * (Vintagestory.API.Config.GlobalConstants.DimensionSizeInChunks), cz);
                    if (chunk?.BlockEntities == null)
                    {
                        continue;
                    }
                    foreach (var be in chunk.BlockEntities.Values)
                    {
                        if (be?.Pos == null || be.Pos.dimension != center.dimension || be.Pos.DistanceSqTo(center.X, center.Y, center.Z) > radiusSq)
                        {
                            continue;
                        }
                        var hit = pick(be);
                        if (hit != null)
                        {
                            found.Add((be.Pos.Copy(), hit));
                        }
                    }
                }
            }
        }
        found.Sort((a, b) => a.Item1.DistanceSqTo(center.X, center.Y, center.Z).CompareTo(b.Item1.DistanceSqTo(center.X, center.Y, center.Z)));
        return found;
    }

    /// <summary>A door that may break when used (the crude door) crashes when moved without a player.</summary>
    public static bool MayBreakWhenMoved(Block block) => block.Attributes?["breakOnTriggerChance"].AsFloat(0) > 0;

    /// <summary>A living creature with a flee task (an animal that could get out) within <paramref name="radius"/> blocks.</summary>
    public static bool AnimalsNear(IWorldAccessor world, BlockPos pos, float radius) =>
        world.GetEntitiesAround(new Vec3d(pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5), radius, 4, e =>
            e is EntityAgent && e is not EntityPlayer && e.Alive && !StalkerModSystem.IsWalkerBody(e) &&
            e.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.AllTasks.Exists(t => t is AiTaskFleeEntity) == true).Length > 0;

    /// <summary>Another hostile creature (one with a melee task) within <paramref name="radius"/> blocks.</summary>
    public static bool HostilesNear(IWorldAccessor world, BlockPos pos, float radius) =>
        world.GetEntitiesAround(new Vec3d(pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5), radius, 8, e =>
            e is EntityAgent && e is not EntityPlayer && e.Alive && !StalkerModSystem.IsWalkerBody(e) &&
            e.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.AllTasks.Exists(t => t is AiTaskMeleeAttack) == true).Length > 0;
}
