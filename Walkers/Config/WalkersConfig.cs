using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Walkers.Config;

/// <summary>
/// Server settings, saved as ModConfig/walkers.json. Read them with <c>api.GetWalkersConfig()</c>
/// (<see cref="WalkersConfigExtensions.GetWalkersConfig"/>).
///
/// The class is partial: each feature keeps its section in its own file next to this one, so feature
/// branches never edit the same file. A section is a property on this class plus a plain class of
/// settings, for example in WalkersConfig.Stalker.cs:
/// <code>
/// public partial class WalkersConfig
/// {
///     [Description("When a player counts as looking at it, and what that does.")]
///     [JsonProperty(Order = 21)]
///     public GazeConfig Gaze { get; set; } = new();
/// }
///
/// public class GazeConfig
/// {
///     [Description("Full width of the view cone, in degrees, that counts as looking at it.")]
///     [Range(30, 180)]
///     public float ConeDegrees { get; set; } = 110;
/// }
/// </code>
/// The rules for a setting:
/// <list type="bullet">
/// <item>A public property with a getter and a setter, and its default as the initializer. In the file
/// it appears in camelCase (gaze.coneDegrees); the commands use the same path.</item>
/// <item>A <see cref="DescriptionAttribute"/> says what it does; <c>/walkers config &lt;path&gt;</c> shows it to admins.</item>
/// <item>Numbers carry a <see cref="RangeAttribute"/>. A value out of range is clamped with a warning.
/// Checks that involve two settings go in <see cref="IWalkersConfigCheck"/>.</item>
/// <item>Allowed types: bool, int, long, float, double, string, enums (written as their name), string
/// arrays, and nested setting classes.</item>
/// <item>A section's defaults live in its own class. A section read from the file is built with
/// <c>new T()</c>, so an object initializer on the property (<c>= new() { PerNight = 3 }</c>) would be
/// lost for every key the file leaves out.</item>
/// <item>Top-level sections take a <see cref="JsonPropertyAttribute.Order"/> so the file reads in a
/// fixed order: director 10s, stalker AI 20s, forms 30s, return 40s, harassment 50s, later features 60 and up.</item>
/// <item>Times say their unit in the name: Seconds, Minutes and Ms are real time; Hours, Days, Nights
/// and Seasons are in-game time.</item>
/// <item>Never rename or remove a setting in a release without a migration (see <see cref="ConfigVersion"/>);
/// before the first release, just change it.</item>
/// </list>
/// </summary>
public partial class WalkersConfig
{
    public const string FileName = "walkers.json";

    /// <summary>Raise when a change needs existing files migrated.</summary>
    [Description("Version of this file's layout. Leave it as it is.")]
    [JsonProperty(Order = 0)]
    public int ConfigVersion { get; set; } = 1;

    [Description("Master switch. When off, no walker appears and none acts.")]
    [JsonProperty(Order = 1)]
    public bool Enabled { get; set; } = true;

    [Description("Mundane mode drops the temporal-stability drain and the storm checks. auto turns it on when the world has temporal stability off.")]
    [JsonProperty(Order = 2)]
    public MundaneMode MundaneMode { get; set; } = MundaneMode.Auto;
}

public enum MundaneMode
{
    Auto,
    On,
    Off,
}

/// <summary>
/// A settings class implements this when a check involves more than one setting, for example a
/// minimum that must not exceed its maximum. Fix the values in place and describe each fix in
/// <paramref name="fixes"/> (path and what changed); the loader logs them as warnings.
/// </summary>
public interface IWalkersConfigCheck
{
    void Check(string path, List<string> fixes);
}
