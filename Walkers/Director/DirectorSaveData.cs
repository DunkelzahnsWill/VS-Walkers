using System.Collections.Generic;
using ProtoBuf;

namespace Walkers.Director;

// The director's whole state, saved in the save game under walkers:director (creature bible, section
// 8). Fields are only ever added: a removed field keeps its number unused, so older saves still load.
// Public because protobuf-net builds the classes.

[ProtoContract]
public class DirectorSaveData
{
    /// <summary>Raise when a change needs old saves converted; <see cref="DirectorSystem"/> checks it on load.</summary>
    public const int CurrentVersion = 1;

    [ProtoMember(1)]
    public int Version { get; set; } = CurrentVersion;

    [ProtoMember(2)]
    public int NextWalkerId { get; set; } = 1;

    [ProtoMember(3)]
    public List<PlayerRecord> Players { get; set; } = new();

    [ProtoMember(4)]
    public List<WalkerRecord> Walkers { get; set; } = new();

    [ProtoMember(5)]
    public List<LedgerRecord> Ledger { get; set; } = new();

    [ProtoMember(6)]
    public List<RestoreRecord> Restore { get; set; } = new();

    [ProtoMember(7)]
    public List<StandInRecord> StandIns { get; set; } = new();

    [ProtoMember(8)]
    public int NextStandInNumber { get; set; } = 1;
}

/// <summary>What the director remembers about one player (or stand-in) across sessions.</summary>
[ProtoContract]
public class PlayerRecord
{
    [ProtoMember(1)]
    public string Uid { get; set; } = "";

    [ProtoMember(2)]
    public string Name { get; set; } = "";

    /// <summary>Calendar.TotalDays when the director first saw them; the grace time counts from here.</summary>
    [ProtoMember(3)]
    public double FirstSeenTotalDays { get; set; }

    /// <summary>No new haunt for them before this Calendar.TotalDays.</summary>
    [ProtoMember(4)]
    public double RestUntilTotalDays { get; set; }

    /// <summary>The in-game day of their last haunt roll, so they roll once a day.</summary>
    [ProtoMember(5)]
    public int LastRollDay { get; set; } = -1;

    /// <summary>A walker they hurt while it haunted someone else: they are its next victim. 0 for none.</summary>
    [ProtoMember(6)]
    public int MarkedByWalker { get; set; }

    /// <summary>Calendar.TotalDays when the director last saw them online.</summary>
    [ProtoMember(7)]
    public double LastSeenTotalDays { get; set; }
}

[ProtoContract]
public class WalkerRecord
{
    [ProtoMember(1)]
    public int Id { get; set; }

    [ProtoMember(2)]
    public WalkerState State { get; set; } = WalkerState.Resting;

    /// <summary>Its victim, or its last victim while it is down or waiting in the ledger.</summary>
    [ProtoMember(3)]
    public string? VictimUid { get; set; }

    [ProtoMember(4)]
    public double HauntStartedTotalDays { get; set; }

    /// <summary>In-game hours the haunt has run unpaused; director.hauntMaxDays counts these.</summary>
    [ProtoMember(5)]
    public double HauntClockHours { get; set; }

    /// <summary>Calendar.TotalDays when the haunt paused; director.pausedHauntMaxDays counts from here.</summary>
    [ProtoMember(6)]
    public double PausedSinceTotalDays { get; set; }

    [ProtoMember(7)]
    public int Returns { get; set; }

    /// <summary>It came back from the ledger and has not haunted since: its next haunt starts as a return.</summary>
    [ProtoMember(8)]
    public bool ReturnPending { get; set; }

    [ProtoMember(9)]
    public string? GuiseType { get; set; }

    /// <summary>The memory tree (<see cref="WalkerMemory"/>), as TreeAttribute bytes.</summary>
    [ProtoMember(10)]
    public byte[]? Memory { get; set; }

    [ProtoMember(11)]
    public double LastX { get; set; }

    [ProtoMember(12)]
    public double LastY { get; set; }

    [ProtoMember(13)]
    public double LastZ { get; set; }

    [ProtoMember(14)]
    public int LastDimension { get; set; }

    [ProtoMember(15)]
    public bool HasLastPos { get; set; }

    /// <summary>Calendar.TotalHours of the last director tick that ran its haunt clock.</summary>
    [ProtoMember(16)]
    public double ClockUpdatedTotalHours { get; set; }

    /// <summary>Its victim when the last haunt ended, so a body that dies while withdrawing still counts against them.</summary>
    [ProtoMember(17)]
    public string? LastVictimUid { get; set; }

    /// <summary>It has appeared at least once in its current haunt; the first appearance uses director.spawnDistance*.</summary>
    [ProtoMember(18)]
    public bool AppearedThisHaunt { get; set; }
}

/// <summary>One death. The ledger only grows; an entry's outcome changes as the walker moves on.</summary>
[ProtoContract]
public class LedgerRecord
{
    [ProtoMember(1)]
    public int WalkerId { get; set; }

    [ProtoMember(2)]
    public string? VictimUid { get; set; }

    [ProtoMember(3)]
    public string? VictimName { get; set; }

    [ProtoMember(4)]
    public double DiedTotalDays { get; set; }

    [ProtoMember(5)]
    public int X { get; set; }

    [ProtoMember(6)]
    public int Y { get; set; }

    [ProtoMember(7)]
    public int Z { get; set; }

    [ProtoMember(8)]
    public int Dimension { get; set; }

    [ProtoMember(9)]
    public string Cause { get; set; } = "";

    [ProtoMember(10)]
    public string? KillerName { get; set; }

    [ProtoMember(11)]
    public LedgerOutcome Outcome { get; set; } = LedgerOutcome.Pending;

    /// <summary>The release reason plus one; 0 while there is none. protobuf-net has no nullable enums.</summary>
    [ProtoMember(12)]
    public int ReleasePlusOne { get; set; }

    [ProtoMember(13)]
    public bool FursTaken { get; set; }

    [ProtoMember(14)]
    public int ReturnsBefore { get; set; }

    [ProtoMember(15)]
    public double ReturnTotalDays { get; set; }

    public WalkerRelease? Release
    {
        get => ReleasePlusOne == 0 ? null : (WalkerRelease)(ReleasePlusOne - 1);
        set => ReleasePlusOne = value == null ? 0 : (int)value.Value + 1;
    }
}

[ProtoContract]
public class RestoreRecord
{
    [ProtoMember(1)]
    public string Kind { get; set; } = "";

    [ProtoMember(2)]
    public int X { get; set; }

    [ProtoMember(3)]
    public int Y { get; set; }

    [ProtoMember(4)]
    public int Z { get; set; }

    [ProtoMember(5)]
    public int Dimension { get; set; }

    [ProtoMember(6)]
    public string Found { get; set; } = "";

    [ProtoMember(7)]
    public string Set { get; set; } = "";

    [ProtoMember(8)]
    public int WalkerId { get; set; }

    [ProtoMember(9)]
    public double DueTotalHours { get; set; }

    [ProtoMember(10)]
    public bool UndoOnWithdraw { get; set; }

    [ProtoMember(11)]
    public bool UndoAtDawn { get; set; }
}

/// <summary>A debug stand-in victim (debug.standInVictims): a vanilla player bot the director treats as a player.</summary>
[ProtoContract]
public class StandInRecord
{
    /// <summary>Its uid in player records and walker memory, such as "standin-1".</summary>
    [ProtoMember(1)]
    public string Uid { get; set; } = "";

    [ProtoMember(2)]
    public long EntityId { get; set; }

    /// <summary>Where it stands, so its chunks can be kept loaded after a restart, before the bot itself loads.</summary>
    [ProtoMember(3)]
    public double X { get; set; }

    [ProtoMember(4)]
    public double Y { get; set; }

    [ProtoMember(5)]
    public double Z { get; set; }
}
