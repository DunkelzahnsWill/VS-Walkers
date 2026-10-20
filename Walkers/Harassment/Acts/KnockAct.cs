using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Walkers.Config;

namespace Walkers.Harassment.Acts;

/// <summary>
/// Knocks: one rap in stage 3, two in stage 4, three at dusk in stage 5 (the lore's count: one is
/// watching, two has learnt you, three is tonight). One knock a night. Raps on the outside of the
/// door nearest the victim within 16 blocks, or on a wall block beside them when there is no door.
/// No other act raps, so the count always means what the lore says. Each counted knock leaves its page
/// (K1, K2, K3) or the fallback letter.
/// </summary>
public sealed class KnockAct : HarassAct
{
    public const string ActCode = "knocks";

    /// <summary>Between two raps, in real milliseconds.</summary>
    private const int RapGapMs = 400;

    /// <summary>A knock this close to the victim also jolts their screen.</summary>
    private const double ShakeRange = 6;

    public override string Code => ActCode;

    public override int MinStage => 3;

    // The stage-5 three raps come at dusk (RunDusk), not from the budget.
    public override int MaxStage => 4;

    public override bool Enabled(HarassConfig config) => config.Knocks.Enabled;

    public override int Cap(HarassConfig config) => 1;

    public override ActResult Run(HarassContext ctx) => Knock(ctx, ctx.Stage >= 4 ? 2 : 1);

    /// <summary>Stage 5, at dusk: the three raps. Once per night, outside the budget.</summary>
    public ActResult RunDusk(HarassContext ctx)
    {
        if (!ctx.Config.Knocks.Enabled || ctx.Stage < 5 || HarassMemory.Count(ctx.Harass, Code) >= 1)
        {
            return ActResult.Skip("off, below stage 5, or knocked already tonight");
        }
        var result = Knock(ctx, 3);
        if (result.Done)
        {
            HarassMemory.AddCount(ctx.Harass, Code);
        }
        return result;
    }

    private static ActResult Knock(HarassContext ctx, int raps)
    {
        var world = ctx.Api.World;
        var spot = FindKnockSpot(ctx, out string what);
        if (spot == null)
        {
            return ActResult.Skip("no door or wall near the victim");
        }
        for (int i = 0; i < raps; i++)
        {
            int delay = 1 + i * RapGapMs;
            ctx.Api.Event.RegisterCallback(_ =>
            {
                float pitch = 0.78f + (float)ctx.Rand.NextDouble() * 0.04f;
                HarassSounds.At(world, HarassSounds.KnockBody, spot, pitch, 32, 1f);
                HarassSounds.At(world, HarassSounds.KnockClick, spot, pitch, 32, 0.9f);
            }, delay);
        }
        if (spot.DistanceTo(ctx.Victim.Pos.XYZ) <= ShakeRange)
        {
            ScreenShake.Send(ctx, 0.08f);
        }
        var trigger = raps switch
        {
            1 => PageTrigger.K1,
            2 => PageTrigger.K2,
            _ => PageTrigger.K3,
        };
        ctx.System.Pages.OnKnock(ctx, trigger);
        return ActResult.Ok($"{raps} rap(s) on {what} at {EchoAct.Fmt(spot)}");
    }

    /// <summary>The outer face of the nearest door within 16 blocks of the victim; else the face of a solid block within 4 blocks of them.</summary>
    private static Vec3d? FindKnockSpot(HarassContext ctx, out string what)
    {
        var world = ctx.Api.World;
        var doors = BlockRules.FindNear(world, ctx.Victim, BlockRules.VictimReach, be => be.GetBehavior<BEBehaviorDoor>());
        var victimPos = ctx.Victim.Pos.XYZ;
        if (doors.Count > 0)
        {
            var pos = doors[0].Pos;
            var center = new Vec3d(pos.X + 0.5, pos.Y + 1, pos.Z + 0.5);
            what = world.BlockAccessor.GetBlock(pos).Code?.ToShortString() ?? "a door";
            return OuterFace(center, victimPos);
        }
        var feet = ctx.Victim.Pos.AsBlockPos;
        var probe = new BlockPos(feet.dimension);
        for (int r = 1; r <= 4; r++)
        {
            foreach (var face in BlockFacing.HORIZONTALS)
            {
                probe.Set(feet.X + face.Normali.X * r, feet.Y + 1, feet.Z + face.Normali.Z * r);
                var block = world.BlockAccessor.GetBlock(probe);
                if (block.SideSolid[face.Opposite.Index])
                {
                    what = block.Code?.ToShortString() ?? "a wall";
                    return OuterFace(new Vec3d(probe.X + 0.5, probe.Y + 0.5, probe.Z + 0.5), victimPos);
                }
            }
        }
        what = "";
        return null;
    }

    /// <summary>Just past the block's centre on the side away from the victim.</summary>
    private static Vec3d OuterFace(Vec3d center, Vec3d victim)
    {
        double dx = center.X - victim.X, dz = center.Z - victim.Z;
        double len = Math.Max(0.001, Math.Sqrt(dx * dx + dz * dz));
        return new Vec3d(center.X + dx / len * 0.6, center.Y, center.Z + dz / len * 0.6);
    }
}
