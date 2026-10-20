using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Walkers.Config;

// Settings of the director (population, pacing) and the return. Drafted from the creature bible's
// "Config knobs" table; from here on this file belongs to the director thread, which renames, adds
// and removes settings as its code needs.
public partial class WalkersConfig
{
    [Description("Who gets haunted, when, and how many walkers at once.")]
    [JsonProperty(Order = 10)]
    public DirectorConfig Director { get; set; } = new();

    [Description("How a walker killed without silver or fire comes back.")]
    [JsonProperty(Order = 40)]
    public ReturnConfig Return { get; set; } = new();
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

    [Description("In-game days after which a haunt that never reached the hunt ends.")]
    [Range(3, 60)]
    public float HauntMaxDays { get; set; } = 12;

    [Description("No new appearances during a temporal storm, and active walkers withdraw.")]
    public bool PauseDuringStorms { get; set; } = true;

    [Description("Lets a player opt out of being chosen at all, with /walkers me noHaunt on.")]
    public bool AllowPlayerOptOut { get; set; } = false;

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

    [Description("Extra true-form health per return, as a share of its base health; the total bonus stops at 0.3.")]
    [Range(0, 0.5)]
    public float HealthBonusPerReturn { get; set; } = 0.1f;

    [Description("If its last victim stays away this many seasons past the return date, it picks the nearest eligible player to where it died.")]
    [Range(0, 8)]
    public float VictimAbsentSeasons { get; set; } = 1;
}
