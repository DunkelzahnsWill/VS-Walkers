using Vintagestory.API.Common;
using System;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Walkers.Config;
using Walkers.Stalker;

namespace Walkers.Harassment;

/// <summary>
/// Everything an act needs to know about one walker and its victim at this moment. Built by
/// <see cref="HarassModSystem"/> once a second for each walker that haunts someone.
/// </summary>
public sealed class HarassContext
{
    public HarassContext(HarassModSystem system, WalkerBrain brain, Entity victim, VictimState state, bool? night = null)
    {
        System = system;
        Brain = brain;
        Victim = victim;
        State = state;
        Memory = brain.Memory;
        Harass = HarassMemory.Of(Memory);
        Stage = brain.Stage;
        Player = (victim as EntityPlayer)?.Player as IServerPlayer;
        Night = night ?? StalkerModSystem.IsNight(system.Api.World, victim.Pos.XYZ);
    }

    public HarassModSystem System { get; }

    public ICoreServerAPI Api => System.Api;

    public WalkerBrain Brain { get; }

    public Entity Body => Brain.Body;

    /// <summary>The victim: a player's entity, or a debug stand-in (any entity) with no player behind it.</summary>
    public Entity Victim { get; }

    /// <summary>The victim's player; null for a stand-in, whose screen effects are logged instead of sent.</summary>
    public IServerPlayer? Player { get; }

    public VictimState State { get; }

    /// <summary>The walker's memory, the <c>walkers</c> tree.</summary>
    public ITreeAttribute Memory { get; }

    /// <summary>The kit's own subtree of the memory.</summary>
    public ITreeAttribute Harass { get; }

    public int Stage { get; }

    /// <summary>Night at the victim's position.</summary>
    public bool Night { get; }

    public HarassConfig Config => System.Config.Harass;

    public Random Rand => System.Rand;

    private WalkersPlayerPrefs? prefs;

    /// <summary>The victim's own choices (/walkers me); the defaults for a stand-in.</summary>
    public WalkersPlayerPrefs Prefs => prefs ??= Player?.GetWalkersPrefs() ?? new WalkersPlayerPrefs();

    /// <summary>A spot <paramref name="minDist"/> to <paramref name="maxDist"/> blocks behind the victim (against where they look), at their feet's height.</summary>
    public Vec3d BehindVictim(double minDist, double maxDist, double spreadRadians = 0.6)
    {
        var view = WalkerGaze.ViewVector(Victim);
        double yaw = Math.Atan2(-view.X, -view.Z) + (Rand.NextDouble() * 2 - 1) * spreadRadians;
        double dist = minDist + Rand.NextDouble() * (maxDist - minDist);
        return new Vec3d(Victim.Pos.X + Math.Sin(yaw) * dist, Victim.Pos.Y + 0.5, Victim.Pos.Z + Math.Cos(yaw) * dist);
    }
}

/// <summary>The outcome of one try at an act.</summary>
public readonly record struct ActResult(bool Done, string Detail)
{
    /// <summary>The act reached the victim; it is counted and spends the budget.</summary>
    public static ActResult Ok(string detail) => new(true, detail);

    /// <summary>Nothing happened (no target, a rule said no); nothing is spent.</summary>
    public static ActResult Skip(string reason) => new(false, reason);
}

/// <summary>
/// One harassment act (creature bible, section 6, "The acts"). An act is drawn by the scheduler when
/// <see cref="Scheduled"/>, its stage allows it, it is night or it also runs by day, its switch is on
/// and tonight's count is below its cap. <see cref="Run"/> then checks its own rules and either acts
/// or skips.
/// </summary>
public abstract class HarassAct
{
    /// <summary>The act's code: in the counters, the log, the ladder's act report and /walkers harass act.</summary>
    public abstract string Code { get; }

    public abstract int MinStage { get; }

    public virtual int MaxStage => 5;

    /// <summary>Also runs by day, from the same budget.</summary>
    public virtual bool ByDay => false;

    /// <summary>Drawn by the scheduler; acts tied to an event (titles, pages, the dusk knock) are not.</summary>
    public virtual bool Scheduled => true;

    public abstract bool Enabled(HarassConfig config);

    /// <summary>Most of this act per night against one victim.</summary>
    public abstract int Cap(HarassConfig config);

    /// <summary>The stage, time and switch allow it; <see cref="Run"/> may still skip.</summary>
    public bool Allowed(HarassContext ctx) =>
        Enabled(ctx.Config) && ctx.Stage >= MinStage && ctx.Stage <= MaxStage && (ctx.Night || ByDay) &&
        HarassMemory.Count(ctx.Harass, Code) < Cap(ctx.Config);

    /// <summary>Weight in the draw: acts new at this stage are drawn twice as often (creature bible, section 6).</summary>
    public virtual double Weight(HarassContext ctx) => ctx.Stage == MinStage ? 2 : 1;

    public abstract ActResult Run(HarassContext ctx);
}
