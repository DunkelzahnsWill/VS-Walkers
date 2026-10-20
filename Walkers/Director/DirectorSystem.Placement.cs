using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Walkers.Director;

// Where a walker appears (creature bible, section 8, "Where an appearance may happen"), and whether
// anyone can see a spot or a body. Placement keeps out of sight of every player except spectators;
// the stand-ins count as players here too.
public partial class DirectorSystem
{
    /// <summary>Entity code of a guise, before its deer type: walkers:skinwalker-animal-{type}.</summary>
    public const string GuiseCodePrefix = "skinwalker-animal-";

    public const string TrueFormCode = "skinwalker-true";

    /// <summary>A body counts as seen when any of these points, as fractions of its height, is in clear view (head, chest and feet).</summary>
    private static readonly float[] SightPoints = { 0.15f, 0.6f, 0.95f };

    /// <summary>Nobody within this many blocks may see an appearance or a removal.</summary>
    private const int SightRange = 96;

    private AABBIntersectionTest? sight;
    private readonly HashSet<string> missingTypes = new();

    /// <summary>
    /// How far from its victim a walker may appear, by stage and time of day (creature bible, section 5).
    /// The first appearance of a haunt uses director.spawnDistanceMin to director.spawnDistanceMax.
    /// </summary>
    private (int Min, int Max) DistanceBand(int stage, bool night, bool first)
    {
        if (first)
        {
            return (config.Director.SpawnDistanceMin, config.Director.SpawnDistanceMax);
        }
        return stage switch
        {
            <= 1 => night ? (24, 40) : (40, 64),
            2 => night ? (24, 36) : (32, 48),
            3 => night ? (16, 32) : (32, 48),
            4 => (32, 48),
            _ => night ? (24, 40) : (32, 48),
        };
    }

    /// <summary>The true form from stage 4 at night; the guise otherwise.</summary>
    private static bool WearsTrueForm(int stage, bool night) => stage >= 4 && night;

    /// <summary>
    /// Tries up to director.spawnTriesPerTick spots around the victim and places the walker's body on the
    /// first that passes every rule. Nothing happens when none does; the next tick tries again.
    /// </summary>
    private void TryPlace(WalkerRecord walker, Candidate victim)
    {
        if (!Exposed(victim))
        {
            return;
        }
        int stage = Math.Max(1, memories[walker.Id].GetInt(WalkerMemory.Stage));
        bool trueForm = WearsTrueForm(stage, victim.Night);
        var code = trueForm
            ? new AssetLocation(Core.WalkersModSystem.ModId, TrueFormCode)
            : new AssetLocation(Core.WalkersModSystem.ModId, GuiseCodePrefix + (walker.GuiseType ?? GuisePicker.Fallback));
        var type = sapi.World.GetEntityType(code);
        if (type == null)
        {
            if (missingTypes.Add(code.ToString()))
            {
                Mod.Logger.Warning("Cannot place walker {0}: the entity type {1} is not loaded", walker.Id, code);
            }
            return;
        }

        var band = DistanceBand(stage, victim.Night, !walker.AppearedThisHaunt);
        float height = type.CollisionBoxSize.Y;
        // Section 8's light rule holds for every appearance; only the Stalker AI's movement ignores light in the hunt.
        int lightLimit = config.Stalk.AvoidBlockLight >= 32 ? int.MaxValue : config.Stalk.AvoidBlockLight;
        var victimPos = victim.Entity.Pos;
        bool wrath = IsWrath(walker);
        while (placementTriesLeft > 0)
        {
            placementTriesLeft--;
            var spot = CandidateSpot(victimPos, band, lightLimit);
            // At Wrath it no longer keeps away from the victim's company; it still appears where nobody can see it.
            if (spot == null || (!wrath && !FarFromBystanders(spot, victimPos.Dimension, victim.Entity)) || CanBeSeen(spot, height, victimPos.Dimension))
            {
                continue;
            }
            Place(walker, victim, type, spot, trueForm);
            return;
        }
    }

    private void Place(WalkerRecord walker, Candidate victim, EntityProperties type, Vec3d spot, bool trueForm)
    {
        var victimPos = victim.Entity.Pos;
        var body = sapi.World.ClassRegistry.CreateEntity(type);
        body.Pos.SetPos(spot);
        body.Pos.Dimension = victimPos.Dimension;
        body.Pos.Pitch = 0;
        // Facing its victim (an entity's yaw is atan2 of x over z).
        body.Pos.Yaw = (float)Math.Atan2(victimPos.X - spot.X, victimPos.Z - spot.Z);
        memories[walker.Id].SetInt(WalkerMemory.Escalation, EscalationLevel(walker));
        AttachBody(walker.Id, body);
        sapi.World.SpawnEntity(body);

        walker.AppearedThisHaunt = true;
        RememberPos(walker, body.Pos);
        Mod.Logger.Notification("Walker {0} appears as {1}, {2:0} blocks from {3}, at stage {4}",
            walker.Id, trueForm ? "the true form" : "a " + walker.GuiseType + " deer", spot.DistanceTo(victimPos.XYZ), victim.Name,
            memories[walker.Id].GetInt(WalkerMemory.Stage));
        Raise(BodyPlaced, Info(walker), body);
    }

    /// <summary>
    /// A random spot in the band around the victim where a creature can stand under open sky (at or above
    /// the rain-map height), on solid ground, out of water, in forest or shrubs, in block light below
    /// <paramref name="lightLimit"/>. Null when this try fails.
    /// </summary>
    private Vec3d? CandidateSpot(EntityPos victimPos, (int Min, int Max) band, int lightLimit)
    {
        var rand = sapi.World.Rand;
        double angle = rand.NextDouble() * GameMath.TWOPI;
        double distance = band.Min + rand.NextDouble() * (band.Max - band.Min);
        var pos = new BlockPos(
            (int)Math.Floor(victimPos.X + Math.Sin(angle) * distance), 0,
            (int)Math.Floor(victimPos.Z + Math.Cos(angle) * distance), victimPos.Dimension);
        var accessor = sapi.World.BlockAccessor;
        if (accessor.GetMapChunkAtBlockPos(pos) == null)
        {
            return null;
        }
        int rain = accessor.GetRainMapHeightAt(pos);
        foreach (int y in new[] { rain + 1, rain })
        {
            pos.Y = y;
            if (y < 1 || y >= accessor.MapSizeY - 2 || accessor.GetChunkAtBlockPos(pos) == null)
            {
                continue;
            }
            if (StandingHeight(accessor, pos) is not { } standY)
            {
                continue;
            }
            if (accessor.GetLightLevel(pos, EnumLightLevelType.OnlyBlockLight) >= lightLimit)
            {
                return null;
            }
            if (config.Director.MinForestDensity > 0 && WoodDensity(pos) < config.Director.MinForestDensity)
            {
                return null;
            }
            return new Vec3d(pos.X + 0.5, standY, pos.Z + 0.5);
        }
        return null;
    }

    /// <summary>Blocks whose collision reaches no higher than this are thin cover a creature stands on (snow layers, loose stones).</summary>
    private const float ThinCover = 0.5f;

    /// <summary>
    /// The height at which a creature stands with its feet in this block, or null: the block is open, or
    /// holds thin cover, and the one below carries it and is natural ground; the two blocks above are open.
    /// </summary>
    private static double? StandingHeight(IBlockAccessor accessor, BlockPos pos)
    {
        var block = accessor.GetBlock(pos);
        if (block.IsLiquid() || accessor.GetBlock(pos, BlockLayersAccess.Fluid).IsLiquid())
        {
            return null;
        }
        float top = TopOf(accessor, block, pos);
        if (top > ThinCover || !IsOpen(accessor, pos.UpCopy()) || !IsOpen(accessor, pos.UpCopy(2)))
        {
            return null;
        }
        var belowPos = pos.DownCopy();
        var below = accessor.GetBlock(belowPos);
        if (below.IsLiquid() || !NaturalGround(below))
        {
            // Never on roofs, walls, tree canopies or logs: only on the land itself.
            return null;
        }
        if (top > 0)
        {
            return pos.Y + top;
        }
        if (below.SideSolid[BlockFacing.UP.Index])
        {
            return pos.Y;
        }
        float belowTop = TopOf(accessor, below, belowPos);
        return belowTop >= ThinCover ? belowPos.Y + belowTop : null;
    }

    /// <summary>Ground a creature walks on in the wild: soil, gravel, sand, stone, snow or ice, not anything built or grown.</summary>
    private static bool NaturalGround(Block block) => block.BlockMaterial is
        EnumBlockMaterial.Soil or EnumBlockMaterial.Gravel or EnumBlockMaterial.Sand or
        EnumBlockMaterial.Stone or EnumBlockMaterial.Snow or EnumBlockMaterial.Ice;

    private static float TopOf(IBlockAccessor accessor, Block block, BlockPos pos)
    {
        var boxes = block.GetCollisionBoxes(accessor, pos);
        return boxes == null || boxes.Length == 0 ? 0 : boxes.Max(b => b.Y2);
    }

    private static bool IsOpen(IBlockAccessor accessor, BlockPos pos)
    {
        var block = accessor.GetBlock(pos);
        return !block.IsLiquid() && TopOf(accessor, block, pos) == 0;
    }

    /// <summary>At least director.isolationRadius from every player (spectators aside) and stand-in who is not the victim.</summary>
    private bool FarFromBystanders(Vec3d spot, int dimension, Entity victim)
    {
        double minSq = (double)config.Director.IsolationRadius * config.Director.IsolationRadius;
        foreach (var viewer in Viewers())
        {
            if (viewer != victim && viewer.Pos.Dimension == dimension && viewer.Pos.SquareDistanceTo(spot) < minSq)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Everyone whose eyes count for placement and removal: online players who are not spectators, and the stand-ins.</summary>
    private IEnumerable<Entity> Viewers()
    {
        foreach (var player in sapi.World.AllOnlinePlayers.OfType<IServerPlayer>())
        {
            if (player.Entity is { Alive: true } entity && player.ConnectionState == EnumClientState.Playing &&
                player.WorldData.CurrentGameMode != EnumGameMode.Spectator)
            {
                yield return entity;
            }
        }
        foreach (var standIn in LoadedStandIns())
        {
            yield return standIn;
        }
    }

    /// <summary>Whether anyone (see <see cref="Viewers"/>) can see this body now.</summary>
    private bool CanBeSeen(Entity body) =>
        CanBeSeen(body.Pos.XYZ, Math.Max(0.5f, body.CollisionBox?.Y2 ?? body.Properties.CollisionBoxSize.Y), body.Pos.Dimension);

    /// <summary>
    /// Whether anyone could see a creature this tall standing here: within gaze.maxDistance, inside their
    /// gaze.coneDegrees view cone, with a clear line from their eyes to its head, chest or feet. Leaves
    /// block the view although nothing collides with them.
    /// </summary>
    private bool CanBeSeen(Vec3d feet, float height, int dimension)
    {
        // At least 96 blocks (creature bible, section 8), whatever gaze.maxDistance says.
        double range = Math.Max(SightRange, config.Gaze.MaxDistance);
        double maxSq = range * range;
        double cosHalfCone = Math.Cos(config.Gaze.ConeDegrees * GameMath.DEG2RAD / 2);
        var chest = feet.AddCopy(0, height * SightPoints[1], 0);
        foreach (var viewer in Viewers())
        {
            var pos = viewer.Pos;
            if (pos.Dimension != dimension || pos.SquareDistanceTo(chest) > maxSq)
            {
                continue;
            }
            var eye = pos.XYZ.Add(0, viewer.LocalEyePos.Y, 0);
            var look = LookVector(viewer);
            var toChest = chest - eye;
            double length = toChest.Length();
            if (length > 0.01 && (look.X * toChest.X + look.Y * toChest.Y + look.Z * toChest.Z) / length < cosHalfCone)
            {
                continue;
            }
            foreach (var fraction in SightPoints)
            {
                if (ClearLine(eye, feet.AddCopy(0, height * fraction, 0)))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Where an entity looks, as a unit vector. A player's Pos.Yaw and Pos.Pitch are their camera
    /// (EntityPos.GetViewVector); any other entity faces atan2 of x over z, the way the game turns creatures.
    /// </summary>
    private static Vec3d LookVector(Entity viewer)
    {
        var pos = viewer.Pos;
        if (viewer is EntityPlayer)
        {
            var view = EntityPos.GetViewVector(pos.Pitch, pos.Yaw);
            return new Vec3d(view.X, view.Y, view.Z);
        }
        return new Vec3d(Math.Sin(pos.Yaw), 0, Math.Cos(pos.Yaw));
    }

    /// <summary>No block between the two points: collision boxes, plus whole leaf blocks, stop the line; glass and fences do not.</summary>
    private bool ClearLine(Vec3d from, Vec3d to)
    {
        sight ??= new AABBIntersectionTest(new SightSupplier(sapi.World.BlockAccessor));
        return sight.GetSelectedBlock(from, to) == null;
    }

    /// <summary>
    /// What a line of sight runs into: each block's collision boxes, and a whole block for leaves, which
    /// have none. Entities never block. A private tester, so the world's own ray tracer is left alone.
    /// </summary>
    private sealed class SightSupplier : IWorldIntersectionSupplier
    {
        private static readonly Cuboidf[] WholeBlock = { new(0, 0, 0, 1, 1, 1) };

        public SightSupplier(IBlockAccessor accessor) => blockAccessor = accessor;

        public IBlockAccessor blockAccessor { get; }

        public Vec3i MapSize => blockAccessor.MapSize;

        public Block GetBlock(BlockPos pos) => blockAccessor.GetBlock(pos);

        public Cuboidf[]? GetBlockIntersectionBoxes(BlockPos pos)
        {
            var block = blockAccessor.GetBlock(pos);
            if (block.BlockMaterial == EnumBlockMaterial.Leaves)
            {
                return WholeBlock;
            }
            return SeeThrough(block) ? null : block.GetCollisionBoxes(blockAccessor, pos);
        }

        /// <summary>
        /// Blocks a player looks through although they collide: glass and panes, and fences of every kind
        /// (wood, roughhewn, bamboo, wattle, iron). Doors and palisades are not, though like these they
        /// absorb no light, so the test goes by material and code rather than by light.
        /// </summary>
        private static bool SeeThrough(Block block) =>
            block.BlockMaterial == EnumBlockMaterial.Glass || (block.Code?.Path.Contains("fence") ?? false);

        public Entity[] GetEntitiesAround(Vec3d position, float horRange, float vertRange, ActionConsumable<Entity>? matches = null) =>
            Array.Empty<Entity>();

        public bool IsValidPos(BlockPos pos) => blockAccessor.IsValidPos(pos);
    }
}
