using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Walkers.Config;

// Settings of the harassment kit: one section per act, each with its own on/off switch. Drafted from
// the creature bible's "Config knobs" table; from here on this file belongs to the harassment kit
// thread, which renames, adds and removes settings as its code needs.
public partial class WalkersConfig
{
    [Description("The non-combat acts, each with its own switch and limits.")]
    [JsonProperty(Order = 50)]
    public HarassConfig Harass { get; set; } = new();
}

public class HarassConfig
{
    [Description("Which blocks the acts may touch: victimAccess only blocks the victim could use, anyClaim never on claimed land, off ignores claims.")]
    public ClaimRespect RespectClaims { get; set; } = ClaimRespect.VictimAccess;

    [Description("Replays the victim's own recent sounds behind them.")]
    public EchoesConfig Echoes { get; set; } = new();

    [Description("One, two or three knocks, by stage.")]
    public HarassActConfig Knocks { get; set; } = new();

    [Description("Deer calls pitched wrong, from where it is not.")]
    public WrongCallsConfig WrongCalls { get; set; } = new();

    [Description("Garbled whispers to the victim, with their name unless they opt out.")]
    public WhispersConfig Whispers { get; set; } = new();

    [Description("The two taglines as on-screen title text, once each per haunt.")]
    public HarassActConfig Titles { get; set; } = new();

    [Description("Temporal-stability drain while it is near, doubled while it is watched.")]
    public StabilityDrainConfig StabilityDrain { get; set; } = new();

    [Description("Animals near it flee before players see it.")]
    public LivestockPanicConfig LivestockPanic { get; set; } = new();

    [Description("Puts out torches near the victim at night.")]
    public DouseTorchesConfig DouseTorches { get; set; } = new();

    [Description("Opens or closes doors, trapdoors and gates.")]
    public DoorsConfig Doors { get; set; } = new();

    [Description("Pops chest lids without touching the contents.")]
    public HarassActConfig ChestLids { get; set; } = new();

    [Description("Wakes the victim or stops the night skip.")]
    public SleepDisturbConfig SleepDisturb { get; set; } = new();

    [Description("Leaves a dead animal at the victim's door.")]
    public OfferingsConfig Offerings { get; set; } = new();
}

public enum ClaimRespect
{
    VictimAccess,
    AnyClaim,
    Off,
}

public class HarassActConfig
{
    [Description("This act is on.")]
    [JsonProperty(Order = -2)]
    public bool Enabled { get; set; } = true;
}

public class EchoesConfig : HarassActConfig
{
    [Description("Most echoes per in-game night, per victim.")]
    [Range(0, 20)]
    public int PerNight { get; set; } = 6;

    [Description("How many of the victim's recent actions it remembers.")]
    [Range(8, 128)]
    public int Buffer { get; set; } = 32;
}

public class WhispersConfig : HarassActConfig
{
    [Description("Most whispers per in-game night, per victim.")]
    [Range(0, 10)]
    public int PerNight { get; set; } = 3;
}

public class DouseTorchesConfig : HarassActConfig
{
    [Description("Most torches put out per in-game night, per victim.")]
    [Range(0, 20)]
    public int PerNight { get; set; } = 6;
}

public class SleepDisturbConfig : HarassActConfig
{
    [Description("Most disturbances per in-game night, per victim. Each one stops the whole server's night skip.")]
    [Range(0, 4)]
    public int PerNight { get; set; } = 2;
}

public class WrongCallsConfig : HarassActConfig
{
    [Description("Pitch of the wrong calls; 1 is the normal pitch.")]
    [Range(0.5, 1)]
    public float Pitch { get; set; } = 0.75f;
}

public class StabilityDrainConfig : HarassActConfig
{
    [Description("Temporal stability drained per real minute while it is near.")]
    [Range(0, 0.2)]
    public float PerMinute { get; set; } = 0.05f;

    [Description("It never drains below this. Vanilla damage starts below 0.13.")]
    [Range(0.13, 1)]
    public float Floor { get; set; } = 0.15f;
}

public class LivestockPanicConfig : HarassActConfig
{
    [Description("Radius, in blocks, in which animals panic.")]
    [Range(4, 24)]
    public int Radius { get; set; } = 10;
}

public class DoorsConfig : HarassActConfig
{
    [Description("Most door, trapdoor or gate moves per in-game night, per victim.")]
    [Range(0, 10)]
    public int PerNight { get; set; } = 4;

    [Description("In-game hours after which a door it opened is closed again.")]
    [Range(0.25, 6)]
    public float MaxOpenHours { get; set; } = 1;
}

public class OfferingsConfig : HarassActConfig
{
    [Description("Most offerings per haunt.")]
    [Range(0, 3)]
    public int PerHaunt { get; set; } = 1;
}
