using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Walkers.Commands;
using Walkers.Config;

namespace Walkers.Director;

/// <summary>
/// The director (creature bible, section 8): decides who is haunted, when, and where a walker
/// appears; keeps every walker's record, the ledger of deaths and returns, and the restore list; and
/// saves all of it in the save game. Server side, apart from registering the walker entity class.
///
/// This file holds the state, the save, and the members of <see cref="IWalkerDirector"/>. The tick is
/// in DirectorSystem.Tick.cs, players and stand-ins in DirectorSystem.Players.cs, placement and sight in
/// DirectorSystem.Placement.cs, the restore list in DirectorSystem.Restore.cs, and /walkers director in
/// DirectorSystem.Commands.cs.
/// </summary>
public partial class DirectorSystem : ModSystem, IWalkerDirector
{
    /// <summary>The save-game key of the whole director state.</summary>
    public const string SaveKey = Core.WalkersModSystem.ModId + ":director";

    private ICoreServerAPI sapi = null!;
    private WalkersConfig config = null!;
    private DirectorSaveData data = new();

    private readonly Dictionary<int, WalkerRecord> walkers = new();
    private readonly Dictionary<int, TreeAttribute> memories = new();
    private readonly Dictionary<string, PlayerRecord> playerRecords = new();
    private IWalkerLadder? ladder;

    // Not saved: bodies never outlive a restart, and the timers only pace placements.

    /// <summary>
    /// Each walker's current body. Held by reference, because a feature may attach a body before
    /// SpawnEntity gives it an id (creature bible, section 4: the record goes in before the spawn).
    /// </summary>
    private readonly Dictionary<int, Entity> bodies = new();
    /// <summary>Bodies the director is removing itself, so their despawn is not read as slipping away.</summary>
    private readonly HashSet<long> removing = new();
    /// <summary>Real time (ElapsedMilliseconds) before which the director places no new body for a walker.</summary>
    private readonly Dictionary<int, long> placeAfterMs = new();
    /// <summary>Real time each walker went down, for the fallback release when nobody reports an outcome.</summary>
    private readonly Dictionary<int, long> downSinceMs = new();
    /// <summary>The stage last seen in each walker's memory, to notice the ladder's changes.</summary>
    private readonly Dictionary<int, int> knownStage = new();

    public override bool ShouldLoad(EnumAppSide forSide) => true;

    public override void Start(ICoreAPI api)
    {
        api.RegisterEntity(EntityWalker.ClassCode, typeof(EntityWalker));
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;
        config = api.GetWalkersConfig();
        api.ObjectCache[WalkersDirectorExtensions.CacheKey] = this;

        api.Event.SaveGameLoaded += OnSaveGameLoaded;
        api.Event.GameWorldSave += OnGameWorldSave;
        api.Event.OnTrySpawnEntity += OnTrySpawnEntity;
        api.Event.OnEntityDeath += OnEntityDeath;
        api.Event.OnEntityDespawn += OnEntityDespawn;
        api.Event.ChunkColumnLoaded += OnChunkColumnLoaded;
        StartPlayers();
        RegisterRestoreHandler(RestoreEntry.KindBlock, UndoBlockSwap);

        // The tick starts once the world runs: every system has started by then, and the calendar shows
        // the real time only from here on (it still shows the world's start inside SaveGameLoaded).
        api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, OnRunGame);

        RegisterCommands();
        RegisterFursCommands();
        api.ModLoader.GetModSystem<WalkersCommandsSystem>().Status += AddStatusLines;
    }

    private void OnRunGame()
    {
        if (config.Debug.StandInVictims)
        {
            foreach (var standIn in standIns.Values)
            {
                KeepLoadedAround(standIn);
            }
        }
        ProcessRestore(startup: true);
        WarnIfDeerCountryRare();
        sapi.Event.RegisterGameTickListener(OnSecond, e => Mod.Logger.Error("Director update failed: {0}", e), 1000);
    }

    public override void Dispose()
    {
        // On /stop the final GameWorldSave comes after Dispose, so the state itself stays.
        sapi?.ObjectCache.Remove(WalkersDirectorExtensions.CacheKey);
    }

    // ---- Save and load ----

    private void OnSaveGameLoaded()
    {
        var bytes = sapi.WorldManager.SaveGame.GetData(SaveKey);
        if (bytes != null)
        {
            try
            {
                data = SerializerUtil.Deserialize<DirectorSaveData>(bytes);
                if (data.Version > DirectorSaveData.CurrentVersion)
                {
                    Mod.Logger.Warning("The director's saved state is from a newer version of the mod ({0}, this one reads {1}); reading what it can", data.Version, DirectorSaveData.CurrentVersion);
                }
            }
            catch (Exception e)
            {
                // As vanilla does with its storm data: a state that cannot be read starts fresh rather than stopping the world.
                Mod.Logger.Warning("Could not read the director's saved state, starting fresh: {0}", e.Message);
                data = new DirectorSaveData();
            }
        }
        data.Version = DirectorSaveData.CurrentVersion;
        Index();

        // Bodies never survive a restart: a walker still down when the server stopped counts as left alone.
        foreach (var walker in walkers.Values.Where(w => w.State == WalkerState.Down).ToList())
        {
            ReleaseCore(walker, WalkerRelease.Unloaded, false);
        }
        LoadStandIns();

        // tests/smoke/director.expect checks this line.
        Mod.Logger.Notification("Director loaded: {0} walker(s) ({1}), {2} ledger entr{3}, {4} block(s) to restore",
            walkers.Count, DescribeStates(), data.Ledger.Count, data.Ledger.Count == 1 ? "y" : "ies", data.Restore.Count);
    }

    private void OnGameWorldSave()
    {
        foreach (var walker in walkers.Values)
        {
            walker.Memory = memories[walker.Id].ToBytes();
        }
        sapi.WorldManager.SaveGame.StoreData(SaveKey, SerializerUtil.Serialize(data));
        Mod.Logger.Debug("Director saved: {0} walker(s), {1} ledger entries, {2} block(s) to restore", walkers.Count, data.Ledger.Count, data.Restore.Count);
    }

    /// <summary>Builds the lookups from <see cref="data"/>, and each walker's memory tree from its bytes.</summary>
    private void Index()
    {
        walkers.Clear();
        memories.Clear();
        playerRecords.Clear();
        foreach (var walker in data.Walkers)
        {
            var tree = new TreeAttribute();
            if (walker.Memory != null)
            {
                try
                {
                    tree.FromBytes(walker.Memory);
                }
                catch (Exception e)
                {
                    Mod.Logger.Warning("Walker {0}'s memory could not be read and starts empty: {1}", walker.Id, e.Message);
                    tree = new TreeAttribute();
                }
            }
            walkers[walker.Id] = walker;
            memories[walker.Id] = tree;
            knownStage[walker.Id] = tree.GetInt(WalkerMemory.Stage);
            SyncMemory(walker);
            data.NextWalkerId = Math.Max(data.NextWalkerId, walker.Id + 1);
        }
        foreach (var record in data.Players)
        {
            playerRecords[record.Uid] = record;
        }
    }

    // ---- Walkers and their memory ----

    public bool TryGetWalkerId(Entity body, out int walkerId)
    {
        walkerId = 0;
        var tree = body.Attributes?.GetTreeAttribute(WalkerMemory.TreeKey);
        if (tree == null || !tree.HasAttribute(WalkerMemory.Id))
        {
            return false;
        }
        walkerId = tree.GetInt(WalkerMemory.Id);
        return walkers.ContainsKey(walkerId);
    }

    public WalkerInfo? GetWalker(int walkerId) => walkers.TryGetValue(walkerId, out var walker) ? Info(walker) : null;

    public IReadOnlyList<WalkerInfo> Walkers => walkers.Values.OrderBy(w => w.Id).Select(Info).ToList();

    public ITreeAttribute? GetMemory(int walkerId) => memories.TryGetValue(walkerId, out var tree) ? tree : null;

    private WalkerInfo Info(WalkerRecord walker) => new(
        walker.Id,
        walker.State,
        walker.VictimUid,
        walker.VictimUid == null ? null : NameOf(walker.VictimUid),
        memories[walker.Id].GetInt(WalkerMemory.Stage),
        walker.Returns,
        walker.GuiseType,
        bodies.TryGetValue(walker.Id, out var body) ? body.EntityId : 0,
        walker.HauntStartedTotalDays);

    /// <summary>A new walker in the roster, resting.</summary>
    private WalkerRecord CreateWalker()
    {
        var walker = new WalkerRecord { Id = data.NextWalkerId++, State = WalkerState.Resting };
        data.Walkers.Add(walker);
        walkers[walker.Id] = walker;
        memories[walker.Id] = new TreeAttribute();
        knownStage[walker.Id] = 0;
        SyncMemory(walker);
        Mod.Logger.Notification("A new walker joins the roster: walker {0}", walker.Id);
        return walker;
    }

    /// <summary>
    /// Writes the director's own keys into the walker's memory from its record. The memory is the
    /// copy every body reads; the record is what the director decides from.
    /// </summary>
    private void SyncMemory(WalkerRecord walker)
    {
        var tree = memories[walker.Id];
        tree.SetInt(WalkerMemory.Id, walker.Id);
        tree.SetInt(WalkerMemory.Returns, walker.Returns);
        tree.SetInt(WalkerMemory.Escalation, EscalationLevel(walker));
        bool keepsVictim = walker.State is WalkerState.Haunting or WalkerState.Paused or WalkerState.Down;
        if (!keepsVictim)
        {
            tree.SetInt(WalkerMemory.Stage, 0);
            knownStage[walker.Id] = 0;
        }
        if (keepsVictim && walker.VictimUid != null)
        {
            tree.SetString(WalkerMemory.Victim, walker.VictimUid);
        }
        else
        {
            tree.RemoveAttribute(WalkerMemory.Victim);
            tree.RemoveAttribute(WalkerMemory.VictimEntity);
        }
        if (walker.GuiseType != null)
        {
            tree.SetString(WalkerMemory.Guise, walker.GuiseType);
        }
        tree.SetBool(WalkerMemory.Withdraw, walker.State is not (WalkerState.Haunting or WalkerState.Down));
        if (!tree.HasAttribute(WalkerMemory.Pace))
        {
            tree.SetFloat(WalkerMemory.Pace, 1);
        }
    }

    // ---- Bodies ----

    public void AttachBody(int walkerId, Entity newBody)
    {
        if (!walkers.TryGetValue(walkerId, out var walker))
        {
            Mod.Logger.Warning("AttachBody for unknown walker {0}; the body keeps no memory", walkerId);
            return;
        }
        newBody.Attributes.SetAttribute(WalkerMemory.TreeKey, memories[walkerId]);
        ForgetBody(walkerId);
        bodies[walkerId] = newBody;
        if (walker.State is WalkerState.Haunting && IsStandInUid(walker.VictimUid))
        {
            newBody.AlwaysActive = true;
        }
    }

    public float ReturnHealthBonus(int walkerId) =>
        walkers.TryGetValue(walkerId, out var walker)
            ? Math.Min(config.Return.HealthBonusCap, walker.Returns * config.Return.HealthBonusPerReturn)
            : 0;

    /// <summary>The walker's current body, alive or dead, once it is spawned and while it is loaded.</summary>
    private Entity? BodyOf(WalkerRecord walker) =>
        bodies.TryGetValue(walker.Id, out var body) && body.EntityId != 0 && sapi.World.GetEntityById(body.EntityId) == body ? body : null;

    /// <summary>Drops what the director tracked about a walker's current body, when that body is replaced or gone.</summary>
    private void ForgetBody(int walkerId)
    {
        bodies.Remove(walkerId);
        leftAloneBodies.Remove(walkerId);
        hurtCounters.Remove(walkerId);
        lastHitMs.Remove(walkerId);
    }

    private bool IsCurrentBody(int walkerId, Entity entity) => bodies.TryGetValue(walkerId, out var body) && body == entity;

    /// <summary>Removes a body silently (no drops, no death), as slipping away does, and remembers that the director did it.</summary>
    private void RemoveBody(WalkerRecord walker, Entity body)
    {
        removing.Add(body.EntityId);
        body.Die(EnumDespawnReason.Expire);
    }

    // ---- Reports ----

    public void ReportAct(int walkerId, string actCode)
    {
        if (walkers.TryGetValue(walkerId, out var walker) && walker.State == WalkerState.Haunting)
        {
            ladder?.OnAct(memories[walkerId], actCode);
            CheckStage(walker);
        }
    }

    public void ReportHarm(int walkerId, Entity byEntity)
    {
        if (!walkers.TryGetValue(walkerId, out var walker) || walker.State is not (WalkerState.Haunting or WalkerState.Paused))
        {
            return;
        }
        var uid = UidOf(byEntity);
        if (uid == null)
        {
            return;
        }
        if (uid == walker.VictimUid)
        {
            ladder?.OnHarm(memories[walkerId]);
            CheckStage(walker);
            return;
        }
        var record = PlayerRecordFor(uid, NameOf(byEntity));
        if (record.MarkedByWalker != walkerId)
        {
            record.MarkedByWalker = walkerId;
            Mod.Logger.Notification("{0} hurt walker {1}, which haunts {2}: they are its next victim", record.Name, walkerId, NameOf(walker.VictimUid!));
        }
    }

    public void EndHaunt(int walkerId, HauntEndReason reason)
    {
        if (walkers.TryGetValue(walkerId, out var walker) && walker.State is WalkerState.Haunting or WalkerState.Paused)
        {
            EndHauntCore(walker, reason);
        }
    }

    public void RegisterLadder(IWalkerLadder ladder)
    {
        this.ladder = ladder;
        Mod.Logger.Notification("Director: the ladder is {0}", ladder.GetType().FullName ?? ladder.GetType().Name);
    }

    /// <summary>Raises <see cref="StageChanged"/> when the ladder moved the walker's stage.</summary>
    private void CheckStage(WalkerRecord walker)
    {
        int stage = memories[walker.Id].GetInt(WalkerMemory.Stage);
        if (!knownStage.TryGetValue(walker.Id, out var before) || before != stage)
        {
            knownStage[walker.Id] = stage;
            Mod.Logger.Notification("Walker {0} moves from stage {1} to stage {2}", walker.Id, before, stage);
            Raise(StageChanged, Info(walker), before);
        }
    }

    // ---- Death and the ledger ----

    private void OnEntityDeath(Entity entity, DamageSource? damage)
    {
        OnPossibleVictimDeath(entity);
        if (!TryGetWalkerId(entity, out var walkerId))
        {
            return;
        }
        var walker = walkers[walkerId];
        if (walker.State == WalkerState.Down)
        {
            // Forms replacing the guise's carcass with the true body (creature bible, section 7): the second
            // death is the same death. If the true body's death came first, it carried no cause; this one does.
            var entry = LatestEntry(walkerId);
            if (entry != null && entry.Cause == UnknownCause && damage != null)
            {
                entry.Cause = DescribeDeath(damage);
                entry.KillerName ??= KillerName(damage);
            }
            return;
        }
        // The current body, or one it just left: any body of a haunting walker that dies is the walker dying.
        // A walker already in the ledger or banished does not die again.
        if (walker.State is WalkerState.Waiting or WalkerState.Banished ||
            (walker.State is not (WalkerState.Haunting or WalkerState.Paused) && !IsCurrentBody(walkerId, entity)))
        {
            return;
        }
        GoDown(walker, entity.Pos, DescribeDeath(damage), KillerName(damage));
        // A HauntEnded handler may already have released it.
        if (damage?.Source == EnumDamageSource.Void && walker.State == WalkerState.Down)
        {
            ReleaseCore(walker, WalkerRelease.Void, false);
        }
    }

    /// <summary>The walker died: its haunt ends, and its return goes into the ledger at once (unless it is being banished).</summary>
    private void GoDown(WalkerRecord walker, EntityPos? pos, string cause, string? killer, bool banishing = false)
    {
        // A body that dies while withdrawing after its haunt ended still counts against its last victim.
        var victimUid = walker.VictimUid ?? walker.LastVictimUid;
        bool wasHaunting = walker.State is WalkerState.Haunting or WalkerState.Paused;
        if (wasHaunting)
        {
            // HauntEnded is raised below, once the ledger entry exists.
            EndHauntCore(walker, HauntEndReason.WalkerDied, WalkerState.Down, raise: false);
        }
        walker.State = WalkerState.Down;
        walker.VictimUid = victimUid;
        var at = pos?.AsBlockPos ?? LastPos(walker);
        var now = sapi.World.Calendar.TotalDays;
        var entry = new LedgerRecord
        {
            WalkerId = walker.Id,
            VictimUid = victimUid,
            VictimName = victimUid == null ? null : NameOf(victimUid),
            DiedTotalDays = now,
            X = at.X,
            Y = at.Y,
            Z = at.Z,
            Dimension = at.dimension,
            Cause = cause,
            KillerName = killer,
            ReturnsBefore = walker.Returns,
            ReturnTotalDays = now + SeasonDays(config.Return.AfterSeasons),
        };
        data.Ledger.Add(entry);
        downSinceMs[walker.Id] = sapi.World.ElapsedMilliseconds;
        SyncMemory(walker);
        if (wasHaunting)
        {
            Raise(HauntEnded, Info(walker), HauntEndReason.WalkerDied);
        }
        if (!banishing)
        {
            Mod.Logger.Notification("Walker {0} died ({1}){2}; it returns on day {3:0.#} unless it is banished",
                walker.Id, cause, killer == null ? "" : ", killed by " + killer, entry.ReturnTotalDays);
        }
    }

    public void Banish(int walkerId, IPlayer? byPlayer, string cause)
    {
        if (!walkers.TryGetValue(walkerId, out var walker) || walker.State == WalkerState.Banished)
        {
            return;
        }
        // A walker that is down or already in the ledger is banished on that same death; any other dies now.
        if (walker.State is not (WalkerState.Down or WalkerState.Waiting) || LatestEntry(walkerId) == null)
        {
            GoDown(walker, BodyOf(walker)?.Pos, cause, byPlayer?.PlayerName, banishing: true);
        }
        var entry = LatestEntry(walkerId)!;
        entry.Outcome = LedgerOutcome.Banished;
        entry.Cause = cause;
        entry.KillerName = byPlayer?.PlayerName ?? entry.KillerName;
        walker.State = WalkerState.Banished;
        walker.VictimUid = null;
        downSinceMs.Remove(walkerId);
        foreach (var record in playerRecords.Values.Where(r => r.MarkedByWalker == walkerId))
        {
            record.MarkedByWalker = 0;
        }
        SyncMemory(walker);
        Mod.Logger.Notification("Walker {0} is banished ({1}{2}). Its id is retired.", walkerId, cause, byPlayer == null ? "" : ", by " + byPlayer.PlayerName);
        Raise(WalkerBanished, Info(walker), Snapshot(entry));
    }

    public void Release(int walkerId, WalkerRelease reason, bool fursTaken)
    {
        if (!walkers.TryGetValue(walkerId, out var walker) || walker.State == WalkerState.Banished)
        {
            return;
        }
        if (walker.State == WalkerState.Waiting)
        {
            // The same death again: only the reason and the furs change.
            var latest = LatestEntry(walkerId);
            if (latest != null && latest.Outcome == LedgerOutcome.Released)
            {
                latest.Release = reason;
                latest.FursTaken |= fursTaken;
            }
            return;
        }
        if (walker.State != WalkerState.Down)
        {
            GoDown(walker, BodyOf(walker)?.Pos, UnknownCause, null);
        }
        ReleaseCore(walker, reason, fursTaken);
    }

    private void ReleaseCore(WalkerRecord walker, WalkerRelease reason, bool fursTaken)
    {
        var entry = LatestEntry(walker.Id);
        if (entry == null || walker.State != WalkerState.Down)
        {
            return;
        }
        entry.Outcome = LedgerOutcome.Released;
        entry.Release = reason;
        entry.FursTaken |= fursTaken;
        walker.State = WalkerState.Waiting;
        downSinceMs.Remove(walker.Id);
        SyncMemory(walker);
        Mod.Logger.Notification("Walker {0} is in the ledger ({1}{2}); it returns on day {3:0.#}",
            walker.Id, reason, entry.FursTaken ? ", furs taken" : "", entry.ReturnTotalDays);
        Raise(WalkerReleased, Info(walker), Snapshot(entry));
    }

    public LedgerEntry? GetLedgerEntry(int walkerId)
    {
        var entry = LatestEntry(walkerId);
        return entry == null ? null : Snapshot(entry);
    }

    public IReadOnlyList<LedgerEntry> Ledger => data.Ledger.Select(Snapshot).ToList();

    private LedgerRecord? LatestEntry(int walkerId) => data.Ledger.LastOrDefault(e => e.WalkerId == walkerId);

    private static LedgerEntry Snapshot(LedgerRecord e) => new(
        e.WalkerId, e.VictimUid, e.VictimName, e.DiedTotalDays, new BlockPos(e.X, e.Y, e.Z, e.Dimension), e.Cause,
        e.KillerName, e.Outcome, e.Release, e.FursTaken, e.ReturnsBefore, e.ReturnTotalDays);

    /// <summary>In-game days in a number of seasons: a season is three months (creature bible, section 7).</summary>
    private double SeasonDays(double seasons) => seasons * 3 * sapi.World.Calendar.DaysPerMonth;

    /// <summary>The ledger's cause when nothing says how it died.</summary>
    private const string UnknownCause = "unknown";

    private static string DescribeDeath(DamageSource? damage)
    {
        if (damage == null)
        {
            return UnknownCause;
        }
        var how = damage.Source switch
        {
            EnumDamageSource.Fall => "a fall",
            EnumDamageSource.Drown => "drowning",
            EnumDamageSource.Void => "the void",
            EnumDamageSource.Suicide => "removed",
            _ => null,
        };
        if (how != null)
        {
            return how;
        }
        var by = damage.GetCauseEntity();
        var type = damage.Type.ToString();
        return by switch
        {
            EntityPlayer => type + " by a player",
            not null => type + " by " + by.Code.ToShortString(),
            _ => type + " (" + damage.Source + ")",
        };
    }

    private static string? KillerName(DamageSource? damage) => damage?.GetCauseEntity() switch
    {
        EntityPlayer player => player.Player?.PlayerName ?? player.GetName(),
        null => null,
        var other => other.GetName(),
    };

    private BlockPos LastPos(WalkerRecord walker) =>
        walker.HasLastPos ? new BlockPos((int)walker.LastX, (int)walker.LastY, (int)walker.LastZ, walker.LastDimension) : new BlockPos(0, 0, 0, 0);

    private void RememberPos(WalkerRecord walker, EntityPos pos)
    {
        walker.LastX = pos.X;
        walker.LastY = pos.Y;
        walker.LastZ = pos.Z;
        walker.LastDimension = pos.Dimension;
        walker.HasLastPos = true;
    }

    // ---- Spawns and despawns ----

    /// <summary>
    /// Vetoes every natural walker spawn: the runtime spawner and worldgen are the only callers of this
    /// event (EntitySpawnerResult.cs:45, GenCreatures.cs:344). The director's own World.SpawnEntity
    /// calls never fire it, and neither do /walkers spawn and /entity spawn.
    /// </summary>
    private bool OnTrySpawnEntity(IBlockAccessor blockAccessor, ref EntityProperties properties, Vec3d spawnPosition, long herdId) =>
        !IsWalkerType(properties.Code);

    public static bool IsWalkerType(AssetLocation? code) =>
        code != null && code.Domain == Core.WalkersModSystem.ModId && code.Path.StartsWith(WalkersCommandsSystem.CreaturePrefix, StringComparison.Ordinal);

    private void OnEntityDespawn(Entity entity, EntityDespawnData? despawn)
    {
        OnPossibleStandInDespawn(entity, despawn);
        bool ours = removing.Remove(entity.EntityId);
        if (!TryGetWalkerId(entity, out var walkerId) || !IsCurrentBody(walkerId, entity))
        {
            return;
        }
        var walker = walkers[walkerId];
        var reason = despawn?.Reason ?? EnumDespawnReason.Removed;
        ForgetBody(walkerId);
        RememberPos(walker, entity.Pos);
        if (walker.State == WalkerState.Down)
        {
            // The body is gone. Forms usually reported an outcome already; if not, nobody burned it.
            ReleaseCore(walker, reason == EnumDespawnReason.Unload ? WalkerRelease.Unloaded : WalkerRelease.LeftAlone, false);
            return;
        }
        if (walker.State == WalkerState.Haunting)
        {
            // Slipped away (the Stalker AI's Die(Expire)): back after stalk.reappearSeconds. Unloaded or removed by
            // the director: placed again as soon as the rules allow.
            bool slippedAway = reason == EnumDespawnReason.Expire && !ours;
            placeAfterMs[walkerId] = sapi.World.ElapsedMilliseconds + (slippedAway ? (long)(config.Stalk.ReappearSeconds * 1000) : 0);
            Mod.Logger.Debug("Walker {0}'s body is gone ({1}{2})", walkerId, reason, slippedAway ? ", slipped away" : "");
        }
    }

    // ---- Events ----

    public event Action<WalkerInfo, Entity>? BodyPlaced;
    public event Action<WalkerInfo>? HauntStarted;
    public event Action<WalkerInfo, HauntEndReason>? HauntEnded;
    public event Action<WalkerInfo, int>? StageChanged;
    public event Action<WalkerInfo, LedgerEntry>? WalkerReleased;
    public event Action<WalkerInfo, LedgerEntry>? WalkerBanished;
    public event Action<WalkerInfo>? WalkerReturned;

    /// <summary>Calls each handler on its own, so one that throws stops neither the others nor the director.</summary>
    private void Raise<T>(Action<T>? handlers, T arg)
    {
        if (handlers == null)
        {
            return;
        }
        foreach (Action<T> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(arg);
            }
            catch (Exception e)
            {
                Mod.Logger.Error("A director event handler failed: {0}", e);
            }
        }
    }

    private void Raise<T1, T2>(Action<T1, T2>? handlers, T1 arg1, T2 arg2)
    {
        if (handlers == null)
        {
            return;
        }
        foreach (Action<T1, T2> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(arg1, arg2);
            }
            catch (Exception e)
            {
                Mod.Logger.Error("A director event handler failed: {0}", e);
            }
        }
    }

    // ---- Status ----

    private string DescribeStates()
    {
        var counts = walkers.Values.GroupBy(w => w.State).OrderBy(g => g.Key).Select(g => $"{g.Count()} {g.Key.ToString().ToLowerInvariant()}");
        var text = string.Join(", ", counts);
        return text.Length == 0 ? "none" : text;
    }

    private void AddStatusLines(List<string> lines)
    {
        lines.Add(Lang.Get("walkers:director-status-line", DescribeStates(), ActiveCount(), ActiveCap(), data.Ledger.Count, data.Restore.Count));
    }
}
