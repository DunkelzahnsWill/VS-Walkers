using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Walkers.Config;

// Settings of the stalker AI: the behaviour ladder, the gaze check and stalking. Drafted from the
// creature bible's "Config knobs" table; from here on this file belongs to the stalker AI thread,
// which renames, adds and removes settings as its code needs.
public partial class WalkersConfig
{
    [Description("How fast a walker climbs the behaviour ladder towards the hunt.")]
    [JsonProperty(Order = 20)]
    public LadderConfig Ladder { get; set; } = new();

    [Description("When a player counts as looking at it, and what that does.")]
    [JsonProperty(Order = 21)]
    public GazeConfig Gaze { get; set; } = new();

    [Description("How it keeps its distance, slips away and comes back.")]
    [JsonProperty(Order = 22)]
    public StalkConfig Stalk { get; set; } = new();
}

public class LadderConfig
{
    [Description("Scales every stage timer; higher is faster.")]
    [Range(0.25, 4)]
    public float PaceMultiplier { get; set; } = 1;

    [Description("Least in-game hours in stage 1, glimpsed.")]
    [Range(0, 96)]
    public float Stage1Hours { get; set; } = 12;

    [Description("Times the victim must have looked at it before stage 2.")]
    [Range(0, 10)]
    public int Stage1Sightings { get; set; } = 2;

    [Description("Least in-game hours in stage 2, watching.")]
    [Range(0, 168)]
    public float Stage2Hours { get; set; } = 24;

    [Description("Nights in stage 3, mimicking.")]
    [Range(0, 5)]
    public int Stage3Nights { get; set; } = 1;

    [Description("Nights in stage 4, testing.")]
    [Range(0, 5)]
    public int Stage4Nights { get; set; } = 1;

    [Description("Highest stage it reaches while the victim is with others.")]
    [Range(1, 5)]
    public int GroupStageCap { get; set; } = 3;

    [Description("Stages added when a player hurts the guise.")]
    [Range(0, 2)]
    public int HarmGuiseBonus { get; set; } = 1;

    [Description("Pace multiplier while the victim's temporal stability is below 0.5.")]
    [Range(1, 4)]
    public float LowStabilityPace { get; set; } = 1.5f;

    [Description("Stage it falls back to when driven off during the hunt.")]
    [Range(1, 4)]
    public int DrivenOffStage { get; set; } = 3;

    [Description("In-game days it rests before climbing again after being driven off.")]
    [Range(0, 10)]
    public float DrivenOffRestDays { get; set; } = 1;
}

public class GazeConfig
{
    [Description("Full width of the view cone, in degrees, that counts as looking at it.")]
    [Range(30, 180)]
    public float ConeDegrees { get; set; } = 110;

    [Description("Farthest distance, in blocks, at which a gaze counts.")]
    [Range(16, 160)]
    public int MaxDistance { get; set; } = 96;

    [Description("Gaze checks per walker per real second.")]
    [Range(1, 5)]
    public float ChecksPerSecond { get; set; } = 2;

    [Description("Any player's gaze stops it walking.")]
    public bool Freeze { get; set; } = true;

    [Description("During the hunt it may attack while watched inside this many blocks.")]
    [Range(0, 12)]
    public float LungeRange { get; set; } = 5;
}

public class StalkConfig
{
    [Description("Stages 2 to 4: watched for this many real seconds, it slips away.")]
    [Range(3, 60)]
    public float VanishWatchedSeconds { get; set; } = 10;

    [Description("Stages 1 to 4: a player this many blocks away or closer makes it slip away.")]
    [Range(4, 48)]
    public int VanishApproach { get; set; } = 16;

    [Description("Least real seconds between slipping away and reappearing.")]
    [Range(20, 600)]
    public float ReappearSeconds { get; set; } = 90;

    [Description("Before the hunt it never stands in block light this bright or brighter; 16 turns the rule off.")]
    [Range(0, 16)]
    public int AvoidBlockLight { get; set; } = 8;
}
