using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Walkers.Config;
using Walkers.Director;

namespace Walkers.Stalker;

/// <summary>
/// The behaviour ladder (creature bible, section 5): five stages against one victim, from a doe
/// glimpsed at the treeline to the hunt. The director calls <see cref="Update"/> every tick for each
/// haunting walker, with or without a body; bodies the director did not place are driven by
/// <see cref="StalkerModSystem"/> with the same calls.
///
/// The stage is the director's top-level <see cref="WalkerMemory.Stage"/>; between <see cref="Begin"/>
/// and the end of a haunt only this class writes it. Its clocks and counters live in the stalker AI's
/// subtree (<see cref="StalkerMemory"/>). All state is in the memory tree, so one instance serves
/// every walker and a restart loses nothing.
/// </summary>
public sealed class StalkerLadder : IWalkerLadder
{
    /// <summary>The highest stage.</summary>
    public const int HuntStage = 5;

    /// <summary>Hours of the clock a single update may add while the victim is awake. A longer gap means
    /// the haunt was not being updated (a pause), which must not count.</summary>
    private const double MaxAwakeStepHours = 1;

    /// <summary>Hours a single update may add across a slept night, which runs up to 285 times faster.</summary>
    private const double MaxAsleepStepHours = 14;

    /// <summary>Lately spent this long with company, the victim drops back to ladder.groupStageCap.
    /// Company decays at the same rate while they are alone, so a friend passing by does not count.</summary>
    private const double CompanyFallbackHours = 1;

    /// <summary>Below this temporal stability the victim climbs at ladder.lowStabilityPace.</summary>
    private const double LowStability = 0.5;

    private readonly WalkersConfig config;
    private readonly ILogger logger;
    private readonly Func<bool> hostilityOff;

    public StalkerLadder(WalkersConfig config, ILogger logger, Func<bool> hostilityOff)
    {
        this.config = config;
        this.logger = logger;
        this.hostilityOff = hostilityOff;
    }

    public void Begin(ITreeAttribute memory, int stage, double nowTotalHours)
    {
        var ai = StalkerMemory.Ai(memory);
        ai.SetDouble(StalkerMemory.HauntBegan, nowTotalHours);
        ai.SetDouble(StalkerMemory.LadderUpdated, nowTotalHours);
        ai.SetBool(StalkerMemory.LadderAsleep, false);
        ai.SetDouble(StalkerMemory.NightStart, -1);
        ai.SetDouble(StalkerMemory.RestUntil, 0);
        ai.SetDouble(StalkerMemory.CompanyHours, 0);
        ai.SetBool(StalkerMemory.HarmBonusUsed, false);
        ai.SetBool(StalkerMemory.Provoked, false);
        ai.SetBool(StalkerMemory.HuntOver, false);
        ai.SetString(StalkerMemory.BreakOff, "");
        ai.SetInt(StalkerMemory.HuntNights, 0);
        ai.SetInt(StalkerMemory.SilhouettesTonight, 0);
        SetStage(memory, Math.Clamp(stage, 1, HuntStage), nowTotalHours, "haunt began");
    }

    public LadderOutcome Update(ITreeAttribute memory, LadderInput input)
    {
        var ai = StalkerMemory.Ai(memory);
        double now = input.NowTotalHours;
        int stage = memory.GetInt(WalkerMemory.Stage);
        double last = ai.GetDouble(StalkerMemory.LadderUpdated, now);
        bool wasAsleep = ai.GetBool(StalkerMemory.LadderAsleep);
        ai.SetDouble(StalkerMemory.LadderUpdated, now);
        ai.SetBool(StalkerMemory.LadderAsleep, input.Asleep);
        if (stage <= 0)
        {
            return LadderOutcome.None;
        }
        if (ai.GetBool(StalkerMemory.HuntOver))
        {
            return LadderOutcome.HuntEnded;
        }

        double maxStep = wasAsleep || input.Asleep ? MaxAsleepStepHours : MaxAwakeStepHours;
        double dt = Math.Clamp(now - last, 0, maxStep);
        // The clock runs while the victim is exposed (online, eligible, on the surface, near forest),
        // and through a night they sleep, so a slept night counts as the bible says.
        bool clock = input.Exposed || input.Asleep;

        if (TrackNight(memory, ai, stage, input, dt, clock))
        {
            return LadderOutcome.HuntEnded;
        }
        TrackCompany(ai, input, dt, clock);

        int cap = config.Ladder.GroupStageCap;
        if (stage > cap && ai.GetDouble(StalkerMemory.CompanyHours) >= CompanyFallbackHours)
        {
            SetStage(memory, cap, now, "the victim is with others");
            return LadderOutcome.None;
        }

        bool resting = now < ai.GetDouble(StalkerMemory.RestUntil);
        if (!clock || resting)
        {
            return LadderOutcome.None;
        }
        ai.SetDouble(StalkerMemory.StageHours, ai.GetDouble(StalkerMemory.StageHours) + dt * Pace(memory, input));

        int next = stage + 1;
        if (stage < HuntStage && ReadyToClimb(memory, ai, stage, input) && (next <= cap || input.Alone))
        {
            SetStage(memory, next, now, "climbed");
        }
        return LadderOutcome.None;
    }

    public void OnAct(ITreeAttribute memory, string actCode)
    {
        var ai = StalkerMemory.Ai(memory);
        ai.SetInt(StalkerMemory.StageActs, ai.GetInt(StalkerMemory.StageActs) + 1);
    }

    public void OnHarm(ITreeAttribute memory)
    {
        var ai = StalkerMemory.Ai(memory);
        ai.SetBool(StalkerMemory.Provoked, true);
        if (ai.GetBool(StalkerMemory.HarmBonusUsed))
        {
            return;
        }
        ai.SetBool(StalkerMemory.HarmBonusUsed, true);
        int stage = memory.GetInt(WalkerMemory.Stage);
        int bonus = config.Ladder.HarmGuiseBonus;
        if (stage >= 1 && stage < 4 && bonus > 0)
        {
            SetStage(memory, Math.Min(4, stage + bonus), ai.GetDouble(StalkerMemory.LadderUpdated), "its victim hurt the guise");
        }
    }

    /// <summary>The victim looked at it for a second or more: one sighting.</summary>
    public static void OnSighting(ITreeAttribute memory)
    {
        var ai = StalkerMemory.Ai(memory);
        ai.SetInt(StalkerMemory.Sightings, ai.GetInt(StalkerMemory.Sightings) + 1);
    }

    /// <summary>A hit either way between the walker and its victim; a hunt night with one does not count toward ladder.huntMaxNights.</summary>
    public static void OnHit(ITreeAttribute memory)
    {
        StalkerMemory.Ai(memory).SetBool(StalkerMemory.NightHit, true);
    }

    /// <summary>Driven off in the hunt (hurt below forms.retreatHealth, or set alight): back to ladder.drivenOffStage with a rest.</summary>
    public void OnDrivenOff(ITreeAttribute memory, double nowTotalHours)
    {
        if (memory.GetInt(WalkerMemory.Stage) < HuntStage)
        {
            return;
        }
        var ai = StalkerMemory.Ai(memory);
        ai.SetDouble(StalkerMemory.RestUntil, nowTotalHours + config.Ladder.DrivenOffRestDays * 24);
        SetStage(memory, Math.Min(config.Ladder.DrivenOffStage, HuntStage - 1), nowTotalHours, "driven off");
    }

    /// <summary>Sets a stage directly, for the admin command; the stage's clocks start again.</summary>
    public void ForceStage(ITreeAttribute memory, int stage, double nowTotalHours)
    {
        if (memory.GetInt(WalkerMemory.Stage) <= 0)
        {
            Begin(memory, stage, nowTotalHours);
            return;
        }
        StalkerMemory.Ai(memory).SetBool(StalkerMemory.HuntOver, false);
        SetStage(memory, Math.Clamp(stage, 1, HuntStage), nowTotalHours, "set by command");
    }

    private bool ReadyToClimb(ITreeAttribute memory, ITreeAttribute ai, int stage, LadderInput input)
    {
        var ladder = config.Ladder;
        double hours = ai.GetDouble(StalkerMemory.StageHours);
        int nights = ai.GetInt(StalkerMemory.StageNights);
        int acts = ai.GetInt(StalkerMemory.StageActs);
        return stage switch
        {
            1 => hours >= ladder.Stage1Hours && ai.GetInt(StalkerMemory.Sightings) >= ladder.Stage1Sightings,
            2 => hours >= ladder.Stage2Hours,
            3 => nights >= ladder.Stage3Nights && acts >= ladder.StageMinActs,
            // The hunt starts on the first night the victim is alone after stage 4 is done.
            4 => nights >= ladder.Stage4Nights && acts >= ladder.StageMinActs && input.Night && input.Alone,
            _ => false,
        };
    }

    /// <summary>Counts nights for stages 3 to 5. Returns true when the hunt has run out.</summary>
    private bool TrackNight(ITreeAttribute memory, ITreeAttribute ai, int stage, LadderInput input, double dt, bool clock)
    {
        double now = input.NowTotalHours;
        double nightStart = ai.GetDouble(StalkerMemory.NightStart, -1);
        if (input.Night)
        {
            if (nightStart < 0)
            {
                ai.SetDouble(StalkerMemory.NightStart, now);
                ai.SetDouble(StalkerMemory.NightClockHours, 0);
                ai.SetBool(StalkerMemory.NightSlept, false);
                ai.SetBool(StalkerMemory.NightHit, false);
                ai.SetInt(StalkerMemory.SilhouettesTonight, 0);
                ai.SetDouble(StalkerMemory.SilhouetteNight, now);
                return false;
            }
            if (clock)
            {
                ai.SetDouble(StalkerMemory.NightClockHours, ai.GetDouble(StalkerMemory.NightClockHours) + dt);
            }
            if (input.Asleep)
            {
                ai.SetBool(StalkerMemory.NightSlept, true);
            }
            return false;
        }
        if (nightStart < 0)
        {
            return false;
        }

        // Dawn. A night counts when the victim was there for a third of it, or slept through it.
        ai.SetDouble(StalkerMemory.NightStart, -1);
        double length = Math.Max(0.01, now - nightStart);
        bool counts = ai.GetBool(StalkerMemory.NightSlept) || ai.GetDouble(StalkerMemory.NightClockHours) >= length / 3;
        if (!counts)
        {
            return false;
        }
        if (stage == 3 || stage == 4)
        {
            ai.SetInt(StalkerMemory.StageNights, ai.GetInt(StalkerMemory.StageNights) + 1);
            return false;
        }
        if (stage != HuntStage)
        {
            return false;
        }
        bool quiet = hostilityOff() || !ai.GetBool(StalkerMemory.NightHit);
        if (!quiet)
        {
            return false;
        }
        int huntNights = ai.GetInt(StalkerMemory.HuntNights) + 1;
        ai.SetInt(StalkerMemory.HuntNights, huntNights);
        if (huntNights < config.Ladder.HuntMaxNights)
        {
            return false;
        }
        ai.SetBool(StalkerMemory.HuntOver, true);
        logger.Debug("Walker {0}: the hunt ran out after {1} quiet night(s)", memory.GetInt(WalkerMemory.Id), huntNights);
        return true;
    }

    private static void TrackCompany(ITreeAttribute ai, LadderInput input, double dt, bool clock)
    {
        double company = ai.GetDouble(StalkerMemory.CompanyHours);
        company = clock && !input.Alone ? company + dt : Math.Max(0, company - dt);
        ai.SetDouble(StalkerMemory.CompanyHours, company);
    }

    private float Pace(ITreeAttribute memory, LadderInput input)
    {
        float pace = config.Ladder.PaceMultiplier * memory.GetFloat(WalkerMemory.Pace, 1);
        if (VictimStability(memory, input) < LowStability)
        {
            pace *= config.Ladder.LowStabilityPace;
        }
        return pace;
    }

    /// <summary>The victim's temporal stability without the walker's own drain (walkers/harass/drainTaken, if the harassment kit writes it).</summary>
    private static double VictimStability(ITreeAttribute memory, LadderInput input)
    {
        if (input.Victim == null)
        {
            return 1;
        }
        double stability = input.Victim.WatchedAttributes.GetDouble("temporalStability", 1);
        double drained = memory.GetTreeAttribute(WalkerMemory.Harass)?.GetDouble("drainTaken") ?? 0;
        return Math.Min(1, stability + Math.Max(0, drained));
    }

    private void SetStage(ITreeAttribute memory, int stage, double now, string reason)
    {
        int before = memory.GetInt(WalkerMemory.Stage);
        var ai = StalkerMemory.Ai(memory);
        memory.SetInt(WalkerMemory.Stage, stage);
        ai.SetDouble(StalkerMemory.StageBegan, now);
        ai.SetDouble(StalkerMemory.StageHours, 0);
        ai.SetInt(StalkerMemory.StageNights, 0);
        ai.SetInt(StalkerMemory.StageActs, 0);
        ai.SetInt(StalkerMemory.Sightings, 0);
        // A night already under way counts for the new stage only from now on.
        ai.SetDouble(StalkerMemory.NightClockHours, 0);
        ai.SetBool(StalkerMemory.NightSlept, false);
        if (stage != HuntStage)
        {
            ai.SetInt(StalkerMemory.HuntNights, 0);
        }
        if (stage != before)
        {
            logger.Debug("Walker {0}: stage {1} -> {2} ({3})", memory.GetInt(WalkerMemory.Id), before, stage, reason);
        }
    }
}
