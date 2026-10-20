using System;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Walkers.Config;
using Walkers.Director;

namespace Walkers.Stalker.Tasks;

/// <summary>
/// walkers-doorway, stage 5 (true form only; creature bible, flag 5): when the victim is behind a door,
/// it goes to the outside of that door, opens it if it is closed and it may, and holds the doorway for
/// up to <c>holdMs</c>. walkers-lunge may strike from there, at most one step in; it never walks
/// inside. A door it opened is closed again when it leaves, and with the director the restore list
/// closes it within harass.doors.maxOpenHours whatever happens to the body.
///
/// It never opens: a door barred from inside, padlocked or story-locked; one that cannot be opened by
/// hand; one that may break when opened (the crude door); one on land the claim rule forbids
/// (harass.respectClaims); one with animals within 6 blocks; or any door while its victim is in bed.
/// Such a door keeps it out. The hunt halts at any door it would walk through (<see cref="WalkerBrain.DoorAheadMs"/>).
///
/// JSON: <c>holdMs</c> (20000), <c>searchRadius</c> (8) blocks around the victim.
/// </summary>
public class AiTaskWalkerDoorway : WalkerTaskBase
{
    public const string Code = "walkers-doorway";

    /// <summary>Restore-list kind of a door it opened (<see cref="IWalkerDirector.RegisterRestoreHandler"/>).</summary>
    public const string RestoreKind = "walkers-doorway";

    /// <summary>It touches a block only this close to its victim (creature bible, section 9).</summary>
    private const double VictimNearDoor = 16;

    /// <summary>A door within this many blocks of the body counts as one it would walk through.</summary>
    private const int AheadRadius = 3;

    /// <summary>The scan for a door near the body runs at most this often.</summary>
    private const long AheadScanMs = 500;

    /// <summary>The scan around the victim, after the hunt found no path, runs at most this often.</summary>
    private const long VictimScanMs = 5000;

    /// <summary>The hunt's failed path counts for this long.</summary>
    private const long NoPathRecentMs = 6000;

    /// <summary>A door it may not open is left alone this long.</summary>
    private const long RefusedDoorMs = 60000;

    /// <summary>No animals within this many blocks of a door it opens.</summary>
    private const float AnimalRadius = 6;

    [JsonProperty]
    public int HoldMs = 20000;

    [JsonProperty]
    public int SearchRadius = 8;

    private BEBehaviorDoor? door;
    private Vec3d? outside;
    private bool holding;
    private bool openedByUs;
    private long holdUntilMs;
    private long nextAheadScanMs;
    private long nextVictimScanMs;
    private BlockPos? refusedDoor;
    private long refusedUntilMs;

    public AiTaskWalkerDoorway(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
    {
    }

    public override bool ShouldExecute()
    {
        var brain = Brain;
        if (brain == null || !brain.IsTrueForm || brain.Mode != StalkerMode.Hunt || brain.Victim == null || entity.Swimming)
        {
            return false;
        }
        var victim = brain.Victim;
        long now = world.ElapsedMilliseconds;
        if (door != null && holding && now < holdUntilMs && StillWorthIt(brain, victim))
        {
            // Back from a lunge: hold the same doorway.
            return true;
        }
        if (door != null)
        {
            CloseIfOurs();
            Reset();
        }

        // The scan for a door ahead runs even while the task cools down, so the hunt never walks through one.
        BEBehaviorDoor? found = null;
        if (now >= nextAheadScanMs)
        {
            nextAheadScanMs = now + AheadScanMs;
            found = FindDoor(entity.Pos.XYZ, AheadRadius, victim, includeRefused: true);
            if (found != null)
            {
                // Even a door it refused keeps the hunt from walking through.
                brain.DoorAheadMs = now;
                if (IsRefused(found))
                {
                    return false;
                }
            }
        }
        if (IsOnCooldown())
        {
            return false;
        }
        if (found == null && now - brain.NoPathToVictimMs < NoPathRecentMs && now >= nextVictimScanMs)
        {
            nextVictimScanMs = now + VictimScanMs;
            found = FindDoor(victim.Pos.XYZ, SearchRadius, victim, includeRefused: false);
        }
        if (found == null)
        {
            return false;
        }
        var spot = OutsideOf(found, victim.Pos.XYZ);
        if (spot == null)
        {
            Refuse(found);
            return false;
        }
        door = found;
        outside = spot;
        return true;
    }

    public override void StartExecute()
    {
        base.StartExecute();
        if (!holding && outside != null)
        {
            GoTo(outside, Speed(false), 0.6f, false, force: true);
        }
    }

    public override bool ContinueExecute(float dt)
    {
        var brain = Brain;
        var d = door;
        var victim = brain?.Victim;
        if (brain == null || d == null || outside == null || victim == null || brain.Mode != StalkerMode.Hunt || entity.Swimming || !StillWorthIt(brain, victim))
        {
            return false;
        }
        long now = world.ElapsedMilliseconds;
        brain.DoorAheadMs = now;
        if (brain.Frozen)
        {
            Halt();
            return true;
        }

        if (!holding)
        {
            if (outside.HorizontalSquareDistanceTo(entity.Pos.X, entity.Pos.Z) <= 1.0)
            {
                Halt();
                if (!d.Opened)
                {
                    if (!MayOpen(brain, d, victim))
                    {
                        Refuse(d);
                        return false;
                    }
                    Open(brain, d);
                }
                holding = true;
                brain.HoldingDoorway = true;
                holdUntilMs = now + HoldMs;
                return true;
            }
            if (NavigationEnded && !GoTo(outside, Speed(false), 0.6f, false) && (Stuck || NoPath))
            {
                // It cannot reach the outside of this door.
                Refuse(d);
                return false;
            }
            return true;
        }

        // Holding: stand in front of the doorway facing in. A closed door (the victim shut it) ends the hold.
        if (!d.Opened || now >= holdUntilMs)
        {
            return false;
        }
        if (outside.HorizontalSquareDistanceTo(entity.Pos.X, entity.Pos.Z) > 1.5 * 1.5)
        {
            GoTo(outside, Speed(false), 0.6f, false);
        }
        else
        {
            TurnTowards(victim.Pos.XYZ, 15 * GameMath.DEG2RAD);
        }
        return true;
    }

    public override void FinishExecute(bool cancelled)
    {
        base.FinishExecute(cancelled);
        var brain = Brain;
        bool lunging = cancelled && brain != null && brain.Mode == StalkerMode.Hunt && holding && world.ElapsedMilliseconds < holdUntilMs;
        if (lunging)
        {
            // A lunge took over; the hold resumes after it.
            return;
        }
        if (door != null)
        {
            // Done with this door for a while; the hunt still never walks through it.
            Refuse(door);
        }
        CloseIfOurs();
        Reset();
    }

    private bool StillWorthIt(WalkerBrain brain, Entity victim)
    {
        var d = door;
        return d != null && world.BlockAccessor.GetBlockEntity(d.Pos)?.GetBehavior<BEBehaviorDoor>() == d &&
               victim.Pos.DistanceTo(Center(d)) <= VictimNearDoor && !StalkerModSystem.IsAsleep(victim);
    }

    /// <summary>
    /// The door nearest the body within <paramref name="radius"/> blocks of <paramref name="around"/>
    /// that stands between the body and the victim, within 16 blocks of the victim. Doors it refused
    /// lately are skipped unless <paramref name="includeRefused"/>.
    /// </summary>
    private BEBehaviorDoor? FindDoor(Vec3d around, int radius, Entity victim, bool includeRefused)
    {
        var accessor = world.BlockAccessor;
        var pos = new BlockPos(0, 0, 0, entity.Pos.Dimension);
        int cx = (int)Math.Floor(around.X), cy = (int)Math.Floor(around.Y), cz = (int)Math.Floor(around.Z);
        BEBehaviorDoor? best = null;
        double bestDist = double.MaxValue;
        double bodyToVictim = entity.Pos.DistanceTo(victim.Pos);
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dy = -2; dy <= 2; dy++)
                {
                    pos.Set(cx + dx, cy + dy, cz + dz);
                    var block = accessor.GetBlock(pos);
                    if (block.Id == 0 || !block.HasBehavior<BlockBehaviorDoor>())
                    {
                        continue;
                    }
                    var found = accessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorDoor>();
                    if (found == null || (!includeRefused && IsRefused(found)))
                    {
                        continue;
                    }
                    var center = Center(found);
                    double doorToVictim = victim.Pos.DistanceTo(center);
                    double bodyToDoor = entity.Pos.DistanceTo(center);
                    // Between: the victim is nearer the door than the body is, and the door is nearer the body than the victim is.
                    if (doorToVictim > VictimNearDoor || doorToVictim >= bodyToVictim || bodyToDoor >= bodyToVictim + 1)
                    {
                        continue;
                    }
                    if (bodyToDoor < bestDist)
                    {
                        bestDist = bodyToDoor;
                        best = found;
                    }
                }
            }
        }
        return best;
    }

    /// <summary>The standing spot on the far side of the door from the victim; null when there is none.</summary>
    private Vec3d? OutsideOf(BEBehaviorDoor d, Vec3d victim)
    {
        var accessor = world.BlockAccessor;
        var facing = d.facingWhenClosed;
        Vec3d? best = null;
        double bestDist = -1;
        foreach (var side in new[] { facing, facing.Opposite })
        {
            var cell = d.Pos.AddCopy(side);
            if (!WalkerSpots.CanStandAt(accessor, cell, BodyHeight))
            {
                continue;
            }
            var spot = new Vec3d(cell.X + 0.5, cell.Y, cell.Z + 0.5);
            double dist = spot.DistanceTo(victim);
            if (dist > bestDist)
            {
                bestDist = dist;
                best = spot;
            }
        }
        return best;
    }

    private bool MayOpen(WalkerBrain brain, BEBehaviorDoor d, Entity victim)
    {
        if (StalkerModSystem.IsAsleep(victim) || d.StoryLockedCode != null || d.doorBh == null || !d.doorBh.handopenable)
        {
            return false;
        }
        // The crude door may break when opened, and the game then needs a player.
        if (d.Block.Attributes?["breakOnTriggerChance"].AsFloat(0) > 0)
        {
            return false;
        }
        if (d.Blockentity.GetBehavior<BEBehaviorDoorBarLock>()?.IsLocked == true)
        {
            return false;
        }
        var reinforcement = world.Api.ModLoader.GetModSystem<ModSystemBlockReinforcement>()?.GetReinforcment(d.Pos);
        if (reinforcement?.Locked == true)
        {
            return false;
        }
        if (!ClaimsAllow(brain.Config.Harass.RespectClaims, d.Pos, victim))
        {
            return false;
        }
        var animals = world.GetEntitiesAround(Center(d), AnimalRadius, 3, e =>
            e is EntityAgent && e is not EntityPlayer && e.Alive && !StalkerModSystem.IsWalkerBody(e) && e.EntityId != victim.EntityId);
        return animals.Length == 0;
    }

    private bool ClaimsAllow(ClaimRespect rule, BlockPos pos, Entity victim)
    {
        switch (rule)
        {
            case ClaimRespect.Off:
                return true;
            case ClaimRespect.AnyClaim:
                return world.Claims.Get(pos) is not { Length: > 0 };
            default:
                if (victim is EntityPlayer ep && ep.Player is IPlayer player)
                {
                    return world.Claims.TestAccess(player, pos, EnumBlockAccessFlags.Use) == EnumWorldAccessResponse.Granted;
                }
                // A stand-in has no rights of its own: only unclaimed land.
                return world.Claims.Get(pos) is not { Length: > 0 };
        }
    }

    private void Open(WalkerBrain brain, BEBehaviorDoor d)
    {
        d.ToggleDoorState(null, true);
        openedByUs = true;
        var director = brain.Owner.Director;
        if (director != null && director.TryGetWalkerId(entity, out int walkerId))
        {
            double due = world.Calendar.TotalHours + brain.Config.Harass.Doors.MaxOpenHours;
            director.AddRestore(new RestoreEntry(RestoreKind, d.Pos.Copy(), "closed", "open", walkerId, due, UndoOnWithdraw: true, UndoAtDawn: true));
        }
    }

    private void CloseIfOurs()
    {
        var d = door;
        if (d == null || !openedByUs || !d.Opened || world.BlockAccessor.GetBlockEntity(d.Pos)?.GetBehavior<BEBehaviorDoor>() != d)
        {
            return;
        }
        if (SomeoneInDoorway(world, d.Pos))
        {
            // The restore list (with the director) closes it later.
            return;
        }
        d.ToggleDoorState(null, false);
    }

    /// <summary>
    /// Undoes a <see cref="RestoreKind"/> entry: closes the door if it is still open. Registered with
    /// the director. Waits while the chunk is not loaded or anyone stands in the doorway; drops the
    /// entry if the door is gone or already closed.
    /// </summary>
    public static RestoreResult UndoRestore(IWorldAccessor world, RestoreEntry entry)
    {
        if (world.BlockAccessor.GetChunkAtBlockPos(entry.Pos) == null)
        {
            return RestoreResult.Later;
        }
        var d = world.BlockAccessor.GetBlockEntity(entry.Pos)?.GetBehavior<BEBehaviorDoor>();
        if (d == null || !d.Opened)
        {
            return RestoreResult.Dropped;
        }
        if (SomeoneInDoorway(world, entry.Pos))
        {
            return RestoreResult.Later;
        }
        d.ToggleDoorState(null, false);
        return RestoreResult.Done;
    }

    private static bool SomeoneInDoorway(IWorldAccessor world, BlockPos pos) =>
        world.GetEntitiesAround(new Vec3d(pos.X + 0.5, pos.Y + 1, pos.Z + 0.5), 1.2f, 1.5f, e => e.Alive && e is EntityAgent).Length > 0;

    private bool IsRefused(BEBehaviorDoor d) => refusedDoor != null && d.Pos.Equals(refusedDoor) && world.ElapsedMilliseconds < refusedUntilMs;

    private void Refuse(BEBehaviorDoor d)
    {
        refusedDoor = d.Pos.Copy();
        refusedUntilMs = world.ElapsedMilliseconds + RefusedDoorMs;
    }

    private void Reset()
    {
        door = null;
        outside = null;
        holding = false;
        openedByUs = false;
        if (Brain != null)
        {
            Brain.HoldingDoorway = false;
        }
    }

    private static Vec3d Center(BEBehaviorDoor d) => new(d.Pos.X + 0.5, d.Pos.Y + 0.5, d.Pos.Z + 0.5);
}
