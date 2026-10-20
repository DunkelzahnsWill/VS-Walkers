using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Walkers.Commands;
using Walkers.Core;
using Walkers.Stalker;

namespace Walkers.Harassment;

/// <summary>
/// <c>/walkers harass</c> (controlserver, from the shared root): <c>status [radius]</c> shows each
/// haunting walker's budget, tonight's counts and the last five acts against its victim;
/// <c>act &lt;code&gt; [radius]</c> runs one act now under its own rules (switch, stage, time, cap);
/// <c>selftest</c> checks every act on a headless server. Plus one line on <c>/walkers status</c>.
/// </summary>
internal static class HarassCommands
{
    private const int MaxListed = 10;

    public static void Register(ICoreServerAPI api, HarassModSystem system)
    {
        var parsers = api.ChatCommands.Parsers;
        var selfTest = new HarassSelfTest(api, system);
        var codes = system.Acts.Select(a => a.Code).ToArray();
        api.ChatCommands.GetOrCreate(WalkersModSystem.ModId)
            .BeginSubCommand("harass")
                .WithDescription(Lang.Get("walkers:harass-desc"))
                .BeginSubCommand("status")
                    .WithDescription(Lang.Get("walkers:harass-status-desc"))
                    .WithArgs(parsers.OptionalIntRange("radius", 1, 4096))
                    .HandleWith(WalkersCommandsSystem.SafeReply(args => OnStatus(api, system, args)))
                .EndSubCommand()
                .BeginSubCommand("act")
                    .WithDescription(Lang.Get("walkers:harass-act-desc"))
                    .WithExamples("/walkers harass act whispers", "/walkers harass act doors 32")
                    .WithArgs(parsers.WordRange("act", codes), parsers.OptionalIntRange("radius", 1, 4096))
                    .HandleWith(WalkersCommandsSystem.SafeReply(args => OnAct(system, args)))
                .EndSubCommand()
                .BeginSubCommand("selftest")
                    .WithDescription(Lang.Get("walkers:harass-selftest-desc"))
                    .HandleWith(WalkersCommandsSystem.SafeReply(args => TextCommandResult.Success(selfTest.Run())))
                .EndSubCommand()
            .EndSubCommand();
    }

    public static void AddStatusLine(HarassModSystem system, List<string> lines)
    {
        var haunting = system.Stalker.Brains.Select(system.ContextOf).Where(c => c != null).ToList();
        int tonight = haunting.Sum(c => c!.Harass.GetInt(HarassMemory.Spent));
        lines.Add(Lang.Get("walkers:harass-status-line", haunting.Count, tonight, system.Restore.LocalCount));
    }

    private static TextCommandResult OnStatus(ICoreServerAPI api, HarassModSystem system, TextCommandCallingArgs args)
    {
        var contexts = Select(system, args, 0);
        var lines = new List<string> { L(args, "harass-status-header", contexts.Count) };
        long now = api.World.ElapsedMilliseconds;
        foreach (var ctx in contexts.Take(MaxListed))
        {
            int budget = ctx.Config.ActsPerNight.For(ctx.Stage);
            var counts = system.Acts.Select(a => a.Code).Append(Acts.OfferingAct.ActCode)
                .Select(code => (code, n: HarassMemory.Count(ctx.Harass, code)))
                .Where(c => c.n > 0)
                .Select(c => $"{c.code} {c.n}");
            long nextS = System.Math.Max(0, (ctx.State.NextActMs - now) / 1000);
            lines.Add(L(args, "harass-status-walker", ctx.Body.Code.ToShortString(), ctx.Body.EntityId, system.VictimKey(ctx), ctx.Stage,
                ctx.Night ? L(args, "harass-night") : L(args, "harass-day"), ctx.Harass.GetInt(HarassMemory.Spent), budget, nextS,
                system.Blocked(ctx) ?? "-"));
            lines.Add(L(args, "harass-status-counts", string.Join(", ", counts.DefaultIfEmpty("-"))));
            foreach (var recent in ctx.State.Recent)
            {
                lines.Add("    " + recent);
            }
        }
        return TextCommandResult.Success(string.Join("\n", lines));
    }

    private static TextCommandResult OnAct(HarassModSystem system, TextCommandCallingArgs args)
    {
        string code = (string)args[0];
        var act = system.Acts.First(a => a.Code == code);
        var contexts = Select(system, args, 1);
        if (contexts.Count == 0)
        {
            return TextCommandResult.Success(L(args, "harass-act-none"));
        }
        var lines = new List<string>();
        foreach (var ctx in contexts)
        {
            var result = system.RunNow(ctx, act);
            lines.Add(L(args, result.Done ? "harass-act-done" : "harass-act-skipped", code, ctx.Body.EntityId, result.Detail));
        }
        return TextCommandResult.Success(string.Join("\n", lines));
    }

    /// <summary>Contexts of haunting walkers within the radius argument of the caller, or all of them.</summary>
    private static List<HarassContext> Select(HarassModSystem system, TextCommandCallingArgs args, int radiusArg)
    {
        var from = args.Caller.Pos;
        int? radius = args.Parsers[radiusArg].IsMissing ? null : (int)args[radiusArg];
        return system.Stalker.Brains
            .Where(b => radius == null || from == null || b.Body.Pos.DistanceTo(from) <= radius.Value)
            .Select(system.ContextOf)
            .Where(c => c != null)
            .Select(c => c!)
            .ToList();
    }

    private static string L(TextCommandCallingArgs args, string key, params object[] values) =>
        Lang.GetL(args.LanguageCode ?? Lang.CurrentLocale, WalkersModSystem.ModId + ":" + key, values);
}
