using System;
using Vintagestory.API.Common;

namespace Walkers.Config;

/// <summary>
/// Loads <see cref="WalkersConfig"/> before any other Walkers system starts, so every system can read
/// it from Start onward. Server side only for now: on a client, Config holds the defaults until the
/// config and commands work syncs the server's values.
/// </summary>
public class WalkersConfigSystem : ModSystem
{
    public WalkersConfig Config { get; private set; } = new();

    // Lower runs first; the game's default is 0.1.
    public override double ExecuteOrder() => 0.01;

    public override void StartPre(ICoreAPI api)
    {
        if (api.Side == EnumAppSide.Server)
        {
            Config = Load(api);
        }
    }

    private WalkersConfig Load(ICoreAPI api)
    {
        WalkersConfig? loaded;
        try
        {
            loaded = api.LoadModConfig<WalkersConfig>(WalkersConfig.FileName);
        }
        catch (Exception e)
        {
            // Leave the broken file for the admin to fix and run on defaults meanwhile.
            Mod.Logger.Error("Could not read ModConfig/{0}, using defaults: {1}", WalkersConfig.FileName, e.Message);
            return new WalkersConfig();
        }

        loaded ??= new WalkersConfig();
        // Write it back so settings added since the file was made appear with their defaults.
        api.StoreModConfig(loaded, WalkersConfig.FileName);
        return loaded;
    }
}

public static class WalkersConfigExtensions
{
    /// <summary>The loaded config. Look it up once in your system's Start, not every tick.</summary>
    public static WalkersConfig GetWalkersConfig(this ICoreAPI api) =>
        api.ModLoader.GetModSystem<WalkersConfigSystem>().Config;
}
