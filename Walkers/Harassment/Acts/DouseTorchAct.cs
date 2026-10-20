using Vintagestory.API.MathTools;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using Walkers.Config;
using Walkers.Director;

namespace Walkers.Harassment.Acts;

/// <summary>
/// Torches, stages 3 to 5, at night: a lit basic torch within 16 blocks of the victim, out of their
/// sight and where the claim rule allows, goes out with the hiss of a doused flame. It becomes the
/// torch's extinct variant, which a player can relight, never the burnt-out one. It stays out until a
/// player relights it (the owner's call, to keep it cheap for servers): nothing tracks or relights it.
/// At most the escalation level's torchesPerNight a night; from Hate (escalation level with eatTorches)
/// the torch is eaten instead: the block is gone and nothing drops.
/// </summary>
public sealed class DouseTorchAct : HarassAct
{
    public const string ActCode = "douseTorches";

    public override string Code => ActCode;

    public override int MinStage => 3;

    public override bool Enabled(HarassConfig config) => config.DouseTorches.Enabled;

    public override int Cap(HarassConfig config) => config.DouseTorches.PerNight;

    public override double Weight(HarassContext ctx) => Spent(ctx) ? 0 : base.Weight(ctx);

    private bool Spent(HarassContext ctx) => HarassMemory.Count(ctx.Harass, Code) >= Enraged.Rules(ctx).TorchesPerNight;

    public override ActResult Run(HarassContext ctx)
    {
        if (Spent(ctx))
        {
            return ActResult.Skip($"tonight's torches at escalation level {Enraged.Level(ctx)} are spent");
        }
        var world = ctx.Api.World;
        var torches = LitTorchesNear(ctx, BlockRules.VictimReach)
            .Where(t => !BlockRules.VictimSees(world, ctx.Victim, t.Pos))
            .Take(8)
            .ToList();
        if (torches.Count == 0)
        {
            return ActResult.Skip("no lit torch it may touch out of the victim's sight");
        }
        var (pos, torch) = torches[ctx.Rand.Next(torches.Count)];
        return Snuff(ctx, pos, torch);
    }

    /// <summary>Puts the torch out, or from an eatTorches level eats it.</summary>
    public static ActResult Snuff(HarassContext ctx, BlockPos pos, BlockTorch torch) =>
        Enraged.Rules(ctx).EatTorches ? Eat(ctx, pos, torch) : Douse(ctx, pos, torch);

    /// <summary>The torch is gone: air where it stood, nothing dropped, nothing put back.</summary>
    public static ActResult Eat(HarassContext ctx, BlockPos pos, BlockTorch torch)
    {
        var world = ctx.Api.World;
        world.BlockAccessor.SetBlock(0, pos);
        world.BlockAccessor.TriggerNeighbourBlockUpdate(pos);
        world.PlaySoundAt(HarassSounds.Extinguish, pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5, null, 0.7f, 16, 1f);
        world.PlaySoundAt(HarassSounds.Eat, pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5, null, 0.55f, 16, 0.8f);
        return ActResult.Ok($"{torch.Code.ToShortString()} at {pos} eaten");
    }

    /// <summary>Lit basic torches within <paramref name="radius"/> of the victim that pass the claim and padlock rules, nearest first.</summary>
    public static System.Collections.Generic.List<(BlockPos Pos, BlockTorch Torch)> LitTorchesNear(HarassContext ctx, int radius)
    {
        var world = ctx.Api.World;
        return BlockRules.FindNear(world, ctx.Victim, radius, be => be is BlockEntityTorch && be.Block is BlockTorch t && IsLit(t) ? t : null)
            .Where(t => BlockRules.MayTouch(world, ctx.Config, t.Pos, ctx.Victim))
            .ToList();
    }

    public static bool IsLit(BlockTorch torch) => torch.Variant["state"] == "lit" && torch.ExtinctVariant != null && !torch.IsExtinct;

    /// <summary>Swaps the torch for its extinct variant, keeping its block entity. Not put back.</summary>
    public static ActResult Douse(HarassContext ctx, BlockPos pos, BlockTorch torch)
    {
        var world = ctx.Api.World;
        var extinct = torch.ExtinctVariant;
        world.BlockAccessor.ExchangeBlock(extinct.Id, pos);
        world.BlockAccessor.MarkBlockDirty(pos);
        world.PlaySoundAt(HarassSounds.Extinguish, pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5, null, 0.9f + (float)ctx.Rand.NextDouble() * 0.2f, 16, 1f);
        return ActResult.Ok($"{torch.Code.ToShortString()} at {pos} put out");
    }
}
