using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Walkers.Director;

namespace Walkers.Forms;

/// <summary>
/// Server side of both walker forms ("walkers-form", added in <see cref="FormsModSystem.AssetsFinalize"/>): the guise's
/// damage clamp, the shift triggers both ways, fire on our own terms, and the death check with the body's
/// burn window afterwards (creature bible, sections 4 and 7).
/// </summary>
public class EntityBehaviorWalkerForm : EntityBehavior
{
    public const string Code = Core.WalkersModSystem.ModId + "-form";

    /// <summary>Guise to true form at night needs its victim this close (creature bible, section 4).</summary>
    private const double NightShiftVictimRange = 48;

    /// <summary>No shift back to the guise within this many real milliseconds of a player's hit.</summary>
    private const long AfterHitMs = 20000;

    /// <summary>A hurt guise is whole again this long after the last blow: it shrugs damage off.</summary>
    private const long GuiseMendMs = 10000;

    /// <summary>A torch hit this recent names the igniter of a fire that starts.</summary>
    private const long IgniterMemoryMs = 2000;

    /// <summary>The vanilla burn rate, which the guise keeps.</summary>
    private const float GuiseBurnPerSecond = 0.5f;

    /// <summary>Right-click reach on a body, as vanilla harvesting checks it.</summary>
    private const double BodyReach = 6;

    /// <summary>Death states of a true body.</summary>
    private enum BodyState { None, Lying, Skinned, Done }

    private FormsModSystem forms = null!;
    private bool isTrue;
    private bool started;
    private float checkAccum;
    private float burnAccum;
    private long burnUntilMs;
    private long lastRefreshMs;
    private long standUpAtMs;
    private long lastPlayerHitMs;
    private long lastHitMs;
    private IPlayer? lastTorchBy;
    private long lastTorchMs;
    private IPlayer? lastAttacker;
    private ItemStack? lastAttackStack;
    private long lastAttackMs;
    private readonly HashSet<string> reportedHarm = new();

    private BodyState bodyState;
    private long diedMs;

    public EntityBehaviorWalkerForm(Entity entity) : base(entity)
    {
    }

    public override string PropertyName() => Code;

    /// <summary>True while it is dead and its body still counts for the ritual.</summary>
    public bool AwaitsRitual => bodyState == BodyState.Lying;

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        base.Initialize(properties, attributes);
        isTrue = FormMemory.IsTrueForm(entity);
    }

    public override void AfterInitialized(bool onFirstSpawn)
    {
        base.AfterInitialized(onFirstSpawn);
        if (entity.Api is not ICoreServerAPI sapi)
        {
            return;
        }
        forms = sapi.ModLoader.GetModSystem<FormsModSystem>();
        var health = entity.GetBehavior<EntityBehaviorHealth>();
        if (health != null)
        {
            health.onDamaged += OnDamaged;
        }
    }

    // ---- Damage ----

    private float OnDamaged(float damage, DamageSource source)
    {
        if (source.Type == EnumDamageType.Heal || !entity.Alive)
        {
            return damage;
        }
        if (IsVanillaBurnTick(source))
        {
            // Ours replaces it (see TickFire): vanilla burning is weak and has no attacker.
            return 0;
        }
        long now = entity.World.ElapsedMilliseconds;
        lastHitMs = now;
        var player = WalkerRitual.PlayerOf(source);
        if (player != null)
        {
            lastPlayerHitMs = now;
            NoteHarm(player);
        }

        if (!isTrue)
        {
            if (player == null)
            {
                // A fall, drowning or another creature can kill the guise; the true body then lies in its place.
                return damage;
            }
            ScheduleStandUp();
            if (!forms.Config.Forms.GuiseShrugsDamage)
            {
                return damage;
            }
            var health = entity.GetBehavior<EntityBehaviorHealth>();
            float allowed = health == null ? damage : Math.Max(0, health.Health - 1);
            if (damage <= allowed)
            {
                return damage;
            }
            if (allowed <= 0.05f)
            {
                // No damage means no hurt animation from vanilla; the doe still flinches (owner's answer 2).
                PlayHurt();
            }
            return allowed;
        }

        var weapon = WalkerRitual.WeaponOf(source, entity.World, AttackSnapshot(player));
        if (WalkerRitual.IsSilver(weapon, forms.Config.Ritual))
        {
            damage *= forms.Config.Ritual.SilverDamageMultiplier;
        }
        return damage;
    }

    private static bool IsVanillaBurnTick(DamageSource source) =>
        source is not WalkersBurnDamageSource &&
        source.Type == EnumDamageType.Fire && source.Source == EnumDamageSource.Internal &&
        source.SourceEntity == null && source.CauseEntity == null && source.SourceBlock == null && source.SourcePos == null;

    /// <summary>
    /// A player's blow or fire got through anyway (a death the walker class refused, see
    /// <see cref="FormsModSystem"/>): the doe keeps a sliver of health, flinches and stands up.
    /// </summary>
    internal void OnDeathRefused()
    {
        var health = entity.GetBehavior<EntityBehaviorHealth>();
        if (health != null && health.Health < 1)
        {
            health.Health = Math.Min(1, health.MaxHealth);
        }
        lastHitMs = lastPlayerHitMs = entity.World.ElapsedMilliseconds;
        PlayHurt();
        ScheduleStandUp();
    }

    /// <summary>The guise stands up forms.hurtShiftDelayMs after a player hurts it or sets it alight (creature bible, section 4).</summary>
    private void ScheduleStandUp()
    {
        if (standUpAtMs == 0)
        {
            standUpAtMs = entity.World.ElapsedMilliseconds + forms.Config.Forms.HurtShiftDelayMs;
        }
    }

    private void PlayHurt()
    {
        entity.AnimManager?.StartAnimation("hurt");
        entity.PlayEntitySound("hurt");
    }

    /// <summary>Tells the director once per attacker per body that a player hurt the walker (it decides what that means).</summary>
    private void NoteHarm(IPlayer player)
    {
        if (player.Entity == null || !reportedHarm.Add(player.PlayerUID))
        {
            return;
        }
        var director = forms.Director;
        if (director != null && director.TryGetWalkerId(entity, out var walkerId))
        {
            director.ReportHarm(walkerId, player.Entity);
        }
    }

    /// <summary>Called by <see cref="FormsModSystem"/> when a player starts an attack on this body, before any damage lands.</summary>
    public void NoteAttack(IPlayer player, ItemStack? stack)
    {
        lastAttacker = player;
        lastAttackStack = stack?.Clone();
        lastAttackMs = entity.World.ElapsedMilliseconds;
    }

    private ItemStack? AttackSnapshot(IPlayer? player) =>
        player != null && lastAttacker?.PlayerUID == player.PlayerUID && entity.World.ElapsedMilliseconds - lastAttackMs < 1000 ? lastAttackStack : null;

    // ---- Interaction: torches on the living, fire on the body ----

    public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode, ref EnumHandling handled)
    {
        if (entity.World.Side != EnumAppSide.Server || byEntity is not EntityPlayer playerEntity || playerEntity.Player == null)
        {
            return;
        }
        var stack = itemslot?.Itemstack;
        if (entity.Alive)
        {
            if (mode == EnumInteractMode.Attack && IsLitTorch(stack))
            {
                lastTorchBy = playerEntity.Player;
                lastTorchMs = entity.World.ElapsedMilliseconds;
                if (!entity.IsOnFire && entity.World.Rand.NextDouble() < forms.Config.Ritual.TorchIgniteChance)
                {
                    entity.Ignite();
                }
            }
            return;
        }
        // Right-click on the body with a lit torch or a firestarter sets it alight.
        if (mode == EnumInteractMode.Interact && bodyState is BodyState.Lying or BodyState.Skinned && !entity.IsOnFire &&
            (IsLitTorch(stack) || stack?.Collectible is ItemFirestarter) &&
            entity.Pos.XYZ.Add(hitPosition).DistanceTo(byEntity.Pos.XYZ.Add(byEntity.LocalEyePos)) <= BodyReach)
        {
            lastTorchBy = playerEntity.Player;
            lastTorchMs = entity.World.ElapsedMilliseconds;
            entity.Ignite();
            handled = EnumHandling.PreventSubsequent;
        }
    }

    private static bool IsLitTorch(ItemStack? stack) => stack?.Block is BlockTorch && stack.Block.Variant["state"] == "lit";

    // ---- Ticks ----

    public override void OnGameTick(float deltaTime)
    {
        if (forms == null || entity.World.Side != EnumAppSide.Server)
        {
            return;
        }
        if (!started)
        {
            started = true;
            if (isTrue && entity.Alive && !entity.Attributes.GetBool(WalkerShift.FromGuiseFlag))
            {
                WalkerShift.ApplyTrueHealth(forms, entity);
                if (FormMemory.Form(entity).GetLong(FormMemory.LastShiftMs) > 0)
                {
                    entity.AnimManager?.StartAnimation("contort");
                }
            }
            // A shift's flicker lasts a second; otherwise the server holds both bodies still (no flicker).
            entity.WatchedAttributes.SetDouble("temporalStability", FormMemory.Form(entity).GetLong(FormMemory.LastShiftMs) > 0 ? 0 : 1);
        }

        TickFire(deltaTime);
        if (standUpAtMs > 0 && entity.World.ElapsedMilliseconds >= standUpAtMs)
        {
            standUpAtMs = 0;
            // Stages 1 and 2 never stand up for a blow; stage 0 is a body nobody is haunting with (a command spawn).
            int stage = FormMemory.Stage(entity);
            if (!isTrue && entity.Alive && (stage == 0 || stage >= 3) && !InWater())
            {
                TryShift("a player hurt it");
                return;
            }
        }
        checkAccum += deltaTime;
        if (checkAccum < 1)
        {
            return;
        }
        checkAccum = 0;
        if (entity.WatchedAttributes.GetDouble("temporalStability", 1) < 1)
        {
            entity.WatchedAttributes.SetDouble("temporalStability", 1);
        }
        if (entity.Alive)
        {
            TickAlive();
        }
        else
        {
            TickDead();
        }
    }

    /// <summary>Runs this second's checks at once (the self-test does not wait for ticks).</summary>
    internal void CheckNow()
    {
        checkAccum = 1;
        OnGameTick(0);
    }

    /// <summary>
    /// Fire on our own terms: ritual.burnSeconds per ignition, ritual.burnDamagePerSecond to the true form,
    /// the vanilla 0.5 to the guise, and every burn tick names its igniter.
    /// </summary>
    private void TickFire(float deltaTime)
    {
        long now = entity.World.ElapsedMilliseconds;
        if (!entity.IsOnFire)
        {
            burnUntilMs = 0;
            return;
        }
        var ritual = forms.Config.Ritual;
        var form = FormMemory.Form(entity);
        if (burnUntilMs == 0)
        {
            burnUntilMs = now + (long)(ritual.BurnSeconds * 1000);
            burnAccum = 0;
            var igniter = lastTorchBy != null && now - lastTorchMs < IgniterMemoryMs ? lastTorchBy : null;
            if (igniter != null)
            {
                form.SetString(FormMemory.IgniterUid, igniter.PlayerUID);
                if (!isTrue && entity.Alive)
                {
                    // Our burn ticks name the igniter, vanilla's do not; a guise a player set alight stands up at once.
                    ScheduleStandUp();
                }
            }
            else if (!form.HasAttribute(FormMemory.IgniterUid) || now - lastPlayerHitMs > IgniterMemoryMs * 5)
            {
                form.RemoveAttribute(FormMemory.IgniterUid);
            }
        }
        if (now >= burnUntilMs)
        {
            entity.IsOnFire = false;
            burnUntilMs = 0;
            return;
        }
        // Vanilla puts the fire out 12 s after it began (Entity.cs:1078); keep it going, and remind clients.
        entity.OnFireBeginTotalMs = now;
        if (now - lastRefreshMs > 5000)
        {
            lastRefreshMs = now;
            entity.WatchedAttributes.MarkPathDirty("onFire");
        }
        if (!entity.Alive)
        {
            return;
        }
        burnAccum += deltaTime;
        if (burnAccum < 1)
        {
            return;
        }
        burnAccum -= 1;
        var igniterPlayer = Igniter();
        entity.ReceiveDamage(new WalkersBurnDamageSource
        {
            Source = igniterPlayer?.Entity != null ? EnumDamageSource.Player : EnumDamageSource.Internal,
            Type = EnumDamageType.Fire,
            CauseEntity = igniterPlayer?.Entity,
            IgnoreInvFrames = true,
        }, isTrue ? ritual.BurnDamagePerSecond : GuiseBurnPerSecond);
    }

    /// <summary>The player who set it alight, while online.</summary>
    private IPlayer? Igniter()
    {
        var uid = FormMemory.Form(entity).GetString(FormMemory.IgniterUid);
        return uid == null ? null : entity.World.PlayerByUid(uid);
    }

    private void TickAlive()
    {
        long now = entity.World.ElapsedMilliseconds;
        var config = forms.Config.Forms;
        int stage = FormMemory.Stage(entity);
        var form = FormMemory.Form(entity);

        if (!isTrue)
        {
            var health = entity.GetBehavior<EntityBehaviorHealth>();
            if (config.GuiseShrugsDamage && health != null && health.Health < health.MaxHealth && now - lastHitMs > GuiseMendMs)
            {
                health.Health = health.MaxHealth;
            }
        }

        bool cooled = now - form.GetLong(FormMemory.LastShiftMs, -1000000) >= config.ShiftCooldownSeconds * 1000;
        if (!cooled || InWater())
        {
            return;
        }

        if (!isTrue)
        {
            if (stage < 4)
            {
                return;
            }
            var victim = Victim();
            if (stage == 5 && config.RevealShift && !form.GetBool(FormMemory.Revealed) && victim != null &&
                victim.Pos.DistanceTo(entity.Pos) <= config.RevealDistance && VictimWatching(victim))
            {
                form.SetBool(FormMemory.Revealed, true);
                TryShift("the reveal");
                return;
            }
            if (forms.IsNight(entity) && victim != null && victim.Pos.DistanceTo(entity.Pos) <= NightShiftVictimRange &&
                (stage == 5 || forms.SilhouetteAllowed(entity)) && !IsWatched())
            {
                TryShift("night");
            }
            return;
        }

        if (entity.IsOnFire || now - lastPlayerHitMs < AfterHitMs || IsWatched())
        {
            return;
        }
        var breakOff = FormMemory.Ai(entity)?.GetString(FormMemory.AiBreakOff);
        if (!string.IsNullOrEmpty(breakOff))
        {
            TryShift("broke off: " + breakOff);
        }
        else if (stage >= 4 && !forms.IsNight(entity))
        {
            TryShift("dawn");
        }
        else if (stage is >= 1 and <= 3)
        {
            TryShift("its stage fell to " + stage);
        }
    }

    private void TryShift(string why)
    {
        if (WalkerShift.Shift(forms, entity, why, out var failure) == null)
        {
            // The spot does not fit the other body; the next check tries again, as Grow does.
            forms.Mod.Logger.Debug("Forms: {0} #{1} could not shift ({2}): {3}", entity.Code.Path, entity.EntityId, why, failure);
        }
    }

    private bool InWater() => entity.Swimming || entity.FeetInLiquid;

    private Entity? Victim()
    {
        var director = forms.Director;
        if (director != null && director.TryGetWalkerId(entity, out var walkerId))
        {
            return director.GetVictim(walkerId);
        }
        long id = FormMemory.Tree(entity).GetLong(WalkerMemory.VictimEntity);
        return id == 0 ? null : entity.World.GetEntityById(id);
    }

    /// <summary>The stalker AI's gaze result when it keeps one; otherwise our own check against every counting player.</summary>
    private bool IsWatched()
    {
        var ai = FormMemory.Ai(entity);
        if (ai != null && ai.HasAttribute(FormMemory.AiWatched))
        {
            return ai.GetBool(FormMemory.AiWatched);
        }
        return forms.CountingPlayersNear(entity, forms.Config.Gaze.MaxDistance).Any(p => forms.Sees(p.Entity, entity));
    }

    private bool VictimWatching(Entity victim)
    {
        var ai = FormMemory.Ai(entity);
        if (ai != null && ai.HasAttribute(FormMemory.AiVictimWatching))
        {
            return ai.GetBool(FormMemory.AiVictimWatching);
        }
        return victim is EntityPlayer && forms.Sees(victim, entity);
    }

    // ---- Death and the body ----

    public override void OnEntityDeath(DamageSource damageSourceForDeath)
    {
        base.OnEntityDeath(damageSourceForDeath);
        if (forms == null)
        {
            return;
        }
        if (damageSourceForDeath?.Source == EnumDamageSource.Void)
        {
            // No body: the director sends it straight to the ledger.
            bodyState = BodyState.Done;
            return;
        }
        if (!isTrue)
        {
            bodyState = BodyState.Done;
            WalkerShift.ReplaceCarcass(forms, entity, damageSourceForDeath);
            return;
        }

        var killer = WalkerRitual.PlayerOf(damageSourceForDeath);
        if (!entity.Attributes.GetBool(WalkerShift.FromGuiseFlag))
        {
            var weapon = WalkerRitual.WeaponOf(damageSourceForDeath, entity.World, AttackSnapshot(killer));
            string? cause = null;
            if (WalkerRitual.IsSilver(weapon, forms.Config.Ritual))
            {
                cause = damageSourceForDeath?.SourceEntity is IProjectile ? WalkerRitual.CauseSilverArrow : WalkerRitual.CauseSilverWeapon;
            }
            else if (entity.IsOnFire || damageSourceForDeath?.Type == EnumDamageType.Fire)
            {
                cause = WalkerRitual.CauseFire;
                killer ??= Igniter();
            }
            if (cause != null)
            {
                FinishBanished(killer, cause);
                return;
            }
        }

        // An ordinary death: the body lies for ritual.huskBurnWindowSeconds; the director has written the return already.
        bodyState = BodyState.Lying;
        diedMs = entity.World.ElapsedMilliseconds;
        var decay = entity.GetBehavior<EntityBehaviorDeadDecay>();
        if (decay != null)
        {
            // Our window removes the body, not the decay timer.
            decay.HoursToDecay = 100000;
        }
        entity.IsOnFire = false;
        var skinners = new List<string>();
        var victimUid = FormMemory.Tree(entity).GetString(WalkerMemory.Victim);
        if (!string.IsNullOrEmpty(victimUid))
        {
            skinners.Add(victimUid!);
        }
        if (killer != null && !skinners.Contains(killer.PlayerUID))
        {
            skinners.Add(killer.PlayerUID);
        }
        EntityBehaviorWalkerHusk.Open(entity, skinners, forms.Config.Ritual.AllowHarvestHusk);
        forms.Mod.Logger.Notification("Forms: true body #{0} lies for {1} s after an ordinary death ({2}); burn it or it returns",
            entity.EntityId, forms.Config.Ritual.HuskBurnWindowSeconds, damageSourceForDeath?.Type.ToString() ?? "unknown");
    }

    private void TickDead()
    {
        long now = entity.World.ElapsedMilliseconds;
        switch (bodyState)
        {
            case BodyState.Lying when entity.IsOnFire:
                FinishBanished(lastTorchBy != null && now - lastTorchMs < IgniterMemoryMs ? lastTorchBy : null, WalkerRitual.CauseBurnedBody);
                return;
            case BodyState.Skinned when entity.IsOnFire:
                // Too late for the ritual; the fire only burns it away.
                WalkerRitual.SpawnAsh(entity.World, entity.Pos.XYZ, 30);
                RemoveBody();
                return;
            case BodyState.Lying or BodyState.Skinned when now - diedMs >= forms.Config.Ritual.HuskBurnWindowSeconds * 1000 && !IsWatched():
                if (bodyState == BodyState.Lying)
                {
                    WalkerRitual.Release(forms, entity, WalkerRelease.LeftAlone, false);
                }
                WhisperToVictim();
                RemoveBody();
                return;
        }
    }

    /// <summary>Called by the husk behavior when a permitted player finished skinning the body.</summary>
    public void OnSkinned(IPlayer byPlayer)
    {
        if (bodyState != BodyState.Lying)
        {
            return;
        }
        bodyState = BodyState.Skinned;
        WalkerRitual.Release(forms, entity, WalkerRelease.Skinned, true);
    }

    private void FinishBanished(IPlayer? byPlayer, string cause)
    {
        bodyState = BodyState.Done;
        WalkerRitual.Banish(forms, entity, byPlayer, cause);
        RemoveBody();
    }

    /// <summary>The unburned body vanishes: its victim hears one whisper (lore bible, W6).</summary>
    private void WhisperToVictim()
    {
        var uid = FormMemory.Tree(entity).GetString(WalkerMemory.Victim);
        if (uid != null && entity.World.PlayerByUid(uid) is IServerPlayer victim && victim.ConnectionState == EnumClientState.Playing)
        {
            forms.Sapi.SendIngameDiscovery(victim, "walkers-whisper-06", null);
        }
    }

    private void RemoveBody()
    {
        bodyState = BodyState.Done;
        var decay = entity.GetBehavior<EntityBehaviorDeadDecay>();
        if (decay != null)
        {
            decay.DecayNow();
        }
        else if (entity is EntityAgent agent)
        {
            agent.AllowDespawn = true;
        }
    }
}
