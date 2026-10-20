using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Walkers.Director;
using Walkers.Harassment.Acts;

namespace Walkers.Harassment;

/// <summary>
/// Every block an act changes goes on the restore list the moment it changes (creature bible, section
/// 8). With the director, the entry goes to its list, which saves it and undoes it when due, when the
/// walker withdraws, at dawn and after a restart. Without one (a test spawn), the kit keeps a small list
/// of its own in memory and undoes entries when due, at dawn, and when the body is gone; that list is
/// not saved.
/// </summary>
public sealed class HarassRestore
{
    private readonly HarassModSystem system;
    private readonly List<(RestoreEntry Entry, long BodyId)> local = new();

    public HarassRestore(HarassModSystem system)
    {
        this.system = system;
    }

    public int LocalCount => local.Count;

    /// <summary>Called once the director is known: it undoes the kit's own kind of change.</summary>
    public void RegisterWith(IWalkerDirector director)
    {
        director.RegisterRestoreHandler(Doors.RestoreKind, Doors.Undo);
        director.RegisterRestoreHandler(TornDoor.RestoreKind, TornDoor.Rehang);
    }

    public void Add(HarassContext ctx, RestoreEntry entry)
    {
        var director = system.Director;
        if (director != null && director.TryGetWalkerId(ctx.Body, out _))
        {
            director.AddRestore(entry);
            return;
        }
        local.RemoveAll(e => e.Entry.Pos.Equals(entry.Pos) && e.Entry.Kind == entry.Kind);
        local.Add((entry, ctx.Body.EntityId));
    }

    /// <summary>The local list's turn, once a second.</summary>
    public void Tick(ICoreServerAPI api)
    {
        if (local.Count == 0)
        {
            return;
        }
        var world = api.World;
        double now = world.Calendar.TotalHours;
        for (int i = local.Count - 1; i >= 0; i--)
        {
            var (entry, bodyId) = local[i];
            bool bodyGone = world.GetEntityById(bodyId) is not { Alive: true };
            bool due = (entry.DueTotalHours > 0 && now >= entry.DueTotalHours) ||
                       (entry.UndoAtDawn && !Stalker.StalkerModSystem.IsNight(world, entry.Pos.ToVec3d())) ||
                       (entry.UndoOnWithdraw && bodyGone);
            if (!due)
            {
                continue;
            }
            var result = Undo(world, entry);
            if (result != RestoreResult.Later)
            {
                local.RemoveAt(i);
            }
        }
    }

    /// <summary>Undoes everything on the local list now, whatever its due time (the self-test, a shutdown).</summary>
    public int UndoAllLocal(IWorldAccessor world)
    {
        int done = 0;
        foreach (var (entry, _) in local)
        {
            if (Undo(world, entry) == RestoreResult.Done)
            {
                done++;
            }
        }
        local.Clear();
        return done;
    }

    public static RestoreResult Undo(IWorldAccessor world, RestoreEntry entry)
    {
        try
        {
            return entry.Kind switch
            {
                Doors.RestoreKind => Doors.Undo(world, entry),
                TornDoor.RestoreKind => TornDoor.Rehang(world, entry),
                RestoreEntry.KindBlock => UndoBlockSwap(world, entry),
                _ => RestoreResult.Dropped,
            };
        }
        catch (Exception)
        {
            return RestoreResult.Dropped;
        }
    }

    /// <summary>The same swap-back the director does: only while the block is still the one the act set.</summary>
    private static RestoreResult UndoBlockSwap(IWorldAccessor world, RestoreEntry entry)
    {
        var accessor = world.BlockAccessor;
        if (accessor.GetChunkAtBlockPos(entry.Pos) == null)
        {
            return RestoreResult.Later;
        }
        var code = accessor.GetBlock(entry.Pos).Code?.ToString();
        if (code != entry.Set)
        {
            return code == entry.Found ? RestoreResult.Done : RestoreResult.Dropped;
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
}
