using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Walkers.Director;
using Walkers.Stalker.Tasks;

namespace Walkers.Stalker;

/// <summary>
/// <c>/walkers stalker selftest</c>: checks the stalker AI on a headless server, where no player can
/// look at anything (creature bible, section 9: testing without a client). It needs the walker
/// entity types and the vanilla playerbot. Every check prints "pass" or "FAIL" with its name:
/// <list type="bullet">
/// <item>The ladder: stage 1 needs time and sightings, stage 2 time; company drops it to the cap; the
/// victim's hit on the guise adds a stage once per haunt; three quiet hunt nights end the hunt; driven
/// off, it falls back and rests.</item>
/// <item>The view cone, on numbers.</item>
/// <item>The gaze, live: a playerbot stand-in in the open air looks at a guise ten blocks away, looks
/// away, then looks from deep inside the ground.</item>
/// <item>The task entries of both forms, and a standing spot on the terrain at the world spawn.</item>
/// <item>The slip-away, over the next three seconds: the stand-in watches the guise at stage 2 from
/// within stalk.vanishApproach, so it freezes and is marked; once the stand-in looks away, the guise
/// is removed. This result is logged by itself as "Stalker self-test: slip-away ...".</item>
/// </list>
/// The bodies it spawns are removed when it ends.
/// </summary>
public sealed class StalkerSelfTest
{
    private const string StandInCode = "game:playerbot";
    private const string GuiseCode = "walkers:skinwalker-animal-whitetail";
    private const string TrueFormCode = "walkers:skinwalker-true";
    private const double Apart = 10;

    private readonly ICoreServerAPI api;
    private readonly StalkerModSystem system;
    private readonly List<Entity> spawned = new();
    private int phase;
    private long phaseAtMs;
    private Entity? standIn;
    private Entity? guise;

    public StalkerSelfTest(ICoreServerAPI api, StalkerModSystem system)
    {
        this.api = api;
        this.system = system;
    }

    /// <summary>Runs the synchronous checks and starts the slip-away; returns the report.</summary>
    public string Run()
    {
        Cleanup();
        var lines = new List<string> { "Stalker self-test:" };
        void Check(string name, bool ok, string detail = "")
        {
            lines.Add($"  {name}: {(ok ? "pass" : "FAIL")}{(detail.Length > 0 && !ok ? " (" + detail + ")" : "")}");
        }

        try
        {
            LadderChecks(Check);
            ConeChecks(Check);
            EntryChecks(Check);
            LiveChecks(Check);
        }
        catch (Exception e)
        {
            Check("no exception", false, e.Message);
            system.Logger.Error("Stalker self-test failed with an exception: {0}", e);
            Cleanup();
        }
        int failed = lines.Count(l => l.EndsWith("FAIL", StringComparison.Ordinal) || l.Contains(": FAIL ("));
        lines.Add(failed == 0 ? "Stalker self-test: all synchronous checks passed" : $"Stalker self-test: {failed} check(s) failed");
        return string.Join("\n", lines);
    }

    /// <summary>Called by the mod system every 100 ms: the slip-away check.</summary>
    public void OnTick(long nowMs)
    {
        if (phase == 0)
        {
            return;
        }
        var brain = StalkerModSystem.BrainOf(guise);
        if (phase == 1 && nowMs - phaseAtMs >= 1500)
        {
            bool ok = brain != null && brain.Mode == StalkerMode.Stalk && brain.Gaze.VictimWatching && brain.Frozen && brain.SlipAwayReason == "approached";
            Report("slip-away, watched: frozen and marked", ok, brain == null
                ? "no brain"
                : $"mode {WalkerBrain.ModeName(brain.Mode)}, watching {brain.Gaze.VictimWatching}, frozen {brain.Frozen}, mark {brain.SlipAwayReason ?? "none"}");
            if (!ok || standIn == null || guise == null)
            {
                Finish(false);
                return;
            }
            // The stand-in looks the other way.
            standIn.Pos.Yaw = WalkerTaskBase.YawTo(standIn.Pos, guise.Pos.XYZ) + GameMath.PI;
            phase = 2;
            phaseAtMs = nowMs;
        }
        else if (phase == 2 && nowMs - phaseAtMs >= 1500)
        {
            bool gone = guise == null || !guise.Alive || api.World.GetEntityById(guise.EntityId) == null;
            Report("slip-away, unwatched: removed", gone, brain == null ? "" : $"watched {brain.Gaze.AnyWatched}");
            Finish(gone);
        }
    }

    private void Report(string name, bool ok, string detail)
    {
        system.Logger.Notification("Stalker self-test: {0}: {1}{2}", name, ok ? "pass" : "FAIL", ok || detail.Length == 0 ? "" : " (" + detail + ")");
    }

    private void Finish(bool ok)
    {
        system.Logger.Notification("Stalker self-test: slip-away {0}", ok ? "passed" : "FAILED");
        phase = 0;
        Cleanup();
    }

    private void LadderChecks(Action<string, bool, string> check)
    {
        var ladder = system.Ladder;
        var cfg = system.Config.Ladder;

        // Stage 1: time alone is not enough; two sightings are needed too.
        var m = new TreeAttribute();
        double now = 1000;
        ladder.Begin(m, 1, now);
        now = Step(ladder, m, now, cfg.Stage1Hours + 1, night: false, alone: true);
        bool heldWithoutSightings = m.GetInt(WalkerMemory.Stage) == 1;
        for (int i = 0; i < cfg.Stage1Sightings; i++)
        {
            StalkerLadder.OnSighting(m);
        }
        now = Step(ladder, m, now, 1, night: false, alone: true);
        check("ladder 1 to 2 needs hours and sightings", heldWithoutSightings && m.GetInt(WalkerMemory.Stage) == 2, $"stage {m.GetInt(WalkerMemory.Stage)}");

        now = Step(ladder, m, now, cfg.Stage2Hours + 1, night: false, alone: true);
        check("ladder 2 to 3 after its hours", m.GetInt(WalkerMemory.Stage) == 3, $"stage {m.GetInt(WalkerMemory.Stage)}");

        // Company above the cap.
        var g = new TreeAttribute();
        ladder.Begin(g, 4, now);
        Step(ladder, g, now, 2, night: false, alone: false);
        check("ladder falls back to the group cap with company", g.GetInt(WalkerMemory.Stage) == Math.Min(cfg.GroupStageCap, 4), $"stage {g.GetInt(WalkerMemory.Stage)}");

        // The victim's hit on the guise: one bonus per haunt.
        var h = new TreeAttribute();
        ladder.Begin(h, 1, now);
        ladder.OnHarm(h);
        int afterFirst = h.GetInt(WalkerMemory.Stage);
        ladder.OnHarm(h);
        int expected = Math.Min(4, 1 + cfg.HarmGuiseBonus);
        check("ladder harm bonus once per haunt", afterFirst == expected && h.GetInt(WalkerMemory.Stage) == expected, $"{afterFirst} then {h.GetInt(WalkerMemory.Stage)}");

        // Quiet hunt nights end the hunt.
        var q = new TreeAttribute();
        double t = now;
        ladder.Begin(q, StalkerLadder.HuntStage, t);
        bool ended = false;
        for (int night = 0; night < cfg.HuntMaxNights && !ended; night++)
        {
            t = Step(ladder, q, t, 10, night: true, alone: true, out ended);
            if (!ended)
            {
                t = Step(ladder, q, t, 1, night: false, alone: true, out ended);
            }
        }
        check("ladder hunt ends after its quiet nights", ended && StalkerMemory.Ai(q).GetBool(StalkerMemory.HuntOver), $"hunt nights {StalkerMemory.Ai(q).GetInt(StalkerMemory.HuntNights)}");

        // Driven off: back to the driven-off stage, and no climbing while it rests.
        var d = new TreeAttribute();
        ladder.Begin(d, StalkerLadder.HuntStage, now);
        ladder.OnDrivenOff(d, now);
        int fell = d.GetInt(WalkerMemory.Stage);
        Step(ladder, d, now, 20, night: false, alone: true);
        check("ladder driven off falls back and rests", fell == Math.Min(cfg.DrivenOffStage, 4) && d.GetInt(WalkerMemory.Stage) == fell, $"{fell} then {d.GetInt(WalkerMemory.Stage)}");
    }

    private static double Step(StalkerLadder ladder, ITreeAttribute memory, double now, double hours, bool night, bool alone) =>
        Step(ladder, memory, now, hours, night, alone, out _);

    /// <summary>Updates the ladder once an in-game hour (the most an awake update may add) for <paramref name="hours"/> hours.</summary>
    private static double Step(StalkerLadder ladder, ITreeAttribute memory, double now, double hours, bool night, bool alone, out bool huntEnded)
    {
        huntEnded = false;
        int steps = Math.Max(1, (int)Math.Ceiling(hours));
        for (int i = 0; i < steps && !huntEnded; i++)
        {
            now += hours / steps;
            huntEnded = ladder.Update(memory, new LadderInput(now, null, Exposed: true, Alone: alone, Night: night, Asleep: false)) == LadderOutcome.HuntEnded;
        }
        return now;
    }

    private static void ConeChecks(Action<string, bool, string> check)
    {
        float cosHalf = MathF.Cos(110 * GameMath.DEG2RAD / 2);
        var eye = new Vec3d(0, 0, 0);
        var view = new Vec3f(0, 0, 1);
        bool ahead = WalkerGaze.InCone(eye, view, new Vec3d(0, 0, 10), cosHalf);
        bool edge = WalkerGaze.InCone(eye, view, new Vec3d(Math.Sin(50 * GameMath.DEG2RAD) * 10, 0, Math.Cos(50 * GameMath.DEG2RAD) * 10), cosHalf);
        bool side = WalkerGaze.InCone(eye, view, new Vec3d(10, 0, 0), cosHalf);
        bool behind = WalkerGaze.InCone(eye, view, new Vec3d(0, 0, -10), cosHalf);
        check("view cone: ahead and 50 degrees in, 90 degrees and behind out", ahead && edge && !side && !behind, $"{ahead} {edge} {side} {behind}");
    }

    private static void EntryChecks(Action<string, bool, string> check)
    {
        // A door at the origin, entered from the south (z = +2): inside is north, up to 8 + 2 blocks.
        var door = new Vec3d(0.5, 0, 0.5);
        var outside = new Vec3d(0.5, 0, 2.5);
        bool inRoom = WalkerEntry.IsPast(door, outside, 1.5, -3, 8);
        bool onStep = WalkerEntry.IsPast(door, outside, 0.5, 1.5, 8);
        bool inDoorway = WalkerEntry.IsPast(door, outside, 0.5, 0.5, 8);
        bool farAway = WalkerEntry.IsPast(door, outside, 0.5, -15, 8);
        check("walk-in: past the door is inside; the step, the doorway and far off are not", inRoom && !onStep && !inDoorway && !farAway, $"{inRoom} {onStep} {inDoorway} {farAway}");
    }

    private void LiveChecks(Action<string, bool, string> check)
    {
        var accessor = api.World.BlockAccessor;
        var spawn = api.World.DefaultSpawnPosition.AsBlockPos;
        if (accessor.GetChunkAtBlockPos(spawn) == null)
        {
            check("world spawn is loaded", false, "chunk not loaded");
            return;
        }
        int ground = accessor.GetTerrainMapheightAt(spawn);
        var standing = WalkerSpots.FindStanding(accessor, spawn.X + 0.5, ground + 1, spawn.Z + 0.5, 12, 1.95f);
        check("standing spot at the world spawn", standing != null, $"terrain height {ground}");

        var standInType = api.World.GetEntityType(new AssetLocation(StandInCode));
        var guiseType = api.World.GetEntityType(new AssetLocation(GuiseCode));
        var trueType = api.World.GetEntityType(new AssetLocation(TrueFormCode));
        if (standInType == null || guiseType == null || trueType == null)
        {
            check("entity types loaded", false, $"{StandInCode} {standInType != null}, {GuiseCode} {guiseType != null}, {TrueFormCode} {trueType != null}");
            return;
        }

        // High in the open air above the spawn, where nothing stands between them.
        double airY = Math.Min(accessor.MapSizeY - 10, ground + 40);
        var a = new Vec3d(spawn.X + 0.5, airY, spawn.Z + 0.5);
        var b = new Vec3d(a.X, airY, a.Z + Apart);
        standIn = Spawn(standInType, a, 0);
        guise = Spawn(guiseType, b, GameMath.PI);
        var trueForm = Spawn(trueType, new Vec3d(a.X + 6, airY, a.Z), 0);

        var guiseBrain = StalkerModSystem.BrainOf(guise);
        check("brains for both forms", guiseBrain != null && StalkerModSystem.BrainOf(trueForm) != null, "");
        check("guise tasks", HasTasks(guise, typeof(AiTaskWalkerCreep), typeof(AiTaskWalkerStare), typeof(AiTaskWalkerRetreat)), TaskList(guise));
        check("true form tasks", HasTasks(trueForm, typeof(AiTaskWalkerCreep), typeof(AiTaskWalkerStare), typeof(AiTaskWalkerHunt),
            typeof(AiTaskWalkerLunge), typeof(AiTaskWalkerRetreat), typeof(AiTaskWalkerDoorway)), TaskList(trueForm));
        if (guiseBrain == null)
        {
            Cleanup();
            return;
        }

        var sight = new GazeSight(api.World);
        var gaze = system.Config.Gaze;
        var watchers = new List<Entity> { standIn };
        standIn.Pos.Yaw = WalkerTaskBase.YawTo(standIn.Pos, guise.Pos.XYZ);
        var looking = WalkerGaze.Check(guise, standIn, watchers, gaze, sight);
        standIn.Pos.Yaw += GameMath.PI;
        var away = WalkerGaze.Check(guise, standIn, watchers, gaze, sight);
        standIn.Pos.Yaw -= GameMath.PI;
        // Both inside the ground, ten blocks apart, the stand-in looking straight at it: the rock is in the way.
        double buriedY = Math.Max(2, ground - 10);
        guise.Pos.SetPos(b.X, buriedY, b.Z);
        standIn.Pos.SetPos(a.X, buriedY, a.Z);
        var buried = WalkerGaze.Check(guise, standIn, watchers, gaze, sight);
        guise.Pos.SetPos(b);
        standIn.Pos.SetPos(a);
        standIn.Pos.Yaw = WalkerTaskBase.YawTo(standIn.Pos, guise.Pos.XYZ);
        check("gaze: a stand-in looking at it sees it", looking.VictimWatching && looking.AnyWatched, $"{looking.VictimWatching}");
        check("gaze: looking away does not", !away.AnyWatched, "");
        check("gaze: rock in between blocks it", !buried.AnyWatched, "");

        // The slip-away runs on the brain's own tick.
        system.SetStandIn(guiseBrain, standIn, 2);
        phase = 1;
        phaseAtMs = api.World.ElapsedMilliseconds;
        check("slip-away started", true, "");
    }

    private Entity Spawn(EntityProperties type, Vec3d pos, float yaw)
    {
        var entity = api.World.ClassRegistry.CreateEntity(type);
        entity.Pos.SetPos(pos);
        entity.Pos.Yaw = yaw;
        entity.Pos.Pitch = 0;
        api.World.SpawnEntity(entity);
        spawned.Add(entity);
        return entity;
    }

    private static bool HasTasks(Entity body, params Type[] types)
    {
        var tasks = body.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.AllTasks;
        return tasks != null && types.All(t => tasks.Any(task => task.GetType() == t));
    }

    private static string TaskList(Entity body)
    {
        var tasks = body.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.AllTasks;
        return tasks == null ? "no task ai" : string.Join(", ", tasks.Select(t => AiTaskRegistry.TaskCodes.TryGetValue(t.GetType(), out var code) ? code : t.GetType().Name));
    }

    private void Cleanup()
    {
        foreach (var entity in spawned)
        {
            if (entity.Alive)
            {
                entity.Die(EnumDespawnReason.Removed);
            }
        }
        spawned.Clear();
        standIn = null;
        guise = null;
        phase = 0;
    }
}
