using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Walkers.Config;

// Settings of the harassment kit (Walkers/Harassment): the act scheduler's nightly budget, then one
// section per act, each with its own on/off switch and cap. The switches are server-admin settings,
// not a player opt-out (the owner's answer 8). The stalker AI also reads respectClaims and
// doors.maxOpenHours for its own doorway task.
public partial class WalkersConfig
{
    [Description("The non-combat acts, each with its own switch and limits.")]
    [JsonProperty(Order = 50)]
    public HarassConfig Harass { get; set; } = new();

    [Description("When a walker is enraged, and what it does then: it comes inside, and from the second return it may tear a door off its hinges.")]
    [JsonProperty(Order = 51)]
    public EnragedConfig Enraged { get; set; } = new();

    [Description("The escalation list: what a victim finds after a walker comes back from an improper kill.")]
    [JsonProperty(Order = 52)]
    public EscalationConfig Escalation { get; set; } = new();
}

public class HarassConfig
{
    [Description("Which blocks the acts may touch: victimAccess only unclaimed land and claims the victim owns or shares through the claim's group, anyClaim never on claimed land, off ignores claims. Padlocks and barred doors are always respected.")]
    public ClaimRespect RespectClaims { get; set; } = ClaimRespect.VictimAccess;

    [Description("How many acts it may spend on its victim each night, by stage, not counting the dusk knocks. The budget resets at dusk; acts that also run by day spend from the same budget.")]
    public ActsPerNightConfig ActsPerNight { get; set; } = new();

    [Description("Least time, in real seconds, between two acts against the same victim; a random 0 to 120 seconds is added.")]
    [Range(30, 600)]
    public int MinGapSeconds { get; set; } = 120;

    [Description("Replays the victim's own recent sounds behind them, and footsteps on their own ground.")]
    public EchoesConfig Echoes { get; set; } = new();

    [Description("One, two or three knocks, by stage: one knock a night.")]
    public HarassActConfig Knocks { get; set; } = new();

    [Description("Deer calls pitched wrong, from where it is not.")]
    public WrongCallsConfig WrongCalls { get; set; } = new();

    [Description("Garbled whispers to the victim, with their name unless they chose noName.")]
    public WhispersConfig Whispers { get; set; } = new();

    [Description("The two taglines as on-screen title text, once each per haunt.")]
    public HarassActConfig Titles { get; set; } = new();

    [Description("Temporal-stability drain while it is near, doubled while it is watched.")]
    public StabilityDrainConfig StabilityDrain { get; set; } = new();

    [Description("Animals near it flee before players see it.")]
    public LivestockPanicConfig LivestockPanic { get; set; } = new();

    [Description("Puts out torches near the victim at night; they stay out until a player relights them.")]
    public DouseTorchesConfig DouseTorches { get; set; } = new();

    [Description("Opens doors and trapdoors, or shuts a door, trapdoor or gate left open. It never opens a fence gate.")]
    public DoorsConfig Doors { get; set; } = new();

    [Description("Pops chest lids without touching the contents.")]
    public ChestLidsConfig ChestLids { get; set; } = new();

    [Description("Wakes the victim, never during a night skip others are waiting on.")]
    public SleepDisturbConfig SleepDisturb { get; set; } = new();

    [Description("Leaves a dead deer at the victim's door at dawn.")]
    public OfferingsConfig Offerings { get; set; } = new();

    [Description("Leaves the lore bible's pages at the victim's door when its events happen.")]
    public HarassActConfig Pages { get; set; } = new();

    [Description("Stage 5: the true form opens and holds an unlocked door (the stalker AI's doorway task reads this switch).")]
    public HarassActConfig Doorway { get; set; } = new();

    [Description("Enraged (see enraged), in stage 5 at night, it forces its way into the victim's home: a plain wooden door bursts open, lids and torches inside are disturbed, and it may come inside. A padlock, bar, reinforcement, anything behind the door, a metal, heavy, gate or double door, or a claim the victim neither owns nor shares keeps it out.")]
    public BreakInConfig BreakIn { get; set; } = new();

    [Description("Small camera jolts for the victim with close knocks and scrapes.")]
    public HarassActConfig ScreenShake { get; set; } = new();

    [Description("Used instead of the drain in mundane mode: a slow warp of the victim's view.")]
    public MundaneWarpConfig MundaneWarp { get; set; } = new();
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

public class ActsPerNightConfig
{
    [Description("Stage 2 (watching).")]
    [Range(0, 30)]
    public int Stage2 { get; set; } = 2;

    [Description("Stage 3 (mimicking).")]
    [Range(0, 30)]
    public int Stage3 { get; set; } = 6;

    [Description("Stage 4 (testing).")]
    [Range(0, 30)]
    public int Stage4 { get; set; } = 9;

    [Description("Stage 5 (the hunt).")]
    [Range(0, 30)]
    public int Stage5 { get; set; } = 6;

    /// <summary>The budget of a stage; 0 below stage 2.</summary>
    public int For(int stage) => stage switch
    {
        2 => Stage2,
        3 => Stage3,
        4 => Stage4,
        >= 5 => Stage5,
        _ => 0,
    };
}

public class EchoesConfig : HarassActConfig
{
    [Description("Most echoes (footsteps included) per in-game night, per victim.")]
    [Range(0, 20)]
    public int PerNight { get; set; } = 6;

    [Description("How many of the victim's recent block actions it remembers.")]
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
    [Description("Most disturbances per in-game night, per victim, at least 20 real seconds apart.")]
    [Range(0, 4)]
    public int PerNight { get; set; } = 2;
}

public class WrongCallsConfig : HarassActConfig
{
    [Description("Pitch of the wrong calls; 1 is the normal pitch.")]
    [Range(0.5, 1)]
    public float Pitch { get; set; } = 0.75f;

    [Description("Most wrong calls per in-game night, per victim.")]
    [Range(0, 10)]
    public int PerNight { get; set; } = 4;
}

public class StabilityDrainConfig : HarassActConfig, IWalkersConfigCheck
{
    [Description("Temporal stability drained per real minute while it is within 48 blocks; twice that while the victim watches it.")]
    [Range(0, 0.2)]
    public float PerMinute { get; set; } = 0.05f;

    [Description("It never drains below this. Vanilla damage starts below 0.13.")]
    [Range(0.13, 1)]
    public float Floor { get; set; } = 0.15f;

    [Description("Where the drain stops, by stage.")]
    public DrainTargetsConfig Targets { get; set; } = new();

    [Description("How low stability dips for a moment at the reveal, which brings on the vanilla glitch.")]
    [Range(0.13, 1)]
    public float RevealDip { get; set; } = 0.15f;

    [Description("How long the reveal dip lasts, in real seconds.")]
    [Range(1, 30)]
    public int RevealDipSeconds { get; set; } = 8;

    public void Check(string path, List<string> fixes)
    {
        foreach (var (name, get, set) in Targets.All())
        {
            if (get() < Floor)
            {
                fixes.Add($"{path}.targets.{name}: {get()} is below the floor, raised to {Floor}");
                set(Floor);
            }
        }
        if (RevealDip < Floor)
        {
            fixes.Add($"{path}.revealDip: {RevealDip} is below the floor, raised to {Floor}");
            RevealDip = Floor;
        }
    }
}

public class DrainTargetsConfig
{
    [Description("Stage 2: the stability bar falls.")]
    [Range(0.13, 1)]
    public float Stage2 { get; set; } = 0.7f;

    [Description("Stage 3.")]
    [Range(0.13, 1)]
    public float Stage3 { get; set; } = 0.5f;

    [Description("Stage 4: the music falls silent, a low hum, the doe stutters.")]
    [Range(0.13, 1)]
    public float Stage4 { get; set; } = 0.35f;

    [Description("Stage 5: fog and rust rain.")]
    [Range(0.13, 1)]
    public float Stage5 { get; set; } = 0.25f;

    /// <summary>The target of a stage; 1 (no drain) below stage 2.</summary>
    public float For(int stage) => stage switch
    {
        2 => Stage2,
        3 => Stage3,
        4 => Stage4,
        >= 5 => Stage5,
        _ => 1,
    };

    internal IEnumerable<(string Name, System.Func<float> Get, System.Action<float> Set)> All()
    {
        yield return ("stage2", () => Stage2, v => Stage2 = v);
        yield return ("stage3", () => Stage3, v => Stage3 = v);
        yield return ("stage4", () => Stage4, v => Stage4 = v);
        yield return ("stage5", () => Stage5, v => Stage5 = v);
    }
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

public class ChestLidsConfig : HarassActConfig
{
    [Description("Most chest lids popped per in-game night, per victim.")]
    [Range(0, 6)]
    public int PerNight { get; set; } = 2;
}

public class OfferingsConfig : HarassActConfig
{
    [Description("Most offerings per haunt.")]
    [Range(0, 3)]
    public int PerHaunt { get; set; } = 1;
}

public class BreakInConfig : HarassActConfig
{
    [Description("Radius, in blocks, around the door it forces in which it disturbs lids and torches inside.")]
    [Range(2, 12)]
    public int Reach { get; set; } = 6;
}

public class MundaneWarpConfig : HarassActConfig
{
    [Description("Psychedelic value written instead of the drain; the client shows about half of it.")]
    [Range(0, 1)]
    public float Value { get; set; } = 0.4f;
}

public class EnragedConfig
{
    [Description("Walkers may be enraged at all; never while the world's creatureHostility is off.")]
    public bool Enabled { get; set; } = true;

    [Description("A walker that came back from an improper kill is enraged for the whole haunt.")]
    public bool OnReturn { get; set; } = true;

    [Description("Hurting or burning a walker enrages it for the rest of the haunt. Off by default, so keeping the fire on it stays the right play.")]
    public bool OnProvoked { get; set; } = false;

    [Description("Most times per in-game night an enraged walker comes in, per victim.")]
    [Range(0, 3)]
    public int EntriesPerNight { get; set; } = 1;

    [Description("Most times per haunt it comes in.")]
    [Range(0, 6)]
    public int EntriesPerHaunt { get; set; } = 2;

    [Description("Longest it stays inside, in real seconds.")]
    [Range(10, 120)]
    public int MaxIndoorSeconds { get; set; } = 30;

    [Description("Farthest it goes inside, in blocks from the door.")]
    [Range(2, 16)]
    public int MaxIndoorBlocks { get; set; } = 8;

    [Description("Doors it tears off their hinges per haunt (only a closed single wooden door: solid, sleek-windowed or crude).")]
    [Range(0, 3)]
    public int TearsPerHaunt { get; set; } = 1;

    [Description("The return from which it may tear a door (1 is the first return).")]
    [Range(1, 5)]
    public int TearFromReturn { get; set; } = 2;

    [Description("A torn door is hung back at dawn if its item still lies within 6 blocks and the doorway is clear.")]
    public bool RehangAtDawn { get; set; } = true;

    [Description("How long the dropped door lasts as an item, in real seconds.")]
    [Range(600, 604800)]
    public int DoorItemDespawnSeconds { get; set; } = 86400;
}

public partial class EscalationConfig
{
    [Description("The escalation list after a return is on.")]
    public bool Enabled { get; set; } = true;

    [Description("The rotting herd (first return on) and, opt-in, the victim's own livestock (second return on).")]
    public EscalationHerdConfig Herd { get; set; } = new();

    [Description("Eaten crops (first return on).")]
    public EscalationCropsConfig Crops { get; set; } = new();
}

public class EscalationHerdConfig
{
    [Description("Dead does found 16 to 24 blocks from the victim's door at the first dawn of stage 2, once per return. A knife gets only rot from them.")]
    [Range(0, 4)]
    public int WildCarcasses { get; set; } = 2;

    [Description("In-game hours the rotting herd lies before it is removed, so no bones stay.")]
    [Range(6, 96)]
    public int DecayHours { get; set; } = 24;

    [Description("Allows one of the victim's own farm animals to be found dead at dawn, once per return from the second, in a claim the victim owns. Never a named, tamed, rideable, pregnant or founder animal, never the last two of either sex.")]
    public bool KillLivestock { get; set; } = false;
}

public class EscalationCropsConfig
{
    [Description("Ripe crops set back in one line, one act a night in stages 2 to 5.")]
    [Range(1, 8)]
    public int BlocksPerAct { get; set; } = 4;

    [Description("Growth stages each eaten crop is set back.")]
    [Range(1, 3)]
    public int StagesBack { get; set; } = 2;

    [Description("Most crop blocks per return.")]
    [Range(0, 64)]
    public int MaxPerReturn { get; set; } = 16;

    [Description("Most share of the crops in range it may eat per return.")]
    [Range(0, 1)]
    public float MaxShare { get; set; } = 0.25f;

    [Description("Where: ownOrShared only claims the victim owns or shares (and unclaimed land in single player); victimAccess adds unclaimed land.")]
    public CropClaimRule ClaimRule { get; set; } = CropClaimRule.OwnOrShared;
}

public enum CropClaimRule
{
    OwnOrShared,
    VictimAccess,
}
