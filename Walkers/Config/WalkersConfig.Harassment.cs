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

    [Description("When a walker is enraged, and what it does then: it tears a door off its hinges and comes inside, within the escalation level's door budget.")]
    [JsonProperty(Order = 51)]
    public EnragedConfig Enraged { get; set; } = new();

    [Description("The escalation ladder, levels 1 to 5 (Interloping, Grudge, Rage, Hate, Wrath): what the kit may do at each level.")]
    [JsonProperty(Order = 52)]
    public EscalationConfig Escalation { get; set; } = new();
}

public class HarassConfig
{
    [Description("Walkers check land claims before they touch, eat, tear or enter anything (the rules below). Off, every act, the break-in, the eaten crops and the stalker AI's doorway ignore claims; padlocks and barred doors still hold.")]
    [JsonProperty(Order = -3)]
    public bool CheckClaims { get; set; } = true;

    [Description("Which blocks the acts may touch: victimAccess only unclaimed land and claims the victim owns, shares through the claim's group, or was granted access to; anyClaim never on claimed land; off ignores claims. Padlocks and barred doors are always respected.")]
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

    [Description("Puts out torches near the victim at night, at most the escalation level's torchesPerNight; they stay out until a player relights them. From Hate it eats them instead.")]
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

    [Description("Enraged (see enraged), on a hunt night (stage 5), it tears a door off its hinges within the escalation level's budget and odds, disturbs lids and torches inside, and may come inside. A padlock, bar, reinforcement, anything behind the door, a gate or double door, or (while checkClaims is on) a claim the victim neither owns, shares nor was granted access to keeps it out.")]
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

    [Description("The escalation level from which a walker is enraged for the whole haunt (3 is Rage).")]
    [Range(1, 5)]
    public int FromLevel { get; set; } = 3;

    [Description("Hurting or burning a walker enrages it for the rest of the haunt, with the break-in odds and door budgets of enraged.fromLevel. Off by default, so keeping the fire on it stays the right play.")]
    public bool OnProvoked { get; set; } = false;

    [Description("Most times per in-game night an enraged walker comes in, per victim.")]
    [Range(0, 3)]
    public int EntriesPerNight { get; set; } = 1;

    [Description("Longest it stays inside, in real seconds.")]
    [Range(10, 120)]
    public int MaxIndoorSeconds { get; set; } = 30;

    [Description("Farthest it goes inside, in blocks from the door.")]
    [Range(2, 16)]
    public int MaxIndoorBlocks { get; set; } = 8;

    [Description("A torn door is hung back at dawn if its item still lies within 6 blocks and the doorway is clear.")]
    public bool RehangAtDawn { get; set; } = true;

    [Description("How long the dropped door lasts as an item, in real seconds.")]
    [Range(600, 604800)]
    public int DoorItemDespawnSeconds { get; set; } = 86400;
}

public partial class EscalationConfig
{
    [Description("The escalation ladder is on. Off, the kit treats every walker as level 1 (Interloping), whatever its level.")]
    public bool Enabled { get; set; } = true;

    [Description("What the kit may do at each escalation level. The level is the director's: 1 for a walker's first haunt, one more per improper death and per escalation.monthsPerLevel of active haunting, at most 5.")]
    public EscalationLevelsConfig Levels { get; set; } = new();

    [Description("The dead animals the levels' animalsPerHaunt budget spends.")]
    public EscalationHerdConfig Herd { get; set; } = new();

    [Description("How the levels' cropsPerHaunt budget is eaten.")]
    public EscalationCropsConfig Crops { get; set; } = new();
}

public class EscalationLevelsConfig
{
    [Description("Interloping: a small crop budget, few torches, no knocks.")]
    public EscalationLevelConfig Level1 { get; set; } = new() { CropsPerHaunt = 8, TorchesPerNight = 1 };

    [Description("Grudge: the night knocks, a modest animal budget, a few more crops and torches.")]
    public EscalationLevelConfig Level2 { get; set; } = new() { Knocks = true, CropsPerHaunt = 12, TorchesPerNight = 3, AnimalsPerHaunt = 1 };

    [Description("Rage: enraged, more animals, now and then a break-in on a hunt night.")]
    public EscalationLevelConfig Level3 { get; set; } = new()
    {
        Knocks = true, CropsPerHaunt = 12, TorchesPerNight = 3, AnimalsPerHaunt = 3,
        BreakInChance = 0.25f, WoodenDoorsPerHaunt = 2, OtherDoorsPerHaunt = 1,
    };

    [Description("Hate: twice the doors, a break-in nearly every hunt night, torches eaten.")]
    public EscalationLevelConfig Level4 { get; set; } = new()
    {
        Knocks = true, CropsPerHaunt = 12, TorchesPerNight = 3, EatTorches = true, AnimalsPerHaunt = 3,
        BreakInChance = 0.9f, WoodenDoorsPerHaunt = 4, OtherDoorsPerHaunt = 2,
    };

    [Description("Wrath: company no longer keeps it away.")]
    public EscalationLevelConfig Level5 { get; set; } = new()
    {
        Knocks = true, CropsPerHaunt = 12, TorchesPerNight = 3, EatTorches = true, AnimalsPerHaunt = 3,
        BreakInChance = 1, WoodenDoorsPerHaunt = 4, OtherDoorsPerHaunt = 2, IgnoresCompany = true,
    };

    /// <summary>The settings of a level, clamped to 1-5.</summary>
    public EscalationLevelConfig For(int level) => level switch
    {
        <= 1 => Level1,
        2 => Level2,
        3 => Level3,
        4 => Level4,
        _ => Level5,
    };
}

public class EscalationLevelConfig
{
    [Description("The knocks (and their pages) happen at this level.")]
    public bool Knocks { get; set; }

    [Description("Most torches put out per in-game night, per victim (harass.douseTorches.perNight caps it too).")]
    [Range(0, 20)]
    public int TorchesPerNight { get; set; }

    [Description("A torch it reaches is eaten: the block is gone, with nothing dropped, instead of put out.")]
    public bool EatTorches { get; set; }

    [Description("Most crop blocks it eats per haunt.")]
    [Range(0, 64)]
    public int CropsPerHaunt { get; set; }

    [Description("Most dead animals per haunt: the rotting herd, plus one of the victim's own if escalation.herd.killLivestock is on.")]
    [Range(0, 6)]
    public int AnimalsPerHaunt { get; set; }

    [Description("Chance, on each hunt night (stage 5), that an enraged walker tries to break in.")]
    [Range(0, 1)]
    public float BreakInChance { get; set; }

    [Description("Plain single wooden doors (solid, sleek-windowed or crude) it may tear off per haunt.")]
    [Range(0, 8)]
    public int WoodenDoorsPerHaunt { get; set; }

    [Description("Bare single iron or heavy doors it may tear off per haunt. A padlock, bar, reinforcement or anything behind a door always keeps it out; gates and double doors are never torn.")]
    [Range(0, 4)]
    public int OtherDoorsPerHaunt { get; set; }

    [Description("Company no longer keeps it from breaking in (the stalker AI reads the level for its own attacks).")]
    public bool IgnoresCompany { get; set; }
}

public class EscalationHerdConfig
{
    [Description("In-game hours the rotting herd lies before it is removed, so no bones stay.")]
    [Range(6, 96)]
    public int DecayHours { get; set; } = 24;

    [Description("One of the animal budget may be one of the victim's own farm animals found dead at dawn, in a claim the victim owns. Never a named, tamed, rideable, pregnant or founder animal, never the last two of either sex. Off by default.")]
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

    [Description("Most share of the crops in range it may eat per haunt.")]
    [Range(0, 1)]
    public float MaxShare { get; set; } = 0.25f;

    [Description("Where: ownOrShared only claims the victim owns, shares or was granted access to (and unclaimed land in single player); victimAccess adds unclaimed land. harass.checkClaims off allows anywhere.")]
    public CropClaimRule ClaimRule { get; set; } = CropClaimRule.OwnOrShared;
}

public enum CropClaimRule
{
    OwnOrShared,
    VictimAccess,
}
