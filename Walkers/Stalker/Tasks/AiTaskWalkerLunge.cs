using System;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Walkers.Stalker.Tasks;

/// <summary>
/// walkers-lunge (true form only; creature bible, flag 6): from within gaze.lungeRange it dashes at
/// its target and strikes once, at forms.trueDamage and forms.trueDamageTier, never above
/// forms.maxHitFraction of the target's maximum health. In the hunt the target is the victim, or a
/// player who just hit it; in the defence, whoever hit it last. A dash from range waits
/// stalk.lungeCooldownSeconds; a strike from close by waits forms.attackCooldownMs. After a strike
/// that lands, the hunt backs off for a moment.
///
/// It never strikes: with creatureHostility off; with passive hostility unless a player has hurt it
/// in this haunt; a sleeping victim, or one who woke less than 10 seconds ago; a player in creative
/// or spectator mode; or through a wall (one ray, at most four times a second).
///
/// JSON: <c>durationMs</c> (1000), <c>hitAtMs</c> (400), <c>reach</c> (2) from its front to the
/// target's box, <c>dashFactor</c> (0.05) of the distance as the dash's push; the vanilla
/// <c>animation</c> and <c>sound</c> play with it.
/// </summary>
public class AiTaskWalkerLunge : WalkerTaskBase
{
    public const string Code = "walkers-lunge";

    /// <summary>No strike within this long of the victim leaving their bed (creature bible, section 5).</summary>
    public const long WakeGraceMs = 10000;

    /// <summary>Closer than this, it strikes without a dash.</summary>
    private const double CloseBlocks = 2.5;

    /// <summary>A player who hit it within this long is fair game in the hunt, as well as the victim.</summary>
    private const long RetaliateMs = 5000;

    /// <summary>The target and line-of-sight check runs at most this often.</summary>
    private const long CheckEveryMs = 250;

    /// <summary>The dash lifts the body this much, so ground drag does not swallow it.</summary>
    private const double DashLift = 0.1;

    /// <summary>How long it keeps its distance after a strike landed.</summary>
    private const long BackOffMs = 2500;

    /// <summary>The push of a lunge from a doorway, about one block.</summary>
    private const double DoorwayStep = 0.25;

    [JsonProperty]
    public int DurationMs = 1000;

    [JsonProperty]
    public int HitAtMs = 400;

    [JsonProperty]
    public float Reach = 2;

    [JsonProperty]
    public float DashFactor = 0.05f;

    private Entity? target;
    private long startedMs;
    private long lastCheckMs;
    private long nextStrikeMs;
    private long nextDashMs;
    private bool struck;
    private readonly GazeSight sight;

    public AiTaskWalkerLunge(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
    {
        sight = new GazeSight(entity.World, physical: true);
    }

    public override bool ShouldExecute()
    {
        var brain = Brain;
        if (brain == null || !brain.IsTrueForm || brain.Mode is not (StalkerMode.Hunt or StalkerMode.Defend) || entity.Swimming)
        {
            return false;
        }
        long now = world.ElapsedMilliseconds;
        if (now < nextStrikeMs || now - lastCheckMs < CheckEveryMs || brain.Frozen)
        {
            // Frozen only outside the hunt's lunge range: a watched defender holds still too.
            return false;
        }
        lastCheckMs = now;
        var candidate = PickTarget(brain);
        if (candidate == null)
        {
            return false;
        }
        double dist = entity.Pos.DistanceTo(candidate.Pos);
        if (dist > brain.Config.Gaze.LungeRange || (dist > CloseBlocks && now < nextDashMs))
        {
            return false;
        }
        if (!MayStrike(brain, candidate) || !ClearLine(candidate))
        {
            return false;
        }
        target = candidate;
        return true;
    }

    public override void StartExecute()
    {
        base.StartExecute();
        var brain = Brain!;
        var t = target!;
        startedMs = world.ElapsedMilliseconds;
        struck = false;
        entity.Pos.Yaw = YawTo(entity.Pos, t.Pos.XYZ);
        double dist = entity.Pos.DistanceTo(t.Pos);
        if (dist > CloseBlocks)
        {
            double dx = t.Pos.X - entity.Pos.X, dz = t.Pos.Z - entity.Pos.Z;
            if (brain.HoldingDoorway)
            {
                // From a doorway: one step in at most, no run.
                double len = Math.Max(1, Math.Sqrt(dx * dx + dz * dz));
                entity.Pos.Motion.Add(dx / len * DoorwayStep, DashLift, dz / len * DoorwayStep);
            }
            else
            {
                // The dash: a push toward the target, and the chase speed while the strike winds up.
                entity.Pos.Motion.Add(dx * DashFactor, DashLift, dz * DashFactor);
                WalkStraight(t.Pos.XYZ, brain.Config.Stalk.ChaseSpeed * SpeedFactor, Reach * 0.5f, true);
            }
            nextDashMs = startedMs + (long)(brain.Config.Stalk.LungeCooldownSeconds * 1000);
        }
    }

    public override bool ContinueExecute(float dt)
    {
        var brain = Brain;
        var t = target;
        if (brain == null || t == null || !t.Alive || t.Pos.Dimension != entity.Pos.Dimension)
        {
            return false;
        }
        long now = world.ElapsedMilliseconds;
        if (!pathTraverser.Active)
        {
            TurnTowards(t.Pos.XYZ, 10 * GameMath.DEG2RAD);
        }
        else if (Goal != null)
        {
            Goal.Set(t.Pos.X, t.Pos.Y, t.Pos.Z);
        }
        if (!struck && now - startedMs >= HitAtMs)
        {
            struck = true;
            Halt();
            TryStrike(brain, t);
        }
        return now - startedMs < DurationMs;
    }

    public override void FinishExecute(bool cancelled)
    {
        base.FinishExecute(cancelled);
        nextStrikeMs = world.ElapsedMilliseconds + Math.Max(0, Brain?.Config.Forms.AttackCooldownMs ?? 1500);
        target = null;
    }

    /// <summary>In the defence, whoever hit it last; in the hunt, a player who just hit it, else the victim.</summary>
    private static Entity? PickTarget(WalkerBrain brain)
    {
        var attacker = brain.LastAttacker;
        bool attackerFresh = attacker != null && attacker.Alive && brain.MsSincePlayerHit < RetaliateMs;
        if (brain.Mode == StalkerMode.Defend)
        {
            return attacker != null && attacker.Alive ? attacker : null;
        }
        if (attackerFresh && attacker!.EntityId != brain.Victim?.EntityId)
        {
            return attacker;
        }
        return brain.Victim;
    }

    private bool MayStrike(WalkerBrain brain, Entity candidate)
    {
        var hostility = StalkerModSystem.HostilityOf(world);
        if (hostility == EnumCreatureHostilityLevel.Off)
        {
            return false;
        }
        if (hostility == EnumCreatureHostilityLevel.Passive && !brain.Ai.GetBool(StalkerMemory.Provoked))
        {
            return false;
        }
        if (StalkerModSystem.IsAsleep(candidate))
        {
            return false;
        }
        bool isVictim = candidate.EntityId == brain.Victim?.EntityId;
        if (isVictim && brain.MsSinceVictimAsleep < WakeGraceMs)
        {
            return false;
        }
        if (candidate is EntityPlayer player)
        {
            var mode = player.Player?.WorldData?.CurrentGameMode;
            if (mode is EnumGameMode.Creative or EnumGameMode.Spectator)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>One ray from its eyes to the target's chest, blocked by anything with a collision box, windows included.</summary>
    private bool ClearLine(Entity candidate)
    {
        var from = WalkerGaze.EyePos(entity);
        var to = new Vec3d(candidate.Pos.X, candidate.Pos.Y + (candidate.SelectionBox.Y2 - candidate.SelectionBox.Y1) * 0.6, candidate.Pos.Z);
        BlockSelection? blockSel = null;
        EntitySelection? entitySel = null;
        world.RayTraceForSelection(sight, from, to, ref blockSel, ref entitySel, null, _ => false);
        return blockSel == null;
    }

    private void TryStrike(WalkerBrain brain, Entity t)
    {
        // Reach is measured from its front to the target's box, as the vanilla melee does.
        double reach = Reach + (entity.SelectionBox.XSize + t.SelectionBox.XSize) / 2;
        if (entity.Pos.DistanceTo(t.Pos) > reach + 0.5 || Math.Abs(entity.Pos.Y - t.Pos.Y) > 2 || !MayStrike(brain, t) || !ClearLine(t))
        {
            return;
        }
        var forms = brain.Config.Forms;
        float damage = forms.TrueDamage * GlobalConstants.CreatureDamageModifier;
        float? maxHealth = t.GetBehavior<EntityBehaviorHealth>()?.MaxHealth;
        if (maxHealth is > 0)
        {
            damage = Math.Min(damage, forms.MaxHitFraction * maxHealth.Value);
        }
        bool landed = t.ReceiveDamage(new DamageSource
        {
            Source = EnumDamageSource.Entity,
            SourceEntity = entity,
            CauseEntity = entity,
            Type = EnumDamageType.SlashingAttack,
            DamageTier = forms.TrueDamageTier,
            KnockbackStrength = GameMath.Sqrt(damage / 4f),
        }, damage);
        if (!landed)
        {
            return;
        }
        if (t.EntityId == brain.Victim?.EntityId)
        {
            brain.OnStruckVictim();
        }
        brain.BackOffUntilMs = world.ElapsedMilliseconds + BackOffMs;
    }
}
