using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Walkers.Config;

// Settings of the director (population, pacing), the return, and the debug aids for headless tests.
// From the creature bible's "Config knobs" table; owned by the director (Walkers/Director).
public partial class WalkersConfig
{
    [Description("Who gets haunted, when, and how many walkers at once.")]
    [JsonProperty(Order = 10)]
    public DirectorConfig Director { get; set; } = new();

    [Description("How a walker killed without silver or fire comes back.")]
    [JsonProperty(Order = 40)]
    public ReturnConfig Return { get; set; } = new();

    [Description("Unnatural furs, skinned from a walker's body: whoever carries them is marked as its target.")]
    [JsonProperty(Order = 41)]
    public FursConfig Furs { get; set; } = new();

    [Description("Aids for testing on a server with no player online. Leave them off on a real server.")]
    [JsonProperty(Order = 90)]
    public DebugConfig Debug { get; set; } = new();
}

public class DirectorConfig : IWalkersConfigCheck
{
    [Description("How often the director updates, in real seconds.")]
    [Range(5, 60)]
    public float TickSeconds { get; set; } = 10;

    [Description("Walkers haunting at once on a small server.")]
    [Range(0, 8)]
    public int MaxActiveWalkers { get; set; } = 1;

    [Description("One more active walker per this many eligible players online.")]
    [Range(2, 64)]
    public int PlayersPerExtraWalker { get; set; } = 8;

    [Description("Hard cap on active walkers, whatever the player count.")]
    [Range(1, 8)]
    public int MaxActiveWalkersCap { get; set; } = 3;

    [Description("In-game days a player rests after a haunt ends before they can be chosen again.")]
    [Range(0, 60)]
    public float MinDaysBetweenHaunts { get; set; } = 5;

    [Description("Chance per in-game day that a rested, eligible player is chosen.")]
    [Range(0, 1)]
    public float HauntChancePerDay { get; set; } = 0.35f;

    [Description("In-game days after a player's first join before they can be chosen.")]
    [Range(0, 30)]
    public float NewPlayerGraceDays { get; set; } = 3;

    [Description("Real minutes idle before a player counts as away and cannot be chosen; 0 turns the check off.")]
    [Range(0, 60)]
    public float AfkMinutes { get; set; } = 5;

    [Description("Another player within this many blocks means the victim is not alone.")]
    [Range(8, 128)]
    public int IsolationRadius { get; set; } = 24;

    [Description("Deeper than this many blocks below the surface the haunt pauses; it waits at the cave mouth.")]
    [Range(0, 64)]
    public int MaxDepthBelowSurface { get; set; } = 10;

    [Description("Forest or shrub density, 0 to 1, needed where it appears.")]
    [Range(0, 1)]
    public float MinForestDensity { get; set; } = 0.25f;

    [Description("Least distance from the victim, in blocks, at which it first appears.")]
    [Range(24, 128)]
    public int SpawnDistanceMin { get; set; } = 48;

    [Description("Greatest distance from the victim, in blocks, at which it first appears.")]
    [Range(24, 128)]
    public int SpawnDistanceMax { get; set; } = 64;

    [Description("Candidate spots tested per director update.")]
    [Range(1, 32)]
    public int SpawnTriesPerTick { get; set; } = 8;

    [Description("Every haunt ends this many in-game days of unpaused time after it began, the hunt included.")]
    [Range(3, 60)]
    public float HauntMaxDays { get; set; } = 12;

    [Description("A haunt whose victim stays away (offline, underground, out of the forest) this many in-game days ends.")]
    [Range(1, 60)]
    public float PausedHauntMaxDays { get; set; } = 10;

    [Description("Two players closer than this many blocks cannot both be victims at once: a group is a group.")]
    [Range(24, 256)]
    public int VictimSeparation { get; set; } = 64;

    [Description("No new appearances during a temporal storm, and active walkers withdraw.")]
    public bool PauseDuringStorms { get; set; } = true;

    public void Check(string path, List<string> fixes)
    {
        if (SpawnDistanceMin > SpawnDistanceMax)
        {
            fixes.Add($"{path}.spawnDistanceMin {SpawnDistanceMin} is above {path}.spawnDistanceMax {SpawnDistanceMax}; using {SpawnDistanceMin} for both");
            SpawnDistanceMax = SpawnDistanceMin;
        }
    }
}

public class ReturnConfig
{
    [Description("In-game seasons until a walker that was not burned or killed with silver returns. 2 seasons are 54 days with default calendar settings.")]
    [Range(0.25, 8)]
    public float AfterSeasons { get; set; } = 2;

    [Description("Ladder stage a returning walker starts at.")]
    [Range(1, 4)]
    public int StartStage { get; set; } = 2;

    [Description("How much faster a returning walker climbs the ladder.")]
    [Range(1, 4)]
    public float PaceMultiplier { get; set; } = 1.5f;

    [Description("Extra true-form health per return, as a share of its base health, up to return.healthBonusCap. 0 by default: a walker comes back angrier (the escalation list), not stronger.")]
    [Range(0, 0.5)]
    public float HealthBonusPerReturn { get; set; } = 0;

    [Description("Most extra true-form health a walker can gain from returns, as a share of its base health.")]
    [Range(0, 1)]
    public float HealthBonusCap { get; set; } = 0.3f;

    [Description("If its last victim stays away this many seasons past the return date, it goes back to the roster for whoever the next normal roll picks.")]
    [Range(0, 8)]
    public float VictimAbsentSeasons { get; set; } = 1;
}

public class FursConfig : IWalkersConfigCheck
{
    [Description("Carried furs mark their carrier as the walker's target; off makes them trophies only.")]
    public bool MarkTarget { get; set; } = true;

    [Description("In-game hours of carrying, online, eligible and not idle, before the mark takes.")]
    [Range(0, 72)]
    public float MarkThresholdHours { get; set; } = 6;

    [Description("The strongest a mark grows, in hours of carrying.")]
    [Range(6, 240)]
    public float MarkCapHours { get; set; } = 72;

    [Description("How many hours a mark fades per in-game hour once its carrier stops carrying.")]
    [Range(0.1, 10)]
    public float DecayPerHour { get; set; } = 1;

    [Description("Furs that another player carried before do not count for this many in-game hours after they are picked up.")]
    [Range(0, 72)]
    public float ForeignGraceHours { get; set; } = 12;

    [Description("A marked player's daily haunt chance is multiplied by this while the walker that marked them rests.")]
    [Range(1, 5)]
    public float RestingRollMultiplier { get; set; } = 2;

    [Description("Burning the furs (in fire, lava or a firepit) clears the mark of whoever dropped or burned them.")]
    public bool BurnClearsMark { get; set; } = true;

    public void Check(string path, List<string> fixes)
    {
        if (MarkThresholdHours > MarkCapHours)
        {
            fixes.Add($"{path}.markThresholdHours {MarkThresholdHours} is above {path}.markCapHours {MarkCapHours}; using {MarkCapHours} for both");
            MarkThresholdHours = MarkCapHours;
        }
    }
}

// The director's half of escalation.*; the harassment kit adds its acts' keys in its own file.
public partial class EscalationConfig
{
    [Description("In-game months of live haunting after its first improper death that add one escalation level, as a death does; 0 turns the monthly step off.")]
    [Range(0, 12)]
    public float MonthsPerLevel { get; set; } = 1;
}

public class DebugConfig
{
    [Description("Allows /walkers director standin, which places a vanilla player bot the director treats as an eligible player who is alone and on the surface. For headless soak tests only.")]
    public bool StandInVictims { get; set; } = false;
}
