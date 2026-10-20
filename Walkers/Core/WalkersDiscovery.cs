using Vintagestory.API.Server;

namespace Walkers.Core;

/// <summary>
/// The one way the mod sends an in-game discovery (the titles, whispers and banishing line).
/// </summary>
public static class WalkersDiscovery
{
    /// <summary>
    /// Sends the discovery text <c>game:ingamediscovery-{code}</c> to the player. Always use this
    /// instead of <see cref="ICoreServerAPI.SendIngameDiscovery"/>: a discovery sent with no lang
    /// parameters reaches the 1.22.7 client with a null parameter array, and the client's
    /// <c>Lang.Get(key, null)</c> then throws inside its own error handler and crashes the game
    /// (GeneralPacketHandler.HandleIngameDiscovery). One unused empty parameter avoids it.
    /// </summary>
    public static void SendWalkersDiscovery(this ICoreServerAPI api, IServerPlayer player, string code, params object[] langParams)
    {
        api.SendIngameDiscovery(player, code, null, langParams.Length > 0 ? langParams : new object[] { "" });
    }
}
