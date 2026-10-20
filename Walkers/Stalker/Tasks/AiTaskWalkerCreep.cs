using System;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Walkers.Stalker.Tasks;

/// <summary>
/// walkers-creep: keeps the body inside its stage's distance band from the victim (creature bible,
/// section 5), and from stage 2 on moves to a new spot after every look, a little closer each time
/// (<see cref="WalkerBrain.CreepStepBlocks"/>). It walks only while nobody watches it: a frozen body
/// holds still mid-step. It never steps into block light of stalk.avoidBlockLight or brighter, and
/// walks out of it when a light appears. In stage 4 at night it places the true form far off in the
/// open, as a silhouette. It holds still between moves, so the vanilla wander never runs meanwhile.
///
/// JSON: the shared fields of <see cref="WalkerTaskBase"/>, and <c>spotTries</c> (6) candidate spots per pick.
/// </summary>
public class AiTaskWalkerCreep : WalkerTaskBase
{
    public const string Code = "walkers-creep";

    /// <summary>The band has this much slack before it moves back into it.</summary>
    private const float BandSlack = 2;

    /// <summary>Silhouettes stand this deep into their band.</summary>
    private const float SilhouetteBandWidth = 16;

    [JsonProperty]
    public int SpotTries = 6;

    private int looksAtSpot = -1;
    private long nextPickMs;

    public AiTaskWalkerCreep(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
    {
    }

    public override bool ShouldExecute()
    {
        var brain = Brain;
        return brain != null && InMode(brain) && brain.Victim != null && !entity.Swimming && !IsOnCooldown();
    }

    public override void StartExecute()
    {
        base.StartExecute();
        looksAtSpot = Brain?.Looks ?? -1;
        nextPickMs = 0;
    }

    public override bool ContinueExecute(float dt)
    {
        var brain = Brain;
        if (brain == null || !InMode(brain) || brain.Victim == null || entity.Swimming)
        {
            return false;
        }
        if (brain.Frozen)
        {
            Halt();
            return true;
        }

        var victim = brain.Victim;
        var (min, max) = Band(brain);
        bool closerEachLook = brain.Stage >= 2 && brain.Mode != StalkerMode.Silhouette;
        if (closerEachLook)
        {
            max = Math.Max(min, max - brain.Looks * WalkerBrain.CreepStepBlocks);
        }
        double dist = brain.VictimDistance;

        if (Moving)
        {
            if (NavigationEnded)
            {
                Halt();
            }
            return true;
        }

        long now = world.ElapsedMilliseconds;
        if (now < nextPickMs)
        {
            return true;
        }
        // Read the light only when a new spot may be picked: a light read waits on the chunk's lock, which
        // the chunk threads hold often (the soak test's profile found this call to be most of the walkers' cost).
        bool lit = LightRuleApplies(brain) && WalkerSpots.BlockLight(world.BlockAccessor, entity.Pos.XYZ) >= Config.Stalk.AvoidBlockLight;
        bool needMove = lit || dist > max + BandSlack || dist < min - BandSlack || (closerEachLook && brain.Looks != looksAtSpot);
        if (!needMove)
        {
            return true;
        }
        nextPickMs = now + RepathMs;
        var spot = PickSpot(brain, victim.Pos.XYZ, min, max);
        if (spot != null)
        {
            looksAtSpot = brain.Looks;
            GoTo(spot, Speed(false), 1.2f, false, force: true);
        }
        return true;
    }

    private static bool InMode(WalkerBrain brain) => brain.Mode is StalkerMode.Stalk or StalkerMode.Silhouette;

    /// <summary>Before the hunt it keeps out of block light; stalk.avoidBlockLight 32 turns the rule off.</summary>
    private bool LightRuleApplies(WalkerBrain brain) => brain.Stage < StalkerLadder.HuntStage && Config.Stalk.AvoidBlockLight < 32;

    /// <summary>The distance band, in blocks from the victim, for the stage, the time of day and the mode.</summary>
    private (float min, float max) Band(WalkerBrain brain)
    {
        var stalk = Config.Stalk;
        if (brain.Mode == StalkerMode.Silhouette)
        {
            return (stalk.SilhouetteMinDistance, stalk.SilhouetteMinDistance + SilhouetteBandWidth);
        }
        bool night = brain.Night;
        (float min, float max) band = brain.Stage switch
        {
            1 => night ? (24, 40) : (40, 64),
            2 => night ? (16, 36) : (24, 48),
            3 => night ? (16, 32) : (32, 48),
            _ => night ? (16, 32) : (32, 48),
        };
        // It never walks into the distance at which a player makes it slip away.
        band.min = Math.Max(band.min, stalk.VanishApproach + BandSlack);
        band.max = Math.Max(band.max, band.min);
        return band;
    }

    /// <summary>
    /// A spot inside the band, near the line from the victim to the body (so it seems to come from the
    /// same treeline), where it can stand, out of the light and not too deep underground. A silhouette
    /// needs open sky above it. Null when no candidate fits.
    /// </summary>
    private Vec3d? PickSpot(WalkerBrain brain, Vec3d victim, float min, float max)
    {
        var accessor = world.BlockAccessor;
        double bearing = Math.Atan2(entity.Pos.X - victim.X, entity.Pos.Z - victim.Z);
        bool lightRule = LightRuleApplies(brain);
        bool silhouette = brain.Mode == StalkerMode.Silhouette;
        int maxDepth = Config.Director.MaxDepthBelowSurface;
        // Prefer the far part of the band it is allowed: each look brings max in.
        float near = Math.Max(min, max - 6);
        for (int i = 0; i < SpotTries; i++)
        {
            double spread = (i < SpotTries / 2 ? 50 : 110) * GameMath.DEG2RAD;
            double angle = bearing + (rand.NextDouble() - 0.5) * spread;
            double radius = near + rand.NextDouble() * (max - near);
            double x = victim.X + Math.Sin(angle) * radius;
            double z = victim.Z + Math.Cos(angle) * radius;
            double y = entity.Pos.Y + (victim.Y - entity.Pos.Y) * 0.5;
            var spot = WalkerSpots.FindStanding(accessor, x, y, z, 10, BodyHeight);
            if (spot == null || WalkerSpots.TooDeep(accessor, spot, maxDepth))
            {
                continue;
            }
            if (lightRule && WalkerSpots.BlockLight(accessor, spot) >= Config.Stalk.AvoidBlockLight)
            {
                continue;
            }
            if (silhouette && !WalkerSpots.UnderOpenSky(accessor, spot))
            {
                continue;
            }
            return spot;
        }
        return null;
    }
}
