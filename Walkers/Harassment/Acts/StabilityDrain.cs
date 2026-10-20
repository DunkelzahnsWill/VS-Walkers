using System;
using Walkers.Config;
using Walkers.Director;

namespace Walkers.Harassment.Acts;

/// <summary>
/// The stability drain, stages 2 to 5, once a second while the walker is within 48 blocks of its
/// victim (creature bible, section 6). It steps the victim's temporalStability down toward the
/// stage's target (harass.stabilityDrain.targets) at perMinute, twice that while the victim watches it,
/// and never below the floor. Its base is its own last write, unless it lost track since (no body, out
/// of range) or the value now is more than 0.02 above that write (a temporal gear, another mod), in
/// which case the base is the value now. So regeneration between writes cannot undo it, it never
/// raises anyone's stability, and it keeps every gain from elsewhere. At the reveal it dips to
/// revealDip for revealDipSeconds. When the haunt pauses for a storm or the walker withdraws, it hands
/// back what it took, over 30 seconds.
///
/// In mundane mode (temporal stability off, or mundaneMode on) it writes the vanilla psychedelic value
/// instead (harass.mundaneWarp). A player who chose noScreenEffects gets neither. Not drawn by the
/// scheduler.
/// </summary>
public sealed class StabilityDrain
{
    public const string ActCode = "stabilityDrain";

    public const double Range = 48;

    /// <summary>A value this far above its own last write came from somewhere else, and is kept.</summary>
    private const double GainTolerance = 0.02;

    private const double HandBackSeconds = 30;

    private const string StabilityKey = "temporalStability";

    private const string PsychedelicKey = "psychedelic";

    /// <summary>Called once a second for each walker with a victim.</summary>
    public void Tick(HarassContext ctx, bool paused)
    {
        var config = ctx.Config.StabilityDrain;
        var state = ctx.State;
        var player = ctx.Player;
        var victim = ctx.Victim;
        if (player == null || !config.Enabled || ctx.Prefs.NoScreenEffects)
        {
            state.DrainTracking = false;
            return;
        }
        if (IsMundane(ctx))
        {
            Warp(ctx, paused);
            return;
        }

        var harass = ctx.Harass;
        double now = victim.WatchedAttributes.GetDouble(StabilityKey, 1);
        if (paused)
        {
            HandBack(ctx, now);
            return;
        }
        state.HandBackPerSecond = 0;
        bool inRange = ctx.Stage >= 2 && victim.Pos.Dimension == ctx.Body.Pos.Dimension && victim.Pos.DistanceTo(ctx.Body.Pos) <= Range;
        if (!inRange)
        {
            state.DrainTracking = false;
            return;
        }

        // The reveal: Forms marks it in its own subtree.
        bool revealed = ctx.Memory.GetTreeAttribute(WalkerMemory.Form)?.GetBool(Forms.FormMemory.Revealed) == true;
        long ms = ctx.Api.World.ElapsedMilliseconds;
        if (revealed && !state.SeenReveal)
        {
            state.RevealUntilMs = ms + config.RevealDipSeconds * 1000L;
            ScreenShake.Send(ctx, 0.3f);
        }
        state.SeenReveal = revealed;

        double last = harass.GetDouble(HarassMemory.DrainLastWrite, -1);
        double target = Math.Max(config.Floor, config.Targets.For(ctx.Stage));
        bool watched = ctx.Brain.Gaze.VictimWatching;
        double step = config.PerMinute / 60.0 * (watched ? 2 : 1);
        double next = NextValue(now, last, state.DrainTracking, target, step);
        if (ms < state.RevealUntilMs)
        {
            next = Math.Min(next, Math.Max(config.Floor, config.RevealDip));
        }
        state.DrainTracking = true;
        if (next < now)
        {
            Write(victim, next);
            harass.SetDouble(HarassMemory.DrainTaken, harass.GetDouble(HarassMemory.DrainTaken) + (now - next));
            harass.SetDouble(HarassMemory.DrainLastWrite, next);
        }
        else
        {
            harass.SetDouble(HarassMemory.DrainLastWrite, now);
        }
    }

    /// <summary>
    /// The base rule: the value to write this second, from the value <paramref name="now"/>, its own
    /// last write (<paramref name="last"/>, -1 for none) and whether it kept track since. A result at or
    /// above <paramref name="now"/> means "write nothing".
    /// </summary>
    public static double NextValue(double now, double last, bool tracking, double target, double step)
    {
        double baseValue = !tracking || last < 0 || now > last + GainTolerance ? now : last;
        return Math.Max(target, Math.Min(now, baseValue) - step);
    }

    /// <summary>Gives back what it took since it last arrived, spread over 30 seconds, never above 1.</summary>
    private static void HandBack(HarassContext ctx, double now)
    {
        var harass = ctx.Harass;
        var state = ctx.State;
        double taken = harass.GetDouble(HarassMemory.DrainTaken);
        state.DrainTracking = false;
        if (taken <= 0)
        {
            return;
        }
        if (state.HandBackPerSecond <= 0)
        {
            state.HandBackPerSecond = taken / HandBackSeconds;
        }
        double give = Math.Min(taken, state.HandBackPerSecond);
        Write(ctx.Victim, Math.Min(1, now + give));
        harass.SetDouble(HarassMemory.DrainTaken, taken - give);
        harass.SetDouble(HarassMemory.DrainLastWrite, -1);
    }

    private static void Write(Vintagestory.API.Common.Entities.Entity victim, double value)
    {
        victim.WatchedAttributes.SetDouble(StabilityKey, value);
    }

    /// <summary>Mundane mode: harass.mundaneMode on, or auto in a world with temporal stability off.</summary>
    public static bool IsMundane(HarassContext ctx) => ctx.System.Config.MundaneMode switch
    {
        MundaneMode.On => true,
        MundaneMode.Off => false,
        _ => !ctx.Api.World.Config.GetBool("temporalStability", true),
    };

    /// <summary>The mundane warp: keeps the psychedelic value up to harass.mundaneWarp.value while it is near; vanilla lets it fade after.</summary>
    private static void Warp(HarassContext ctx, bool paused)
    {
        var warp = ctx.Config.MundaneWarp;
        if (!warp.Enabled || paused || ctx.Stage < 2 || ctx.Victim.Pos.DistanceTo(ctx.Body.Pos) > Range)
        {
            return;
        }
        var attrs = ctx.Victim.WatchedAttributes;
        float now = attrs.GetFloat(PsychedelicKey);
        if (now < warp.Value)
        {
            attrs.SetFloat(PsychedelicKey, warp.Value);
        }
    }
}
