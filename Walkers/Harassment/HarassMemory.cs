using Vintagestory.API.Datastructures;
using Walkers.Director;

namespace Walkers.Harassment;

/// <summary>
/// Keys the harassment kit keeps in the walker's memory, in the <see cref="WalkerMemory.Harass"/>
/// subtree of the <c>walkers</c> tree (creature bible, section 4). The subtree belongs to the kit;
/// other features read it and never write it. With the director the tree is the walker's record, so
/// the counters survive slipping away, shifts and restarts; a body the director did not place keeps
/// them only while it lives.
/// </summary>
public static class HarassMemory
{
    // ---- Tonight's budget (reset at dusk) ----

    /// <summary>bool: it was night at the victim at the last scheduler update.</summary>
    public const string IsNight = "isNight";

    /// <summary>double: TotalHours of the dusk the counters count from.</summary>
    public const string Dusk = "dusk";

    /// <summary>int: acts spent from tonight's budget.</summary>
    public const string Spent = "spent";

    /// <summary>bool: tonight's quiet stretch of 5 real minutes has happened.</summary>
    public const string QuietDone = "quietDone";

    /// <summary>string: the code of the last act, so the scheduler never draws it twice in a row.</summary>
    public const string LastAct = "lastAct";

    /// <summary>Prefix of the per-act counters of tonight: <c>n-&lt;act code&gt;</c> (int).</summary>
    public const string CountPrefix = "n-";

    // ---- Once per haunt (reset when a new haunt begins) ----

    /// <summary>double: the stalker AI's hauntBegan of the haunt these flags belong to.</summary>
    public const string Haunt = "haunt";

    /// <summary>bool: "Something's moving out there." was shown.</summary>
    public const string TitleWatching = "titleWatching";

    /// <summary>bool: "The Forest Saw That." was shown.</summary>
    public const string TitleForestSaw = "titleForestSaw";

    /// <summary>int: offerings left in this haunt.</summary>
    public const string Offerings = "offerings";

    /// <summary>int: break-ins in this haunt.</summary>
    public const string BreakIns = "breakIns";

    /// <summary>double: TotalHours of the dusk of the last stage-5 three-rap knock.</summary>
    public const string DuskKnockFor = "duskKnock";

    // ---- Pages (per cycle; once-per-player triggers live in the player's own data) ----

    /// <summary>int: the cycle (1 + returns) whose K1 page was left.</summary>
    public const string PageK1Cycle = "pageK1";

    /// <summary>int: the cycle whose K3 page was left.</summary>
    public const string PageK3Cycle = "pageK3";

    // ---- The drain (creature bible, section 6: its base rule) ----

    /// <summary>double: the value it last wrote to the victim's temporalStability; -1 when it has not, or lost track.</summary>
    public const string DrainLastWrite = "drainLast";

    /// <summary>double: the total it took from the victim since it last arrived; handed back on a pause. The stalker AI reads it to leave the walker's own drain out of the low-stability pace.</summary>
    public const string DrainTaken = "drainTaken";

    /// <summary>The kit's subtree of a memory tree, created if missing.</summary>
    public static ITreeAttribute Of(ITreeAttribute memory) => memory.GetOrAddTreeAttribute(WalkerMemory.Harass);

    /// <summary>Tonight's count of an act.</summary>
    public static int Count(ITreeAttribute harass, string act) => harass.GetInt(CountPrefix + act);

    public static void AddCount(ITreeAttribute harass, string act) => harass.SetInt(CountPrefix + act, Count(harass, act) + 1);

    /// <summary>Clears tonight's counters at dusk.</summary>
    public static void NewNight(ITreeAttribute harass, double duskHours)
    {
        foreach (var key in System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(harass, kv => kv.Key)))
        {
            if (key.StartsWith(CountPrefix, System.StringComparison.Ordinal))
            {
                harass.RemoveAttribute(key);
            }
        }
        harass.SetInt(Spent, 0);
        harass.SetBool(QuietDone, false);
        harass.SetDouble(Dusk, duskHours);
    }
}
