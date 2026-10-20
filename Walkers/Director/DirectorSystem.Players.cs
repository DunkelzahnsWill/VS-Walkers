using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Walkers.Config;

namespace Walkers.Director;

// Players as the director sees them: who counts, who is eligible, and how exposed each one is
// (creature bible, sections 5 and 8). A victim is an entity with an optional player behind it, so a
// debug stand-in (a vanilla player bot) runs through the same code as a player.
public partial class DirectorSystem
{
    /// <summary>Vanilla's player bot, which serves as a debug stand-in victim.</summary>
    private static readonly AssetLocation StandInCode = new("game", "playerbot");

    private const string StandInPrefix = "standin-";

    /// <summary>How far out the near-forest ring samples the climate, in blocks (creature bible, section 8).</summary>
    private const int ForestRingRadius = 32;

    private const int ForestRingSamples = 8;

    /// <summary>A stand-in turns its head this many radians a second, so it looks around like a player would.</summary>
    private const float StandInTurnPerSecond = 0.05f;

    /// <summary>Chunk columns kept loaded around a stand-in, in each direction: covers the 64-block placement ring and its sight lines.</summary>
    private const int StandInChunkRadius = 3;

    /// <summary>
    /// What the director knows about one player or stand-in this tick. Rebuilt on every director tick
    /// from the online players and the loaded stand-ins.
    /// </summary>
    private sealed class Candidate
    {
        public required string Uid;
        public required string Name;
        public required Entity Entity;
        public IServerPlayer? Player;
        public bool IsStandIn;

        /// <summary>Counting player, creature bible section 5.</summary>
        public bool Counting;
        /// <summary>Counting, in the main world, past their grace time, not opted out: may be chosen and their clock runs.</summary>
        public bool Eligible;
        public bool Surface;
        public bool NearForest;
        public bool Alone;
        public bool Night;
        public bool Asleep;
        /// <summary>Distance to the nearest other counting player in the same dimension; infinity when there is none.</summary>
        public double NearestOther = double.PositiveInfinity;
    }

    /// <summary>When each online player last moved, turned or used a block, for director.afkMinutes.</summary>
    private sealed class Activity
    {
        public double X, Y, Z;
        public float Yaw, Pitch;
        public long LastActiveMs;
    }

    private readonly Dictionary<string, Candidate> candidates = new();
    private readonly Dictionary<string, Activity> activity = new();
    /// <summary>Stand-ins by uid, from the save data.</summary>
    private readonly Dictionary<string, StandInRecord> standIns = new();

    private void StartPlayers()
    {
        sapi.Event.PlayerNowPlaying += player => MarkActive(player.PlayerUID, player.Entity);
        sapi.Event.PlayerDisconnect += player => activity.Remove(player.PlayerUID);
        sapi.Event.PlayerDeath += (player, _) => OnPossibleVictimDeath(player.Entity);
        sapi.Event.DidUseBlock += (player, sel) => OnPlayerBlockChange(player, sel);
        sapi.Event.DidBreakBlock += (player, _, sel) => OnPlayerBlockChange(player, sel);
        sapi.Event.DidPlaceBlock += (player, _, sel, _) => OnPlayerBlockChange(player, sel);
    }

    /// <summary>A player used, broke or placed a block: they are active, and a walker's change to that block is theirs now.</summary>
    private void OnPlayerBlockChange(IServerPlayer player, BlockSelection? selection)
    {
        MarkActive(player.PlayerUID, player.Entity);
        DropRestoreAt(selection?.Position);
    }

    // ---- Counting players ----

    public bool IsCounting(Entity entity)
    {
        if (!entity.Alive)
        {
            return false;
        }
        if (entity is EntityPlayer playerEntity)
        {
            return playerEntity.Player is IServerPlayer player && IsCountingPlayer(player, playerEntity);
        }
        return IsLiveStandIn(entity);
    }

    public IEnumerable<Entity> CountingEntitiesNear(Vec3d pos, int dimension, double range)
    {
        double rangeSq = range * range;
        foreach (var entity in CountingEntities())
        {
            if (entity.Pos.Dimension == dimension && entity.Pos.SquareDistanceTo(pos) <= rangeSq)
            {
                yield return entity;
            }
        }
    }

    private IEnumerable<Entity> CountingEntities()
    {
        foreach (var player in sapi.World.AllOnlinePlayers.OfType<IServerPlayer>())
        {
            if (player.Entity != null && IsCountingPlayer(player, player.Entity))
            {
                yield return player.Entity;
            }
        }
        foreach (var standIn in LoadedStandIns())
        {
            yield return standIn;
        }
    }

    private bool IsCountingPlayer(IServerPlayer player, EntityPlayer entity) =>
        entity.Alive &&
        player.ConnectionState == EnumClientState.Playing &&
        player.WorldData.CurrentGameMode is EnumGameMode.Survival or EnumGameMode.Guest &&
        !IsAsleep(entity) &&
        !IsIdle(player.PlayerUID);

    private static bool IsAsleep(Entity entity) => entity is EntityAgent agent && agent.MountedOn is BlockEntityBed;

    private bool IsIdle(string uid)
    {
        var minutes = config.Director.AfkMinutes;
        if (minutes <= 0)
        {
            return false;
        }
        // A player not seen yet (just joined) counts as active.
        return activity.TryGetValue(uid, out var seen) && sapi.World.ElapsedMilliseconds - seen.LastActiveMs > minutes * 60_000;
    }

    private void MarkActive(string uid, Entity? entity)
    {
        if (!activity.TryGetValue(uid, out var seen))
        {
            seen = activity[uid] = new Activity();
        }
        seen.LastActiveMs = sapi.World.ElapsedMilliseconds;
        if (entity != null)
        {
            Sample(seen, entity.Pos);
        }
    }

    private static void Sample(Activity seen, EntityPos pos)
    {
        seen.X = pos.X;
        seen.Y = pos.Y;
        seen.Z = pos.Z;
        seen.Yaw = pos.Yaw;
        seen.Pitch = pos.Pitch;
    }

    /// <summary>Once a second: a player who moved or turned since the last sample is active.</summary>
    private void TrackActivity()
    {
        long now = sapi.World.ElapsedMilliseconds;
        foreach (var player in sapi.World.AllOnlinePlayers.OfType<IServerPlayer>())
        {
            if (player.Entity == null)
            {
                continue;
            }
            var pos = player.Entity.Pos;
            if (!activity.TryGetValue(player.PlayerUID, out var seen))
            {
                seen = activity[player.PlayerUID] = new Activity { LastActiveMs = now };
                Sample(seen, pos);
                continue;
            }
            bool moved = Math.Abs(pos.X - seen.X) + Math.Abs(pos.Y - seen.Y) + Math.Abs(pos.Z - seen.Z) > 0.1;
            bool turned = Math.Abs(GameMath.AngleRadDistance(pos.Yaw, seen.Yaw)) > 0.02f || Math.Abs(pos.Pitch - seen.Pitch) > 0.02f;
            if (moved || turned)
            {
                seen.LastActiveMs = now;
            }
            Sample(seen, pos);
        }
    }

    // ---- Candidates: who is online, and how exposed ----

    /// <summary>Rebuilds <see cref="candidates"/>: every online player and every loaded stand-in, with this tick's facts.</summary>
    private void UpdateCandidates()
    {
        candidates.Clear();
        double now = sapi.World.Calendar.TotalDays;
        foreach (var player in sapi.World.AllOnlinePlayers.OfType<IServerPlayer>())
        {
            if (player.Entity == null || player.ConnectionState != EnumClientState.Playing)
            {
                continue;
            }
            PlayerRecordFor(player.PlayerUID, player.PlayerName).LastSeenTotalDays = now;
            candidates[player.PlayerUID] = new Candidate
            {
                Uid = player.PlayerUID,
                Name = player.PlayerName,
                Entity = player.Entity,
                Player = player,
                Counting = IsCountingPlayer(player, player.Entity),
                Asleep = IsAsleep(player.Entity),
            };
        }
        foreach (var (uid, standIn) in standIns)
        {
            // With debug.standInVictims off a stand-in is nobody: its haunt pauses as if it were offline, and ends after director.pausedHauntMaxDays.
            if (config.Debug.StandInVictims && sapi.World.GetEntityById(standIn.EntityId) is { Alive: true } entity)
            {
                PlayerRecordFor(uid, uid).LastSeenTotalDays = now;
                candidates[uid] = new Candidate { Uid = uid, Name = uid, Entity = entity, IsStandIn = true, Counting = true };
            }
        }

        var counting = candidates.Values.Where(c => c.Counting).ToList();
        foreach (var candidate in candidates.Values)
        {
            var pos = candidate.Entity.Pos;
            foreach (var other in counting)
            {
                if (other != candidate && other.Entity.Pos.Dimension == pos.Dimension)
                {
                    candidate.NearestOther = Math.Min(candidate.NearestOther, other.Entity.Pos.DistanceTo(pos.XYZ));
                }
            }
            candidate.Night = IsNight(pos.XYZ);
            if (candidate.IsStandIn)
            {
                // A stand-in is alone and on the surface by definition (creature bible, section 9); the forest is real.
                candidate.Surface = true;
                candidate.Alone = true;
                candidate.NearForest = IsNearForest(pos.AsBlockPos);
                candidate.Eligible = config.Debug.StandInVictims;
                continue;
            }
            candidate.Surface = IsOnSurface(pos.AsBlockPos);
            candidate.NearForest = IsNearForest(pos.AsBlockPos);
            candidate.Alone = candidate.NearestOther > config.Director.IsolationRadius;
            var record = playerRecords[candidate.Uid];
            candidate.Eligible = candidate.Counting &&
                pos.Dimension == Dimensions.NormalWorld &&
                now >= record.FirstSeenTotalDays + config.Director.NewPlayerGraceDays &&
                !(config.Director.AllowPlayerOptOut && candidate.Player!.GetWalkersPrefs().NoHaunt);
        }
    }

    /// <summary>Exposed: the creature bible's gate for a victim's clock and placements (online, eligible, on the surface, near forest).</summary>
    private static bool Exposed(Candidate c) => c.Eligible && c.Surface && c.NearForest;

    private bool IsOnSurface(BlockPos pos)
    {
        int terrain = sapi.World.BlockAccessor.GetTerrainMapheightAt(pos);
        return pos.Y >= terrain - config.Director.MaxDepthBelowSurface;
    }

    /// <summary>
    /// Near forest: forest or shrub density of at least director.minForestDensity where they stand, or at
    /// any of eight worldgen climate samples on a ring 32 blocks out, so a treeline counts.
    /// </summary>
    private bool IsNearForest(BlockPos pos)
    {
        float min = config.Director.MinForestDensity;
        if (min <= 0 || WoodDensity(pos) >= min)
        {
            return true;
        }
        var sample = new BlockPos(pos.dimension);
        for (int i = 0; i < ForestRingSamples; i++)
        {
            double angle = i * GameMath.TWOPI / ForestRingSamples;
            sample.Set(pos.X + (int)(Math.Cos(angle) * ForestRingRadius), pos.Y, pos.Z + (int)(Math.Sin(angle) * ForestRingRadius));
            if (WoodDensity(sample) >= min)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The larger of forest and shrub density from the worldgen climate, 0 where the map region is not loaded.</summary>
    private float WoodDensity(BlockPos pos)
    {
        var climate = sapi.World.BlockAccessor.GetClimateAt(pos, EnumGetClimateMode.WorldGenValues);
        return climate == null ? 0 : Math.Max(climate.ForestDensity, climate.ShrubDensity);
    }

    /// <summary>Night: the sun below the horizon at this spot (creature bible, section 8).</summary>
    private bool IsNight(Vec3d pos) => sapi.World.Calendar.GetSunPosition(pos, sapi.World.Calendar.TotalDays).Y < 0;

    // ---- Player records and names ----

    private PlayerRecord PlayerRecordFor(string uid, string name)
    {
        if (!playerRecords.TryGetValue(uid, out var record))
        {
            record = new PlayerRecord { Uid = uid, Name = name, FirstSeenTotalDays = sapi.World.Calendar.TotalDays };
            if (IsStandInUid(uid))
            {
                // A stand-in skips the grace time: it exists to be haunted.
                record.FirstSeenTotalDays = double.MinValue / 2;
            }
            playerRecords[uid] = record;
            data.Players.Add(record);
        }
        else if (record.Name != name && !IsStandInUid(uid))
        {
            record.Name = name;
        }
        return record;
    }

    private string NameOf(string uid)
    {
        if (candidates.TryGetValue(uid, out var candidate))
        {
            return candidate.Name;
        }
        if (playerRecords.TryGetValue(uid, out var record) && record.Name.Length > 0)
        {
            return record.Name;
        }
        return sapi.PlayerData.GetPlayerDataByUid(uid)?.LastKnownPlayername ?? uid;
    }

    private string NameOf(Entity entity) => UidOf(entity) is { } uid ? NameOf(uid) : entity.GetName();

    /// <summary>The uid of a player's entity or a stand-in, or null for anything else.</summary>
    private string? UidOf(Entity entity)
    {
        if (entity is EntityPlayer player)
        {
            return player.PlayerUID;
        }
        foreach (var (uid, standIn) in standIns)
        {
            if (standIn.EntityId == entity.EntityId)
            {
                return uid;
            }
        }
        return null;
    }

    public Entity? GetVictim(int walkerId) =>
        walkers.TryGetValue(walkerId, out var walker) && walker.VictimUid != null ? VictimEntity(walker.VictimUid) : null;

    /// <summary>The victim's entity while they are online (or the stand-in while it is loaded).</summary>
    private Entity? VictimEntity(string uid)
    {
        if (standIns.TryGetValue(uid, out var standIn))
        {
            return sapi.World.GetEntityById(standIn.EntityId) is { Alive: true } entity ? entity : null;
        }
        return sapi.World.PlayerByUid(uid) is IServerPlayer { ConnectionState: EnumClientState.Playing } player ? player.Entity : null;
    }

    /// <summary>A victim died: the haunt ends, and they rest twice as long (creature bible, section 9).</summary>
    private void OnPossibleVictimDeath(Entity? entity)
    {
        if (entity == null || UidOf(entity) is not { } uid)
        {
            return;
        }
        foreach (var walker in walkers.Values.Where(w => w.VictimUid == uid && w.State is WalkerState.Haunting or WalkerState.Paused).ToList())
        {
            EndHauntCore(walker, HauntEndReason.VictimDied);
        }
    }

    // ---- Debug stand-ins ----

    private static bool IsStandInUid(string? uid) => uid != null && uid.StartsWith(StandInPrefix, StringComparison.Ordinal);

    private bool IsLiveStandIn(Entity entity) =>
        config.Debug.StandInVictims && entity.Alive && standIns.Values.Any(s => s.EntityId == entity.EntityId);

    private IEnumerable<Entity> LoadedStandIns()
    {
        if (!config.Debug.StandInVictims)
        {
            yield break;
        }
        foreach (var standIn in standIns.Values)
        {
            if (sapi.World.GetEntityById(standIn.EntityId) is { Alive: true } entity)
            {
                yield return entity;
            }
        }
    }

    /// <summary>Reads the stand-ins from the save data; their chunks are kept loaded once the world runs.</summary>
    private void LoadStandIns()
    {
        standIns.Clear();
        foreach (var standIn in data.StandIns)
        {
            standIns[standIn.Uid] = standIn;
        }
    }

    /// <summary>
    /// Places a vanilla player bot at the spot and registers it as a stand-in victim. Returns its uid,
    /// or null with the reason in <paramref name="error"/>.
    /// </summary>
    internal string? AddStandIn(Vec3d spot, out string? error)
    {
        error = null;
        var type = sapi.World.GetEntityType(StandInCode);
        if (type == null)
        {
            error = "the vanilla player bot (game:playerbot) is not loaded";
            return null;
        }
        var entity = sapi.World.ClassRegistry.CreateEntity(type);
        entity.Pos.SetPos(spot);
        entity.Pos.Yaw = (float)(sapi.World.Rand.NextDouble() * GameMath.TWOPI);
        sapi.World.SpawnEntity(entity);
        var record = new StandInRecord { Uid = StandInPrefix + data.NextStandInNumber++, EntityId = entity.EntityId, X = spot.X, Y = spot.Y, Z = spot.Z };
        data.StandIns.Add(record);
        standIns[record.Uid] = record;
        KeepLoadedAround(record);
        Mod.Logger.Notification("Stand-in victim {0} placed at {1}", record.Uid, entity.Pos.AsBlockPos.ToLocalPosition(sapi));
        return record.Uid;
    }

    internal bool RemoveStandIn(string uid)
    {
        if (!standIns.Remove(uid, out var record))
        {
            return false;
        }
        data.StandIns.Remove(record);
        sapi.World.GetEntityById(record.EntityId)?.Die(EnumDespawnReason.Removed);
        ReleaseLoadedAround(record);
        Mod.Logger.Notification("Stand-in victim {0} removed", uid);
        foreach (var walker in walkers.Values.Where(w => w.VictimUid == uid && w.State is WalkerState.Haunting or WalkerState.Paused).ToList())
        {
            EndHauntCore(walker, HauntEndReason.Command);
        }
        return true;
    }

    internal IReadOnlyCollection<string> StandInUids => standIns.Keys;

    /// <summary>Keeps the chunks around a stand-in loaded, since no player is near it to keep them.</summary>
    private void KeepLoadedAround(StandInRecord standIn)
    {
        int cx = (int)standIn.X / GlobalConstants.ChunkSize, cz = (int)standIn.Z / GlobalConstants.ChunkSize;
        sapi.WorldManager.LoadChunkColumnPriority(cx - StandInChunkRadius, cz - StandInChunkRadius, cx + StandInChunkRadius, cz + StandInChunkRadius,
            new ChunkLoadOptions { KeepLoaded = true });
    }

    /// <summary>
    /// Lets go of the chunk columns <see cref="KeepLoadedAround"/> held for a removed stand-in: unloads each
    /// one no other stand-in holds and no player is near, since an unload ignores players.
    /// </summary>
    private void ReleaseLoadedAround(StandInRecord removed)
    {
        int size = GlobalConstants.ChunkSize;
        int playerRadius = sapi.Server.Config.MaxChunkRadius + 1;
        var players = sapi.World.AllOnlinePlayers.Where(p => p.Entity != null)
            .Select(p => ((int)p.Entity.Pos.X / size, (int)p.Entity.Pos.Z / size)).ToList();
        int cx = (int)removed.X / size, cz = (int)removed.Z / size;
        for (int x = cx - StandInChunkRadius; x <= cx + StandInChunkRadius; x++)
        {
            for (int z = cz - StandInChunkRadius; z <= cz + StandInChunkRadius; z++)
            {
                bool held = standIns.Values.Any(s => Math.Abs((int)s.X / size - x) <= StandInChunkRadius && Math.Abs((int)s.Z / size - z) <= StandInChunkRadius);
                bool watched = players.Any(p => Math.Abs(p.Item1 - x) <= playerRadius && Math.Abs(p.Item2 - z) <= playerRadius);
                if (!held && !watched)
                {
                    sapi.WorldManager.UnloadChunkColumn(x, z);
                }
            }
        }
    }

    /// <summary>Once a second: each stand-in turns its head a little, and keeps its record's position.</summary>
    private void TurnStandIns()
    {
        foreach (var standIn in standIns.Values)
        {
            if (sapi.World.GetEntityById(standIn.EntityId) is not { Alive: true } entity)
            {
                continue;
            }
            entity.Pos.Yaw = GameMath.Mod(entity.Pos.Yaw + StandInTurnPerSecond, GameMath.TWOPI);
            entity.Pos.HeadYaw = 0;
            standIn.X = entity.Pos.X;
            standIn.Y = entity.Pos.Y;
            standIn.Z = entity.Pos.Z;
        }
    }

    /// <summary>A stand-in that died or was removed is no longer one; one that unloaded with its chunk comes back with it.</summary>
    private void OnPossibleStandInDespawn(Entity entity, EntityDespawnData? despawn)
    {
        if (despawn?.Reason == EnumDespawnReason.Unload)
        {
            return;
        }
        var uid = standIns.FirstOrDefault(s => s.Value.EntityId == entity.EntityId).Key;
        if (uid != null)
        {
            RemoveStandIn(uid);
        }
    }
}
