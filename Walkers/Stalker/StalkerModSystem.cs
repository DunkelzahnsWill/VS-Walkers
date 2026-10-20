using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Walkers.Commands;
using Walkers.Config;
using Walkers.Core;
using Walkers.Director;
using Walkers.Stalker.Tasks;

namespace Walkers.Stalker;

/// <summary>
/// The stalker AI, on the server: registers the walkers- AI tasks, keeps a <see cref="WalkerBrain"/>
/// for every loaded walker body and ticks it, installs the ladder with the director, and adds
/// <c>/walkers stalker</c>. Bodies the director did not place (a JSON spawn, <c>/walkers spawn</c>)
/// get their own victim and their own ladder updates from here, so the AI works without a director.
/// </summary>
public sealed class StalkerModSystem : ModSystem
{
    /// <summary>Entity code path (walkers domain) of the true form; every other walker code is a guise.</summary>
    public const string TrueFormCode = "skinwalker-true";

    /// <summary>A body without a director picks the nearest counting player within this many blocks.</summary>
    public const double OwnVictimRange = 64;

    /// <summary>The brains' tick, in real milliseconds.</summary>
    private const int TickMs = 100;

    /// <summary>How often bodies without a director climb their ladder, in real milliseconds (the director's own tick is 10 s too).</summary>
    private const int OwnLadderTickMs = 10000;

    /// <summary>A body without a director looks for a victim at most this often.</summary>
    private const int OwnVictimSearchMs = 5000;

    private static StalkerModSystem? instance;

    private ICoreServerAPI api = null!;
    private readonly Dictionary<long, WalkerBrain> brains = new();
    private readonly List<WalkerBrain> tickBuffer = new();

    /// <summary>Doors a walker opened and could not close because someone stood in them, with when to give up.</summary>
    private readonly List<(BlockPos Pos, long UntilMs)> doorsToClose = new();
    private long nextDoorCloseMs;

    /// <summary>Doors queued by <see cref="QueueDoorClose"/> are tried this often, for this long.</summary>
    private const long DoorCloseEveryMs = 1000, DoorCloseForMs = 600000;
    private bool directorChecked;
    private StalkerSelfTest? selfTest;

    public WalkersConfig Config { get; private set; } = null!;

    public StalkerLadder Ladder { get; private set; } = null!;

    /// <summary>The director, once it has started; null without one.</summary>
    public IWalkerDirector? Director
    {
        get
        {
            if (!directorChecked)
            {
                FindDirector();
            }
            return director;
        }
    }

    private IWalkerDirector? director;

    public ILogger Logger => Mod.Logger;

    /// <summary>The brains of every loaded walker body.</summary>
    public IReadOnlyCollection<WalkerBrain> Brains => brains.Values;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void Start(ICoreAPI api)
    {
        // Before any entity is loaded, so the entity JSON finds the codes.
        AiTaskRegistry.Register<AiTaskWalkerStare>(AiTaskWalkerStare.Code);
        AiTaskRegistry.Register<AiTaskWalkerCreep>(AiTaskWalkerCreep.Code);
        AiTaskRegistry.Register<AiTaskWalkerHunt>(AiTaskWalkerHunt.Code);
        AiTaskRegistry.Register<AiTaskWalkerLunge>(AiTaskWalkerLunge.Code);
        AiTaskRegistry.Register<AiTaskWalkerRetreat>(AiTaskWalkerRetreat.Code);
        AiTaskRegistry.Register<AiTaskWalkerDoorway>(AiTaskWalkerDoorway.Code);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        this.api = api;
        instance = this;
        Config = api.GetWalkersConfig();
        Ladder = new StalkerLadder(Config, Mod.Logger, () => Hostility == EnumCreatureHostilityLevel.Off);

        api.Event.OnEntitySpawn += Track;
        api.Event.OnEntityLoaded += Track;
        api.Event.OnEntityDespawn += (entity, _) => Untrack(entity);
        api.Event.RegisterGameTickListener(OnTick, TickMs);
        api.Event.RegisterGameTickListener(OnOwnLadderTick, OwnLadderTickMs, 5000);
        api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, FindDirector);

        selfTest = new StalkerSelfTest(api, this);
        StalkerCommands.Register(api, this, selfTest);
        api.ModLoader.GetModSystem<WalkersCommandsSystem>().Status += lines => StalkerCommands.AddStatusLine(this, lines);
    }

    public override void Dispose()
    {
        if (ReferenceEquals(instance, this))
        {
            instance = null;
        }
        brains.Clear();
    }

    /// <summary>The brain of a loaded walker body; null for any other entity, or on a client.</summary>
    public static WalkerBrain? BrainOf(Entity? body)
    {
        if (body == null || instance == null)
        {
            return null;
        }
        return instance.brains.TryGetValue(body.EntityId, out var brain) && ReferenceEquals(brain.Body, body) ? brain : null;
    }

    /// <summary>True for a body of any walker form: an agent in the walkers domain whose code starts with skinwalker-.</summary>
    public static bool IsWalkerBody(Entity entity) =>
        entity is EntityAgent and not EntityPlayer &&
        entity.Code?.Domain == WalkersModSystem.ModId &&
        entity.Code.Path.StartsWith(WalkersCommandsSystem.CreaturePrefix, StringComparison.Ordinal);

    /// <summary>The world's creatureHostility setting.</summary>
    public EnumCreatureHostilityLevel Hostility => HostilityOf(api.World);

    /// <summary>A world's creatureHostility setting; anything unknown counts as aggressive, as in vanilla.</summary>
    public static EnumCreatureHostilityLevel HostilityOf(IWorldAccessor world) => world.Config.GetString("creatureHostility", "aggressive") switch
    {
        "passive" => EnumCreatureHostilityLevel.Passive,
        "off" => EnumCreatureHostilityLevel.Off,
        _ => EnumCreatureHostilityLevel.Aggressive,
    };

    /// <summary>True when the director placed this body (it has a walker id the director knows).</summary>
    public bool IsDirectorBody(Entity body) => Director != null && Director.TryGetWalkerId(body, out _);

    /// <summary>
    /// The counting players (creature bible, section 5) in a dimension within a range of a point: the
    /// director's list when it runs, else every online player who is alive, fully connected, in
    /// survival or guest mode and not asleep in a bed.
    /// </summary>
    public IEnumerable<Entity> CountingNear(Vec3d pos, int dimension, double range)
    {
        if (Director != null)
        {
            return Director.CountingEntitiesNear(pos, dimension, range);
        }
        return CountingPlayersNear(pos, dimension, range);
    }

    private IEnumerable<Entity> CountingPlayersNear(Vec3d pos, int dimension, double range)
    {
        double rangeSq = range * range;
        foreach (var player in api.World.AllOnlinePlayers)
        {
            var entity = player.Entity;
            if (entity == null || entity.Pos.Dimension != dimension || entity.Pos.SquareDistanceTo(pos) > rangeSq)
            {
                continue;
            }
            if (IsCountingPlayer(player))
            {
                yield return entity;
            }
        }
    }

    /// <summary>Whether an entity counts: the director decides when it runs; without one, players by <see cref="IsCountingPlayer"/>, and any other living entity (a stand-in) counts.</summary>
    public bool IsCounting(Entity entity)
    {
        if (Director != null)
        {
            return Director.IsCounting(entity);
        }
        if (entity is EntityPlayer ep)
        {
            return ep.Player is IPlayer player && IsCountingPlayer(player);
        }
        return entity.Alive;
    }

    private static bool IsCountingPlayer(IPlayer player)
    {
        var entity = player.Entity;
        if (entity == null || !entity.Alive || IsAsleep(entity))
        {
            return false;
        }
        if (player is IServerPlayer sp && sp.ConnectionState != EnumClientState.Playing)
        {
            return false;
        }
        var mode = player.WorldData?.CurrentGameMode;
        return mode == EnumGameMode.Survival || mode == EnumGameMode.Guest;
    }

    /// <summary>True when the entity lies in a bed.</summary>
    public static bool IsAsleep(Entity? entity) => entity is EntityAgent agent && agent.MountedOn is BlockEntityBed;

    /// <summary>True when the sun is below the horizon at a position.</summary>
    public static bool IsNight(IWorldAccessor world, Vec3d pos) => world.Calendar.GetSunPosition(pos, world.Calendar.TotalDays).Y < 0;

    /// <summary>
    /// The victim of a body the director did not place: a stand-in set by command, else the player it
    /// picked, else the nearest counting player within <see cref="OwnVictimRange"/> blocks (searched
    /// at most every 5 seconds). Picking a victim for a body at stage 0 begins a haunt: stage 5 for a
    /// true form, 1 for a guise.
    /// </summary>
    public Entity? ResolveOwnVictim(WalkerBrain brain, ITreeAttribute memory, long nowMs, ref long lastSearchMs)
    {
        var ai = StalkerMemory.Ai(memory);
        long standInId = ai.GetLong(StalkerMemory.OwnVictimEntity);
        if (standInId != 0)
        {
            var standIn = api.World.GetEntityById(standInId);
            if (standIn != null && standIn.Alive)
            {
                return standIn;
            }
            ai.RemoveAttribute(StalkerMemory.OwnVictimEntity);
            if (memory.GetInt(WalkerMemory.Stage) > 0)
            {
                // A stand-in's haunt ends with it, as a player's does.
                memory.SetInt(WalkerMemory.Stage, 0);
                brain.MarkSlipAway("victim died");
            }
            return null;
        }

        var body = brain.Body;
        bool haunting = memory.GetInt(WalkerMemory.Stage) > 0;
        string uid = ai.GetString(StalkerMemory.OwnVictim, "");
        if (uid.Length > 0)
        {
            var chosen = api.World.PlayerByUid(uid) as IServerPlayer;
            if (chosen?.Entity is { } chosenEntity && chosen.ConnectionState == EnumClientState.Playing)
            {
                if (!chosenEntity.Alive)
                {
                    // The haunt ends on the victim's death (creature bible, owner's answers).
                    if (haunting)
                    {
                        memory.SetInt(WalkerMemory.Stage, 0);
                        brain.MarkSlipAway("victim died");
                    }
                    return null;
                }
                if (chosenEntity.Pos.Dimension == body.Pos.Dimension && chosenEntity.Pos.DistanceTo(body.Pos) <= OwnVictimRange * 2)
                {
                    return chosenEntity;
                }
            }
            if (haunting)
            {
                // The stage belongs to this victim: it waits for them rather than carry it to someone else.
                return null;
            }
            ai.RemoveAttribute(StalkerMemory.OwnVictim);
        }

        if (brain.SlipAwayReason != null)
        {
            // A body on its way out begins no new haunt.
            return null;
        }
        if (nowMs - lastSearchMs < OwnVictimSearchMs && lastSearchMs != 0)
        {
            return null;
        }
        lastSearchMs = nowMs;
        Entity? nearest = null;
        double best = double.MaxValue;
        foreach (var candidate in CountingNear(body.Pos.XYZ, body.Pos.Dimension, OwnVictimRange))
        {
            if (candidate is not EntityPlayer)
            {
                continue;
            }
            double distSq = candidate.Pos.SquareDistanceTo(body.Pos);
            if (distSq < best)
            {
                best = distSq;
                nearest = candidate;
            }
        }
        if (nearest is EntityPlayer picked)
        {
            ai.SetString(StalkerMemory.OwnVictim, picked.PlayerUID);
            if (memory.GetInt(WalkerMemory.Stage) <= 0)
            {
                Ladder.Begin(memory, brain.IsTrueForm ? StalkerLadder.HuntStage : 1, api.World.Calendar.TotalHours);
            }
        }
        return nearest;
    }

    /// <summary>Sets a stand-in victim (any entity) for a body the director did not place, and begins a haunt if none runs.</summary>
    public void SetStandIn(WalkerBrain brain, Entity standIn, int stage)
    {
        var memory = brain.Memory;
        var ai = StalkerMemory.Ai(memory);
        ai.SetLong(StalkerMemory.OwnVictimEntity, standIn.EntityId);
        ai.RemoveAttribute(StalkerMemory.OwnVictim);
        Ladder.ForceStage(memory, stage, api.World.Calendar.TotalHours);
    }

    /// <summary>
    /// The gaze just froze a body: stop the vanilla tasks that would walk or turn it, and the path it
    /// was on. While it stays frozen <see cref="AllowTask"/> keeps them from starting.
    /// </summary>
    public void OnFreeze(WalkerBrain brain)
    {
        var taskAi = brain.Body.GetBehavior<EntityBehaviorTaskAI>();
        if (taskAi == null)
        {
            return;
        }
        var active = taskAi.TaskManager.ActiveTasksBySlot;
        bool ownsPath = true;
        for (int slot = 0; slot < active.Length; slot++)
        {
            if (active[slot] is AiTaskWander or AiTaskLookAround)
            {
                taskAi.TaskManager.StopTask(slot);
            }
            else if (active[slot] is AiTaskGetOutOfWater)
            {
                // Its path ends only through the traverser's callbacks, which Stop never fires.
                ownsPath = false;
            }
        }
        if (ownsPath)
        {
            taskAi.PathTraverser?.Stop();
        }
    }

    /// <summary>
    /// Keeps the vanilla wander and look-around tasks off a body that is frozen or busy with a victim,
    /// so they never walk it into the open or turn it while it is watched.
    /// </summary>
    private static bool AllowTask(WalkerBrain brain, IAiTask task)
    {
        if (task is AiTaskWander or AiTaskLookAround)
        {
            return !brain.Frozen && brain.Mode == StalkerMode.Idle;
        }
        return true;
    }

    private void FindDirector()
    {
        if (api == null)
        {
            return;
        }
        directorChecked = true;
        var found = api.GetWalkersDirector();
        if (found == null || ReferenceEquals(found, director))
        {
            return;
        }
        director = found;
        director.RegisterLadder(Ladder);
        director.RegisterRestoreHandler(AiTaskWalkerDoorway.RestoreKind, AiTaskWalkerDoorway.UndoRestore);
        Mod.Logger.Notification("Stalker AI: ladder registered with the director");
    }

    private void Track(Entity entity)
    {
        if (!IsWalkerBody(entity) || brains.TryGetValue(entity.EntityId, out var existing) && ReferenceEquals(existing.Body, entity))
        {
            return;
        }
        var body = (EntityAgent)entity;
        var brain = new WalkerBrain(body, this, api);
        brains[entity.EntityId] = brain;
        var taskAi = body.GetBehavior<EntityBehaviorTaskAI>();
        if (taskAi != null)
        {
            taskAi.TaskManager.OnShouldExecuteTask += task => AllowTask(brain, task);
        }
    }

    private void Untrack(Entity entity)
    {
        if (brains.TryGetValue(entity.EntityId, out var brain) && ReferenceEquals(brain.Body, entity))
        {
            // Let go of a held door first, while the body still has its brain.
            brain.Doorway?.Release();
            brains.Remove(entity.EntityId);
        }
    }

    /// <summary>Closes the door at <paramref name="pos"/> once nobody stands in it, trying each second for ten minutes.</summary>
    public void QueueDoorClose(BlockPos pos)
    {
        if (!doorsToClose.Exists(d => d.Pos.Equals(pos)))
        {
            doorsToClose.Add((pos.Copy(), api.World.ElapsedMilliseconds + DoorCloseForMs));
        }
    }

    private void CloseQueuedDoors(long now)
    {
        if (doorsToClose.Count == 0 || now < nextDoorCloseMs)
        {
            return;
        }
        nextDoorCloseMs = now + DoorCloseEveryMs;
        doorsToClose.RemoveAll(d => now >= d.UntilMs || AiTaskWalkerDoorway.TryClose(api.World, d.Pos) != RestoreResult.Later);
    }

    private void OnTick(float dt)
    {
        CloseQueuedDoors(api.World.ElapsedMilliseconds);
        if (brains.Count == 0)
        {
            return;
        }
        if (!directorChecked)
        {
            FindDirector();
        }
        long now = api.World.ElapsedMilliseconds;
        tickBuffer.Clear();
        tickBuffer.AddRange(brains.Values);
        foreach (var brain in tickBuffer)
        {
            try
            {
                brain.Tick(now);
                brain.Doorway?.ReleaseIfStale(now);
            }
            catch (Exception e)
            {
                Mod.Logger.Error("Stalker AI: the tick of {0} #{1} failed, removing its brain: {2}", brain.Body.Code, brain.Body.EntityId, e);
                brains.Remove(brain.Body.EntityId);
            }
        }
        selfTest?.OnTick(now);
    }

    /// <summary>The ladder of bodies the director did not place; the director updates its own walkers.</summary>
    private void OnOwnLadderTick(float dt)
    {
        if (brains.Count == 0)
        {
            return;
        }
        double now = api.World.Calendar.TotalHours;
        tickBuffer.Clear();
        tickBuffer.AddRange(brains.Values);
        foreach (var brain in tickBuffer)
        {
            var body = brain.Body;
            if (!body.Alive || IsDirectorBody(body))
            {
                continue;
            }
            var memory = brain.Memory;
            if (memory.GetInt(WalkerMemory.Stage) <= 0)
            {
                continue;
            }
            var victim = brain.Victim;
            bool asleep = IsAsleep(victim);
            var input = new LadderInput(
                now,
                victim,
                Exposed: victim != null && (IsCounting(victim) || asleep),
                Alone: !brain.Company,
                Night: victim != null && IsNight(api.World, victim.Pos.XYZ),
                Asleep: asleep);
            if (Ladder.Update(memory, input) == LadderOutcome.HuntEnded)
            {
                memory.SetInt(WalkerMemory.Stage, 0);
                brain.MarkSlipAway("hunt over");
            }
        }
    }
}

/// <summary>The world's creatureHostility setting, as the stalker AI reads it.</summary>
public enum EnumCreatureHostilityLevel
{
    Aggressive,
    Passive,
    Off,
}
