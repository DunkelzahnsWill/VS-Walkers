using System;
using Walkers.Config;
using Walkers.Director;
using Walkers.Stalker;

namespace Walkers.Harassment.Acts;

/// <summary>
/// The escalation level and when a walker is enraged (creature bible, section 7, "The escalation
/// list" and "Enraged"; the owner's ladder of 2026-10-20: 1 Interloping, 2 Grudge, 3 Rage, 4 Hate,
/// 5 Wrath). Enraged means from enraged.fromLevel for the whole haunt, or after a player hurt or
/// burned it (enraged.onProvoked, off by default), and never while the world's creatureHostility is
/// off. The kit writes the answer to walkers/harass/enraged each second, so the stalker AI reads the
/// same flag.
/// </summary>
public static class Enraged
{
    public static bool Is(HarassContext ctx)
    {
        var config = ctx.System.Config.Enraged;
        if (!config.Enabled || HostilityOff(ctx))
        {
            return false;
        }
        return Level(ctx) >= config.FromLevel ||
               (config.OnProvoked && ctx.Brain.Ai.GetBool(StalkerMemory.Provoked));
    }

    /// <summary>
    /// The escalation level, 1 to 5: the director's walkers/escalation when it writes one, else 1 plus
    /// the return count; always 1 while escalation.enabled is off.
    /// </summary>
    public static int Level(HarassContext ctx)
    {
        if (!ctx.System.Config.Escalation.Enabled)
        {
            return 1;
        }
        int level = ctx.Memory.HasAttribute(LevelKey) ? ctx.Memory.GetInt(LevelKey) : 1 + ctx.Memory.GetInt(WalkerMemory.Returns);
        return Math.Clamp(level, 1, 5);
    }

    /// <summary>What the kit may do at the walker's level.</summary>
    public static EscalationLevelConfig Rules(HarassContext ctx) => ctx.System.Config.Escalation.Levels.For(Level(ctx));

    /// <summary>The level whose break-in odds and door budgets apply: a walker enraged only by provocation takes enraged.fromLevel's.</summary>
    public static EscalationLevelConfig BreakInRules(HarassContext ctx) =>
        ctx.System.Config.Escalation.Levels.For(Math.Max(Level(ctx), ctx.System.Config.Enraged.FromLevel));

    /// <summary>Top-level key of the director's escalation level.</summary>
    public const string LevelKey = "escalation";

    public static bool HostilityOff(HarassContext ctx) => ctx.Api.World.Config.GetString("creatureHostility", "aggressive") == "off";
}
