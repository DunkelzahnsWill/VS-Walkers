using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Walkers.Director;
using Walkers.Harassment;

namespace Walkers.Stalker;

/// <summary>
/// The enraged walk-in (creature bible, section 7, "Enraged", flag 5). The harassment kit's break-in
/// opens or tears a door and writes the way in to walkers/harass (entryX/Y/Z, entryUntilMs,
/// entryTorn); while entryUntilMs lies ahead, the hunt may follow the victim through that door, at
/// most enraged.maxIndoorBlocks from it. It leaves on its first landed hit, at the timeout, when the
/// victim lies down, when company comes, when it burns or breaks off (low health, dawn), when it goes
/// too far in, or when it makes no progress for <see cref="StuckMs"/>; then it sets entryUntilMs to 0
/// and walks back out through the door before anything else. A body still inside
/// <see cref="VanishMs"/> after the entry began vanishes as a withdrawal. Every hunt rule still
/// applies inside: the freeze, the lunge range, the hit cap, no strike on a sleeper, the wake grace.
/// </summary>
public sealed class WalkerEntry
{
    /// <summary>No progress toward the victim for this long, inside, and it gives up.</summary>
    public const long StuckMs = 5000;

    /// <summary>Still inside this long after the entry began, it vanishes.</summary>
    public const long VanishMs = 50000;

    /// <summary>Progress means the victim distance shrank by this much.</summary>
    private const double ProgressBlocks = 0.5;

    private long lastProgressMs;
    private double bestVictimDistance;
    private bool wasInside;

    /// <summary>The lower cell of the entry door; null when there is no entry.</summary>
    public BlockPos? Door { get; private set; }

    /// <summary>Where it stood outside when the entry began, the spot it walks back to.</summary>
    public Vec3d? Outside { get; private set; }

    /// <summary>The door was torn off, so the doorway is open.</summary>
    public bool Torn { get; private set; }

    /// <summary>World.ElapsedMilliseconds at which the entry began.</summary>
    public long StartedMs { get; private set; }

    /// <summary>It has left (or is leaving) and walks back out; null while it may go in.</summary>
    public string? LeftBecause { get; private set; }

    /// <summary>It may go in now.</summary>
    public bool Open => Door != null && LeftBecause == null;

    /// <summary>The block centre of the door's lower cell.</summary>
    public Vec3d? DoorCenter => Door == null ? null : new Vec3d(Door.X + 0.5, Door.Y, Door.Z + 0.5);

    /// <summary>True for the entry door (either of its cells).</summary>
    public bool IsDoor(BlockPos pos) => Door != null && pos.X == Door.X && pos.Z == Door.Z && Math.Abs(pos.Y - Door.Y) <= 1;

    /// <summary>The body stands on the far side of the door from where it came in, near the door.</summary>
    public bool IsInside(Entity body, int maxBlocks)
    {
        var center = DoorCenter;
        var outside = Outside;
        return center != null && outside != null && IsPast(center, outside, body.Pos.X, body.Pos.Z, maxBlocks);
    }

    /// <summary>
    /// A point (x, z) lies past the door plane, on the side away from <paramref name="outside"/>,
    /// within <paramref name="maxBlocks"/> + 2 of the door's centre.
    /// </summary>
    public static bool IsPast(Vec3d doorCenter, Vec3d outside, double x, double z, int maxBlocks)
    {
        double bx = x - doorCenter.X, bz = z - doorCenter.Z;
        double ox = outside.X - doorCenter.X, oz = outside.Z - doorCenter.Z;
        double olen = Math.Sqrt(ox * ox + oz * oz);
        if (olen < 0.01)
        {
            return false;
        }
        double across = (bx * ox + bz * oz) / olen;
        return across < -0.3 && bx * bx + bz * bz <= (maxBlocks + 2) * (maxBlocks + 2);
    }

    /// <summary>
    /// Called on each gaze check of a hunting true form: begins an entry the kit opened, ends it on
    /// the bible's exits, and clears it once the body is back outside. Returns true when the body vanished.
    /// </summary>
    public bool Update(WalkerBrain brain, long nowMs)
    {
        var body = brain.Body;
        var harass = brain.Memory.GetTreeAttribute(WalkerMemory.Harass);
        long until = harass?.GetLong(HarassMemory.EntryUntilMs) ?? 0;
        int maxBlocks = brain.Config.Enraged.MaxIndoorBlocks;

        if (Door == null)
        {
            if (harass != null && until > nowMs && brain.IsTrueForm && brain.Mode == StalkerMode.Hunt && brain.Victim != null)
            {
                Begin(brain, harass, until, nowMs);
            }
            return false;
        }

        bool inside = IsInside(body, maxBlocks);
        if (inside && !wasInside)
        {
            // Through the door: the stuck clock starts here.
            lastProgressMs = nowMs;
            bestVictimDistance = brain.VictimDistance;
        }
        wasInside = inside;
        if (inside && nowMs - StartedMs >= VanishMs)
        {
            Vanish(brain);
            return true;
        }

        if (LeftBecause == null)
        {
            string? reason = ExitReason(brain, until, nowMs, inside, maxBlocks);
            if (reason != null)
            {
                Leave(brain, reason);
            }
            return false;
        }
        if (!inside)
        {
            // Back out: the entry is over.
            Clear();
        }
        return false;
    }

    /// <summary>The lunge landed on the victim: it leaves.</summary>
    public void OnStruckVictim(WalkerBrain brain)
    {
        if (Open)
        {
            Leave(brain, "hit landed");
        }
    }

    private void Begin(WalkerBrain brain, ITreeAttribute harass, long until, long nowMs)
    {
        var body = brain.Body;
        Door = new BlockPos(harass.GetInt(HarassMemory.EntryX), harass.GetInt(HarassMemory.EntryY), harass.GetInt(HarassMemory.EntryZ), body.Pos.Dimension);
        Torn = harass.GetBool(HarassMemory.EntryTorn);
        StartedMs = nowMs;
        LeftBecause = null;
        lastProgressMs = nowMs;
        bestVictimDistance = brain.VictimDistance;
        var center = DoorCenter!;
        double dx = body.Pos.X - center.X, dz = body.Pos.Z - center.Z;
        double len = Math.Sqrt(dx * dx + dz * dz);
        if (len < 1 && brain.Victim != null)
        {
            // Standing in the doorway already: outside is away from the victim.
            dx = center.X - brain.Victim.Pos.X;
            dz = center.Z - brain.Victim.Pos.Z;
            len = Math.Sqrt(dx * dx + dz * dz);
        }
        len = Math.Max(len, 0.01);
        double back = Math.Clamp(len, 1.5, 3);
        Outside = new Vec3d(center.X + dx / len * back, body.Pos.Y, center.Z + dz / len * back);
        brain.Owner.Logger.Debug("Walker {0} ({1} #{2}) comes in through the door at {3}", brain.Memory.GetInt(WalkerMemory.Id), body.Code.Path, body.EntityId, Door);
    }

    private string? ExitReason(WalkerBrain brain, long until, long nowMs, bool inside, int maxBlocks)
    {
        var body = brain.Body;
        if (until <= 0 || nowMs >= until)
        {
            return "time";
        }
        if (brain.Mode != StalkerMode.Hunt)
        {
            return "broke off";
        }
        if (brain.Victim == null || StalkerModSystem.IsAsleep(brain.Victim))
        {
            return "victim lay down";
        }
        if (brain.Company)
        {
            return "company";
        }
        if (body.IsOnFire)
        {
            return "fire";
        }
        if (!inside)
        {
            // Still on its way to the door: the time limit covers it.
            return null;
        }
        var center = DoorCenter!;
        if (center.HorizontalSquareDistanceTo(body.Pos.X, body.Pos.Z) > maxBlocks * maxBlocks)
        {
            return "too far in";
        }
        if (brain.VictimDistance < bestVictimDistance - ProgressBlocks || brain.VictimDistance <= brain.Config.Gaze.LungeRange || brain.Frozen)
        {
            // Closing in, at the lunge, or held by the gaze: not stuck.
            bestVictimDistance = Math.Min(bestVictimDistance, brain.VictimDistance);
            lastProgressMs = nowMs;
        }
        else if (nowMs - lastProgressMs >= StuckMs)
        {
            return "stuck";
        }
        return null;
    }

    private void Leave(WalkerBrain brain, string reason)
    {
        LeftBecause = reason;
        brain.Memory.GetTreeAttribute(WalkerMemory.Harass)?.SetLong(HarassMemory.EntryUntilMs, 0);
        brain.Owner.Logger.Debug("Walker {0} ({1} #{2}) leaves the house: {3}", brain.Memory.GetInt(WalkerMemory.Id), brain.Body.Code.Path, brain.Body.EntityId, reason);
        if (!IsInside(brain.Body, brain.Config.Enraged.MaxIndoorBlocks))
        {
            Clear();
        }
    }

    private void Vanish(WalkerBrain brain)
    {
        var body = brain.Body;
        brain.Memory.GetTreeAttribute(WalkerMemory.Harass)?.SetLong(HarassMemory.EntryUntilMs, 0);
        brain.Memory.SetBool(WalkerMemory.Withdraw, true);
        brain.Owner.Logger.Debug("Walker {0} ({1} #{2}) was still inside after {3} s: gone, as a withdrawal", brain.Memory.GetInt(WalkerMemory.Id), body.Code.Path, body.EntityId, VanishMs / 1000);
        Clear();
        body.Die(EnumDespawnReason.Expire);
    }

    private void Clear()
    {
        wasInside = false;
        Door = null;
        Outside = null;
        Torn = false;
        LeftBecause = null;
    }
}
