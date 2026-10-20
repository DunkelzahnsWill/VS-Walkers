using System;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Walkers.Harassment.Acts;

/// <summary>
/// Screen shake, with close knocks and scrapes (0.05 to 0.15) and the reveal (0.3): the vanilla
/// "screenshake" channel the eidolon uses, sent to the victim alone. A vanilla internal (creature
/// bible, flag 9), so a failure is logged once and the shake switches itself off until a restart.
/// Never for a player who chose noScreenEffects.
/// </summary>
public static class ScreenShake
{
    private static bool broken;

    public static void Send(HarassContext ctx, float strength)
    {
        if (!ctx.Config.ScreenShake.Enabled || ctx.Prefs.NoScreenEffects)
        {
            return;
        }
        if (ctx.Player == null)
        {
            ctx.System.Log(ctx, "screenShake", $"shake {strength:0.00} (stand-in: logged, not sent)");
            return;
        }
        Send(ctx.Api, ctx.Player, strength, ctx.System);
    }

    public static void Send(ICoreServerAPI api, IServerPlayer player, float strength, HarassModSystem system)
    {
        if (broken)
        {
            return;
        }
        try
        {
            if (api.Network.GetChannel("screenshake") is IServerNetworkChannel channel)
            {
                channel.SendPacket(new ScreenshakePacket { Strength = strength }, player);
            }
        }
        catch (Exception e)
        {
            broken = true;
            system.Mod.Logger.Warning("Harassment: the vanilla screenshake channel failed, so the shake is off until a restart: {0}", e.Message);
        }
    }
}
