using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Walkers.Config;

// Settings of the 1.2 prototype (record/road-bible.md, part 2). This file belongs to the proto thread, with
// Walkers/Proto/. Nothing outside Walkers/Proto/ reads this section, so the prototype can be removed on its
// own. While proto.enabled is on, Walkers/Proto lays the values below over the settings they name, in the
// server's live config only; the file keeps the admin's own values, and switching off puts them back.
public partial class WalkersConfig
{
    [Description("The 1.2 prototype. Off, the mod plays exactly as version 1.1.1. Not for real servers.")]
    [JsonProperty(Order = 80)]
    public ProtoConfig Proto { get; set; } = new();
}

public class ProtoConfig
{
    [Description("Switches the prototype on: its fight values below take the place of the settings they name, until it is switched off.")]
    public bool Enabled { get; set; }

    [Description("The prototype's first retune of the fight. Each value stands in for the setting it names while the prototype is on.")]
    public ProtoFightConfig Fight { get; set; } = new();

    [Description("The prototype's faster return. Each value stands in for the setting it names while the prototype is on.")]
    public ProtoReturnConfig Return { get; set; } = new();
}

/// <summary>
/// The faster return, a first guess (R37 leaves the measure to the brief of feature 8). A walker's return
/// day is set when it dies, so a walker killed while the prototype is off keeps the 1.1.1 day.
/// </summary>
public class ProtoReturnConfig
{
    [Description("Stands in for return.afterSeasons (1.1.1: 2, which is 54 days with the default calendar). 0.11 is about 3 in-game days; the return still waits out its victim's rest after the death (director.minDaysBetweenHaunts, 5 days), so it comes back after about 5 days, ahead of any new walker.")]
    [Range(0.05, 8)]
    public float AfterSeasons { get; set; } = 0.11f;

    [Description("Stands in for return.paceMultiplier (1.1.1: 1.5): how much faster a returning walker climbs the haunt stages.")]
    [Range(1, 4)]
    public float PaceMultiplier { get; set; } = 2;
}

/// <summary>Each value stands in for the setting in its name while proto.enabled is on. The ranges are those of the settings.</summary>
public class ProtoFightConfig
{
    [Description("Stands in for forms.trueHealth (1.1.1: 50). For scale: a player has 15, a brown bear 64.")]
    [Range(20, 200)]
    public float TrueHealth { get; set; } = 70;

    [Description("Stands in for forms.trueDamage (1.1.1: 6). For scale: a wolf does 8, a brown bear 12.")]
    [Range(1, 15)]
    public float TrueDamage { get; set; } = 7;

    [Description("Stands in for forms.attackCooldownMs (1.1.1: 1500).")]
    [Range(500, 5000)]
    public int AttackCooldownMs { get; set; } = 1200;

    [Description("Stands in for forms.retreatHealth (1.1.1: 0.3): it breaks off the hunt below this share of its health.")]
    [Range(0, 0.9)]
    public float RetreatHealth { get; set; } = 0.2f;

    [Description("Stands in for stalk.stalkSpeed (1.1.1: 0.02, about 2.7 blocks a second).")]
    [Range(0.005, 0.05)]
    public float StalkSpeed { get; set; } = 0.022f;

    [Description("Stands in for stalk.chaseSpeed (1.1.1: 0.045, about 6.1 blocks a second; a sprinting player makes 6.8).")]
    [Range(0.02, 0.08)]
    public float ChaseSpeed { get; set; } = 0.048f;

    [Description("Stands in for stalk.lungeCooldownSeconds (1.1.1: 6).")]
    [Range(2, 30)]
    public float LungeCooldownSeconds { get; set; } = 4;

    // Two of the four cases in which it moves under a gaze (record/road-bible.md, part 1, point 5), tried
    // behind the switch as R34 allows. The rule itself stays: a gaze still holds it in every other case.

    [Description("Stands in for gaze.lungeRange (1.1.1: 5): in the hunt the gaze does not hold it within this many blocks of its victim.")]
    [Range(0, 12)]
    public float LungeRange { get; set; } = 7;

    [Description("Stands in for gaze.stareAggroSeconds (1.1.1: 2): from escalation 2, an unbroken stare this many seconds turns the true form on the one who stares.")]
    [Range(1, 10)]
    public float StareAggroSeconds { get; set; } = 1.5f;
}
