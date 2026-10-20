using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Walkers.Core;

/// <summary>
/// Startup and identity of the mod. Feature code does not go here: each feature adds its own
/// ModSystem in its own folder (README, "Working in parallel").
/// </summary>
public class WalkersModSystem : ModSystem
{
    /// <summary>The asset domain, and the prefix for every code the mod registers (AI tasks, behaviors, commands).</summary>
    public const string ModId = "walkers";

    public override void StartServerSide(ICoreServerAPI api)
    {
        // scripts/smoke-test.sh checks for this line (tests/smoke/core.expect).
        bool assetsLoaded = Lang.HasTranslation(ModId + ":mod-name", findWildcarded: false, logErrors: false);
        Mod.Logger.Notification("Server side started, version {0}, assets {1}", Mod.Info.Version, assetsLoaded ? "loaded" : "missing");
        if (!assetsLoaded)
        {
            Mod.Logger.Error("The {0} asset domain did not load. Check that assets/{0} is in the mod zip.", ModId);
        }
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        Mod.Logger.Notification("Client side started, version {0}", Mod.Info.Version);
    }
}
