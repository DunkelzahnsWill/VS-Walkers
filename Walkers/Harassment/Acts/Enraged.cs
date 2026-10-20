using Walkers.Director;
using Walkers.Stalker;

namespace Walkers.Harassment.Acts;

/// <summary>
/// When a walker is enraged (creature bible, section 7, "Enraged"): for the whole haunt after any
/// return (enraged.onReturn), or after a player hurt or burned it (enraged.onProvoked, off by default),
/// and never while the world's creatureHostility is off. The kit writes the answer to
/// walkers/harass/enraged each second, so the stalker AI reads the same flag.
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
        return (config.OnReturn && Level(ctx) >= 1) ||
               (config.OnProvoked && ctx.Brain.Ai.GetBool(StalkerMemory.Provoked));
    }

    /// <summary>
    /// The escalation level the tiers are keyed on (creature bible, rev 111): the director's
    /// walkers/escalation when it writes one, else the return count.
    /// </summary>
    public static int Level(HarassContext ctx) =>
        ctx.Memory.HasAttribute(LevelKey) ? ctx.Memory.GetInt(LevelKey) : ctx.Memory.GetInt(WalkerMemory.Returns);

    /// <summary>Top-level key of the director's escalation level.</summary>
    public const string LevelKey = "escalation";

    public static bool HostilityOff(HarassContext ctx) => ctx.Api.World.Config.GetString("creatureHostility", "aggressive") == "off";
}
