using Walkers.Config;
using Walkers.Director;
using Walkers.Stalker;

namespace Walkers.Harassment.Acts;

/// <summary>
/// Titles, once each per haunt, not drawn by the scheduler: "Something's moving out there." for the
/// victim when stage 2 begins, and "The Forest Saw That." for the first player who hurts the guise.
/// A player who chose noTitles gets the breath instead. Read from the walker's memory once a second,
/// so it needs no hook in the stalker AI or forms.
/// </summary>
public sealed class TitleAct
{
    public const string ActCode = "titles";

    public const string WatchingCode = "walkers-title-watching";
    public const string ForestSawCode = "walkers-title-forestsaw";

    public void Update(HarassContext ctx)
    {
        if (!ctx.Config.Titles.Enabled)
        {
            return;
        }
        var harass = ctx.Harass;
        if (ctx.Stage >= 2 && !harass.GetBool(HarassMemory.TitleWatching))
        {
            harass.SetBool(HarassMemory.TitleWatching, true);
            // A returning walker starts at stage 2, and the title opens that haunt too.
            Show(ctx, ctx.Player, WatchingCode);
        }

        // A player hurt the guise: the stalker AI keeps who and when. A hurt guise may stand up as the
        // true form within the same second, so a hit counts as one on the guise when the body was a
        // guise at the last look or is one now.
        long hitAt = ctx.Brain.Ai.GetLong(Stalker.StalkerMemory.LastPlayerHitMs, -1);
        bool wasGuise = ctx.State.WasGuise || !ctx.Brain.IsTrueForm;
        ctx.State.WasGuise = !ctx.Brain.IsTrueForm;
        if (hitAt <= ctx.State.SeenHitMs)
        {
            return;
        }
        ctx.State.SeenHitMs = hitAt;
        if (!wasGuise || ctx.Brain.MsSincePlayerHit > 5000 || harass.GetBool(HarassMemory.TitleForestSaw) ||
            ctx.Brain.LastAttacker is not Vintagestory.API.Common.EntityPlayer { Player: Vintagestory.API.Server.IServerPlayer hitter })
        {
            return;
        }
        harass.SetBool(HarassMemory.TitleForestSaw, true);
        if (hitter.GetWalkersPrefs().NoTitles)
        {
            HarassSounds.For(hitter, HarassSounds.BreathIn, 0.65f, 0.8f);
        }
        else
        {
            ctx.Api.SendIngameDiscovery(hitter, ForestSawCode, null);
        }
        ctx.System.Log(ctx, ActCode, $"\"The Forest Saw That.\" for {hitter.PlayerName}");
    }

    private static void Show(HarassContext ctx, Vintagestory.API.Server.IServerPlayer? player, string code)
    {
        if (player == null)
        {
            ctx.System.Log(ctx, ActCode, $"{code} (stand-in: logged, not sent)");
            return;
        }
        if (ctx.Prefs.NoTitles)
        {
            WhisperAct.Breath(ctx);
        }
        else
        {
            ctx.Api.SendIngameDiscovery(player, code, null);
        }
        ctx.System.Log(ctx, ActCode, code);
    }
}
