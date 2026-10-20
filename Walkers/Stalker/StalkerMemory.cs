using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Walkers.Director;

namespace Walkers.Stalker;

/// <summary>
/// Keys the stalker AI keeps in the walker's memory, the <c>walkers</c> tree in <c>entity.Attributes</c>
/// (server only). The stage itself is the director's top-level <see cref="WalkerMemory.Stage"/>, which
/// only the ladder writes between the start and the end of a haunt. Everything below lives in the
/// <see cref="WalkerMemory.Ai"/> subtree, which belongs to the stalker AI: other features read it and
/// never write it. Forms copies the whole tree across a shift.
///
/// What the other features read:
/// <list type="bullet">
/// <item><see cref="Watched"/>, <see cref="VictimWatching"/>, <see cref="Watchers"/> and
/// <see cref="LastSeenMs"/>: the last gaze check (twice a second by default). Prefer
/// <see cref="WalkerGaze.IsWatched"/> in code.</item>
/// <item><see cref="BreakOff"/>: why a true form broke off (empty when it has not). Forms shifts a
/// true form with a break-off reason back to the guise once nobody sees it.</item>
/// <item><see cref="SilhouettesTonight"/>: silhouettes shown since dusk (stage 4).</item>
/// <item><see cref="Mode"/>: what the AI is doing, for /walkers status.</item>
/// </list>
/// The ladder's clocks and counters (<see cref="StageHours"/> and the rest) are for the ladder and the
/// status commands only.
/// </summary>
public static class StalkerMemory
{
    // ---- Gaze, written by WalkerGaze after every check ----

    /// <summary>bool: a counting player (or the victim's stand-in) sees it.</summary>
    public const string Watched = "watched";

    /// <summary>bool: its victim sees it.</summary>
    public const string VictimWatching = "victimWatching";

    /// <summary>int: watchers found at the last check. The check stops early once it knows enough, so above 1 this is a lower bound.</summary>
    public const string Watchers = "watchers";

    /// <summary>long: World.ElapsedMilliseconds of the last check in which anyone saw it; 0 if nobody has yet.</summary>
    public const string LastSeenMs = "lastSeenMs";

    // ---- Behaviour, written by the brain ----

    /// <summary>string: why a true form broke off: health, fire, company, dawn, drivenoff, stoodup, withdraw, huntover; empty when it has not.</summary>
    public const string BreakOff = "breakOff";

    /// <summary>string: what the AI is doing now: idle, stalk, silhouette, hunt, defend, retreat, slipaway.</summary>
    public const string Mode = "mode";

    /// <summary>int: silhouettes shown since <see cref="SilhouetteNight"/> began.</summary>
    public const string SilhouettesTonight = "silhouettesTonight";

    /// <summary>double: TotalHours of the dusk that <see cref="SilhouettesTonight"/> counts from.</summary>
    public const string SilhouetteNight = "silhouetteNight";

    /// <summary>bool: a player has hurt it during this haunt. In a world with passive creature hostility, it only strikes once this is set.</summary>
    public const string Provoked = "provoked";

    /// <summary>double: TotalHours of its last lunge that landed on the victim.</summary>
    public const string LastHitHours = "lastHitHours";

    /// <summary>long: World.ElapsedMilliseconds of the last hit a player gave it. Kept here so the true form a hurt guise stands up as knows it was hit.</summary>
    public const string LastPlayerHitMs = "lastPlayerHitMs";

    /// <summary>long: entity id of the player who hit it last.</summary>
    public const string LastAttacker = "lastAttacker";

    // ---- The victim of a body the director did not place (a JSON spawn, /entity spawn) ----

    /// <summary>string: the player uid the stalker AI picked for a body without a director id.</summary>
    public const string OwnVictim = "victim";

    /// <summary>long: entity id of a stand-in victim (any entity) set by /walkers stalker victim or the self-test, for a body without a director id.</summary>
    public const string OwnVictimEntity = "victimEntity";

    // ---- Ladder clocks and counters, written by StalkerLadder ----

    /// <summary>double: TotalHours when the current haunt began.</summary>
    public const string HauntBegan = "hauntBegan";

    /// <summary>double: TotalHours when the current stage began.</summary>
    public const string StageBegan = "stageBegan";

    /// <summary>double: in-game hours the stage clock has run in this stage, after the pace multipliers.</summary>
    public const string StageHours = "stageHours";

    /// <summary>int: nights that counted in this stage.</summary>
    public const string StageNights = "stageNights";

    /// <summary>int: harassment acts that reached the victim in this stage.</summary>
    public const string StageActs = "stageActs";

    /// <summary>int: times the victim saw it in this stage (one per look of a second or more).</summary>
    public const string Sightings = "sightings";

    /// <summary>double: TotalHours of the ladder's last update.</summary>
    public const string LadderUpdated = "ladderUpdated";

    /// <summary>bool: the victim was asleep at the ladder's last update.</summary>
    public const string LadderAsleep = "ladderAsleep";

    /// <summary>double: TotalHours when the current night began; -1 by day.</summary>
    public const string NightStart = "nightStart";

    /// <summary>double: in-game hours of the current night the clock ran in this stage.</summary>
    public const string NightClockHours = "nightClockHours";

    /// <summary>bool: the victim slept during the current night.</summary>
    public const string NightSlept = "nightSlept";

    /// <summary>bool: a hit either way between it and the victim during the current night.</summary>
    public const string NightHit = "nightHit";

    /// <summary>int: hunt nights that ended with no hit either way.</summary>
    public const string HuntNights = "huntNights";

    /// <summary>bool: the hunt ran out (ladder.huntMaxNights); the haunt ends.</summary>
    public const string HuntOver = "huntOver";

    /// <summary>double: TotalHours until which it does not climb (after it was driven off).</summary>
    public const string RestUntil = "restUntil";

    /// <summary>double: in-game hours the victim has lately spent with company (falls back while alone).</summary>
    public const string CompanyHours = "companyHours";

    /// <summary>bool: the victim's first hit on the guise in this haunt has given its bonus.</summary>
    public const string HarmBonusUsed = "harmBonusUsed";

    /// <summary>The walker's memory tree of a body, or null if it has none yet.</summary>
    public static ITreeAttribute? Of(Entity body) => body.Attributes.GetTreeAttribute(WalkerMemory.TreeKey);

    /// <summary>The walker's memory tree of a body, created if missing.</summary>
    public static ITreeAttribute OrAdd(Entity body) => body.Attributes.GetOrAddTreeAttribute(WalkerMemory.TreeKey);

    /// <summary>The stalker AI's subtree of a memory tree, created if missing.</summary>
    public static ITreeAttribute Ai(ITreeAttribute memory) => memory.GetOrAddTreeAttribute(WalkerMemory.Ai);

    /// <summary>Top-level int: the walker's escalation level, 1 (its first haunt) to 5 (wrath), across returns (the owner's ladder, 09:22Z).</summary>
    public const string Escalation = "escalation";

    /// <summary>The escalation level: walkers/escalation when written, else the return count plus one; 1 to 5.</summary>
    public static int Level(ITreeAttribute memory)
    {
        int level = memory.HasAttribute(Escalation) ? memory.GetInt(Escalation) : memory.GetInt(WalkerMemory.Returns) + 1;
        return level < 1 ? 1 : level > 5 ? 5 : level;
    }
}
