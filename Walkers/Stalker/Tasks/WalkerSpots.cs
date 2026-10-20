using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Walkers.Stalker.Tasks;

/// <summary>
/// Where a walker may stand: on solid ground, out of liquid, with room for its body, out of bright
/// block light before the hunt, and not deep underground. All checks are block lookups; none trace rays.
/// </summary>
public static class WalkerSpots
{
    /// <summary>
    /// The feet position of a standing spot in the column of (x, z), searched up and down from
    /// <paramref name="y"/> by up to <paramref name="searchHeight"/> blocks; null when there is none
    /// or the chunk is not loaded. The spot is the centre of the block.
    /// </summary>
    public static Vec3d? FindStanding(IBlockAccessor accessor, double x, double y, double z, int searchHeight, float bodyHeight)
    {
        var pos = new BlockPos((int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Floor(z), 0);
        int startY = pos.Y;
        for (int offset = 0; offset <= searchHeight; offset++)
        {
            for (int sign = 1; sign >= -1; sign -= 2)
            {
                if (offset == 0 && sign < 0)
                {
                    continue;
                }
                pos.Y = startY + offset * sign;
                if (pos.Y < 1 || pos.Y >= accessor.MapSizeY - 3)
                {
                    continue;
                }
                if (accessor.GetChunkAtBlockPos(pos) == null)
                {
                    return null;
                }
                if (CanStandAt(accessor, pos, bodyHeight))
                {
                    return new Vec3d(pos.X + 0.5, pos.Y, pos.Z + 0.5);
                }
            }
        }
        return null;
    }

    /// <summary>The feet block is open, the blocks the body reaches into are open, and the block below carries it.</summary>
    public static bool CanStandAt(IBlockAccessor accessor, BlockPos feet, float bodyHeight)
    {
        int headroom = Math.Max(1, (int)Math.Ceiling(bodyHeight));
        for (int dy = 0; dy < headroom; dy++)
        {
            if (!IsOpen(accessor, feet.UpCopy(dy)))
            {
                return false;
            }
        }
        var belowPos = feet.DownCopy();
        var below = accessor.GetBlock(belowPos);
        if (below.IsLiquid() || accessor.GetBlock(belowPos, BlockLayersAccess.Fluid).IsLiquid())
        {
            return false;
        }
        if (below.SideSolid[BlockFacing.UP.Index])
        {
            return true;
        }
        var boxes = below.GetCollisionBoxes(accessor, belowPos);
        if (boxes == null)
        {
            return false;
        }
        foreach (var box in boxes)
        {
            if (box.Y2 >= 0.5f)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>No collision box and no liquid.</summary>
    public static bool IsOpen(IBlockAccessor accessor, BlockPos pos)
    {
        var block = accessor.GetBlock(pos);
        if (block.IsLiquid() || accessor.GetBlock(pos, BlockLayersAccess.Fluid).IsLiquid())
        {
            return false;
        }
        var boxes = block.GetCollisionBoxes(accessor, pos);
        return boxes == null || boxes.Length == 0;
    }

    /// <summary>The block light at chest height of a spot (torches give 14 or 15).</summary>
    public static int BlockLight(IBlockAccessor accessor, Vec3d feet) =>
        accessor.GetLightLevel(new BlockPos((int)Math.Floor(feet.X), (int)Math.Floor(feet.Y) + 1, (int)Math.Floor(feet.Z), 0), EnumLightLevelType.OnlyBlockLight);

    /// <summary>The spot is deeper below the terrain surface than <paramref name="maxDepth"/> blocks.</summary>
    public static bool TooDeep(IBlockAccessor accessor, Vec3d feet, int maxDepth)
    {
        var pos = new BlockPos((int)Math.Floor(feet.X), (int)Math.Floor(feet.Y), (int)Math.Floor(feet.Z), 0);
        return accessor.GetTerrainMapheightAt(pos) - pos.Y > maxDepth;
    }

    /// <summary>Nothing above the spot keeps the rain off: open sky, not under a canopy or a roof.</summary>
    public static bool UnderOpenSky(IBlockAccessor accessor, Vec3d feet)
    {
        var pos = new BlockPos((int)Math.Floor(feet.X), (int)Math.Floor(feet.Y), (int)Math.Floor(feet.Z), 0);
        return accessor.GetRainMapHeightAt(pos) <= pos.Y;
    }
}
