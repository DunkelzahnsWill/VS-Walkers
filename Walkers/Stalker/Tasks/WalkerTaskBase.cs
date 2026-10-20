using System;
using System.Linq;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Walkers.Config;

namespace Walkers.Stalker.Tasks;

/// <summary>
/// What every walkers- AI task shares: the body's <see cref="WalkerBrain"/>, hits forwarded to it,
/// pathing at most every <see cref="RepathMs"/> with a short straight step when no path is found,
/// and one movement animation at a time.
///
/// JSON fields, besides the vanilla ones (priority, slot, animation, sound, cooldowns):
/// <c>walkAnimation</c> and <c>runAnimation</c> (codes from the entity's client animations),
/// <c>walkAnimationSpeed</c>, <c>runAnimationSpeed</c>, and <c>speedFactor</c>, which scales the
/// configured speed (smaller deer types walk slower).
/// </summary>
public abstract class WalkerTaskBase : AiTaskBase
{
    /// <summary>A new path at most this often (creature bible, section 9: performance budget).</summary>
    public const int RepathMs = 2000;

    /// <summary>A* node budget for a path, searched off the main thread.</summary>
    protected const int SearchDepth = 900;

    [JsonProperty]
    public string? WalkAnimation = "walk";

    [JsonProperty]
    public float WalkAnimationSpeed = 1;

    [JsonProperty]
    public string? RunAnimation = "run";

    [JsonProperty]
    public float RunAnimationSpeed = 1;

    [JsonProperty]
    public float SpeedFactor = 1;

    private WalkerBrain? brain;
    private AnimationMetaData? walkMeta;
    private AnimationMetaData? runMeta;
    private AnimationMetaData? playing;
    private long lastPathMs = -RepathMs;

    /// <summary>The goal of the current navigation; the traverser holds the same instance.</summary>
    protected Vec3d? Goal;

    /// <summary>This task asked the traverser to go somewhere and it has not arrived, given up or been stopped.</summary>
    protected bool Moving;

    /// <summary>The last navigation ended: arrived, stuck, or no path.</summary>
    protected bool Arrived, Stuck, NoPath;

    protected WalkerTaskBase(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
    {
    }

    /// <summary>The body's brain; null for a body the stalker AI does not track (it then does nothing).</summary>
    protected WalkerBrain? Brain => brain ??= StalkerModSystem.BrainOf(entity);

    protected WalkersConfig Config => Brain!.Config;

    protected AnimationMetaData? WalkMeta => walkMeta ??= FindAnimation(WalkAnimation, WalkAnimationSpeed);

    protected AnimationMetaData? RunMeta => runMeta ??= FindAnimation(RunAnimation, RunAnimationSpeed) ?? WalkMeta;

    /// <summary>Configured speed for this body's form and mode, times speedFactor.</summary>
    protected float Speed(bool run)
    {
        var stalk = Config.Stalk;
        float speed = Brain!.IsTrueForm ? (run ? stalk.ChaseSpeed : stalk.StalkSpeed) : (run ? stalk.GuiseFleeSpeed : stalk.GuiseSpeed);
        return speed * SpeedFactor;
    }

    public override void OnEntityHurt(DamageSource source, float damage)
    {
        // Every walkers task forwards the hit; the brain counts each source once.
        Brain?.OnHurt(source, damage);
    }

    public override void FinishExecute(bool cancelled)
    {
        base.FinishExecute(cancelled);
        Halt();
    }

    /// <summary>
    /// Paths to <paramref name="target"/>, at most every <see cref="RepathMs"/> unless forced. Returns
    /// false when it did not ask (too soon). The search runs off the main thread; until it is done
    /// the body coasts and <see cref="Moving"/> is already true.
    /// </summary>
    protected bool GoTo(Vec3d target, float speed, float targetDistance, bool run, bool force = false)
    {
        long now = world.ElapsedMilliseconds;
        if (!force && now - lastPathMs < RepathMs)
        {
            return false;
        }
        lastPathMs = now;
        // A search still pending would ignore the new request and later overwrite it (WaypointsTraverser).
        pathTraverser.Stop();
        Goal = target.Clone();
        Arrived = Stuck = NoPath = false;
        Moving = true;
        float moveSpeed = speed;
        float distance = targetDistance;
        pathTraverser.NavigateTo_Async(Goal, moveSpeed, distance, OnArrived, OnStuck, () => OnNoPath(moveSpeed, distance), SearchDepth, 1);
        PlayMove(run ? RunMeta : WalkMeta);
        return true;
    }

    /// <summary>Walks straight at <paramref name="target"/> without a path search.</summary>
    protected void WalkStraight(Vec3d target, float speed, float targetDistance, bool run)
    {
        pathTraverser.Stop();
        Goal = target.Clone();
        Arrived = Stuck = NoPath = false;
        Moving = true;
        lastPathMs = world.ElapsedMilliseconds;
        pathTraverser.WalkTowards(Goal, speed, targetDistance, OnArrived, OnStuck);
        PlayMove(run ? RunMeta : WalkMeta);
    }

    /// <summary>Stops walking and any movement animation this task started.</summary>
    protected void Halt()
    {
        // Only a path this task started: the stare runs beside the task that walks.
        if (Moving)
        {
            pathTraverser.Stop();
        }
        Moving = false;
        StopMove();
    }

    /// <summary>True when the last navigation has ended one way or another.</summary>
    protected bool NavigationEnded => !Moving || Arrived || Stuck || (NoPath && !pathTraverser.Active) || (pathTraverser.Ready && !pathTraverser.Active);

    private void OnArrived()
    {
        Arrived = true;
        Moving = false;
        StopMove();
    }

    private void OnStuck()
    {
        Stuck = true;
        Moving = false;
        StopMove();
    }

    /// <summary>No path: a short straight step toward the goal when there is ground for it, else stop.</summary>
    private void OnNoPath(float speed, float targetDistance)
    {
        NoPath = true;
        pathTraverser.Stop();
        Moving = false;
        var goal = Goal;
        if (goal == null)
        {
            StopMove();
            return;
        }
        var step = StepTowards(goal, 4);
        if (step != null)
        {
            Goal = step;
            Moving = true;
            pathTraverser.WalkTowards(step, speed, Math.Min(targetDistance, 0.8f), OnArrived, OnStuck);
        }
        else
        {
            StopMove();
        }
    }

    /// <summary>A spot up to <paramref name="blocks"/> blocks from the body along the line to <paramref name="goal"/> where it can stand.</summary>
    protected Vec3d? StepTowards(Vec3d goal, double blocks)
    {
        double dx = goal.X - entity.Pos.X, dz = goal.Z - entity.Pos.Z;
        double len = Math.Sqrt(dx * dx + dz * dz);
        if (len < 0.5)
        {
            return null;
        }
        double k = Math.Min(blocks, len) / len;
        return WalkerSpots.FindStanding(world.BlockAccessor, entity.Pos.X + dx * k, entity.Pos.Y, entity.Pos.Z + dz * k, 3, BodyHeight);
    }

    /// <summary>The body's collision height, for spots it must fit in.</summary>
    protected float BodyHeight => entity.CollisionBox.Y2 - entity.CollisionBox.Y1;

    protected void PlayMove(AnimationMetaData? meta)
    {
        if (ReferenceEquals(playing, meta))
        {
            return;
        }
        StopMove();
        if (meta != null)
        {
            entity.AnimManager.StartAnimation(meta);
            playing = meta;
        }
    }

    protected void StopMove()
    {
        if (playing != null)
        {
            entity.AnimManager.StopAnimation(playing.Code);
            playing = null;
        }
    }

    /// <summary>Turns the body toward a point by at most <paramref name="maxRad"/>; returns the remaining angle.</summary>
    protected float TurnTowards(Vec3d target, float maxRad)
    {
        float want = YawTo(entity.Pos, target);
        float diff = GameMath.AngleRadDistance(entity.Pos.Yaw, want);
        if (Math.Abs(diff) > 0.0001f)
        {
            float yaw = (entity.Pos.Yaw + Math.Clamp(diff, -maxRad, maxRad)) % GameMath.TWOPI;
            entity.Pos.Yaw = yaw < 0 ? yaw + GameMath.TWOPI : yaw;
        }
        return Math.Abs(diff) - Math.Min(Math.Abs(diff), maxRad);
    }

    /// <summary>The yaw that faces from a position toward a point (the game's yaw is atan2 of x over z).</summary>
    public static float YawTo(EntityPos from, Vec3d to) => (float)Math.Atan2(to.X - from.X, to.Z - from.Z);

    private AnimationMetaData? FindAnimation(string? code, float speed)
    {
        if (string.IsNullOrEmpty(code))
        {
            return null;
        }
        code = code.ToLowerInvariant();
        var meta = entity.Properties.Client?.Animations?.FirstOrDefault(a => a.Code == code);
        if (meta == null)
        {
            return null;
        }
        meta = meta.Clone();
        meta.AnimationSpeed = speed;
        return meta;
    }
}
