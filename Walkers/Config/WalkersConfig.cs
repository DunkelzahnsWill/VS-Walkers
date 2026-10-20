namespace Walkers.Config;

/// <summary>
/// Server settings, saved as ModConfig/walkers.json. The class is partial so that each feature adds
/// its own section in its own file next to this one, for example WalkersConfig.Stalker.cs:
/// <code>
/// public partial class WalkersConfig { public StalkerConfig Stalker { get; set; } = new(); }
/// </code>
/// Feature branches then never edit the same file. Read it with <see cref="WalkersConfigExtensions.GetWalkersConfig"/>.
/// </summary>
public partial class WalkersConfig
{
    public const string FileName = "walkers.json";

    /// <summary>Raise when a change needs existing files migrated.</summary>
    public int ConfigVersion { get; set; } = 1;
}
