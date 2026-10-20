using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Walkers.Config;

// Settings of the stalker AI: the behaviour ladder, the gaze check and stalking. This file belongs to
// the stalker AI (Walkers/Stalker), which reads every value at the moment it uses it, so a reload
// applies at once. Times ending in Seconds are real time; Hours and Nights are in-game time.
public partial class WalkersConfig
{
    [Description("How fast a walker climbs the behaviour ladder towards the hunt.")]
    [JsonProperty(Order = 20)]
    public LadderConfig Ladder { get; set; } = new();

    [Description("When a player counts as looking at it, and what that does.")]
    [JsonProperty(Order = 21)]
    public GazeConfig Gaze { get; set; } = new();

    [Description("How it keeps its distance, slips away, comes back and closes in.")]
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

    [Description("Harassment acts the victim must have received before stages 3 and 4 move on.")]
    [Range(0, 20)]
    public int StageMinActs { get; set; } = 4;

    [Description("Highest stage it reaches while the victim is with others; above it, company makes it fall back.")]
    [Range(1, 5)]
    public int GroupStageCap { get; set; } = 3;

    [Description("Stages added the first time its victim hurts the guise in a haunt, up to stage 4.")]
    [Range(0, 2)]
    public int HarmGuiseBonus { get; set; } = 1;

    [Description("Pace multiplier while the victim's temporal stability, not counting the walker's own drain, is below 0.5.")]
    [Range(1, 4)]
    public float LowStabilityPace { get; set; } = 1.5f;

    [Description("Stage it falls back to when driven off during the hunt.")]
    [Range(1, 4)]
    public int DrivenOffStage { get; set; } = 3;

    [Description("In-game days it rests before climbing again after being driven off.")]
    [Range(0, 10)]
    public float DrivenOffRestDays { get; set; } = 1;

    [Description("The hunt ends after this many nights the victim spent online in stage 5 with no hit either way.")]
    [Range(1, 10)]
    public int HuntMaxNights { get; set; } = 3;
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

    [Description("The stare reactions (the doe's eye contact and bolt, the look-back, the true form's stare aggro) only within this many blocks; farther off it just holds still.")]
    [Range(8, 96)]
    public int StareRange { get; set; } = 48;

    [Description("Watched this many real seconds without a break before it freezes.")]
    [Range(0, 3)]
    public float FreezeAfterSeconds { get; set; } = 0.5f;

    [Description("Unwatched this many real seconds before a freeze ends, so flickering eye contact does not make it stop and start.")]
    [Range(0, 5)]
    public float FreezeReleaseSeconds { get; set; } = 1.5f;

    [Description("From escalation 2 (grudge): an unbroken stare this many real seconds turns the true form on the one who stares.")]
    [Range(1, 10)]
    public float StareAggroSeconds { get; set; } = 2;
}

public class StalkConfig
{
    [Description("Stages 2 to 4: watched for this many real seconds, it slips away once nobody sees it.")]
    [Range(3, 60)]
    public float VanishWatchedSeconds { get; set; } = 10;

    [Description("Stages 1 to 4: a player this many blocks away or closer makes it slip away; any doe bolts from them.")]
    [Range(4, 48)]
    public int VanishApproach { get; set; } = 16;

    [Description("Least real seconds between slipping away and reappearing.")]
    [Range(20, 600)]
    public float ReappearSeconds { get; set; } = 90;

    [Description("Before the hunt it never stands in block light this bright or brighter; 32 turns the rule off. Torches give 14 or 15, lanterns 18 to 20.")]
    [Range(1, 32)]
    public int AvoidBlockLight { get; set; } = 8;

    [Description("Stage 4: the true form may be seen standing far off at night.")]
    public bool Silhouettes { get; set; } = true;

    [Description("Most silhouettes a night.")]
    [Range(0, 5)]
    public int SilhouettesPerNight { get; set; } = 2;

    [Description("Closest a silhouette stands to the victim, in blocks.")]
    [Range(16, 96)]
    public int SilhouetteMinDistance { get; set; } = 32;

    [Description("A silhouette slips away after being watched this many real seconds.")]
    [Range(2, 30)]
    public float SilhouetteWatchedSeconds { get; set; } = 5;

    [Description("Guise walking speed, a deer's wander (about 0.8 blocks a second). Smaller deer types scale it down.")]
    [Range(0.002, 0.03)]
    public float GuiseSpeed { get; set; } = 0.006f;

    [Description("The doe holds eye contact this many real seconds (long enough to see its eyes), then bolts.")]
    [Range(0.5, 5)]
    public float EyeContactSeconds { get; set; } = 1.5f;

    [Description("The doe's bolt, a vanilla deer's flight (about 8 blocks a second). Smaller deer types scale it down.")]
    [Range(0.02, 0.1)]
    public float GuiseFleeSpeed { get; set; } = 0.08f;

    [Description("Real seconds the doe keeps running once it bolts.")]
    [Range(2, 20)]
    public float FleeSeconds { get; set; } = 6;

    [Description("True-form stalking speed, about 2.7 blocks a second (a walking player makes 3.4).")]
    [Range(0.005, 0.05)]
    public float StalkSpeed { get; set; } = 0.02f;

    [Description("True-form chase, about 6.1 blocks a second, a wolf's pace (a sprinting player makes 6.8).")]
    [Range(0.02, 0.08)]
    public float ChaseSpeed { get; set; } = 0.045f;

    [Description("Least real seconds between two lunges.")]
    [Range(2, 30)]
    public float LungeCooldownSeconds { get; set; } = 6;
}
