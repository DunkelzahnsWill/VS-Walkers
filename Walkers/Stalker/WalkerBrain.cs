using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Walkers.Config;
using Walkers.Director;
using Walkers.Stalker.Tasks;

namespace Walkers.Stalker;

/// <summary>What a walker's AI is doing; the tasks run the matching behaviour.</summary>
public enum StalkerMode
{
    /// <summary>No victim: the vanilla tasks (wander, idle) run.</summary>
    Idle,
    /// <summary>Keeps to its stage's distance band, stares, creeps closer while unobserved (guise; true form in stage 4).</summary>
    Stalk,
    /// <summary>Stage 4 at night: the true form standing still in the open, far off.</summary>
    Silhouette,
    /// <summary>Stage 5: the true form closes in while unobserved and lunges.</summary>
    Hunt,
    /// <summary>Stages 3 and 4, just stood up after a player's hit: strikes back and backs into cover.</summary>
    Defend,
    /// <summary>Walks or runs into cover (a break-off, a withdraw, a hurt guise walking off); runs for water while burning.</summary>
    Retreat,
}

/// <summary>
/// The server-side state of one walker body: its victim, the latest gaze check, its mode, the freeze
/// and the slip-away. <see cref="StalkerModSystem"/> keeps one per loaded body and ticks it; the AI
/// tasks read it. Anything that must survive a shift or a restart is in the memory tree
/// (<see cref="StalkerMemory"/>); the rest belongs to this body only, so a new body is a new appearance.
/// </summary>
public sealed class WalkerBrain
{
    /// <summary>A player's hit frees it from any gaze for this long (creature bible, section 5).</summary>
    public const int HitBreaksFreezeMs = 3000;

    /// <summary>No removal within this long of a player's hit (creature bible, section 4).</summary>
    public const int NoRemovalAfterHitMs = 20000;

    /// <summary>A victim's look of this long is a sighting.</summary>
    private const double SightingSeconds = 1;

    /// <summary>Unwatched this long, a look has ended: the next look finds it a little closer.</summary>
    private const double LookEndsSeconds = 1.5;

    /// <summary>Blocks the creep band's target moves closer after each look.</summary>
    public const float CreepStepBlocks = 3;

    public readonly EntityAgent Body;
    private readonly StalkerModSystem system;
    private readonly ICoreServerAPI api;
    private readonly GazeSight sight;
    private readonly List<Entity> watcherBuffer = new();

    private long lastGazeMs;
    private long nextGazeMs;
    private double victimLookSeconds;
    private double victimUnseenSeconds;
    private bool sightingCounted;
    private double unwatchedSeconds;
    private bool lookUnderway;
    private DamageSource? lastHurtSource;
    private long lastOwnVictimSearchMs;
    private bool silhouetteCounted;
    private bool companyNow;
    private double aloneSeconds;

    /// <summary>The latest gaze check.</summary>
    public GazeResult Gaze;

    /// <summary>The victim at the latest check: a player's entity or a stand-in; null when none is near or online.</summary>
    public Entity? Victim { get; private set; }

    /// <summary>Horizontal distance to the victim at the latest check; MaxValue without one.</summary>
    public double VictimDistance { get; private set; } = double.MaxValue;

    /// <summary>What the AI does now.</summary>
    public StalkerMode Mode { get; private set; } = StalkerMode.Idle;

    /// <summary>Watched and held still: no task may walk or turn it (creature bible, rule 3).</summary>
    public bool Frozen { get; private set; }

    /// <summary>Real seconds anyone has watched this body, in total.</summary>
    public double WatchedSeconds { get; private set; }

    /// <summary>Why it is marked to slip away (removed at the first check nobody sees it), or null.</summary>
    public string? SlipAwayReason { get; private set; }

    /// <summary>Looks this body has survived; each one sets its creep target a little closer.</summary>
    public int Looks { get; private set; }

    /// <summary>The sun is below the horizon at the body.</summary>
    public bool Night { get; private set; }

    /// <summary>A counting player other than the victim is near the victim or the body (director.isolationRadius).</summary>
    public bool Company => companyNow;

    /// <summary>World.ElapsedMilliseconds until which the hunt keeps its distance after a lunge.</summary>
    public long BackOffUntilMs { get; set; }

    /// <summary>World.ElapsedMilliseconds of the hunt's last failed path to the victim (a closed door, a wall); 0 if none.</summary>
    public long NoPathToVictimMs { get; set; }

    /// <summary>World.ElapsedMilliseconds at which walkers-doorway last saw a door between the body and the victim; the hunt never walks through one.</summary>
    public long DoorAheadMs { get; set; }

    /// <summary>The body holds a doorway (walkers-doorway): a lunge from there carries it one step in at most.</summary>
    public bool HoldingDoorway { get; set; }

    /// <summary>The body's walkers-doorway task while it has a door in hand; the mod system lets go of a stale one.</summary>
    public AiTaskWalkerDoorway? Doorway { get; set; }

    /// <summary>Real milliseconds since the victim was last seen asleep in a bed; long.MaxValue if never.</summary>
    public long MsSinceVictimAsleep => lastVictimAsleepMs == 0 ? long.MaxValue : api.World.ElapsedMilliseconds - lastVictimAsleepMs;

    private long lastVictimAsleepMs;

    public WalkerBrain(EntityAgent body, StalkerModSystem system, ICoreServerAPI api)
    {
        Body = body;
        this.system = system;
        this.api = api;
        sight = new GazeSight(api.World);
        // Spread the checks of several walkers over the interval.
        nextGazeMs = api.World.ElapsedMilliseconds + (body.EntityId % 10) * 50;
    }

    public WalkersConfig Config => system.Config;

    /// <summary>The mod system that keeps this brain.</summary>
    public StalkerModSystem Owner => system;

    public bool IsTrueForm => Body.Code.Path.StartsWith(StalkerModSystem.TrueFormCode, StringComparison.Ordinal);

    public ITreeAttribute Memory => StalkerMemory.OrAdd(Body);

    public ITreeAttribute Ai => StalkerMemory.Ai(Memory);

    public int Stage => Memory.GetInt(WalkerMemory.Stage);

    /// <summary>Real milliseconds since a player last hit it (kept in the memory, so it survives a shift); long.MaxValue if never.</summary>
    public long MsSincePlayerHit
    {
        get
        {
            long at = Ai.GetLong(StalkerMemory.LastPlayerHitMs, -1);
            long now = api.World.ElapsedMilliseconds;
            // A time ahead of the clock comes from before a restart; treat it as long ago.
            return at < 0 || at > now ? long.MaxValue : now - at;
        }
    }

    /// <summary>The entity that hit it last (a player), if it is still around.</summary>
    public Entity? LastAttacker
    {
        get
        {
            long id = Ai.GetLong(StalkerMemory.LastAttacker);
            return id == 0 ? null : api.World.GetEntityById(id);
        }
    }

    /// <summary>Called every 100 ms by the mod system.</summary>
    public void Tick(long nowMs)
    {
        if (!Body.Alive)
        {
            return;
        }
        if (nowMs >= nextGazeMs)
        {
            nextGazeMs = nowMs + (long)(1000 / Math.Max(0.5f, Config.Gaze.ChecksPerSecond));
            RunGazeCheck();
            UpdateMode();
            MarkSlipAway();
            if (TrySlipAway())
            {
                return;
            }
        }
        UpdateFreeze(nowMs);
    }

    /// <summary>One gaze check: who watches, how far the victim is, and the timers that hang on it.</summary>
    public void RunGazeCheck()
    {
        long now = api.World.ElapsedMilliseconds;
        double dt = lastGazeMs == 0 ? 0 : Math.Clamp((now - lastGazeMs) / 1000.0, 0, 2);
        lastGazeMs = now;

        Victim = ResolveVictim(now);
        VictimDistance = Victim == null ? double.MaxValue : HorizontalDistance(Body.Pos, Victim.Pos);
        if (StalkerModSystem.IsAsleep(Victim))
        {
            lastVictimAsleepMs = now;
        }
        GatherWatchers(Victim, Config.Gaze.MaxDistance);
        Gaze = WalkerGaze.Check(Body, Victim, watcherBuffer, Config.Gaze, sight);
        Night = StalkerModSystem.IsNight(api.World, Body.Pos.XYZ);
        UpdateCompany(dt);

        if (Gaze.AnyWatched)
        {
            WatchedSeconds += dt;
            unwatchedSeconds = 0;
            lookUnderway = true;
            Ai.SetLong(StalkerMemory.LastSeenMs, now);
        }
        else
        {
            unwatchedSeconds += dt;
            if (lookUnderway && unwatchedSeconds >= LookEndsSeconds)
            {
                lookUnderway = false;
                Looks++;
            }
        }

        if (Gaze.VictimWatching)
        {
            victimLookSeconds += dt;
            victimUnseenSeconds = 0;
            if (!sightingCounted && victimLookSeconds >= SightingSeconds && Stage > 0)
            {
                sightingCounted = true;
                StalkerLadder.OnSighting(Memory);
            }
        }
        else
        {
            victimUnseenSeconds += dt;
            if (victimUnseenSeconds >= LookEndsSeconds)
            {
                victimLookSeconds = 0;
                sightingCounted = false;
            }
        }

        var ai = Ai;
        ai.SetBool(StalkerMemory.Watched, Gaze.AnyWatched);
        ai.SetBool(StalkerMemory.VictimWatching, Gaze.VictimWatching);
        ai.SetInt(StalkerMemory.Watchers, Gaze.Watchers);
    }

    /// <summary>A hit reached the body; every walkers task forwards it, so the same source is counted once.</summary>
    public void OnHurt(DamageSource source, float damage)
    {
        if (ReferenceEquals(source, lastHurtSource))
        {
            return;
        }
        lastHurtSource = source;
        var cause = source.GetCauseEntity();
        if (cause is not EntityPlayer)
        {
            return;
        }
        var ai = Ai;
        long now = api.World.ElapsedMilliseconds;
        ai.SetLong(StalkerMemory.LastPlayerHitMs, now);
        ai.SetLong(StalkerMemory.LastAttacker, cause.EntityId);
        ai.SetBool(StalkerMemory.Provoked, true);
        Frozen = false;

        var victim = Victim ?? ResolveVictim(now);
        bool byVictim = victim != null && cause.EntityId == victim.EntityId;
        if (byVictim)
        {
            StalkerLadder.OnHit(Memory);
            // With a director, Forms reports the victim's hit on the guise through it (IWalkerDirector.ReportHarm).
            if (!IsTrueForm && !system.IsDirectorBody(Body))
            {
                system.Ladder.OnHarm(Memory);
            }
        }
        // Stages 1 and 2: a hurt doe neither shifts nor runs; it walks into the trees and is gone the
        // first moment nobody sees it (after the 20 s guard).
        if (!IsTrueForm && Stage <= 2)
        {
            SlipAwayReason ??= "hurt";
        }
    }

    /// <summary>The lunge landed on the victim.</summary>
    public void OnStruckVictim()
    {
        StalkerLadder.OnHit(Memory);
        Ai.SetDouble(StalkerMemory.LastHitHours, api.World.Calendar.TotalHours);
    }

    /// <summary>Marks this body to slip away at the first check nobody sees it.</summary>
    public void MarkSlipAway(string reason)
    {
        SlipAwayReason ??= reason;
    }

    private void UpdateMode()
    {
        var memory = Memory;
        var ai = Ai;
        int stage = memory.GetInt(WalkerMemory.Stage);
        string breakOff = ai.GetString(StalkerMemory.BreakOff, "");
        StalkerMode mode;

        if (!IsTrueForm && breakOff.Length > 0)
        {
            // Forms copies the whole memory into the guise it shifts back to.
            ai.SetString(StalkerMemory.BreakOff, breakOff = "");
        }

        if (!Config.Enabled)
        {
            // The master switch: the body is left to the vanilla tasks.
            mode = StalkerMode.Idle;
        }
        else if (memory.GetBool(WalkerMemory.Withdraw))
        {
            mode = StalkerMode.Retreat;
        }
        else if (Victim == null || stage <= 0)
        {
            mode = StalkerMode.Idle;
        }
        else if (!IsTrueForm)
        {
            mode = SlipAwayReason == "hurt" ? StalkerMode.Retreat : StalkerMode.Stalk;
        }
        else
        {
            mode = TrueFormMode(memory, ai, stage, ref breakOff);
        }

        if (mode != Mode)
        {
            Mode = mode;
            if (mode == StalkerMode.Silhouette)
            {
                silhouetteCounted = false;
            }
        }
        ai.SetString(StalkerMemory.Mode, ModeName(mode));
    }

    private StalkerMode TrueFormMode(ITreeAttribute memory, ITreeAttribute ai, int stage, ref string breakOff)
    {
        bool hurtLately = MsSincePlayerHit < NoRemovalAfterHitMs;
        double now = api.World.Calendar.TotalHours;

        if (Body.IsOnFire)
        {
            SetBreakOff(ai, ref breakOff, "fire");
            if (stage >= StalkerLadder.HuntStage)
            {
                system.Ladder.OnDrivenOff(memory, now);
            }
            return StalkerMode.Retreat;
        }
        if (stage >= StalkerLadder.HuntStage)
        {
            var health = Body.GetBehavior<EntityBehaviorHealth>();
            if (health != null && health.MaxHealth > 0 && health.Health / health.MaxHealth < Config.Forms.RetreatHealth)
            {
                SetBreakOff(ai, ref breakOff, "health");
                system.Ladder.OnDrivenOff(memory, now);
                return StalkerMode.Retreat;
            }
            if (ai.GetBool(StalkerMemory.HuntOver))
            {
                SetBreakOff(ai, ref breakOff, "huntover");
                return StalkerMode.Retreat;
            }
            // Break-offs that pass on their own: night comes back, the company leaves.
            if ((breakOff == "dawn" && Night) || (breakOff == "company" && aloneSeconds >= 10))
            {
                ai.SetString(StalkerMemory.BreakOff, breakOff = "");
            }
            if (breakOff.Length == 0 && companyNow)
            {
                SetBreakOff(ai, ref breakOff, "company");
            }
            if (breakOff.Length == 0 && !Night && !hurtLately)
            {
                SetBreakOff(ai, ref breakOff, "dawn");
            }
            return breakOff.Length > 0 ? StalkerMode.Retreat : StalkerMode.Hunt;
        }
        if (hurtLately && stage >= 3)
        {
            SetBreakOff(ai, ref breakOff, "stoodup");
            return StalkerMode.Defend;
        }
        if (breakOff.Length > 0)
        {
            return StalkerMode.Retreat;
        }
        if (stage == 4 && Night)
        {
            var stalk = Config.Stalk;
            // A silhouette already counted tonight keeps its body; past the night's cap a new one goes back to cover.
            if (stalk.Silhouettes && (silhouetteCounted && Mode == StalkerMode.Silhouette || ai.GetInt(StalkerMemory.SilhouettesTonight) < stalk.SilhouettesPerNight))
            {
                return StalkerMode.Silhouette;
            }
            if (!stalk.Silhouettes)
            {
                return StalkerMode.Stalk;
            }
        }
        // A true form outside stage 4's night has nothing to do in this body: back to cover, and Forms
        // shifts it to the guise once nobody sees it.
        SetBreakOff(ai, ref breakOff, "withdraw");
        return StalkerMode.Retreat;
    }

    private static void SetBreakOff(ITreeAttribute ai, ref string current, string reason)
    {
        if (current.Length == 0)
        {
            current = reason;
            ai.SetString(StalkerMemory.BreakOff, reason);
        }
    }

    private void MarkSlipAway()
    {
        if (Mode != StalkerMode.Stalk && Mode != StalkerMode.Silhouette)
        {
            return;
        }
        int stage = Stage;
        var stalk = Config.Stalk;
        if (Mode == StalkerMode.Silhouette)
        {
            if (Gaze.AnyWatched && !silhouetteCounted)
            {
                silhouetteCounted = true;
                Ai.SetInt(StalkerMemory.SilhouettesTonight, Ai.GetInt(StalkerMemory.SilhouettesTonight) + 1);
            }
            if (WatchedSeconds >= stalk.SilhouetteWatchedSeconds)
            {
                SlipAwayReason ??= "silhouette seen";
            }
        }
        else if (stage >= 2 && WatchedSeconds >= stalk.VanishWatchedSeconds)
        {
            SlipAwayReason ??= "watched";
        }
        if (Gaze.NearestWatcherDistance <= stalk.VanishApproach)
        {
            SlipAwayReason ??= "approached";
        }
    }

    /// <summary>Removes the body when it is marked, nobody sees it, it is not burning and no player hit it lately.</summary>
    private bool TrySlipAway()
    {
        if (SlipAwayReason == null || Gaze.AnyWatched || Body.IsOnFire || MsSincePlayerHit < NoRemovalAfterHitMs)
        {
            return false;
        }
        Ai.SetString(StalkerMemory.Mode, "slipaway");
        system.Logger.Debug("Walker {0} ({1} #{2}) slipped away: {3}", Memory.GetInt(WalkerMemory.Id), Body.Code.Path, Body.EntityId, SlipAwayReason);
        Body.Die(EnumDespawnReason.Expire);
        return true;
    }

    private void UpdateFreeze(long nowMs)
    {
        // A burning body runs for water whoever watches, and a swimming one keeps swimming for the shore.
        bool frozen = Config.Gaze.Freeze && Gaze.AnyWatched && MsSincePlayerHit >= HitBreaksFreezeMs && !Body.IsOnFire && !Body.Swimming;
        if (frozen && Mode == StalkerMode.Hunt && VictimDistance <= Config.Gaze.LungeRange)
        {
            frozen = false;
        }
        if (frozen && !Frozen)
        {
            Frozen = true;
            system.OnFreeze(this);
        }
        Frozen = frozen;
    }

    private void UpdateCompany(double dt)
    {
        companyNow = false;
        var victim = Victim;
        if (victim != null)
        {
            double radius = Config.Director.IsolationRadius;
            foreach (var other in system.CountingNear(victim.Pos.XYZ, victim.Pos.Dimension, radius))
            {
                if (other.EntityId != victim.EntityId)
                {
                    companyNow = true;
                    break;
                }
            }
            if (!companyNow)
            {
                foreach (var watcher in watcherBuffer)
                {
                    if (watcher.EntityId != victim.EntityId && watcher.Pos.DistanceTo(Body.Pos) <= radius)
                    {
                        companyNow = true;
                        break;
                    }
                }
            }
        }
        aloneSeconds = companyNow ? 0 : aloneSeconds + dt;
    }

    /// <summary>
    /// The victim: the director's for a body it placed; otherwise the stalker AI's own pick, the nearest
    /// counting player within <see cref="StalkerModSystem.OwnVictimRange"/> blocks.
    /// </summary>
    private Entity? ResolveVictim(long nowMs)
    {
        var memory = Memory;
        if (system.Director != null && system.Director.TryGetWalkerId(Body, out int walkerId))
        {
            return system.Director.GetVictim(walkerId);
        }
        if (memory.HasAttribute(WalkerMemory.Id))
        {
            long id = memory.GetLong(WalkerMemory.VictimEntity);
            var placed = id == 0 ? null : api.World.GetEntityById(id);
            return placed?.Alive == true ? placed : null;
        }
        return system.ResolveOwnVictim(this, memory, nowMs, ref lastOwnVictimSearchMs);
    }

    private void GatherWatchers(Entity? victim, double range)
    {
        watcherBuffer.Clear();
        foreach (var entity in system.CountingNear(Body.Pos.XYZ, Body.Pos.Dimension, range))
        {
            watcherBuffer.Add(entity);
        }
        if (victim == null)
        {
            return;
        }
        int at = watcherBuffer.FindIndex(e => e.EntityId == victim.EntityId);
        if (at > 0)
        {
            watcherBuffer.RemoveAt(at);
            watcherBuffer.Insert(0, victim);
        }
        else if (at < 0 && victim is not EntityPlayer && victim.Pos.Dimension == Body.Pos.Dimension)
        {
            // A stand-in victim watches too, even without a director to list it.
            watcherBuffer.Insert(0, victim);
        }
    }

    public static double HorizontalDistance(EntityPos a, EntityPos b)
    {
        double dx = a.X - b.X, dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    public static string ModeName(StalkerMode mode) => mode switch
    {
        StalkerMode.Idle => "idle",
        StalkerMode.Stalk => "stalk",
        StalkerMode.Silhouette => "silhouette",
        StalkerMode.Hunt => "hunt",
        StalkerMode.Defend => "defend",
        _ => "retreat",
    };
}
