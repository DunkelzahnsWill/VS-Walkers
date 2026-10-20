using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Walkers.Director;
using Walkers.Stalker;

namespace Walkers.Harassment.Acts;

/// <summary>
/// Moving doors and trapdoors without a player, safely: the engine's own toggle with no player (as
/// the stalker AI's doorway task does), never on a door that may break when moved (the crude door
/// would then need a player and crash), a barred, padlocked or story-locked one, or one that cannot be
/// opened by hand. Also the undo of the restore-list kind <see cref="RestoreKind"/>.
/// </summary>
public static class Doors
{
    /// <summary>Restore-list kind of a door or trapdoor the kit opened.</summary>
    public const string RestoreKind = "walkers-harass-door";

    /// <summary>A door or trapdoor at a position, as one handle.</summary>
    public sealed class Handle
    {
        public BEBehaviorDoor? Door;
        public BEBehaviorTrapDoor? Trapdoor;

        public BlockPos Pos => Door?.Pos ?? Trapdoor!.Pos;

        public bool Opened => Door?.Opened ?? Trapdoor!.Opened;

        public string Name => (Door?.Block ?? Trapdoor!.Block).Code?.ToShortString() ?? "door";

        public void Set(bool opened)
        {
            if (Door != null)
            {
                Door.ToggleDoorState(null, opened);
            }
            else
            {
                Trapdoor!.ToggleDoorState(null, opened);
            }
        }
    }

    public static Handle? At(IWorldAccessor world, BlockPos pos)
    {
        var be = world.BlockAccessor.GetBlockEntity(pos);
        return From(be);
    }

    public static Handle? From(BlockEntity? be)
    {
        if (be == null)
        {
            return null;
        }
        var door = be.GetBehavior<BEBehaviorDoor>();
        if (door != null)
        {
            return new Handle { Door = door };
        }
        var trapdoor = be.GetBehavior<BEBehaviorTrapDoor>();
        return trapdoor != null ? new Handle { Trapdoor = trapdoor } : null;
    }

    /// <summary>Whether the kit may move this door at all (either way); claims and sight are the caller's.</summary>
    public static bool MayMove(IWorldAccessor world, Handle handle)
    {
        if (handle.Door is { } door)
        {
            // ToggleDoorState reads Block.Attributes without a null check.
            if (door.Block.Attributes == null || BlockRules.MayBreakWhenMoved(door.Block) || door.StoryLockedCode != null ||
                door.doorBh == null || !door.doorBh.handopenable)
            {
                return false;
            }
            if (door.Blockentity.GetBehavior<BEBehaviorDoorBarLock>()?.IsLocked == true)
            {
                return false;
            }
        }
        return !BlockRules.IsPadlocked(world, handle.Pos);
    }

    /// <summary>A player or creature in or right next to the doorway (a walker counts only inside the door's own cell).</summary>
    public static bool SomeoneInDoorway(IWorldAccessor world, BlockPos pos)
    {
        double cx = pos.X + 0.5, cz = pos.Z + 0.5;
        return world.GetEntitiesAround(new Vec3d(cx, pos.Y + 1, cz), 1.2f, 1.5f, e =>
        {
            if (!e.Alive || e is not EntityAgent)
            {
                return false;
            }
            double dx = e.Pos.X - cx, dz = e.Pos.Z - cz;
            return !StalkerModSystem.IsWalkerBody(e) || dx * dx + dz * dz < 0.64;
        }).Length > 0;
    }

    /// <summary>Undo of <see cref="RestoreKind"/>: close the door if it still stands open and nobody is in it.</summary>
    public static RestoreResult Undo(IWorldAccessor world, RestoreEntry entry)
    {
        if (world.BlockAccessor.GetChunkAtBlockPos(entry.Pos) == null)
        {
            return RestoreResult.Later;
        }
        var handle = At(world, entry.Pos);
        if (handle == null || !handle.Opened)
        {
            return RestoreResult.Dropped;
        }
        if (SomeoneInDoorway(world, entry.Pos))
        {
            return RestoreResult.Later;
        }
        handle.Set(false);
        return RestoreResult.Done;
    }
}
