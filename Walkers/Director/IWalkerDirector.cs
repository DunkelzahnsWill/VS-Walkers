using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Walkers.Director;

/// <summary>
/// What the other features may ask of the director, on the server. The director owns every walker's
/// record: its memory, its haunt, its ledger entry and the restore list (creature bible, sections 4, 7
/// and 8). Get it with <c>api.GetWalkersDirector()</c>; it is null on a client, and null when the
/// director is not loaded, so a feature can still run on its own.
///
/// All members run on the server main thread. Nothing here is saved by the caller: the director saves
/// everything under <c>walkers:director</c> in the save game.
/// </summary>
public interface IWalkerDirector
{
    // ---- Walkers and their memory ----

    /// <summary>
    /// The walker a body belongs to, read from its memory (<see cref="WalkerMemory.Id"/>). False for a
    /// body the director did not place, such as a test spawn by command; such bodies are left alone.
    /// </summary>
    bool TryGetWalkerId(Entity body, out int walkerId);

    /// <summary>A snapshot of one walker, or null for an unknown id.</summary>
    WalkerInfo? GetWalker(int walkerId);

    /// <summary>Snapshots of every walker in the roster, banished ones included.</summary>
    IReadOnlyList<WalkerInfo> Walkers { get; }

    /// <summary>
    /// The master copy of a walker's memory: the <see cref="WalkerMemory.TreeKey"/> tree. Every body of
    /// the walker holds this same instance in <c>entity.Attributes</c>, so a value set on a body is in
    /// the record at once and survives the body. Change values inside it; never replace the tree on a
    /// body. Each feature writes only its own subtree (<see cref="WalkerMemory.Ai"/>,
    /// <see cref="WalkerMemory.Harass"/>, <see cref="WalkerMemory.Form"/>); the top-level keys belong
    /// to the director and are read-only for everyone else.
    /// </summary>
    ITreeAttribute? GetMemory(int walkerId);

    // ---- Bodies ----

    /// <summary>
    /// For a shift: gives the walker's memory to a body that is about to be spawned and makes it the
    /// walker's current body. Call it after <c>CreateEntity</c> and before <c>World.SpawnEntity</c>,
    /// then retire the old body with <c>Die(EnumDespawnReason.Expire)</c>. The director ignores the
    /// old body's despawn, because it is no longer the current body. Also works while the walker is
    /// down, to swap the guise's carcass for the true form's body.
    /// </summary>
    void AttachBody(int walkerId, Entity newBody);

    /// <summary>
    /// Extra true-form health from returns, as a share of its base health: return.healthBonusPerReturn
    /// per return, up to return.healthBonusCap. 0 for a walker that never returned.
    /// </summary>
    float ReturnHealthBonus(int walkerId);

    // ---- Victims and counting players ----

    /// <summary>The walker's victim: a player's entity, or a debug stand-in. Null while the victim is offline.</summary>
    Entity? GetVictim(int walkerId);

    /// <summary>
    /// The creature bible's counting player (section 5): alive, in survival or guest mode, fully
    /// connected, not asleep in a bed and not idle for director.afkMinutes. Debug stand-ins count.
    /// Only counting players freeze a walker, count as company, or make it slip away.
    /// </summary>
    bool IsCounting(Entity entity);

    /// <summary>Every counting player's entity (and stand-in) in a dimension within a range of a point.</summary>
    IEnumerable<Entity> CountingEntitiesNear(Vec3d pos, int dimension, double range);

    // ---- Reports from the AI, the harassment kit and forms ----

    /// <summary>
    /// A harassment act reached the victim. The director passes it to the ladder
    /// (<see cref="IWalkerLadder.OnAct"/>), where it counts toward ladder.stageMinActs.
    /// </summary>
    void ReportAct(int walkerId, string actCode);

    /// <summary>
    /// A player hurt the guise. A hit by its victim goes to the ladder (<see cref="IWalkerLadder.OnHarm"/>,
    /// ladder.harmGuiseBonus); anyone else is marked as its next victim (creature bible, section 4).
    /// </summary>
    void ReportHarm(int walkerId, Entity byEntity);

    /// <summary>Ends the walker's haunt now; it withdraws and rests, and the victim rests director.minDaysBetweenHaunts.</summary>
    void EndHaunt(int walkerId, HauntEndReason reason);

    /// <summary>
    /// Installs the ladder: the rules for when a haunt climbs (the Stalker AI's). Without one, a haunt
    /// stays at the stage it began with. A second call replaces the first.
    /// </summary>
    void RegisterLadder(IWalkerLadder ladder);

    // ---- Death and the ledger ----
    //
    // The director catches every death of a walker's current body itself (OnEntityDeath): the walker
    // goes down, and its return is written into the ledger at once, so a crash or a server stop still
    // brings it back. Forms then reports one outcome: Banish, or Release. A walker that is down when
    // its body goes and nobody reported an outcome is released as LeftAlone.

    /// <summary>
    /// The kill ritual is met (silver, fire, or the body burned in time): the walker's id is retired,
    /// its ledger entry no longer returns, and its haunt ends. <paramref name="cause"/> is a short
    /// English note for the ledger and /walkers director ledger, such as "silver arrow".
    /// </summary>
    void Banish(int walkerId, IPlayer? byPlayer, string cause);

    /// <summary>
    /// The walker is gone without the ritual: it waits in the ledger and returns after
    /// return.afterSeasons. Calling it again for the same death only updates the reason and furs.
    /// </summary>
    void Release(int walkerId, WalkerRelease reason, bool fursTaken);

    /// <summary>
    /// Clears the mark a player carries from walkers' unnatural furs (creature bible, section 7): Forms
    /// calls it when the player burns furs or drops them into fire (furs.burnClearsMark).
    /// </summary>
    void ClearFurMarks(string playerUid);

    /// <summary>The walker's latest death, with its return day; null if it never died.</summary>
    LedgerEntry? GetLedgerEntry(int walkerId);

    /// <summary>Every death in the ledger, oldest first.</summary>
    IReadOnlyList<LedgerEntry> Ledger { get; }

    // ---- Restore list (creature bible, section 8) ----

    /// <summary>
    /// Records a block a walker changed, so the director undoes it when it is due, when the walker
    /// withdraws, and after a restart, even if the walker's body is gone.
    /// </summary>
    void AddRestore(RestoreEntry entry);

    /// <summary>
    /// Registers how to undo entries of one kind. The director has a handler for
    /// <see cref="RestoreEntry.KindBlock"/> (swap the block back by code); a feature that changes
    /// blocks another way (a door's open state) registers its own kind.
    /// </summary>
    void RegisterRestoreHandler(string kind, System.Func<IWorldAccessor, RestoreEntry, RestoreResult> undo);

    // ---- Events (server main thread) ----

    /// <summary>The director placed a body for a walker (a first appearance, a return, after slipping away or following the victim). Not raised for <see cref="AttachBody"/>.</summary>
    event Action<WalkerInfo, Entity>? BodyPlaced;

    /// <summary>A haunt began. The walker's victim and stage are set.</summary>
    event Action<WalkerInfo>? HauntStarted;

    /// <summary>A haunt ended, for the reason given.</summary>
    event Action<WalkerInfo, HauntEndReason>? HauntEnded;

    /// <summary>The walker's stage changed; the int is the stage before.</summary>
    event Action<WalkerInfo, int>? StageChanged;

    /// <summary>The walker went into the ledger without the ritual (the body was skinned, left alone, or lost).</summary>
    event Action<WalkerInfo, LedgerEntry>? WalkerReleased;

    /// <summary>The walker was banished for good.</summary>
    event Action<WalkerInfo, LedgerEntry>? WalkerBanished;

    /// <summary>A walker from the ledger came back for a new haunt.</summary>
    event Action<WalkerInfo>? WalkerReturned;
}

/// <summary>
/// Keys of the walker's memory tree, <c>entity.Attributes["walkers"]</c>. The top-level keys are the
/// director's; each feature owns one subtree.
/// </summary>
public static class WalkerMemory
{
    /// <summary>The tree's key in <c>entity.Attributes</c>.</summary>
    public const string TreeKey = "walkers";

    /// <summary>int: the walker's persistent id, kept through shifts, deaths and returns.</summary>
    public const string Id = "id";

    /// <summary>string: the victim's player uid, or a stand-in's id; absent while it haunts nobody.</summary>
    public const string Victim = "victim";

    /// <summary>long: the entity id of the victim's current entity (the player's, or a stand-in's); absent while the victim is offline. Updated every director tick.</summary>
    public const string VictimEntity = "victimEntity";

    /// <summary>
    /// int: the ladder stage, 1 to 5; 0 while it haunts nobody. The director sets it when a haunt
    /// begins or ends; in between only the ladder (<see cref="IWalkerLadder"/>) changes it.
    /// </summary>
    public const string Stage = "stage";

    /// <summary>float: how much faster this haunt climbs than ladder.paceMultiplier alone; return.paceMultiplier for a returning walker, else 1.</summary>
    public const string Pace = "pace";

    /// <summary>int: how many times it has come back from the ledger.</summary>
    public const string Returns = "returns";

    /// <summary>string: the deer type the guise wears for this haunt, such as "whitetail".</summary>
    public const string Guise = "guise";

    /// <summary>
    /// int, 1 to 5: the walker's escalation level on the owner's ladder (1 Interloping, 2 Grudge, 3 Rage,
    /// 4 Hate, 5 Wrath): 1, plus its improper deaths, plus one for every escalation.monthsPerLevel
    /// in-game months of live haunting since the first; 0 once banished. Set when a haunt starts or
    /// returns and when a body is placed; the harassment kit reads it. At 5 its return comes in half the time.
    /// </summary>
    public const string Escalation = "escalation";

    /// <summary>bool: the director wants the body gone (the haunt paused or ended): walk into cover. The director removes it once nobody sees it.</summary>
    public const string Withdraw = "withdraw";

    /// <summary>Subtree of the Stalker AI.</summary>
    public const string Ai = "ai";

    /// <summary>Subtree of the harassment kit: tonight's counters, titles, the offering, pages sent.</summary>
    public const string Harass = "harass";

    /// <summary>Subtree of forms and the kill ritual: the reveal, the health share, the last igniter.</summary>
    public const string Form = "form";
}

/// <summary>
/// The rules for when a haunt climbs the ladder (creature bible, section 5). The Stalker AI implements
/// it and registers it with <see cref="IWalkerDirector.RegisterLadder"/>. It keeps its clocks and
/// counters in the <see cref="WalkerMemory.Ai"/> subtree and writes <see cref="WalkerMemory.Stage"/>;
/// the director notices the change and raises <see cref="IWalkerDirector.StageChanged"/>.
/// </summary>
public interface IWalkerLadder
{
    /// <summary>A haunt begins at <paramref name="stage"/> (1, or return.startStage for a return). Reset the clocks.</summary>
    void Begin(ITreeAttribute memory, int stage, double nowTotalHours);

    /// <summary>
    /// Called every director tick for each haunting walker, with or without a body. Uses the time since
    /// its own last update, so a second call in a tick does no harm.
    /// </summary>
    LadderOutcome Update(ITreeAttribute memory, LadderInput input);

    /// <summary>A harassment act reached the victim.</summary>
    void OnAct(ITreeAttribute memory, string actCode);

    /// <summary>The victim hurt the guise.</summary>
    void OnHarm(ITreeAttribute memory);
}

/// <summary>
/// What the director knows about the victim this tick. <see cref="Exposed"/> is the creature bible's
/// gate for the clock: online, eligible, on the surface and near forest (section 8).
/// </summary>
public readonly record struct LadderInput(
    double NowTotalHours,
    Entity? Victim,
    bool Exposed,
    bool Alone,
    bool Night,
    bool Asleep);

public enum LadderOutcome
{
    /// <summary>Nothing for the director to do; a stage change is read from the memory.</summary>
    None,
    /// <summary>The hunt ran out (ladder.huntMaxNights): the director ends the haunt.</summary>
    HuntEnded,
}

public enum WalkerState
{
    /// <summary>In the roster, haunting nobody, ready for the next roll.</summary>
    Resting,
    /// <summary>Haunting its victim; it may or may not have a body right now.</summary>
    Haunting,
    /// <summary>Its haunt is paused (victim offline, underground, away from forest, a storm); it holds no active slot.</summary>
    Paused,
    /// <summary>It died; its body may lie in the burn window. Its return is already in the ledger.</summary>
    Down,
    /// <summary>In the ledger, waiting for its return day.</summary>
    Waiting,
    /// <summary>Banished for good; its id is retired.</summary>
    Banished,
}

public enum HauntEndReason
{
    /// <summary>director.hauntMaxDays passed since it began.</summary>
    Timeout,
    /// <summary>The hunt ran ladder.huntMaxNights quiet nights (the Stalker AI decides).</summary>
    HuntTimeout,
    /// <summary>Its victim died. The victim rests twice as long.</summary>
    VictimDied,
    /// <summary>The walker died; see the ledger.</summary>
    WalkerDied,
    /// <summary>The haunt stayed paused for director.pausedHauntMaxDays.</summary>
    PausedTooLong,
    /// <summary>An admin command.</summary>
    Command,
}

public enum WalkerRelease
{
    /// <summary>Its victim or killer skinned the body for its furs.</summary>
    Skinned,
    /// <summary>Nobody burned the body within the window.</summary>
    LeftAlone,
    /// <summary>The body was unloaded with its chunk, or the server stopped, while it lay in the window.</summary>
    Unloaded,
    /// <summary>It died in the void and left no body.</summary>
    Void,
}

public enum LedgerOutcome
{
    /// <summary>Down: the body may still lie in the burn window. Its return is already scheduled.</summary>
    Pending,
    /// <summary>Released without the ritual; it returns on <see cref="LedgerEntry.ReturnTotalDays"/>.</summary>
    Released,
    /// <summary>Banished; it never returns.</summary>
    Banished,
    /// <summary>It came back; this death is history.</summary>
    Returned,
}

/// <summary>A snapshot of one walker in the roster. Changing it changes nothing.</summary>
public sealed record WalkerInfo(
    int Id,
    WalkerState State,
    string? VictimUid,
    string? VictimName,
    int Stage,
    int Returns,
    string? GuiseType,
    long BodyEntityId,
    double HauntStartedTotalDays);

/// <summary>One death in the ledger. A snapshot: changing it changes nothing.</summary>
public sealed record LedgerEntry(
    int WalkerId,
    string? VictimUid,
    string? VictimName,
    double DiedTotalDays,
    BlockPos Pos,
    string Cause,
    string? KillerName,
    LedgerOutcome Outcome,
    WalkerRelease? Release,
    bool FursTaken,
    int ReturnsBefore,
    double ReturnTotalDays);

/// <summary>
/// One block a walker changed. <see cref="Found"/> and <see cref="Set"/> are what the undo handler of
/// <see cref="Kind"/> needs to tell whether the change still stands; for <see cref="KindBlock"/> they
/// are block codes. A due time of 0 means "only when the walker withdraws, or at dawn if
/// <see cref="UndoAtDawn"/>".
/// </summary>
public sealed record RestoreEntry(
    string Kind,
    BlockPos Pos,
    string Found,
    string Set,
    int WalkerId,
    double DueTotalHours,
    bool UndoOnWithdraw,
    bool UndoAtDawn)
{
    /// <summary>Swap the block at Pos from code Set back to code Found, if it still is Set.</summary>
    public const string KindBlock = "block";
}

public enum RestoreResult
{
    /// <summary>Undone; the entry is dropped.</summary>
    Done,
    /// <summary>Cannot be done now (chunk not loaded, a player stands in the doorway); try again later.</summary>
    Later,
    /// <summary>A player changed the block since; the entry is dropped without touching it.</summary>
    Dropped,
}

public static class WalkersDirectorExtensions
{
    /// <summary>Key of the director in <c>api.ObjectCache</c>.</summary>
    public const string CacheKey = "walkers:director";

    /// <summary>The director on the server; null on a client or when it is not loaded.</summary>
    public static IWalkerDirector? GetWalkersDirector(this ICoreAPI api) =>
        api.ObjectCache.TryGetValue(CacheKey, out var director) ? director as IWalkerDirector : null;
}
