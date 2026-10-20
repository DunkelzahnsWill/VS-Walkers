using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;
using Walkers.Config;

namespace Walkers.Director;

// The director's clock (creature bible, section 8): once a second it keeps bodies in order, and every
// director.tickSeconds it updates the players, runs each haunt, and starts new haunts and returns.
public partial class DirectorSystem
{
    /// <summary>No removal while a body burns or within this many real seconds of a hit on it (creature bible, section 4).</summary>
    private const long RecentHitMs = 20_000;

    /// <summary>A body this much farther than director.spawnDistanceMax from its victim follows them: removed unseen, placed again near them.</summary>
    private const int FollowSlack = 32;

    /// <summary>Real seconds past ritual.huskBurnWindowSeconds after which a body nobody reported on counts as left alone.</summary>
    private const int DownGraceSeconds = 60;

    private float secondsSinceTick;
    /// <summary>The walker /walkers director return now brings back, skipping its victim's rest; 0 for none.</summary>
    private int returnNow;
    /// <summary>Candidate spots left in this director tick (director.spawnTriesPerTick, shared by every walker).</summary>
    private int placementTriesLeft;
    /// <summary>Why each paused haunt is paused, for /walkers director status.</summary>
    private readonly Dictionary<int, string> pauseReasons = new();
    /// <summary>Dead bodies of walkers the director itself released as left alone, to remove unseen.</summary>
    private readonly HashSet<int> leftAloneBodies = new();
    private readonly Dictionary<int, long> lastHitMs = new();
    private readonly Dictionary<int, int> hurtCounters = new();

    private void OnSecond(float dt)
    {
        // One failing step logs and leaves the others, and the server, running.
        Guard("activity", TrackActivity);
        Guard("stand-ins", TurnStandIns);
        Guard("bodies", MaintainBodies);
        secondsSinceTick += dt;
        if (secondsSinceTick >= config.Director.TickSeconds)
        {
            secondsSinceTick = 0;
            Guard("tick", Tick);
        }
    }

    private void Guard(string step, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Mod.Logger.Error("Director {0} failed: {1}", step, e);
        }
    }

    /// <summary>One director update. /walkers director tick runs it at once.</summary>
    internal void Tick()
    {
        placementTriesLeft = config.Director.SpawnTriesPerTick;
        UpdateCandidates();
        Guard("furs", UpdateFurs);
        bool storm = StormActive();
        foreach (var walker in walkers.Values.OrderBy(w => w.Id).ToList())
        {
            switch (walker.State)
            {
                case WalkerState.Haunting:
                case WalkerState.Paused:
                    Guard("haunt of walker " + walker.Id, () => UpdateHaunt(walker, storm));
                    break;
                case WalkerState.Down:
                    Guard("check of walker " + walker.Id, () => CheckDown(walker));
                    break;
            }
        }
        // A slot freed later in the loop goes to a paused haunt that waits for one, ahead of any new roll.
        foreach (var walker in walkers.Values.Where(w => w.State == WalkerState.Paused && WaitsForSlot(w)).OrderBy(w => w.Id).ToList())
        {
            if (ActiveCount() >= ActiveCap())
            {
                break;
            }
            Guard("haunt of walker " + walker.Id, () => UpdateHaunt(walker, storm));
        }
        StartHaunts(storm);
        ProcessRestore(false);
    }

    private const string WaitingForSlot = "waiting for a free slot";

    private bool WaitsForSlot(WalkerRecord walker) => pauseReasons.TryGetValue(walker.Id, out var reason) && reason == WaitingForSlot;

    // ---- Haunts ----

    private void UpdateHaunt(WalkerRecord walker, bool storm)
    {
        var calendar = sapi.World.Calendar;
        double nowHours = calendar.TotalHours;
        var memory = memories[walker.Id];
        var victim = walker.VictimUid != null && candidates.TryGetValue(walker.VictimUid, out var found) ? found : null;
        if (victim != null)
        {
            memory.SetLong(WalkerMemory.VictimEntity, victim.Entity.EntityId);
        }
        else
        {
            memory.RemoveAttribute(WalkerMemory.VictimEntity);
        }

        // A paused haunt ends after director.pausedHauntMaxDays, whether its victim is still away or back
        // and waiting for a free slot.
        if (walker.State == WalkerState.Paused && calendar.TotalDays - walker.PausedSinceTotalDays > config.Director.PausedHauntMaxDays)
        {
            EndHauntCore(walker, HauntEndReason.PausedTooLong);
            return;
        }

        var pause = PauseReason(victim, storm);
        if (pause == null && walker.GuiseType != null && GuisePicker.OutOfCountry(sapi, victim!.Entity.Pos.AsBlockPos, walker.GuiseType, config.Forms))
        {
            // A travelling victim keeps the same doe; out of its country, the haunt waits for them to come back.
            pause = "the victim is out of the " + walker.GuiseType + " deer's country";
        }
        if (pause == null && victim != null && !IsWrath(walker) && !FarFromOtherVictims(victim, walker))
        {
            pause = "another victim is nearby";
        }
        if (pause != null)
        {
            KeepLadderClock(walker, memory, victim, nowHours);
            walker.ClockUpdatedTotalHours = nowHours;
            pauseReasons[walker.Id] = pause;
            if (walker.State == WalkerState.Haunting)
            {
                walker.State = WalkerState.Paused;
                walker.PausedSinceTotalDays = calendar.TotalDays;
                SyncMemory(walker);
                Mod.Logger.Notification("Walker {0}'s haunt on {1} pauses: {2}", walker.Id, NameOf(walker.VictimUid!), pause);
            }
            return;
        }

        if (walker.State == WalkerState.Paused)
        {
            // A paused haunt holds no slot; it resumes as soon as there is one, ahead of any new roll.
            if (ActiveCount() >= ActiveCap())
            {
                pauseReasons[walker.Id] = WaitingForSlot;
                KeepLadderClock(walker, memory, victim, nowHours);
                walker.ClockUpdatedTotalHours = nowHours;
                return;
            }
            walker.State = WalkerState.Haunting;
            pauseReasons.Remove(walker.Id);
            SyncMemory(walker);
            placeAfterMs[walker.Id] = 0;
            Mod.Logger.Notification("Walker {0}'s haunt on {1} resumes", walker.Id, victim!.Name);
        }

        // The haunt clock runs only while the victim is eligible (not idle) or asleep, and the haunt is not
        // paused. A slept night can jump it by hours.
        double elapsed = Math.Max(0, nowHours - walker.ClockUpdatedTotalHours);
        if (victim!.Eligible || victim.Asleep)
        {
            walker.HauntClockHours += elapsed;
        }
        // The escalation clock runs only during a live haunt, once the walker has died improperly.
        if (ImproperDeaths(walker.Id) > 0)
        {
            walker.EscalationHauntHours += elapsed;
        }
        walker.ClockUpdatedTotalHours = nowHours;
        if (walker.HauntClockHours >= config.Director.HauntMaxDays * calendar.HoursPerDay)
        {
            EndHauntCore(walker, HauntEndReason.Timeout);
            return;
        }

        if (ladder != null)
        {
            var input = new LadderInput(nowHours, victim!.Entity, Exposed(victim), victim.Alone, victim.Night, victim.Asleep);
            LadderOutcome outcome;
            try
            {
                outcome = ladder.Update(memory, input);
            }
            catch (Exception e)
            {
                Mod.Logger.Error("The ladder failed on walker {0}: {1}", walker.Id, e);
                outcome = LadderOutcome.None;
            }
            CheckStage(walker);
            if (walker.State != WalkerState.Haunting)
            {
                // A handler of the ladder or of StageChanged ended or moved the haunt.
                return;
            }
            if (outcome == LadderOutcome.HuntEnded)
            {
                EndHauntCore(walker, HauntEndReason.HuntTimeout);
                return;
            }
        }

        if (BodyOf(walker) == null && sapi.World.ElapsedMilliseconds >= placeAfterMs.GetValueOrDefault(walker.Id))
        {
            TryPlace(walker, victim!);
        }
    }

    /// <summary>
    /// While a haunt is paused the ladder still hears from the director, with Exposed false, so its own
    /// clock moves on and the paused time never counts toward a stage when the haunt resumes.
    /// </summary>
    private void KeepLadderClock(WalkerRecord walker, Vintagestory.API.Datastructures.ITreeAttribute memory, Candidate? victim, double nowHours)
    {
        if (ladder == null)
        {
            return;
        }
        try
        {
            ladder.Update(memory, new LadderInput(nowHours, victim?.Entity, false, victim?.Alone ?? false, victim?.Night ?? false, victim?.Asleep ?? false));
        }
        catch (Exception e)
        {
            Mod.Logger.Error("The ladder failed on walker {0}: {1}", walker.Id, e);
        }
    }

    /// <summary>Why the haunt must pause now, or null (creature bible, section 8, "Withdraw and pause").</summary>
    private string? PauseReason(Candidate? victim, bool storm)
    {
        if (!config.Enabled)
        {
            return "the mod is switched off";
        }
        if (victim == null)
        {
            return "the victim is offline";
        }
        if (victim.Entity.Pos.Dimension != Vintagestory.API.Config.Dimensions.NormalWorld)
        {
            return "the victim is in another dimension";
        }
        if (victim.Player != null && victim.Player.WorldData.CurrentGameMode is not (EnumGameMode.Survival or EnumGameMode.Guest))
        {
            return "the victim is in creative or spectator mode";
        }
        if (victim.Player != null && !victim.Counting && !victim.Asleep)
        {
            // Idle for director.afkMinutes: the slot goes to someone who plays. A slept night does not pause.
            return "the victim is idle";
        }
        if (!victim.Surface)
        {
            return "the victim is underground";
        }
        if (!victim.NearForest)
        {
            return "the victim is away from the forest";
        }
        if (storm)
        {
            return "a temporal storm";
        }
        return null;
    }

    /// <summary>Starts a haunt at stage 1, or at return.startStage for a return, unless an admin names the stage.</summary>
    private void StartHaunt(WalkerRecord walker, Candidate victim, bool isReturn, int? stageOverride = null)
    {
        var calendar = sapi.World.Calendar;
        walker.State = WalkerState.Haunting;
        walker.VictimUid = victim.Uid;
        walker.HauntStartedTotalDays = calendar.TotalDays;
        walker.HauntClockHours = 0;
        walker.ClockUpdatedTotalHours = calendar.TotalHours;
        walker.PausedSinceTotalDays = 0;
        if (isReturn)
        {
            walker.Returns++;
            walker.ReturnPending = false;
        }
        // Rolled haunts start only in deer country; an admin's haunt elsewhere wears the old fallback.
        walker.GuiseType = GuisePicker.Pick(sapi, victim.Entity.Pos.AsBlockPos, config.Forms) ?? GuisePicker.Fallback;

        int stage = stageOverride ?? (isReturn ? config.Return.StartStage : 1);
        var memory = memories[walker.Id];
        memory.SetInt(WalkerMemory.Stage, stage);
        memory.SetFloat(WalkerMemory.Pace, isReturn ? config.Return.PaceMultiplier : 1);
        knownStage[walker.Id] = stage;
        SyncMemory(walker);
        memory.SetLong(WalkerMemory.VictimEntity, victim.Entity.EntityId);
        try
        {
            ladder?.Begin(memory, stage, calendar.TotalHours);
        }
        catch (Exception e)
        {
            Mod.Logger.Error("The ladder failed to begin walker {0}'s haunt: {1}", walker.Id, e);
        }
        walker.AppearedThisHaunt = false;
        walker.LastVictimUid = null;
        pauseReasons.Remove(walker.Id);
        placeAfterMs[walker.Id] = 0;
        if (playerRecords.TryGetValue(victim.Uid, out var record) && record.MarkedByWalker == walker.Id)
        {
            record.MarkedByWalker = 0;
        }

        Mod.Logger.Notification("Walker {0} begins haunting {1} at stage {2}, wearing a {3} deer{4}",
            walker.Id, victim.Name, stage, walker.GuiseType, isReturn ? $" (return {walker.Returns})" : "");
        var info = Info(walker);
        Raise(HauntStarted, info);
        if (isReturn)
        {
            Raise(WalkerReturned, info);
        }
    }

    /// <summary>
    /// Ends a haunt: the victim rests (twice as long when they died), and the walker goes to
    /// <paramref name="next"/>, normally back to the roster.
    /// </summary>
    private void EndHauntCore(WalkerRecord walker, HauntEndReason reason, WalkerState next = WalkerState.Resting, bool raise = true)
    {
        var uid = walker.VictimUid;
        if (uid != null && playerRecords.TryGetValue(uid, out var record))
        {
            double rest = config.Director.MinDaysBetweenHaunts * (reason == HauntEndReason.VictimDied ? 2 : 1);
            record.RestUntilTotalDays = Math.Max(record.RestUntilTotalDays, sapi.World.Calendar.TotalDays + rest);
        }
        walker.State = next;
        walker.LastVictimUid = uid;
        if (next != WalkerState.Down)
        {
            walker.VictimUid = null;
        }
        walker.AppearedThisHaunt = false;
        pauseReasons.Remove(walker.Id);
        SyncMemory(walker);
        Mod.Logger.Notification("Walker {0}'s haunt on {1} ends: {2}", walker.Id, uid == null ? "nobody" : NameOf(uid), reason);
        if (raise)
        {
            Raise(HauntEnded, Info(walker), reason);
        }
    }

    private void StartHaunts(bool storm)
    {
        if (!config.Enabled)
        {
            return;
        }
        var calendar = sapi.World.Calendar;
        double now = calendar.TotalDays;
        SendBackAbsentReturns(now);
        int cap = ActiveCap();
        int active = ActiveCount();
        if (active >= cap)
        {
            return;
        }
        var free = candidates.Values.Where(c => CanBeChosen(c, storm, now)).ToList();

        // 1. Returns that are due, for their last victim.
        foreach (var walker in walkers.Values.Where(w => w.State == WalkerState.Waiting).OrderBy(w => LatestEntry(w.Id)?.ReturnTotalDays ?? 0).ToList())
        {
            var entry = LatestEntry(walker.Id);
            if (entry == null || entry.ReturnTotalDays > now)
            {
                continue;
            }
            // Like any haunt, a return waits for its target's rest; /walkers director return now skips it.
            // Its target is the most-marked fur carrier who has no walker of their own, else its last victim.
            bool skipRest = returnNow == walker.Id;
            double restBy = skipRest ? double.MaxValue : now;
            // At Wrath groups no longer hold it back: a victim near another victim may still be its target.
            bool wrath = IsWrath(walker);
            var victim = MarkedCarriers().FirstOrDefault(c => CanBeChosen(c, storm, restBy) && (wrath || FarFromOtherVictims(c)));
            if (victim == null && walker.VictimUid != null && candidates.TryGetValue(walker.VictimUid, out var last) && CanBeChosen(last, storm, restBy))
            {
                victim = last;
            }
            if (victim != null && active < cap && (wrath || FarFromOtherVictims(victim)))
            {
                entry.Outcome = LedgerOutcome.Returned;
                StartHaunt(walker, victim, isReturn: true);
                free.Remove(victim);
                active++;
            }
        }

        // 2. Players who hurt a walker that was haunting someone else: they are its next victim.
        foreach (var victim in free.ToList())
        {
            int markedBy = playerRecords[victim.Uid].MarkedByWalker;
            if (active >= cap || markedBy == 0 || !walkers.TryGetValue(markedBy, out var walker) || walker.State != WalkerState.Resting || !FarFromOtherVictims(victim))
            {
                continue;
            }
            StartHaunt(walker, victim, walker.ReturnPending);
            free.Remove(victim);
            active++;
        }

        // 3. Each rested, eligible player near forest rolls once per in-game day, the most isolated first.
        int today = (int)now;
        foreach (var victim in free.OrderByDescending(c => c.NearestOther))
        {
            if (active >= cap)
            {
                break;
            }
            var record = playerRecords[victim.Uid];
            if (record.LastRollDay >= today)
            {
                continue;
            }
            record.LastRollDay = today;
            // While a walker rests, a marked player rolls furs.restingRollMultiplier times as often.
            double chance = config.Director.HauntChancePerDay * (MarkedWhileResting(victim.Uid) ? config.Furs.RestingRollMultiplier : 1);
            if (sapi.World.Rand.NextDouble() >= chance || !FarFromOtherVictims(victim))
            {
                continue;
            }
            // A walker a player who hurt it is owed stays reserved for them.
            var reserved = playerRecords.Values.Select(r => r.MarkedByWalker).Where(id => id != 0).ToHashSet();
            var walker = walkers.Values.Where(w => w.State == WalkerState.Resting && !reserved.Contains(w.Id)).OrderBy(w => w.Id).FirstOrDefault() ?? CreateWalker();
            StartHaunt(walker, victim, walker.ReturnPending);
            active++;
        }
    }

    /// <summary>
    /// A due return whose victim has stayed away for return.victimAbsentSeasons, counted from the return
    /// day or from when they were last seen, whichever is later, rests in the roster instead; its next
    /// haunt, whoever it is for, is a return. Runs every tick, whether or not a slot is free.
    /// </summary>
    private void SendBackAbsentReturns(double now)
    {
        foreach (var walker in walkers.Values.Where(w => w.State == WalkerState.Waiting).ToList())
        {
            var entry = LatestEntry(walker.Id);
            if (entry == null || entry.ReturnTotalDays > now)
            {
                continue;
            }
            double lastSeen = walker.VictimUid != null && playerRecords.TryGetValue(walker.VictimUid, out var record) ? record.LastSeenTotalDays : 0;
            if (now <= Math.Max(entry.ReturnTotalDays, lastSeen) + SeasonDays(config.Return.VictimAbsentSeasons))
            {
                continue;
            }
            entry.Outcome = LedgerOutcome.Returned;
            walker.State = WalkerState.Resting;
            walker.VictimUid = null;
            walker.ReturnPending = true;
            SyncMemory(walker);
            Mod.Logger.Notification("Walker {0} is back, but its last victim stayed away; it waits in the roster", walker.Id);
        }
    }

    /// <summary>
    /// May this player or stand-in be chosen now: exposed, in deer country, rested by <paramref name="now"/>, not already
    /// haunted, past the world's grace time, and no storm.
    /// </summary>
    private bool CanBeChosen(Candidate c, bool storm, double now) =>
        !storm &&
        Exposed(c) &&
        c.DeerCountry &&
        WorldGracePassed() &&
        now >= playerRecords[c.Uid].RestUntilTotalDays &&
        !walkers.Values.Any(w => w.VictimUid == c.Uid && w.State is WalkerState.Haunting or WalkerState.Paused or WalkerState.Down);

    /// <summary>
    /// director.victimSeparation: no victim may be this close to another online victim. For a running
    /// haunt (<paramref name="self"/> haunting) only running haunts with a lower id count, so of two
    /// victims who come together one haunt goes on and the other pauses.
    /// </summary>
    private bool FarFromOtherVictims(Candidate c, WalkerRecord? self = null)
    {
        double min = config.Director.VictimSeparation;
        bool running = self?.State == WalkerState.Haunting;
        foreach (var walker in walkers.Values)
        {
            if (walker == self || walker.State is not (WalkerState.Haunting or WalkerState.Paused) || walker.VictimUid == null || walker.VictimUid == c.Uid ||
                !candidates.TryGetValue(walker.VictimUid, out var other))
            {
                continue;
            }
            if (running && (walker.State != WalkerState.Haunting || walker.Id > self!.Id))
            {
                continue;
            }
            if (other.Entity.Pos.Dimension == c.Entity.Pos.Dimension && other.Entity.Pos.DistanceTo(c.Entity.Pos.XYZ) < min)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Active walkers allowed now: director.maxActiveWalkers, one more per director.playersPerExtraWalker eligible players, at most the cap.</summary>
    internal int ActiveCap()
    {
        var d = config.Director;
        int eligible = candidates.Values.Count(c => c.Eligible);
        return Math.Min(d.MaxActiveWalkersCap, d.MaxActiveWalkers + eligible / d.PlayersPerExtraWalker);
    }

    /// <summary>Walkers haunting now. Paused haunts hold no slot.</summary>
    private int ActiveCount() => walkers.Values.Count(w => w.State == WalkerState.Haunting);

    /// <summary>
    /// No haunts until the world's graceTimer has passed (creature bible, section 8). The spawner turns the
    /// setting into an absolute day in the save game (ServerSystemEntitySpawner.cs:98).
    /// </summary>
    private bool WorldGracePassed()
    {
        double until;
        try
        {
            until = sapi.WorldManager.SaveGame.GetData<double>("graceTimeUntilTotalDays");
        }
        catch (Exception)
        {
            until = 0;
        }
        return sapi.World.Calendar.TotalDays >= until;
    }

    /// <summary>
    /// A temporal storm is on and director.pauseDuringStorms is on. A world without storms never reports
    /// one, so mundane mode needs no check here.
    /// </summary>
    private bool StormActive()
    {
        if (!config.Director.PauseDuringStorms)
        {
            return false;
        }
        return sapi.ModLoader.GetModSystem<SystemTemporalStability>()?.StormData?.nowStormActive == true;
    }

    /// <summary>Mundane mode: no stability drain and no storm checks (creature bible, section 8).</summary>
    internal bool IsMundane() => config.MundaneMode switch
    {
        MundaneMode.On => true,
        MundaneMode.Off => false,
        _ => !sapi.World.Config.GetAsBool("temporalStability", true),
    };

    // ---- Bodies ----

    /// <summary>
    /// Once a second: a body whose walker no longer haunts (paused, ended) withdraws, and a body too far
    /// from its victim follows them; either is removed the first moment nobody can see it, never while it
    /// burns or right after a hit.
    /// </summary>
    private void MaintainBodies()
    {
        long now = sapi.World.ElapsedMilliseconds;
        foreach (var (walkerId, body) in bodies.ToList())
        {
            var walker = walkers[walkerId];
            if (body.EntityId == 0)
            {
                // Attached, not spawned yet.
                continue;
            }
            if (sapi.World.GetEntityById(body.EntityId) != body)
            {
                ForgetBody(walkerId);
                continue;
            }
            if (!body.Alive)
            {
                // A body the director released as left alone goes the first moment nobody sees it; Forms removes the others.
                if (leftAloneBodies.Contains(walkerId) && !body.IsOnFire && !CanBeSeen(body))
                {
                    leftAloneBodies.Remove(walkerId);
                    RemoveBody(walker, body);
                }
                continue;
            }
            RememberPos(walker, body.Pos);
            int hurt = body.WatchedAttributes.GetInt("onHurtCounter");
            if (hurtCounters.TryGetValue(walkerId, out var before) && before != hurt)
            {
                lastHitMs[walkerId] = now;
            }
            hurtCounters[walkerId] = hurt;

            bool withdraw = walker.State != WalkerState.Haunting;
            bool follow = !withdraw && TooFarFromVictim(walker, body);
            if (!withdraw && !follow)
            {
                continue;
            }
            if (body.IsOnFire || now - lastHitMs.GetValueOrDefault(walkerId, long.MinValue / 2) < RecentHitMs || CanBeSeen(body))
            {
                continue;
            }
            Mod.Logger.Debug("Walker {0}'s body {1}", walkerId, withdraw ? "withdraws" : "follows its victim");
            RemoveBody(walker, body);
        }
    }

    private bool TooFarFromVictim(WalkerRecord walker, Entity body)
    {
        var victim = walker.VictimUid == null ? null : VictimEntity(walker.VictimUid);
        if (victim == null)
        {
            return false;
        }
        // The stage bands reach 64 blocks whatever director.spawnDistanceMax says.
        int range = Math.Max(config.Director.SpawnDistanceMax, 64) + FollowSlack;
        return victim.Pos.Dimension != body.Pos.Dimension || victim.Pos.SquareDistanceTo(body.Pos.XYZ) > range * range;
    }

    /// <summary>A walker that stays down with nobody reporting an outcome is released as left alone, after the burn window.</summary>
    private void CheckDown(WalkerRecord walker)
    {
        if (!downSinceMs.TryGetValue(walker.Id, out var since))
        {
            downSinceMs[walker.Id] = since = sapi.World.ElapsedMilliseconds;
        }
        long limit = (long)((config.Ritual.HuskBurnWindowSeconds + DownGraceSeconds) * 1000);
        var body = BodyOf(walker);
        if (body == null || sapi.World.ElapsedMilliseconds - since > limit)
        {
            ReleaseCore(walker, WalkerRelease.LeftAlone, false);
            if (body != null)
            {
                leftAloneBodies.Add(walker.Id);
            }
        }
    }
}
