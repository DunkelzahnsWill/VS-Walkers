using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Walkers.Config;
using Walkers.Director;

namespace Walkers.Harassment.Acts;

/// <summary>
/// Wrong calls, stages 2 to 5, day and night: the guise's own deer call pitched down to
/// harass.wrongCalls.pitch, from 20 to 40 blocks off, where nothing stands, and never from where the
/// walker is. A small deer guise screams, a caribou or elk bellows. Once in four times another call
/// answers a few seconds later, 6 to 10 blocks from the first. Range 64, because the server sends a
/// sound only to players inside its range.
/// </summary>
public sealed class WrongCallAct : HarassAct
{
    public const string ActCode = "wrongCalls";

    private const float Range = 64;

    public override string Code => ActCode;

    public override int MinStage => 2;

    public override bool ByDay => true;

    public override bool Enabled(HarassConfig config) => config.WrongCalls.Enabled;

    public override int Cap(HarassConfig config) => config.WrongCalls.PerNight;

    public override ActResult Run(HarassContext ctx)
    {
        var world = ctx.Api.World;
        var call = CallFor(ctx.Memory.GetString(WalkerMemory.Guise, ""), ctx.Rand);
        Vec3d? at = null;
        for (int attempt = 0; attempt < 6 && at == null; attempt++)
        {
            double yaw = ctx.Rand.NextDouble() * GameMath.TWOPI;
            double dist = 20 + ctx.Rand.NextDouble() * 20;
            var spot = new Vec3d(ctx.Victim.Pos.X + Math.Sin(yaw) * dist, ctx.Victim.Pos.Y + 1, ctx.Victim.Pos.Z + Math.Cos(yaw) * dist);
            // Never where it stands: at least 12 blocks from the body, and nothing living right there.
            if (spot.DistanceTo(ctx.Body.Pos.XYZ) < 12 || world.GetEntitiesAround(spot, 6, 6, e => e.Alive && e.EntityId != ctx.Victim.EntityId).Length > 0)
            {
                continue;
            }
            at = spot;
        }
        if (at == null)
        {
            return ActResult.Skip("no empty spot 20 to 40 blocks off");
        }
        float pitch = ctx.Config.WrongCalls.Pitch * (0.97f + (float)ctx.Rand.NextDouble() * 0.06f);
        float range = call.Path.Contains("elk-bellow") ? 96 : Range;
        HarassSounds.At(world, call, at, pitch, range, 0.6f);
        bool answer = ctx.Rand.Next(4) == 0;
        if (answer)
        {
            double yaw = ctx.Rand.NextDouble() * GameMath.TWOPI;
            double off = 6 + ctx.Rand.NextDouble() * 4;
            var second = new Vec3d(at.X + Math.Sin(yaw) * off, at.Y, at.Z + Math.Cos(yaw) * off);
            var answerCall = CallFor(ctx.Memory.GetString(WalkerMemory.Guise, ""), ctx.Rand);
            ctx.Api.Event.RegisterCallback(_ => HarassSounds.At(world, answerCall, second, pitch, range, 0.5f), 2000 + ctx.Rand.Next(2000));
        }
        return ActResult.Ok($"{call.Path} at {EchoAct.Fmt(at)}, pitch {pitch:0.00}{(answer ? ", answered" : "")}");
    }

    /// <summary>The call of a guise's deer type (Art and sound's list, rows 46 to 48), numbered so it is one exact file.</summary>
    public static AssetLocation CallFor(string guise, Random rand) => guise switch
    {
        "redbrocket" or "pudu" or "water" or "pampas" => new AssetLocation("game", $"sounds/creature/hooved/small/smallscream{1 + rand.Next(2)}"),
        "caribou" or "elk" => new AssetLocation("game", $"sounds/creature/animal/mammal/hooved/deer/elk-bellow{1 + rand.Next(2)}"),
        _ => new AssetLocation("game", $"sounds/creature/hooved/medium/deer-call{1 + rand.Next(3)}"),
    };
}
