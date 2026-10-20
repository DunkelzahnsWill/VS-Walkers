using System.Collections.Generic;
using Walkers.Config;

namespace Walkers.Harassment.Acts;

/// <summary>
/// Whispers, stages 3 to 5, day and night: a short, garbled line from the lore bible in the title area
/// of the victim's screen, with a breath close by (victim only). Lines are drawn from the stage's pool;
/// W5 carries the victim's name and is left out for a player who chose noName; W6 is reserved for the
/// vanishing body (Forms sends it). A player who chose noTitles gets a breath in and out instead of
/// the text. Aimed at a stand-in, the line is only logged.
/// </summary>
public sealed class WhisperAct : HarassAct
{
    public const string ActCode = "whispers";

    /// <summary>The lore bible's whisper table: line number and the stages it may come in.</summary>
    private static readonly (int Line, int MinStage, int MaxStage)[] Lines =
    {
        (1, 3, 4), (2, 4, 5), (3, 3, 3), (4, 4, 5), (5, 4, 5), (7, 3, 3), (8, 3, 3),
        (9, 4, 5), (10, 3, 4), (11, 3, 4), (12, 3, 3), (13, 4, 5), (14, 3, 4),
    };

    /// <summary>The line that carries the player's name as {0}.</summary>
    public const int NameLine = 5;

    public override string Code => ActCode;

    public override int MinStage => 3;

    public override bool ByDay => true;

    public override bool Enabled(HarassConfig config) => config.Whispers.Enabled;

    public override int Cap(HarassConfig config) => config.Whispers.PerNight;

    /// <summary>The discovery code of a line; the client shows the game-domain lang key ingamediscovery-{code}.</summary>
    public static string CodeOf(int line) => $"walkers-whisper-{line:00}";

    /// <summary>The lines a stage may draw from, for a player with or without their name allowed.</summary>
    public static List<int> Pool(int stage, bool allowName)
    {
        var pool = new List<int>();
        foreach (var (line, min, max) in Lines)
        {
            if (stage >= min && stage <= max && (allowName || line != NameLine))
            {
                pool.Add(line);
            }
        }
        return pool;
    }

    public override ActResult Run(HarassContext ctx)
    {
        var prefs = ctx.Prefs;
        var pool = Pool(ctx.Stage, !prefs.NoName);
        if (pool.Count == 0)
        {
            return ActResult.Skip("no line for this stage");
        }
        return Say(ctx, pool[ctx.Rand.Next(pool.Count)]);
    }

    /// <summary>The tell's whisper on an enraged dusk (creature bible, section 7): iron and stone in the door frame.</summary>
    public const int IronLine = 15;

    /// <summary>One line to the victim alone, with the breath under it; the breath alone for noTitles.</summary>
    public static ActResult Say(HarassContext ctx, int line)
    {
        var prefs = ctx.Prefs;
        var player = ctx.Player;
        if (player == null)
        {
            return ActResult.Ok($"whisper {line:00} (stand-in: logged, not sent)");
        }
        if (prefs.NoTitles)
        {
            Breath(ctx);
            return ActResult.Ok("a breath in place of a whisper (noTitles)");
        }
        if (line == NameLine)
        {
            ctx.Api.SendIngameDiscovery(player, CodeOf(line), null, player.PlayerName);
        }
        else
        {
            ctx.Api.SendIngameDiscovery(player, CodeOf(line), null);
        }
        HarassSounds.For(player, HarassSounds.BreathOut, 0.62f + (float)ctx.Rand.NextDouble() * 0.06f, 0.6f);
        return ActResult.Ok($"whisper {line:00}");
    }

    /// <summary>The breath that replaces text for a noTitles player: in, then out a second later.</summary>
    public static void Breath(HarassContext ctx)
    {
        var player = ctx.Player;
        HarassSounds.For(player, HarassSounds.BreathIn, 0.65f, 0.8f);
        ctx.Api.Event.RegisterCallback(_ => HarassSounds.For(player, HarassSounds.BreathOut, 0.6f, 0.8f), 1000);
    }
}
