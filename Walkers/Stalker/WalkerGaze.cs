using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Walkers.Config;

namespace Walkers.Stalker;

/// <summary>
/// The gaze rule (creature bible, section 5): a walker counts as watched when a counting player within
/// gaze.maxDistance has it inside a gaze.coneDegrees view cone and at least one of three rays (to its
/// head, chest and feet) is clear. The view-cone test comes first; rays are cast only for points
/// inside the cone, at most three per watcher and <see cref="MaxRaysPerCheck"/> per check. Blocks
/// without a collision box (tall grass, flowers) and glass do not block the view; leaves and walls do
/// (<see cref="GazeSight"/>).
///
/// The check runs gaze.checksPerSecond times a second per walker (<see cref="WalkerBrain"/>).
/// Other features read the last result through <see cref="IsWatched"/>, or force a fresh one with
/// <see cref="CheckNow"/>.
/// </summary>
public static class WalkerGaze
{
    /// <summary>Ray budget of one check: three watchers' worth. The victim is always tested first.</summary>
    public const int MaxRaysPerCheck = 9;

    /// <summary>Heights of the three target points, as shares of the walker's selection box.</summary>
    private static readonly float[] TargetHeights = { 0.9f, 0.55f, 0.12f };

    /// <summary>
    /// Whether anyone saw the walker at its last gaze check (at most a check interval old), and how many
    /// watchers that check found. False for a body the stalker AI does not track.
    /// </summary>
    public static bool IsWatched(Entity walker, out int watchers)
    {
        var brain = StalkerModSystem.BrainOf(walker);
        watchers = brain?.Gaze.Watchers ?? 0;
        return brain?.Gaze.AnyWatched ?? false;
    }

    /// <summary>Runs a gaze check now and returns its result; the brain keeps it as its latest.</summary>
    public static bool CheckNow(Entity walker, out int watchers)
    {
        var brain = StalkerModSystem.BrainOf(walker);
        if (brain == null)
        {
            watchers = 0;
            return false;
        }
        brain.RunGazeCheck();
        watchers = brain.Gaze.Watchers;
        return brain.Gaze.AnyWatched;
    }

    /// <summary>
    /// Tests every watcher against the walker. <paramref name="watchers"/> must hold the victim first
    /// when it is among them. Stops once the victim is tested and someone is known to see it.
    /// </summary>
    public static GazeResult Check(Entity walker, Entity? victim, IReadOnlyList<Entity> watchers, GazeConfig gaze, IWorldIntersectionSupplier sight)
    {
        var result = new GazeResult { NearestWatcherDistance = double.MaxValue };
        float cosHalf = MathF.Cos(gaze.ConeDegrees * GameMath.DEG2RAD / 2);
        double maxDistSq = (double)gaze.MaxDistance * gaze.MaxDistance;
        int raysLeft = MaxRaysPerCheck;
        var walkerPos = walker.Pos.XYZ;

        foreach (var watcher in watchers)
        {
            double distSq = watcher.Pos.SquareDistanceTo(walkerPos);
            double dist = Math.Sqrt(distSq);
            if (dist < result.NearestWatcherDistance)
            {
                result.NearestWatcherDistance = dist;
            }
            bool isVictim = victim != null && watcher.EntityId == victim.EntityId;
            if (distSq > maxDistSq || raysLeft <= 0)
            {
                continue;
            }
            if (result.AnyWatched && !isVictim && (victim == null || result.VictimTested))
            {
                // Enough is known; keep counting distances, which cost nothing.
                continue;
            }
            bool sees = Sees(watcher, walker, cosHalf, sight, ref raysLeft);
            if (isVictim)
            {
                result.VictimTested = true;
                result.VictimWatching = sees;
            }
            if (sees)
            {
                result.AnyWatched = true;
                result.Watchers++;
            }
        }
        return result;
    }

    /// <summary>Whether <paramref name="watcher"/> sees <paramref name="walker"/>: inside the cone, and one clear ray.</summary>
    public static bool Sees(Entity watcher, Entity walker, float cosHalfCone, IWorldIntersectionSupplier sight, ref int raysLeft)
    {
        if (watcher.Pos.Dimension != walker.Pos.Dimension)
        {
            return false;
        }
        var eye = EyePos(watcher);
        var view = ViewVector(watcher);
        double height = walker.SelectionBox.Y2 - walker.SelectionBox.Y1;
        double baseY = walker.Pos.Y + walker.SelectionBox.Y1;
        var target = new Vec3d();
        var from = new Vec3d();
        BlockSelection? blockSel = null;
        EntitySelection? entitySel = null;

        foreach (float share in TargetHeights)
        {
            target.Set(walker.Pos.X, baseY + height * share, walker.Pos.Z);
            if (!InCone(eye, view, target, cosHalfCone))
            {
                continue;
            }
            if (raysLeft <= 0)
            {
                return false;
            }
            raysLeft--;
            from.Set(eye);
            blockSel = null;
            entitySel = null;
            watcher.World.RayTraceForSelection(sight, from, target, ref blockSel, ref entitySel, null, NoEntities);
            if (blockSel == null)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>True when <paramref name="point"/> lies inside the cone around <paramref name="view"/> (a unit vector) from <paramref name="eye"/>.</summary>
    public static bool InCone(Vec3d eye, Vec3f view, Vec3d point, float cosHalfCone)
    {
        double dx = point.X - eye.X, dy = point.Y - eye.Y, dz = point.Z - eye.Z;
        double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (len < 0.001)
        {
            return true;
        }
        double dot = (dx * view.X + dy * view.Y + dz * view.Z) / len;
        return dot >= cosHalfCone;
    }

    /// <summary>
    /// Where an entity looks, as a unit vector. A player's pitch is π when level, so the game's view
    /// vector points ahead; a creature's pitch is 0, which turns that vector backwards, so a stand-in
    /// looks along its yaw.
    /// </summary>
    public static Vec3f ViewVector(Entity entity)
    {
        if (entity is EntityPlayer)
        {
            return entity.Pos.GetViewVector();
        }
        float yaw = entity.Pos.Yaw;
        return new Vec3f(MathF.Sin(yaw), 0, MathF.Cos(yaw));
    }

    /// <summary>Where an entity's eyes are, in world coordinates.</summary>
    public static Vec3d EyePos(Entity entity)
    {
        double eyeHeight = entity.LocalEyePos.Y > 0 ? entity.LocalEyePos.Y : entity.Properties.EyeHeight;
        return new Vec3d(entity.Pos.X, entity.Pos.Y + eyeHeight, entity.Pos.Z);
    }

    private static bool NoEntities(Entity entity) => false;
}

/// <summary>The outcome of one gaze check.</summary>
public struct GazeResult
{
    /// <summary>Somebody counting sees the walker.</summary>
    public bool AnyWatched;

    /// <summary>Its victim sees the walker.</summary>
    public bool VictimWatching;

    /// <summary>The victim was among the watchers and was tested.</summary>
    public bool VictimTested;

    /// <summary>Watchers found; a lower bound above 1, since the check stops early.</summary>
    public int Watchers;

    /// <summary>Distance to the nearest counting watcher, whether or not they look; MaxValue for none.</summary>
    public double NearestWatcherDistance;
}

/// <summary>
/// What the gaze rays hit: blocks with a collision box, except glass, and leaves, which have none. So
/// a walker is seen through tall grass, flowers and windows but hides behind leaves and walls.
/// Entities never block the view.
/// </summary>
public sealed class GazeSight : IWorldIntersectionSupplier
{
    private static readonly Cuboidf[] FullBlock = { new Cuboidf(0, 0, 0, 1, 1, 1) };

    private readonly IWorldAccessor world;

    public GazeSight(IWorldAccessor world)
    {
        this.world = world;
    }

    public IBlockAccessor blockAccessor => world.BlockAccessor;

    public Vec3i MapSize => world.BlockAccessor.MapSize;

    public Block GetBlock(BlockPos pos) => world.BlockAccessor.GetBlock(pos);

    public Cuboidf[]? GetBlockIntersectionBoxes(BlockPos pos)
    {
        var block = world.BlockAccessor.GetBlock(pos);
        switch (block.BlockMaterial)
        {
            case EnumBlockMaterial.Glass:
                return null;
            case EnumBlockMaterial.Leaves:
                // Most leaves have no collision box, but a canopy hides what stands behind it.
                return FullBlock;
            default:
                return block.GetCollisionBoxes(world.BlockAccessor, pos);
        }
    }

    public bool IsValidPos(BlockPos pos) => world.BlockAccessor.IsValidPos(pos);

    public Entity[] GetEntitiesAround(Vec3d position, float horRange, float vertRange, ActionConsumable<Entity>? matches = null) => Array.Empty<Entity>();
}
