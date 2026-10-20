using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Walkers.Config;
using Walkers.Director;
using Walkers.Stalker.Tasks;

namespace Walkers.Harassment.Acts;

/// <summary>
/// The escalation list (creature bible, section 7): what a victim finds after a walker comes back
/// from an improper kill, keyed on the escalation level (<see cref="Enraged.Level"/>): "first return" is level 1. The kit runs it itself from the walker's memory;
/// the director only counts the returns.
/// <list type="bullet">
/// <item>First return, the rotting herd: at the first dawn of stage 2 or later, once per return,
/// escalation.herd.wildCarcasses dead does of the guise's own type lie 16 to 24 blocks from the
/// victim's door, laid out only while no player is within 32 blocks. A knife gets only rot. They are
/// removed after escalation.herd.decayHours, before vanilla decay can leave bones.</item>
/// <item>First return, eaten crops: in stages 2 to 5, once a night, a line of
/// escalation.crops.blocksPerAct ripe crops set back escalation.crops.stagesBack growth stages; at
/// most maxPerReturn blocks and maxShare of the crops in range per return, only where the claim rule
/// allows. Only crop blocks, so never berry bushes, troughs, hay or traps.</item>
/// <item>Second return, livestock (escalation.herd.killLivestock, off by default): one of the
/// victim's own farm animals found dead at dawn, once per return, inside a claim the victim owns.</item>
/// </list>
/// The torn door and the enraged break-in are <see cref="BreakInAct"/>'s.
/// </summary>
public sealed class Escalation
{
    public const string HerdCode = "rottingHerd";
    public const string CropsCode = "eatenCrops";
    public const string LivestockCode = "livestock";

    private const double HerdMin = 16, HerdMax = 24, HerdClearOfPlayers = 32;
    private const int CropRange = 24, CropHeight = 6;

    /// <summary>In-game hours after dusk before the night's crop act.</summary>
    private const double CropsAfterDuskHours = 1;

    private static readonly AssetLocation Eat = new("game", "sounds/player/eat");
    private static readonly AssetLocation Plant = new("game", "sounds/block/plant");
    private static readonly AssetLocation Rot = new("game", "rot");

    private readonly HarassModSystem system;

    public Escalation(HarassModSystem system)
    {
        this.system = system;
    }

    private EscalationConfig Config => system.Config.Escalation;

    private static int Returns(HarassContext ctx) => Enraged.Level(ctx);

    /// <summary>At dawn: the herd falls due, and (opt-in) an animal is found dead.</summary>
    public IEnumerable<(string Code, ActResult Result)> OnDawn(HarassContext ctx)
    {
        if (!Config.Enabled || Returns(ctx) < 1)
        {
            yield break;
        }
        var harass = ctx.Harass;
        if (ctx.Stage >= 2 && Config.Herd.WildCarcasses > 0 && harass.GetInt(HarassMemory.HerdFor) != Returns(ctx))
        {
            harass.SetInt(HarassMemory.HerdFor, Returns(ctx));
            harass.SetBool(HarassMemory.HerdPending, true);
            var home = Home(ctx);
            harass.SetDouble("herdHomeX", home.X);
            harass.SetDouble("herdHomeY", home.Y);
            harass.SetDouble("herdHomeZ", home.Z);
        }
        if (Config.Herd.KillLivestock && Returns(ctx) >= 2 && harass.GetInt(HarassMemory.LivestockFor) != Returns(ctx))
        {
            var result = KillLivestock(ctx);
            if (result.Done)
            {
                harass.SetInt(HarassMemory.LivestockFor, Returns(ctx));
            }
            yield return (LivestockCode, result);
        }
    }

    /// <summary>Each second: the pending herd, its removal, and the night's crops.</summary>
    public IEnumerable<(string Code, ActResult Result)> Tick(HarassContext ctx)
    {
        var harass = ctx.Harass;
        RemoveHerdWhenDue(ctx);
        if (!Config.Enabled || Returns(ctx) < 1)
        {
            yield break;
        }
        if (harass.GetBool(HarassMemory.HerdPending))
        {
            var herd = LayHerd(ctx);
            if (herd.Done)
            {
                harass.SetBool(HarassMemory.HerdPending, false);
                system.Pages.OnLoss(ctx, PagesAct.LossHerd);
                yield return (HerdCode, herd);
            }
        }
        double dusk = harass.GetDouble(HarassMemory.Dusk);
        if (ctx.Night && ctx.Stage >= 2 && harass.GetDouble(HarassMemory.CropsNight, -1) != dusk &&
            ctx.Api.World.Calendar.TotalHours - dusk >= CropsAfterDuskHours)
        {
            harass.SetDouble(HarassMemory.CropsNight, dusk);
            var crops = EatCrops(ctx);
            if (crops.Done)
            {
                system.Pages.OnLoss(ctx, PagesAct.LossCrops);
            }
            yield return (CropsCode, crops);
        }
    }

    /// <summary>The door nearest the victim, else where they stand.</summary>
    private static Vec3d Home(HarassContext ctx)
    {
        var doors = BlockRules.FindNear(ctx.Api.World, ctx.Victim, BlockRules.VictimReach, be => be.GetBehavior<BEBehaviorDoor>());
        return doors.Count > 0 ? doors[0].Pos.ToVec3d().Add(0.5, 0, 0.5) : ctx.Victim.Pos.XYZ;
    }

    // ---- The rotting herd ----

    public ActResult LayHerd(HarassContext ctx)
    {
        var world = ctx.Api.World;
        var harass = ctx.Harass;
        var home = new Vec3d(harass.GetDouble("herdHomeX"), harass.GetDouble("herdHomeY"), harass.GetDouble("herdHomeZ"));
        var type = OfferingAct.DoeType(ctx);
        if (type == null)
        {
            return ActResult.Skip("no vanilla deer type");
        }
        int unloaded = 0, noGround = 0, near = 0;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            double yaw = ctx.Rand.NextDouble() * GameMath.TWOPI;
            double dist = HerdMin + ctx.Rand.NextDouble() * (HerdMax - HerdMin);
            double x = home.X + Math.Sin(yaw) * dist, z = home.Z + Math.Cos(yaw) * dist;
            if (world.BlockAccessor.GetChunkAtBlockPos(new BlockPos((int)x, (int)home.Y, (int)z, 0)) == null)
            {
                unloaded++;
                continue;
            }
            var spot = WalkerSpots.FindStanding(world.BlockAccessor, x, home.Y, z, 8, 1.2f);
            if (spot == null)
            {
                noGround++;
                continue;
            }
            if (PlayerNear(ctx, spot, HerdClearOfPlayers))
            {
                near++;
                continue;
            }
            var ids = new List<long>();
            for (int i = 0; i < Config.Herd.WildCarcasses; i++)
            {
                var at = i == 0 ? spot : WalkerSpots.FindStanding(world.BlockAccessor, spot.X + ctx.Rand.NextDouble() * 4 - 2, spot.Y, spot.Z + ctx.Rand.NextDouble() * 4 - 2, 3, 1.2f) ?? spot;
                var body = OfferingAct.SpawnDead(ctx, type, at, "walkersRottingHerd", RotOnly);
                ids.Add(body.EntityId);
            }
            harass["herdIds"] = new LongArrayAttribute(ids.ToArray());
            harass.SetDouble(HarassMemory.HerdUntil, world.Calendar.TotalHours + Config.Herd.DecayHours);
            harass.SetDouble("herdX", spot.X);
            harass.SetDouble("herdY", spot.Y);
            harass.SetDouble("herdZ", spot.Z);
            return ActResult.Ok($"{ids.Count} rotting {type.Code.ToShortString()} at {EchoAct.Fmt(spot)}");
        }
        return ActResult.Skip($"no spot 16 to 24 blocks from the door that no player is near ({near} near a player, {noGround} without ground, {unloaded} not loaded)");
    }

    /// <summary>Hollowed out: whatever the knife finds is rot.</summary>
    private void RotOnly(Entity body)
    {
        var harvestable = body.GetBehavior<EntityBehaviorHarvestable>();
        var rot = body.World.GetItem(Rot);
        if (harvestable == null || rot == null)
        {
            return;
        }
        harvestable.DropsGenerated = true;
        var inv = (InventoryGeneric)harvestable.Inventory;
        if (inv.Count < 1)
        {
            inv.AddSlots(1);
        }
        for (int i = 0; i < inv.Count; i++)
        {
            inv[i].Itemstack = null;
        }
        inv[0].Itemstack = new ItemStack(rot, 2 + body.World.Rand.Next(3));
        var tree = new TreeAttribute();
        inv.ToTreeAttributes(tree);
        body.WatchedAttributes["harvestableInv"] = tree;
        body.WatchedAttributes.MarkPathDirty("harvestableInv");
    }

    private void RemoveHerdWhenDue(HarassContext ctx)
    {
        var harass = ctx.Harass;
        if (harass["herdIds"] is not LongArrayAttribute ids || ids.value.Length == 0 ||
            ctx.Api.World.Calendar.TotalHours < harass.GetDouble(HarassMemory.HerdUntil))
        {
            return;
        }
        var world = ctx.Api.World;
        var at = new BlockPos((int)harass.GetDouble("herdX"), (int)harass.GetDouble("herdY"), (int)harass.GetDouble("herdZ"), 0);
        if (world.BlockAccessor.GetChunkAtBlockPos(at) == null)
        {
            // Not loaded: they wait for it, and go the moment it is.
            return;
        }
        foreach (long id in ids.value)
        {
            world.GetEntityById(id)?.Die(EnumDespawnReason.Removed);
        }
        harass.RemoveAttribute("herdIds");
    }

    /// <summary>Removes the herd now, whatever its time (the self-test).</summary>
    public void RemoveHerdNow(HarassContext ctx)
    {
        ctx.Harass.SetDouble(HarassMemory.HerdUntil, 0);
        RemoveHerdWhenDue(ctx);
    }

    private bool PlayerNear(HarassContext ctx, Vec3d spot, double range) =>
        ctx.Victim.Pos.DistanceTo(spot) <= range ||
        system.Stalker.CountingNear(spot, ctx.Victim.Pos.Dimension, range).Any() ||
        ctx.Api.World.AllOnlinePlayers.Any(p => p.Entity != null && p.Entity.Pos.Dimension == ctx.Victim.Pos.Dimension && p.Entity.Pos.DistanceTo(spot) <= range);

    // ---- Eaten crops ----

    public ActResult EatCrops(HarassContext ctx)
    {
        var config = Config.Crops;
        var world = ctx.Api.World;
        var harass = ctx.Harass;
        if (harass.GetInt(HarassMemory.CropsFor) != Returns(ctx))
        {
            harass.SetInt(HarassMemory.CropsFor, Returns(ctx));
            harass.SetInt(HarassMemory.CropsEaten, 0);
        }
        int eaten = harass.GetInt(HarassMemory.CropsEaten);
        var crops = CropsNear(ctx);
        int allowed = Math.Min(config.MaxPerReturn, (int)Math.Floor(config.MaxShare * crops.Count)) - eaten;
        int want = Math.Min(config.BlocksPerAct, allowed);
        if (want <= 0)
        {
            return ActResult.Skip($"this return's crops are spent ({eaten} eaten, {crops.Count} in range)");
        }
        var ripe = crops.Where(c => c.Value.CurrentStage() >= (c.Value.CropProps?.GrowthStages ?? int.MaxValue)).ToDictionary(c => c.Key, c => c.Value);
        List<BlockPos>? best = null;
        foreach (var start in ripe.Keys.OrderBy(_ => ctx.Rand.Next()).Take(8))
        {
            if (BlockRules.VictimSees(world, ctx.Victim, start))
            {
                continue;
            }
            foreach (var face in BlockFacing.HORIZONTALS.OrderBy(_ => ctx.Rand.Next()))
            {
                var line = new List<BlockPos> { start };
                var next = start.AddCopy(face);
                while (line.Count < want && ripe.ContainsKey(next))
                {
                    line.Add(next);
                    next = next.AddCopy(face);
                }
                if (best == null || line.Count > best.Count)
                {
                    best = line;
                }
            }
            if (best != null && best.Count >= want)
            {
                break;
            }
        }
        if (best == null)
        {
            return ActResult.Skip("no ripe crop out of the victim's sight where it may eat");
        }
        int i = 0;
        foreach (var pos in best)
        {
            var crop = ripe[pos];
            int stage = Math.Max(1, crop.CurrentStage() - config.StagesBack);
            var younger = world.GetBlock(crop.CodeWithVariant("stage", stage.ToString()));
            if (younger == null)
            {
                continue;
            }
            world.BlockAccessor.ExchangeBlock(younger.Id, pos);
            world.BlockAccessor.MarkBlockDirty(pos);
            var at = new Vec3d(pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5);
            ctx.Api.Event.RegisterCallback(_ =>
            {
                HarassSounds.At(world, Plant, at, 0.8f, 16, 0.8f);
                HarassSounds.At(world, Eat, at, 0.6f, 16, 0.7f);
            }, 1 + i * 900);
            i++;
        }
        harass.SetInt(HarassMemory.CropsEaten, eaten + i);
        return i > 0 ? ActResult.Ok($"{i} crop(s) eaten back {config.StagesBack} stage(s) from {best[0]}") : ActResult.Skip("no younger stage for these crops");
    }

    /// <summary>Crop blocks within range of the victim where the claim rule lets it eat.</summary>
    public Dictionary<BlockPos, BlockCrop> CropsNear(HarassContext ctx)
    {
        var world = ctx.Api.World;
        var center = ctx.Victim.Pos.AsBlockPos;
        var found = new Dictionary<BlockPos, BlockCrop>();
        world.BlockAccessor.WalkBlocks(center.AddCopy(-CropRange, -CropHeight, -CropRange), center.AddCopy(CropRange, CropHeight, CropRange), (block, x, y, z) =>
        {
            if (block is BlockCrop crop)
            {
                var pos = new BlockPos(x, y, z, center.dimension);
                if (CropClaimAllows(ctx, pos))
                {
                    found[pos] = crop;
                }
            }
        }, centerOrder: false);
        return found;
    }

    private bool CropClaimAllows(HarassContext ctx, BlockPos pos)
    {
        var claims = ctx.Api.World.Claims.Get(pos);
        if (claims == null || claims.Length == 0)
        {
            return Config.Crops.ClaimRule == CropClaimRule.VictimAccess || !ctx.Api.Server.IsDedicated;
        }
        return BlockRules.ClaimsAllow(ctx.Api.World, ClaimRespect.VictimAccess, pos, ctx.Victim);
    }

    // ---- Livestock (opt-in) ----

    public ActResult KillLivestock(HarassContext ctx)
    {
        if (ctx.Victim is not EntityPlayer { PlayerUID: { } uid })
        {
            return ActResult.Skip("a stand-in owns no animals");
        }
        var world = ctx.Api.World;
        var animals = world.GetEntitiesAround(ctx.Victim.Pos.XYZ, 32, 12, e => e is EntityAgent && e.Alive && e != ctx.Victim && OwnClaim(world, e.Pos.AsBlockPos, uid));
        var herds = animals.Where(e => Gender(e) != null).GroupBy(e => (e.Code.FirstCodePart(), Gender(e))).ToDictionary(g => g.Key, g => g.Count());
        var victim = animals
            .Where(e => Gender(e) is { } g && herds[(e.Code.FirstCodePart(), g)] > 2 && MayDie(e))
            .OrderBy(_ => ctx.Rand.Next())
            .FirstOrDefault();
        if (victim == null)
        {
            return ActResult.Skip("no farm animal it may take");
        }
        victim.Die(EnumDespawnReason.Death, new DamageSource { Source = EnumDamageSource.Unknown, Type = EnumDamageType.Hunger });
        return ActResult.Ok($"{victim.Code.ToShortString()} #{victim.EntityId} found dead");
    }

    private static bool OwnClaim(IWorldAccessor world, BlockPos pos, string uid)
    {
        var claims = world.Claims.Get(pos);
        return claims != null && claims.Length > 0 && claims.All(c => c.OwnedByPlayerUid == uid);
    }

    private static string? Gender(Entity e) => e.Properties.Variant != null && e.Properties.Variant.TryGetValue("gender", out string? g) ? g : null;

    /// <summary>Bred on the farm (generation 1 or more), and not named, tamed, rideable or pregnant.</summary>
    private static bool MayDie(Entity e)
    {
        var watched = e.WatchedAttributes;
        if (watched.GetInt("generation") < 1 || watched.HasAttribute("ownedby") || e.GetBehavior("rideable") != null)
        {
            return false;
        }
        if (!string.IsNullOrEmpty(e.GetBehavior<EntityBehaviorNameTag>()?.DisplayName))
        {
            return false;
        }
        return watched.GetTreeAttribute("multiply")?.GetBool("isPregnant") != true;
    }
}
