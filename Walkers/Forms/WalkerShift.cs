using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;
using Walkers.Director;

namespace Walkers.Forms;

/// <summary>
/// The shift between the two bodies (creature bible, section 4), done as vanilla grows a chick into a
/// hen (BehaviorGrow.cs:127 to :172): spawn the other type on the same spot with the memory already in
/// it, then retire the old body with Die(Expire), which leaves no corpse, no drops and no death event.
/// </summary>
public static class WalkerShift
{
    /// <summary>Set on a body that a shift retired, so its despawn is not mistaken for anything else.</summary>
    public const string ShiftedFlag = "walkersShifted";

    /// <summary>Set before spawning on a true body that replaces a guise's carcass: its death is the guise's ordinary death.</summary>
    public const string FromGuiseFlag = "walkersFromGuise";

    /// <summary>
    /// Swaps <paramref name="old"/> for a body of the other form. Returns the new body, or null with a
    /// reason when the type is missing or the new body does not fit where the old one stands.
    /// </summary>
    public static Entity? Shift(FormsModSystem forms, Entity old, string why, out string? failure)
    {
        bool toTrue = !FormMemory.IsTrueForm(old);
        var world = old.World;
        var code = toTrue ? FormMemory.TrueCode : FormMemory.GuiseCode(FormMemory.Guise(old));
        var type = world.GetEntityType(code);
        if (type == null)
        {
            failure = "no entity type " + code;
            return null;
        }
        if (world.CollisionTester.IsColliding(world.BlockAccessor, type.SpawnCollisionBox, old.Pos.XYZ, false))
        {
            failure = "no room for " + code.Path;
            return null;
        }

        var form = FormMemory.Form(old);
        long now = world.ElapsedMilliseconds;
        if (toTrue)
        {
            if (FormMemory.IsGuise(old))
            {
                form.SetString(FormMemory.GuiseType, old.Code.Path[FormMemory.GuisePrefix.Length..]);
            }
        }
        else
        {
            // The guise keeps the true form's wounds for the next time it stands up, less what it heals meanwhile.
            var health = old.GetBehavior<EntityBehaviorHealth>();
            form.SetFloat(FormMemory.TrueShare, health == null || health.MaxHealth <= 0 ? 1 : health.Health / health.MaxHealth);
            form.SetDouble(FormMemory.TrueShareDay, world.Calendar.TotalDays);
        }
        form.SetLong(FormMemory.LastShiftMs, now);

        var body = world.ClassRegistry.CreateEntity(type);
        body.Pos.SetFrom(old.Pos);
        body.PositionBeforeFalling.Set(old.PositionBeforeFalling);
        if (old is EntityAgent oldAgent && body is EntityAgent agent)
        {
            agent.BodyYaw = oldAgent.BodyYaw;
            agent.HerdId = oldAgent.HerdId;
        }
        Carry(forms, old, body);

        Mask(old);
        world.SpawnEntity(body);
        if (old.IsOnFire)
        {
            // A guise that stands up while burning brings the fire with it, and the igniter stays in the memory.
            body.Ignite();
        }
        old.Attributes.SetBool(ShiftedFlag, true);
        old.Die(EnumDespawnReason.Expire);

        forms.Mod.Logger.Notification("Forms: {0} #{1} shifted into {2} #{3} ({4})", old.Code.Path, old.EntityId, body.Code.Path, body.EntityId, why);
        failure = null;
        return body;
    }

    /// <summary>
    /// The guise died (with no player as the cause, or with forms.guiseShrugsDamage off): its carcass gives way at once to the true form's body,
    /// which dies of the same death (creature bible, section 7). Returns the dead true body.
    /// </summary>
    public static Entity? ReplaceCarcass(FormsModSystem forms, Entity guise, DamageSource? damage)
    {
        var world = guise.World;
        var type = world.GetEntityType(FormMemory.TrueCode);
        if (type == null)
        {
            return null;
        }
        if (FormMemory.IsGuise(guise))
        {
            FormMemory.Form(guise).SetString(FormMemory.GuiseType, guise.Code.Path[FormMemory.GuisePrefix.Length..]);
        }
        var body = world.ClassRegistry.CreateEntity(type);
        body.Pos.SetFrom(guise.Pos);
        body.Pos.Motion.Set(0, 0, 0);
        body.Attributes.SetBool(FromGuiseFlag, true);
        Carry(forms, guise, body);
        Mask(guise);
        world.SpawnEntity(body);
        body.Die(EnumDespawnReason.Death, damage);
        // The guise has no deaddecay, so its carcass despawns on the next tick; nothing of it stays.
        forms.Mod.Logger.Notification("Forms: {0} #{1} died; true body #{2} lies in its place", guise.Code.Path, guise.EntityId, body.EntityId);
        return body;
    }

    /// <summary>
    /// The walker's memory goes with it: through the director for a walker it placed (the new body shares
    /// the record), else as a copy of the old body's tree.
    /// </summary>
    private static void Carry(FormsModSystem forms, Entity old, Entity body)
    {
        var director = forms.Director;
        if (director != null && director.TryGetWalkerId(old, out var walkerId))
        {
            director.AttachBody(walkerId, body);
            return;
        }
        if (old.Attributes[WalkerMemory.TreeKey] is ITreeAttribute tree)
        {
            body.Attributes[WalkerMemory.TreeKey] = tree.Clone();
        }
    }

    /// <summary>Bark and fur bursting off it, a crack of wood, and a moment of flicker on the old body.</summary>
    private static void Mask(Entity old)
    {
        var world = old.World;
        WalkerRitual.SpawnAsh(world, old.Pos.XYZ, 40);
        world.PlaySoundAt(new AssetLocation("game", "sounds/block/woodcreak"), old, null, true, 32, 1f);
        old.WatchedAttributes.SetDouble("temporalStability", 0);
    }

    /// <summary>
    /// Health of a true body: forms.trueHealth plus the director's bonus for each return, at the share
    /// the memory holds. Applied once, on the body's first tick, because the health behavior sets its
    /// own maximum while the body spawns.
    /// </summary>
    public static void ApplyTrueHealth(FormsModSystem forms, Entity body)
    {
        var health = body.GetBehavior<EntityBehaviorHealth>();
        if (health == null)
        {
            return;
        }
        var config = forms.Config.Forms;
        float bonus = 0;
        var director = forms.Director;
        if (director != null && director.TryGetWalkerId(body, out var walkerId))
        {
            bonus = director.ReturnHealthBonus(walkerId);
        }
        var form = FormMemory.Form(body);
        float share = 1;
        if (form.HasAttribute(FormMemory.TrueShare))
        {
            double days = Math.Max(0, body.World.Calendar.TotalDays - form.GetDouble(FormMemory.TrueShareDay, body.World.Calendar.TotalDays));
            share = (float)Math.Min(1, form.GetFloat(FormMemory.TrueShare, 1) + days * config.HealPerDay);
        }
        health.BaseMaxHealth = config.TrueHealth * (1 + bonus);
        health.MarkDirty();
        health.Health = Math.Max(1, share * health.MaxHealth);
    }
}
