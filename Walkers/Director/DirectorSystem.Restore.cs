using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Walkers.Director;

// The restore list (creature bible, section 8, "What survives a restart"): every block a walker
// changed, undone when due, when its walker withdraws or is removed, and at server start. An entry
// whose chunk is not loaded waits for it; one a player changed themselves is dropped. The feature that
// made the change registers how to undo its kind; the director keeps the list and decides when.
public partial class DirectorSystem
{
    /// <summary>How far ahead the next dawn is searched, in in-game hours, and in what steps.</summary>
    private const double DawnSearchHours = 48, DawnSearchStep = 0.25;

    private readonly Dictionary<string, System.Func<IWorldAccessor, RestoreEntry, RestoreResult>> restoreHandlers = new();
    /// <summary>Entries still to undo from the server start, waiting for their chunk.</summary>
    private readonly HashSet<RestoreRecord> undoOnLoad = new();
    private readonly HashSet<string> missingRestoreHandlers = new();

    public void AddRestore(RestoreEntry entry)
    {
        var pos = entry.Pos;
        double due = entry.DueTotalHours;
        if (entry.UndoAtDawn)
        {
            double dawn = NextDawnHours(pos);
            due = due > 0 ? Math.Min(due, dawn) : dawn;
        }
        // One entry per block: a second change to the same block keeps the state first found.
        var existing = data.Restore.FirstOrDefault(r => r.X == pos.X && r.Y == pos.Y && r.Z == pos.Z && r.Dimension == pos.dimension && r.Kind == entry.Kind);
        if (existing != null)
        {
            existing.Set = entry.Set;
            existing.WalkerId = entry.WalkerId;
            existing.DueTotalHours = existing.DueTotalHours > 0 && due > 0 ? Math.Min(existing.DueTotalHours, due) : Math.Max(existing.DueTotalHours, due);
            existing.UndoOnWithdraw |= entry.UndoOnWithdraw;
            existing.UndoAtDawn |= entry.UndoAtDawn;
            return;
        }
        data.Restore.Add(new RestoreRecord
        {
            Kind = entry.Kind,
            X = pos.X,
            Y = pos.Y,
            Z = pos.Z,
            Dimension = pos.dimension,
            Found = entry.Found,
            Set = entry.Set,
            WalkerId = entry.WalkerId,
            DueTotalHours = due,
            UndoOnWithdraw = entry.UndoOnWithdraw,
            UndoAtDawn = entry.UndoAtDawn,
        });
        Mod.Logger.Debug("Restore list: walker {0} changed {1} at {2} ({3} to {4})", entry.WalkerId, entry.Kind, pos, entry.Found, entry.Set);
    }

    public void RegisterRestoreHandler(string kind, System.Func<IWorldAccessor, RestoreEntry, RestoreResult> undo)
    {
        restoreHandlers[kind] = undo;
    }

    /// <summary>
    /// Undoes every entry that is due: its time has come, or its walker no longer haunts (withdrawn,
    /// paused, ended, down or gone), or the server just started. Entries whose chunk is not loaded wait.
    /// </summary>
    private void ProcessRestore(bool startup)
    {
        if (startup)
        {
            undoOnLoad.Clear();
            undoOnLoad.UnionWith(data.Restore);
        }
        if (data.Restore.Count == 0)
        {
            return;
        }
        double now = sapi.World.Calendar.TotalHours;
        foreach (var record in data.Restore.ToList())
        {
            if (undoOnLoad.Contains(record) || IsDue(record, now))
            {
                TryUndo(record);
            }
        }
    }

    private bool IsDue(RestoreRecord record, double nowHours) =>
        (record.DueTotalHours > 0 && nowHours >= record.DueTotalHours) ||
        (record.UndoOnWithdraw && !(walkers.TryGetValue(record.WalkerId, out var walker) && walker.State == WalkerState.Haunting));

    /// <summary>Undoes one entry if its chunk is loaded. Returns false when it has to wait.</summary>
    private bool TryUndo(RestoreRecord record)
    {
        var pos = new BlockPos(record.X, record.Y, record.Z, record.Dimension);
        if (sapi.World.BlockAccessor.GetChunkAtBlockPos(pos) == null)
        {
            return false;
        }
        if (!restoreHandlers.TryGetValue(record.Kind, out var undo))
        {
            if (missingRestoreHandlers.Add(record.Kind))
            {
                Mod.Logger.Warning("Restore list: nothing undoes changes of kind {0} (is the feature that made them loaded?); they wait", record.Kind);
            }
            return false;
        }
        RestoreResult result;
        try
        {
            result = undo(sapi.World, ToEntry(record));
        }
        catch (Exception e)
        {
            Mod.Logger.Error("Restore list: undoing {0} at {1} failed, so the entry is dropped: {2}", record.Kind, pos, e);
            result = RestoreResult.Dropped;
        }
        if (result == RestoreResult.Later)
        {
            return false;
        }
        data.Restore.Remove(record);
        undoOnLoad.Remove(record);
        Mod.Logger.Debug("Restore list: {0} at {1} {2}", record.Kind, pos, result == RestoreResult.Done ? "undone" : "dropped");
        return true;
    }

    private static RestoreEntry ToEntry(RestoreRecord r) => new(
        r.Kind, new BlockPos(r.X, r.Y, r.Z, r.Dimension), r.Found, r.Set, r.WalkerId, r.DueTotalHours, r.UndoOnWithdraw, r.UndoAtDawn);

    /// <summary>A chunk column loaded: entries inside it that waited for it are undone now.</summary>
    private void OnChunkColumnLoaded(Vec2i chunkCoord, IWorldChunk[] chunks)
    {
        if (data.Restore.Count == 0 || sapi.World.Calendar == null)
        {
            return;
        }
        int size = Vintagestory.API.Config.GlobalConstants.ChunkSize;
        double now = sapi.World.Calendar.TotalHours;
        foreach (var record in data.Restore.ToList())
        {
            if (record.X / size == chunkCoord.X && record.Z / size == chunkCoord.Y && (undoOnLoad.Contains(record) || IsDue(record, now)))
            {
                TryUndo(record);
            }
        }
    }

    /// <summary>A player changed a block themselves: any entry for it is dropped (creature bible, section 8).</summary>
    private void DropRestoreAt(BlockPos? pos)
    {
        if (pos == null || data.Restore.Count == 0)
        {
            return;
        }
        int removed = data.Restore.RemoveAll(r => r.X == pos.X && r.Y == pos.Y && r.Z == pos.Z && r.Dimension == pos.dimension);
        if (removed > 0)
        {
            undoOnLoad.RemoveWhere(r => r.X == pos.X && r.Y == pos.Y && r.Z == pos.Z && r.Dimension == pos.dimension);
            Mod.Logger.Debug("Restore list: a player changed {0} themselves; entry dropped", pos);
        }
    }

    /// <summary>
    /// Undoes a block swap: when the block there is still the one the walker set, the one it found goes
    /// back. Exchanged rather than replaced, so a block entity (a torch's burn time) stays.
    /// </summary>
    private RestoreResult UndoBlockSwap(IWorldAccessor world, RestoreEntry entry)
    {
        var accessor = world.BlockAccessor;
        var current = accessor.GetBlock(entry.Pos);
        var code = current.Code?.ToString();
        if (code == entry.Found)
        {
            return RestoreResult.Done;
        }
        if (code != entry.Set)
        {
            return RestoreResult.Dropped;
        }
        var found = world.GetBlock(new AssetLocation(entry.Found));
        if (found == null)
        {
            return RestoreResult.Dropped;
        }
        accessor.ExchangeBlock(found.Id, entry.Pos);
        accessor.MarkBlockDirty(entry.Pos);
        return RestoreResult.Done;
    }

    /// <summary>Calendar.TotalHours of the next dawn at this spot: the sun crossing the horizon upwards.</summary>
    private double NextDawnHours(BlockPos pos)
    {
        var calendar = sapi.World.Calendar;
        var at = pos.ToVec3d();
        double hoursPerDay = calendar.HoursPerDay;
        double now = calendar.TotalHours;
        bool wasNight = calendar.GetSunPosition(at, now / hoursPerDay).Y < 0;
        for (double h = DawnSearchStep; h <= DawnSearchHours; h += DawnSearchStep)
        {
            bool night = calendar.GetSunPosition(at, (now + h) / hoursPerDay).Y < 0;
            if (wasNight && !night)
            {
                return now + h;
            }
            wasNight = night;
        }
        // No dawn within two days (polar summer or night): undo after one day.
        return now + hoursPerDay;
    }
}
