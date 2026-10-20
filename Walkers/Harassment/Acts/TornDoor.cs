using System;
using System.Globalization;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Walkers.Director;

namespace Walkers.Harassment.Acts;

/// <summary>
/// The torn door (creature bible, section 7, "Enraged"): within the escalation level's door budget
/// (woodenDoorsPerHaunt, otherDoorsPerHaunt), after 3 seconds of heavy blows an enraged walker tears a
/// closed single door out of its frame: a plain wooden one (solid, sleek-windowed or crude), or from
/// the other budget a bare iron or 1x2 heavy door. Gates and double doors are never torn, and a lock
/// always holds (the owner's choice, 2026-10-20). The door block is set to air, never broken, and drops
/// intact as an item that lasts enraged.doorItemDespawnSeconds. At dawn the restore list hangs it back
/// (kind <see cref="RestoreKind"/>) if its item still lies within 6 blocks and the doorway is clear; a
/// door a player picked up is theirs.
/// </summary>
public static class TornDoor
{
    /// <summary>Restore-list kind of a torn door. Found holds "code|rotateYRad|invertHandles", Set the item entity's id.</summary>
    public const string RestoreKind = "walkers-harass-torndoor";

    /// <summary>Real milliseconds of blows before the door gives.</summary>
    public const int BlowsMs = 3000;

    private const double RehangItemRange = 6;

    /// <summary>The self-test's switch: the door gives at once, without the blows.</summary>
    internal static bool Immediate;

    private static readonly AssetLocation Splinter = new("game", "sounds/block/stickbreak");

    /// <summary>A plain single wooden door: solid, sleek-windowed or crude, one block wide, not half of a double door.</summary>
    public static bool IsPlainWooden(BEBehaviorDoor door) => MaterialOf(door) == DoorMaterial.Wooden;

    /// <summary>Which door budget a door spends, or <see cref="DoorMaterial.None"/> when it can never be torn.</summary>
    public static DoorMaterial MaterialOf(BEBehaviorDoor door)
    {
        var code = door.Block.Code;
        if (code == null || code.Domain != "game" || door.LeftDoor != null || door.RightDoor != null || door.doorBh == null ||
            door.doorBh.width != 1 || door.doorBh.height != 2)
        {
            return DoorMaterial.None;
        }
        string path = code.Path;
        if (path == "door-crude" || path.StartsWith("door-solid-", StringComparison.Ordinal) || path.StartsWith("door-sleek-windowed-", StringComparison.Ordinal))
        {
            return DoorMaterial.Wooden;
        }
        if (path.StartsWith("metaldoor-", StringComparison.Ordinal) || path.StartsWith("door-1x2heavy-", StringComparison.Ordinal))
        {
            return DoorMaterial.Other;
        }
        return DoorMaterial.None;
    }

    /// <summary>Starts the blows; after <see cref="BlowsMs"/> the door gives unless the way in changed, and <paramref name="done"/> hears whether it did.</summary>
    public static void Tear(HarassContext ctx, BEBehaviorDoor door, Action<bool> done)
    {
        if (Immediate)
        {
            done(TearNow(ctx, door.Pos.Copy()));
            return;
        }
        var world = ctx.Api.World;
        var pos = door.Pos.Copy();
        var center = new Vec3d(pos.X + 0.5, pos.Y + 1, pos.Z + 0.5);
        for (int i = 0; i < 3; i++)
        {
            float pitch = 0.5f + (float)ctx.Rand.NextDouble() * 0.08f;
            ctx.Api.Event.RegisterCallback(_ =>
            {
                HarassSounds.At(world, HarassSounds.KnockBody, center, pitch, 32, 1f);
                HarassSounds.At(world, HarassSounds.LargeDoorClose, center, pitch + 0.2f, 32, 0.8f);
            }, 1 + i * 1000);
        }
        ctx.Api.Event.RegisterCallback(_ => done(TearNow(ctx, pos)), BlowsMs);
    }

    /// <summary>The door gives: air where it hung, the door as an item, an entry on the restore list.</summary>
    public static bool TearNow(HarassContext ctx, BlockPos pos)
    {
        var world = ctx.Api.World;
        var accessor = world.BlockAccessor;
        var door = accessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorDoor>();
        if (door == null || door.Opened || MaterialOf(door) == DoorMaterial.None || Doors.IsLockedShut(world, door))
        {
            return false;
        }
        var block = door.Block;
        var stack = block.OnPickBlock(world, pos) ?? new ItemStack(block);
        string found = string.Join("|", block.Code.ToString(), door.RotateYRad.ToString("R", CultureInfo.InvariantCulture), door.InvertHandles ? "1" : "0");
        accessor.SetBlock(0, pos);
        accessor.TriggerNeighbourBlockUpdate(pos);

        var center = new Vec3d(pos.X + 0.5, pos.Y + 1, pos.Z + 0.5);
        HarassSounds.At(world, Splinter, center, 0.5f, 32, 1f);
        HarassSounds.At(world, HarassSounds.LargeDoorClose, center, 0.6f, 32, 1f);
        var item = world.SpawnItemEntity(stack, new Vec3d(pos.X + 0.5, pos.Y + 0.6, pos.Z + 0.5), new Vec3d(0, 0.05, 0));
        if (item != null)
        {
            int seconds = ctx.System.Config.Enraged.DoorItemDespawnSeconds;
            item.Attributes.SetInt("minsecondsToDespawn", seconds);
            if (item.GetBehavior("timeddespawn") is ITimedDespawn timed)
            {
                timed.DespawnSeconds = seconds;
            }
            item.Attributes.SetBool("walkersTornDoor", true);
        }
        if (ctx.System.Config.Enraged.RehangAtDawn && item != null)
        {
            ctx.System.Restore.Add(ctx, new RestoreEntry(RestoreKind, pos.Copy(), found, item.EntityId.ToString(CultureInfo.InvariantCulture),
                ctx.System.WalkerIdOf(ctx), 0, UndoOnWithdraw: false, UndoAtDawn: true));
        }
        if (ctx.Victim.Pos.DistanceTo(center) <= 8)
        {
            ScreenShake.Send(ctx, 0.25f);
        }
        return true;
    }

    /// <summary>Undo of <see cref="RestoreKind"/>: hang the door back while its item still lies close and the doorway is clear.</summary>
    public static RestoreResult Rehang(IWorldAccessor world, RestoreEntry entry)
    {
        var accessor = world.BlockAccessor;
        var pos = entry.Pos;
        if (accessor.GetChunkAtBlockPos(pos) == null)
        {
            return RestoreResult.Later;
        }
        string[] parts = entry.Found.Split('|');
        var block = parts.Length == 3 ? world.GetBlock(new AssetLocation(parts[0])) : null;
        if (block == null || !long.TryParse(entry.Set, NumberStyles.Integer, CultureInfo.InvariantCulture, out long itemId))
        {
            return RestoreResult.Dropped;
        }
        var item = world.GetEntityById(itemId);
        if (item is not EntityItem { Alive: true } || item.Pos.Dimension != pos.dimension || item.Pos.DistanceTo(pos.ToVec3d().Add(0.5, 0.5, 0.5)) > RehangItemRange)
        {
            // Picked up (it is theirs now), carried off, or gone.
            return RestoreResult.Dropped;
        }
        for (int dy = 0; dy < 2; dy++)
        {
            if (accessor.GetBlock(pos.UpCopy(dy)).Id != 0)
            {
                return RestoreResult.Dropped;
            }
        }
        if (Doors.SomeoneInDoorway(world, pos))
        {
            return RestoreResult.Later;
        }
        accessor.SetBlock(block.Id, pos);
        var be = accessor.GetBlockEntity(pos);
        var door = be?.GetBehavior<BEBehaviorDoor>();
        if (be != null && door != null)
        {
            var tree = new TreeAttribute();
            be.ToTreeAttributes(tree);
            tree.SetFloat("rotateYRad", float.Parse(parts[1], CultureInfo.InvariantCulture));
            tree.SetBool("invertHandles", parts[2] == "1");
            tree.SetBool("opened", false);
            be.FromTreeAttributes(tree, world);
            // The server's collision boxes follow the rotation only through this internal setup call.
            typeof(BEBehaviorDoor).GetMethod("SetupRotationsAndColSelBoxes", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.Invoke(door, new object[] { false });
            door.doorBh?.placeMultiblockParts(world, pos);
            be.MarkDirty(true);
        }
        item.Die(EnumDespawnReason.Removed);
        return RestoreResult.Done;
    }
}

/// <summary>The door budget a door belongs to.</summary>
public enum DoorMaterial
{
    None,
    Wooden,
    Other,
}
