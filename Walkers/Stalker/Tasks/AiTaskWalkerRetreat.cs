using System;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Walkers.Stalker.Tasks;

/// <summary>
/// walkers-retreat: gets the body out of sight, so the director can remove it or forms can shift it
/// once nobody sees it. It walks into cover away from the players who count, under a canopy where it
/// can, and out of block light. The true form runs after a hit that broke off the hunt (health) or
/// while burning, when it heads for water within 16 blocks first. A hurt guise only ever walks
/// (creature bible, section 4). In the defence (stages 3 and 4, just stood up) it backs away slowly,
/// facing whoever hit it, and walkers-lunge strikes back when they close in. It obeys the freeze,
/// except while burning.
///
/// JSON: the shared fields of <see cref="WalkerTaskBase"/>, <c>coverDistance</c> (24) blocks from the
/// nearest watcher, <c>spotTries</c> (6).
/// </summary>
public class AiTaskWalkerRetreat : WalkerTaskBase
{
    public const string Code = "walkers-retreat";

    /// <summary>It looks for water this far around while burning.</summary>
    private const int WaterSearchRadius = 16;

    /// <summary>Arrived and still watched, it looks for better cover after this long.</summary>
    private const long RecheckMs = 4000;

    [JsonProperty]
    public float CoverDistance = 24;

    [JsonProperty]
    public int SpotTries = 6;

    private long nextPickMs;

    public AiTaskWalkerRetreat(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
    {
    }

    public override bool ShouldExecute()
    {
        var brain = Brain;
        return brain != null && InMode(brain) && !entity.Swimming && !IsOnCooldown();
    }

    public override void StartExecute()
    {
        base.StartExecute();
        nextPickMs = 0;
    }

    public override bool ContinueExecute(float dt)
    {
        var brain = Brain;
        if (brain == null || !InMode(brain) || entity.Swimming)
        {
            return false;
        }
        if (brain.Frozen)
        {
            Halt();
            return true;
        }
        long now = world.ElapsedMilliseconds;
        bool burning = entity.IsOnFire;
        bool run = brain.IsTrueForm && (burning || brain.Ai.GetString(StalkerMemory.BreakOff, "") == "health");
        bool defend = brain.Mode == StalkerMode.Defend;

        if (Moving && !NavigationEnded)
        {
            return true;
        }
        if (Moving)
        {
            Halt();
            nextPickMs = Math.Max(nextPickMs, now + RecheckMs);
        }
        // Stay put once out of sight; look again while somebody still sees it, or while it burns.
        if (now < nextPickMs || (!brain.Gaze.AnyWatched && !burning && !defend && nextPickMs != 0))
        {
            return true;
        }
        nextPickMs = now + RepathMs;

        Vec3d? spot = burning ? FindWater() : null;
        var threat = defend ? brain.LastAttacker ?? brain.Victim : NearestThreat(brain);
        spot ??= CoverFrom(brain, threat?.Pos.XYZ ?? brain.Victim?.Pos.XYZ, defend ? 6 : CoverDistance);
        if (spot != null)
        {
            GoTo(spot, Speed(run), 1.2f, run, force: true);
        }
        return true;
    }

    private static bool InMode(WalkerBrain brain) => brain.Mode is StalkerMode.Retreat or StalkerMode.Defend;

    /// <summary>The nearest counting watcher, else the victim.</summary>
    private Entity? NearestThreat(WalkerBrain brain)
    {
        Entity? nearest = brain.Victim;
        double best = nearest == null ? double.MaxValue : nearest.Pos.SquareDistanceTo(entity.Pos);
        foreach (var other in brain.Owner.CountingNear(entity.Pos.XYZ, entity.Pos.Dimension, brain.Config.Gaze.MaxDistance))
        {
            double d = other.Pos.SquareDistanceTo(entity.Pos);
            if (d < best)
            {
                best = d;
                nearest = other;
            }
        }
        return nearest;
    }

    /// <summary>A standing spot about <paramref name="distance"/> blocks beyond the body, away from <paramref name="from"/>: out of the light, under cover where it can.</summary>
    private Vec3d? CoverFrom(WalkerBrain brain, Vec3d? from, float distance)
    {
        var accessor = world.BlockAccessor;
        double bearing = from == null
            ? entity.Pos.Yaw
            : Math.Atan2(entity.Pos.X - from.X, entity.Pos.Z - from.Z);
        bool lightRule = brain.Stage < StalkerLadder.HuntStage && Config.Stalk.AvoidBlockLight < 32;
        Vec3d? fallback = null;
        for (int i = 0; i < SpotTries; i++)
        {
            double angle = bearing + (rand.NextDouble() - 0.5) * (i < SpotTries / 2 ? 60 : 140) * GameMath.DEG2RAD;
            double radius = distance * (0.75 + rand.NextDouble() * 0.5);
            var spot = WalkerSpots.FindStanding(accessor, entity.Pos.X + Math.Sin(angle) * radius, entity.Pos.Y, entity.Pos.Z + Math.Cos(angle) * radius, 8, BodyHeight);
            if (spot == null || WalkerSpots.TooDeep(accessor, spot, Config.Director.MaxDepthBelowSurface))
            {
                continue;
            }
            if (lightRule && WalkerSpots.BlockLight(accessor, spot) >= Config.Stalk.AvoidBlockLight)
            {
                continue;
            }
            if (!WalkerSpots.UnderOpenSky(accessor, spot))
            {
                return spot;
            }
            fallback ??= spot;
        }
        return fallback;
    }

    /// <summary>The nearest water surface within <see cref="WaterSearchRadius"/> blocks, sampled in rings; null if none.</summary>
    private Vec3d? FindWater()
    {
        var accessor = world.BlockAccessor;
        var pos = new BlockPos(0, 0, 0, entity.Pos.Dimension);
        for (int radius = 4; radius <= WaterSearchRadius; radius += 4)
        {
            int samples = radius * 2;
            for (int i = 0; i < samples; i++)
            {
                double angle = i * GameMath.TWOPI / samples;
                pos.Set((int)Math.Floor(entity.Pos.X + Math.Sin(angle) * radius), (int)Math.Floor(entity.Pos.Y), (int)Math.Floor(entity.Pos.Z + Math.Cos(angle) * radius));
                for (int dy = -3; dy <= 2; dy++)
                {
                    pos.Y = (int)Math.Floor(entity.Pos.Y) + dy;
                    var fluid = accessor.GetBlock(pos, BlockLayersAccess.Fluid);
                    if (fluid.IsLiquid() && fluid.LiquidCode == "water")
                    {
                        return new Vec3d(pos.X + 0.5, pos.Y, pos.Z + 0.5);
                    }
                }
            }
        }
        return null;
    }
}
