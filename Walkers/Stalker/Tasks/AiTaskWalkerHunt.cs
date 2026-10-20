using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Walkers.Stalker.Tasks;

/// <summary>
/// walkers-hunt, stage 5 (true form only): closes in on the victim while nobody watches, at
/// stalk.stalkSpeed, and runs at stalk.chaseSpeed within <see cref="ChaseWithin"/> blocks.
/// walkers-lunge takes over within gaze.lungeRange. After a lunge it keeps its distance for a moment.
/// It holds where it is while the victim sleeps (it never attacks a sleeping victim), while the
/// victim is deeper underground than director.maxDepthBelowSurface, and while the victim swims (it
/// never swims). A failed path is noted for walkers-doorway, and it halts at a door between it and the
/// victim rather than walk through it.
/// </summary>
public class AiTaskWalkerHunt : WalkerTaskBase
{
    public const string Code = "walkers-hunt";

    /// <summary>Within this many blocks it runs.</summary>
    public const float ChaseWithin = 16;

    /// <summary>Re-paths once the victim is this far from the path's end.</summary>
    private const double RepathMoved = 3;

    /// <summary>How far it backs off after a lunge.</summary>
    private const double BackOffBlocks = 7;

    /// <summary>It holds this long after walkers-doorway last saw a door between it and the victim.</summary>
    private const long DoorHoldMs = 1500;

    /// <summary>A path search through an open door fails about a third of the time (AStar.cs:73): tries before a failure counts.</summary>
    private const int PathTries = 3;

    private bool running;
    private int failedSearches;

    public AiTaskWalkerHunt(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
    {
    }

    public override bool ShouldExecute()
    {
        var brain = Brain;
        return brain != null && brain.IsTrueForm && brain.Mode == StalkerMode.Hunt && brain.Victim != null && !entity.Swimming && !IsOnCooldown();
    }

    public override bool ContinueExecute(float dt)
    {
        var brain = Brain;
        if (brain == null || brain.Mode != StalkerMode.Hunt || brain.Victim == null || entity.Swimming)
        {
            return false;
        }
        var victim = brain.Victim;
        if (brain.Frozen || StalkerModSystem.IsAsleep(victim) || victim.Swimming ||
            WalkerSpots.TooDeep(world.BlockAccessor, victim.Pos.XYZ, Config.Director.MaxDepthBelowSurface))
        {
            Halt();
            return true;
        }

        long now = world.ElapsedMilliseconds;
        if (now - brain.DoorAheadMs < DoorHoldMs)
        {
            // A door stands between it and the victim: walkers-doorway decides; it never walks in.
            Halt();
            return true;
        }
        double dist = brain.VictimDistance;
        if (now < brain.BackOffUntilMs)
        {
            if (dist < BackOffBlocks && (!Moving || NavigationEnded))
            {
                var away = AwayFrom(victim.Pos.XYZ, BackOffBlocks + 1);
                if (away != null)
                {
                    GoTo(away, Speed(false), 1f, false);
                }
            }
            return true;
        }

        bool run = dist <= ChaseWithin;
        bool moved = Goal == null || Goal.HorizontalSquareDistanceTo(victim.Pos.X, victim.Pos.Z) >= RepathMoved * RepathMoved;
        if (!Moving || NavigationEnded || moved || run != running)
        {
            if (GoTo(victim.Pos.XYZ, Speed(run), 1.5f, run))
            {
                running = run;
            }
        }
        else if (pathTraverser.Active && ReferenceEquals(pathTraverser.CurrentTarget, Goal))
        {
            // Small moves of the victim: follow the end of the path, as the vanilla seek does.
            Goal!.Set(victim.Pos.X, victim.Pos.Y, victim.Pos.Z);
        }
        if (NoPath)
        {
            if (failedSearches < PathTries - 1)
            {
                // Search again at once before the failure counts (GoTo clears NoPath).
                failedSearches++;
                GoTo(victim.Pos.XYZ, Speed(run), 1.5f, run, force: true);
            }
            else
            {
                // Given up: walkers-doorway looks for a door; the usual re-path every 2 s goes on.
                failedSearches = PathTries;
                brain.NoPathToVictimMs = now;
            }
        }
        else if (Arrived || pathTraverser.Active)
        {
            failedSearches = 0;
        }
        return true;
    }

    public override void FinishExecute(bool cancelled)
    {
        base.FinishExecute(cancelled);
        running = false;
        failedSearches = 0;
    }

    /// <summary>A standing spot about <paramref name="blocks"/> blocks from the victim, on the body's side.</summary>
    private Vec3d? AwayFrom(Vec3d from, double blocks)
    {
        double dx = entity.Pos.X - from.X, dz = entity.Pos.Z - from.Z;
        double len = Math.Sqrt(dx * dx + dz * dz);
        if (len < 0.1)
        {
            double yaw = entity.Pos.Yaw + GameMath.PI;
            dx = Math.Sin(yaw);
            dz = Math.Cos(yaw);
            len = 1;
        }
        return WalkerSpots.FindStanding(world.BlockAccessor, from.X + dx / len * blocks, entity.Pos.Y, from.Z + dz / len * blocks, 4, BodyHeight);
    }
}
