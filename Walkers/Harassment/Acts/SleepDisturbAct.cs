using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Walkers.Config;
using Walkers.Stalker;

namespace Walkers.Harassment.Acts;

/// <summary>
/// Broken sleep, stages 4 and 5: the victim wakes to a call or a scrape on the wall close by and a
/// small shake, and can lie down again. Only the victim is unmounted from the bed (never
/// WakeAllPlayers, which leaves clients fast-forwarding), and their tiredness is put back to at least
/// 9, because a bed refuses anyone at 8 or less and a slept night burns tiredness off in seconds. It
/// never fires while every other counting player online is asleep (a night skip waiting on the
/// victim), unless the victim is the only one, and never within 20 real seconds of the last wake.
/// Never a rap: raps belong to the knock count.
/// </summary>
public sealed class SleepDisturbAct : HarassAct
{
    public const string ActCode = "sleepDisturb";

    private const long MinApartMs = 20000;

    /// <summary>The bed refuses anyone at this tiredness or less (BlockBed.cs:72).</summary>
    private const float MinTiredness = 9;

    public override string Code => ActCode;

    public override int MinStage => 4;

    public override bool ByDay => true;

    public override bool Enabled(HarassConfig config) => config.SleepDisturb.Enabled;

    public override int Cap(HarassConfig config) => config.SleepDisturb.PerNight;

    // Drawn first whenever the victim is in bed, because there is little else to do then.
    public override double Weight(HarassContext ctx) => StalkerModSystem.IsAsleep(ctx.Victim) ? 4 : 0;

    public override ActResult Run(HarassContext ctx)
    {
        var player = ctx.Player;
        if (player == null || !StalkerModSystem.IsAsleep(ctx.Victim))
        {
            return ActResult.Skip("the victim is not in bed");
        }
        long now = ctx.Api.World.ElapsedMilliseconds;
        if (now - ctx.State.LastWakeMs < MinApartMs)
        {
            return ActResult.Skip("woke them less than 20 s ago");
        }
        if (OthersWaitingOnSkip(ctx.Api, player))
        {
            return ActResult.Skip("every other player is asleep: it never breaks a night skip");
        }
        var world = ctx.Api.World;
        var bedPos = ctx.Victim.Pos.XYZ;
        bool scrape = ctx.Rand.NextDouble() < 0.5;
        if (scrape)
        {
            var at = Around(bedPos, 1.5, ctx.Rand);
            HarassSounds.At(world, HarassSounds.Scrape, at, 0.6f + (float)(ctx.Rand.NextDouble() * 0.06 - 0.03), 12, 1f);
            ctx.Api.Event.RegisterCallback(_ => HarassSounds.At(world, HarassSounds.WoodCreak, at, 0.7f, 12, 0.8f), 400);
        }
        else
        {
            var call = WrongCallAct.CallFor(ctx.Memory.GetString(Director.WalkerMemory.Guise, ""), ctx.Rand);
            HarassSounds.At(world, call, Around(bedPos, 3 + ctx.Rand.NextDouble() * 3, ctx.Rand), ctx.Config.WrongCalls.Pitch, 16, 0.9f);
        }

        float tiredness = Math.Max(ctx.State.TirednessAtBed, ReadTiredness(ctx.Victim));
        if (ctx.Victim is EntityAgent agent)
        {
            agent.TryUnmount();
        }
        WriteTiredness(ctx.Victim, Math.Max(MinTiredness, tiredness));
        ScreenShake.Send(ctx, 0.1f);
        ctx.State.LastWakeMs = now;
        return ActResult.Ok(scrape ? "a scrape on the wall woke them" : "a call close by woke them");
    }

    /// <summary>True when other counting players are online and every one of them is asleep.</summary>
    public static bool OthersWaitingOnSkip(ICoreServerAPI api, IServerPlayer victim)
    {
        var others = api.World.AllOnlinePlayers.OfType<IServerPlayer>()
            .Where(p => p.PlayerUID != victim.PlayerUID && p.ConnectionState == EnumClientState.Playing && p.Entity?.Alive == true &&
                        (p.WorldData.CurrentGameMode == EnumGameMode.Survival || p.WorldData.CurrentGameMode == EnumGameMode.Guest))
            .ToList();
        return others.Count > 0 && others.All(p => StalkerModSystem.IsAsleep(p.Entity));
    }

    public static float ReadTiredness(Vintagestory.API.Common.Entities.Entity entity) =>
        entity.WatchedAttributes.GetTreeAttribute("tiredness")?.GetFloat("tiredness") ?? 0;

    private static void WriteTiredness(Vintagestory.API.Common.Entities.Entity entity, float value)
    {
        var tree = entity.WatchedAttributes.GetTreeAttribute("tiredness");
        if (tree == null)
        {
            return;
        }
        tree.SetFloat("tiredness", value);
        entity.WatchedAttributes.MarkPathDirty("tiredness");
    }

    private static Vec3d Around(Vec3d center, double dist, Random rand)
    {
        double yaw = rand.NextDouble() * GameMath.TWOPI;
        return new Vec3d(center.X + Math.Sin(yaw) * dist, center.Y + 0.5, center.Z + Math.Cos(yaw) * dist);
    }
}
