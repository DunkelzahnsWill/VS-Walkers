using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Walkers.Config;
using Walkers.Director;
using Walkers.Stalker.Tasks;

namespace Walkers.Harassment.Acts;

/// <summary>
/// The offering, stage 4, at dawn, once a haunt by default: a dead deer of the guise's own type lies a
/// few steps from the victim's door, and it can be butchered. A vanilla doe is spawned and dies on the
/// next tick with no attacker, as vanilla's creature container does. Not drawn by the scheduler.
/// </summary>
public sealed class OfferingAct
{
    public const string ActCode = "offerings";

    public ActResult RunDawn(HarassContext ctx)
    {
        var config = ctx.Config.Offerings;
        if (!config.Enabled || ctx.Stage != 4)
        {
            return ActResult.Skip("off, or not stage 4");
        }
        if (ctx.Harass.GetInt(HarassMemory.Offerings) >= config.PerHaunt)
        {
            return ActResult.Skip("this haunt's offerings are given");
        }
        var type = DoeType(ctx);
        if (type == null)
        {
            return ActResult.Skip("no vanilla deer type");
        }
        var spot = DoorstepSpot(ctx);
        if (spot == null)
        {
            return ActResult.Skip("no open ground near the victim's door");
        }
        SpawnDead(ctx, type, spot, "walkersOffering", null);
        ctx.Harass.SetInt(HarassMemory.Offerings, ctx.Harass.GetInt(HarassMemory.Offerings) + 1);
        return ActResult.Ok($"{type.Code} at {EchoAct.Fmt(spot)}");
    }

    /// <summary>The vanilla doe of the guise's own type (whitetail when the walker has none).</summary>
    public static EntityProperties? DoeType(HarassContext ctx)
    {
        var world = ctx.Api.World;
        string guise = ctx.Memory.GetString(WalkerMemory.Guise, "");
        if (guise.Length == 0)
        {
            guise = "whitetail";
        }
        return world.GetEntityType(new AssetLocation("game", $"deer-{guise}-adult-female")) ?? world.GetEntityType(new AssetLocation("game", "deer-whitetail-adult-female"));
    }

    /// <summary>
    /// Spawns a creature that dies on the next tick with no attacker, as vanilla's creature container
    /// does; <paramref name="afterDeath"/> runs right after it died.
    /// </summary>
    public static Entity SpawnDead(HarassContext ctx, EntityProperties type, Vec3d spot, string markAttribute, Action<Entity>? afterDeath)
    {
        var world = ctx.Api.World;
        var body = world.ClassRegistry.CreateEntity(type);
        body.Pos.SetPos(spot);
        body.Pos.Yaw = (float)(ctx.Rand.NextDouble() * GameMath.TWOPI);
        body.Attributes.SetBool(markAttribute, true);
        world.SpawnEntity(body);
        ctx.Api.Event.EnqueueMainThreadTask(() =>
        {
            if (body.Alive)
            {
                body.Die(EnumDespawnReason.Death, new DamageSource { Source = EnumDamageSource.Unknown, Type = EnumDamageType.Hunger });
                body.Properties.ResolvedSounds = null;
                afterDeath?.Invoke(body);
            }
        }, "walkers-" + markAttribute);
        return body;
    }

    /// <summary>Open ground two to four blocks out from the door nearest the victim; at most 4 blocks from the victim when there is no door.</summary>
    private static Vec3d? DoorstepSpot(HarassContext ctx)
    {
        var world = ctx.Api.World;
        var doors = BlockRules.FindNear(world, ctx.Victim, BlockRules.VictimReach, be => be.GetBehavior<Vintagestory.GameContent.BEBehaviorDoor>());
        var anchor = doors.Count > 0 ? doors[0].Pos.ToVec3d().Add(0.5, 0, 0.5) : ctx.Victim.Pos.XYZ;
        var away = doors.Count > 0 ? anchor.SubCopy(ctx.Victim.Pos.XYZ) : new Vec3d(ctx.Rand.NextDouble() - 0.5, 0, ctx.Rand.NextDouble() - 0.5);
        double len = Math.Max(0.001, Math.Sqrt(away.X * away.X + away.Z * away.Z));
        for (double dist = 2; dist <= 4; dist += 1)
        {
            double x = anchor.X + away.X / len * dist, z = anchor.Z + away.Z / len * dist;
            var spot = WalkerSpots.FindStanding(world.BlockAccessor, x, anchor.Y, z, 4, 1.2f);
            if (spot != null)
            {
                return spot;
            }
        }
        return null;
    }
}
