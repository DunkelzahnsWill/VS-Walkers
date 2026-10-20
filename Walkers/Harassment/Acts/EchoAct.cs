using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Walkers.Config;

namespace Walkers.Harassment.Acts;

/// <summary>
/// Echoes, stages 3 to 5, day and night: the victim's own sounds from earlier that day, 6 to 10 blocks
/// behind them (the stone they mined, the door they used, the planks they laid), or footsteps a few
/// blocks behind on the ground they stand on, which count as an echo. Positional, so anyone within
/// about 24 blocks hears them too. The first echo of a player's first haunt leaves page S3.
/// </summary>
public sealed class EchoAct : HarassAct
{
    public const string ActCode = "echoes";

    /// <summary>Only actions from the last in-game day are played back ("from earlier that day").</summary>
    private const double WithinHours = 24;

    private const float Range = 24;

    public override string Code => ActCode;

    public override int MinStage => 3;

    public override bool ByDay => true;

    public override bool Enabled(HarassConfig config) => config.Echoes.Enabled;

    public override int Cap(HarassConfig config) => config.Echoes.PerNight;

    public override ActResult Run(HarassContext ctx)
    {
        var world = ctx.Api.World;
        EchoEntry? entry = null;
        if (ctx.Player != null && ctx.Rand.NextDouble() < 0.6)
        {
            entry = ctx.System.EchoesOf(ctx.Player.PlayerUID)?.Pick(ctx.Rand, world.Calendar.TotalHours, WithinHours);
        }
        ActResult result;
        if (entry is { } e)
        {
            var at = ctx.BehindVictim(6, 10);
            Replay(ctx, e, at);
            result = ActResult.Ok($"{e.Kind.ToString().ToLowerInvariant()} {world.GetBlock(e.BlockId)?.Code} at {Fmt(at)}");
        }
        else
        {
            result = Footsteps(ctx);
        }
        if (result.Done)
        {
            ctx.System.Pages.OnTrigger(ctx, PageTrigger.S3);
        }
        return result;
    }

    private static void Replay(HarassContext ctx, EchoEntry entry, Vec3d at)
    {
        var world = ctx.Api.World;
        var sound = entry.Sound!;
        if (entry.Kind == EchoKind.Broke)
        {
            // A few hits, 300 ms apart, then the break (sound list, row 43).
            var block = world.GetBlock(entry.BlockId);
            var hit = block?.Sounds?.Hit.Location ?? HarassSounds.DestructFallback;
            int hits = 3 + ctx.Rand.Next(3);
            for (int i = 0; i < hits; i++)
            {
                int delay = i * 300 + ctx.Rand.Next(-40, 41);
                ctx.Api.Event.RegisterCallback(_ => HarassSounds.At(world, hit, at, Pitch(ctx.Rand), Range), Math.Max(1, delay));
            }
            ctx.Api.Event.RegisterCallback(_ => HarassSounds.At(world, sound, at, Pitch(ctx.Rand), Range), hits * 300 + 300);
            return;
        }
        HarassSounds.At(world, sound, at, Pitch(ctx.Rand), Range);
    }

    /// <summary>Three to five steps behind the victim, on the block they stand on, at a walking pace.</summary>
    private static ActResult Footsteps(HarassContext ctx)
    {
        var world = ctx.Api.World;
        var under = ctx.Victim.Pos.AsBlockPos.DownCopy();
        var ground = world.BlockAccessor.GetBlock(under);
        if (ground.Id == 0 || ctx.Victim.Swimming)
        {
            return ActResult.Skip("no ground under the victim");
        }
        var walk = ground.GetSounds(world.BlockAccessor, new BlockSelection { Position = under })?.Walk.Location ?? HarassSounds.StepFallback;
        var start = ctx.BehindVictim(3, 5, 0.3);
        int steps = 3 + ctx.Rand.Next(3);
        for (int i = 0; i < steps; i++)
        {
            var at = start.Clone();
            ctx.Api.Event.RegisterCallback(_ => HarassSounds.At(world, walk, at, 0.9f + (float)ctx.Rand.NextDouble() * 0.2f, 12, 0.8f), 1 + i * 450);
        }
        return ActResult.Ok($"{steps} footsteps on {ground.Code} at {Fmt(start)}");
    }

    private static float Pitch(Random rand) => 0.75f + (float)rand.NextDouble() * 0.5f;

    internal static string Fmt(Vec3d pos) => $"{pos.X:0},{pos.Y:0},{pos.Z:0}";
}
