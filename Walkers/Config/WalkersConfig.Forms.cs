using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Walkers.Config;

// Settings of the two forms, the shift between them and the kill ritual. Drafted from the creature
// bible's "Config knobs" table; from here on this file belongs to the forms and kill ritual thread,
// which renames, adds and removes settings as its code needs.
public partial class WalkersConfig
{
    [Description("The guise, the true form and the shift between them.")]
    [JsonProperty(Order = 30)]
    public FormsConfig Forms { get; set; } = new();

    [Description("What kills a walker for good: silver and fire.")]
    [JsonProperty(Order = 31)]
    public RitualConfig Ritual { get; set; } = new();
}

public class FormsConfig
{
    [Description("Deer types the guise may wear, such as [\"whitetail\"]; [\"auto\"] wears the type whose climate fits the spot.")]
    public string[] GuiseTypes { get; set; } = { "auto" };

    [Description("The guise shrugs off what players do to it: no blow or burn of theirs kills it, and it is whole again soon after. Off, players can kill the guise like a deer; the true body then lies in its place.")]
    public bool GuiseShrugsDamage { get; set; } = true;

    [Description("From stage 3, a guise a player hurts or sets alight stands up as the true form this many real milliseconds later.")]
    [Range(0, 500)]
    public int HurtShiftDelayMs { get; set; } = 400;

    [Description("At escalation level 1 (Interloping) the walker rarely takes its true form unless attacked: the chance, rolled once per night, that it may shift at night at all. From level 2 it shifts whenever the night allows.")]
    [Range(0, 1)]
    public float InterlopingNightShiftChance { get; set; } = 0.2f;

    [Description("Least real seconds between two shifts.")]
    [Range(10, 600)]
    public float ShiftCooldownSeconds { get; set; } = 60;

    [Description("Allows the one shift in plain sight per haunt.")]
    public bool RevealShift { get; set; } = true;

    [Description("The reveal happens only while its victim watches from within this many blocks.")]
    [Range(4, 32)]
    public float RevealDistance { get; set; } = 12;

    [Description("Share of its true-form health it heals per in-game day while it wears the guise.")]
    [Range(0, 1)]
    public float HealPerDay { get; set; } = 0.25f;

    [Description("True-form health. For scale: a player has 15, a brown bear 64.")]
    [Range(20, 200)]
    public float TrueHealth { get; set; } = 50;

    [Description("True-form damage per hit before armour. For scale: a wolf does 8, a brown bear 12.")]
    [Range(1, 15)]
    public float TrueDamage { get; set; } = 6;

    [Description("Armour-piercing tier of its claws.")]
    [Range(0, 5)]
    public int TrueDamageTier { get; set; } = 3;

    [Description("Least real milliseconds between two hits.")]
    [Range(500, 5000)]
    public int AttackCooldownMs { get; set; } = 1500;

    [Description("No single hit takes more than this share of the victim's max health.")]
    [Range(0.1, 1)]
    public float MaxHitFraction { get; set; } = 0.4f;

    [Description("It breaks off the hunt below this share of its health.")]
    [Range(0, 0.9)]
    public float RetreatHealth { get; set; } = 0.3f;
}

public class RitualConfig
{
    [Description("Items that count as silver. Any item with the attribute walkersBane counts too.")]
    public string[] SilverItems { get; set; } =
    {
        "game:arrow-silver",
        "game:knife-generic-silver",
        "game:blade-falx-silver",
        "game:spear-generic-ornatesilver",
        "game:axe-felling-silver",
    };

    [Description("Damage multiplier for silver hits.")]
    [Range(1, 5)]
    public float SilverDamageMultiplier { get; set; } = 1.5f;

    [Description("Chance that a hit with a lit torch sets it alight. Vanilla uses 0.1.")]
    [Range(0, 1)]
    public float TorchIgniteChance { get; set; } = 1;

    [Description("Burn damage per second while it is alight. Vanilla uses 0.5.")]
    [Range(0.5, 10)]
    public float BurnDamagePerSecond { get; set; } = 2;

    [Description("Real seconds one ignition burns. Vanilla uses 12.")]
    [Range(5, 60)]
    public float BurnSeconds { get; set; } = 12;

    [Description("Real seconds the players have to burn the body after an ordinary kill.")]
    [Range(10, 600)]
    public float HuskBurnWindowSeconds { get; set; } = 120;

    [Description("The body can be skinned for its furs instead of burned; the walker then returns.")]
    public bool AllowHarvestHusk { get; set; } = true;

    [Description("Most unnatural furs one skinning gives; it gives from 2 up to this many.")]
    [Range(2, 4)]
    public int HuskFurCount { get; set; } = 4;

    [Description("Temporal stability given back to the killer on a ritual kill.")]
    [Range(0, 1)]
    public float BanishStabilityRecovery { get; set; } = 0.5f;
}
