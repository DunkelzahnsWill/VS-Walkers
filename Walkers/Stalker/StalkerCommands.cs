using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Walkers.Commands;
using Walkers.Core;
using Walkers.Director;

namespace Walkers.Stalker;

/// <summary>
/// <c>/walkers stalker</c> (controlserver, from the shared root): <c>status [radius]</c>,
/// <c>stage &lt;1-5&gt; [radius]</c>, <c>victim &lt;player or entity id&gt; [radius]</c> and
/// <c>selftest</c>, plus one line on <c>/walkers status</c>. Without a radius a command applies to
/// every loaded walker body; from the console there is no position, so it always does.
/// </summary>
internal static class StalkerCommands
{
    private const int MaxListed = 10;

    public static void Register(ICoreServerAPI api, StalkerModSystem system, StalkerSelfTest selfTest)
    {
        var parsers = api.ChatCommands.Parsers;
        api.ChatCommands.GetOrCreate(WalkersModSystem.ModId)
            .BeginSubCommand("stalker")
                .WithDescription(Lang.Get("walkers:stalker-desc"))
                .BeginSubCommand("status")
                    .WithDescription(Lang.Get("walkers:stalker-status-desc"))
                    .WithExamples("/walkers stalker status", "/walkers stalker status 64")
                    .WithArgs(parsers.OptionalIntRange("radius", 1, 4096))
                    .HandleWith(WalkersCommandsSystem.SafeReply(args => OnStatus(api, system, args)))
                .EndSubCommand()
                .BeginSubCommand("stage")
                    .WithDescription(Lang.Get("walkers:stalker-stage-desc"))
                    .WithExamples("/walkers stalker stage 5", "/walkers stalker stage 2 32")
                    .WithArgs(parsers.IntRange("stage", 1, StalkerLadder.HuntStage), parsers.OptionalIntRange("radius", 1, 4096))
                    .HandleWith(WalkersCommandsSystem.SafeReply(args => OnStage(api, system, args)))
                .EndSubCommand()
                .BeginSubCommand("victim")
                    .WithDescription(Lang.Get("walkers:stalker-victim-desc"))
                    .WithExamples("/walkers stalker victim SomePlayer", "/walkers stalker victim 1234 16")
                    .WithArgs(parsers.Word("player or entity id"), parsers.OptionalIntRange("radius", 1, 4096))
                    .HandleWith(WalkersCommandsSystem.SafeReply(args => OnVictim(api, system, args)))
                .EndSubCommand()
                .BeginSubCommand("selftest")
                    .WithDescription(Lang.Get("walkers:stalker-selftest-desc"))
                    .HandleWith(WalkersCommandsSystem.SafeReply(args => TextCommandResult.Success(selfTest.Run())))
                .EndSubCommand()
            .EndSubCommand();
    }

    /// <summary>The stalker AI's line on /walkers status.</summary>
    public static void AddStatusLine(StalkerModSystem system, List<string> lines)
    {
        var brains = system.Brains.Where(b => b.Body.Alive).ToList();
        var modes = brains.GroupBy(b => WalkerBrain.ModeName(b.Mode)).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Count()} {g.Key}");
        string ladder = Lang.Get(system.Director != null ? "walkers:stalker-status-ladder-director" : "walkers:stalker-status-ladder-own");
        lines.Add(Lang.Get("walkers:stalker-status-line", brains.Count, brains.Count == 0 ? "-" : string.Join(", ", modes), ladder));
    }

    private static TextCommandResult OnStatus(ICoreServerAPI api, StalkerModSystem system, TextCommandCallingArgs args)
    {
        var selected = Select(system, args, 0).ToList();
        var lines = new List<string> { L(args, "stalker-status-header", selected.Count) };
        var from = args.Caller.Pos;
        foreach (var brain in selected.OrderBy(b => from == null ? 0 : b.Body.Pos.DistanceTo(from)).Take(MaxListed))
        {
            lines.AddRange(Describe(api, brain, args));
        }
        if (selected.Count > MaxListed)
        {
            lines.Add(L(args, "stalker-status-more", selected.Count - MaxListed));
        }
        return TextCommandResult.Success(string.Join("\n", lines));
    }

    private static TextCommandResult OnStage(ICoreServerAPI api, StalkerModSystem system, TextCommandCallingArgs args)
    {
        int stage = (int)args[0];
        int count = 0;
        foreach (var brain in Select(system, args, 1))
        {
            system.Ladder.ForceStage(brain.Memory, stage, api.World.Calendar.TotalHours);
            count++;
        }
        api.World.Logger.Audit("{0} set the stalker stage of {1} walker(s) to {2}", CallerName(args), count, stage);
        return TextCommandResult.Success(L(args, "stalker-stage-set", count, stage));
    }

    private static TextCommandResult OnVictim(ICoreServerAPI api, StalkerModSystem system, TextCommandCallingArgs args)
    {
        var who = (string)args[0];
        Entity? victim = null;
        if (long.TryParse(who, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
        {
            victim = api.World.GetEntityById(id);
        }
        victim ??= api.World.AllOnlinePlayers.FirstOrDefault(p => string.Equals(p.PlayerName, who, StringComparison.OrdinalIgnoreCase))?.Entity;
        if (victim == null || !victim.Alive)
        {
            return TextCommandResult.Error(L(args, "stalker-victim-unknown", who));
        }
        int count = 0, skipped = 0;
        foreach (var brain in Select(system, args, 1))
        {
            if (system.IsDirectorBody(brain.Body))
            {
                // The director picks its walkers' victims.
                skipped++;
                continue;
            }
            int stage = Math.Max(1, brain.Stage);
            if (victim is EntityPlayer player)
            {
                var ai = StalkerMemory.Ai(brain.Memory);
                ai.RemoveAttribute(StalkerMemory.OwnVictimEntity);
                ai.SetString(StalkerMemory.OwnVictim, player.PlayerUID);
                system.Ladder.ForceStage(brain.Memory, stage, api.World.Calendar.TotalHours);
            }
            else
            {
                system.SetStandIn(brain, victim, stage);
            }
            count++;
        }
        api.World.Logger.Audit("{0} set the victim of {1} walker(s) to {2} #{3}", CallerName(args), count, victim.Code, victim.EntityId);
        return TextCommandResult.Success(L(args, "stalker-victim-set", count, victim.GetName() ?? victim.Code.ToShortString(), victim.EntityId, skipped));
    }

    /// <summary>The brains of loaded bodies within the radius argument at <paramref name="radiusArg"/> of the caller, or all of them.</summary>
    private static IEnumerable<WalkerBrain> Select(StalkerModSystem system, TextCommandCallingArgs args, int radiusArg)
    {
        var from = args.Caller.Pos;
        int? radius = args.Parsers[radiusArg].IsMissing ? null : (int)args[radiusArg];
        return system.Brains.Where(b => b.Body.Alive && (radius == null || from == null || b.Body.Pos.DistanceTo(from) <= radius.Value)).ToList();
    }

    private static IEnumerable<string> Describe(ICoreServerAPI api, WalkerBrain brain, TextCommandCallingArgs args)
    {
        var body = brain.Body;
        var memory = brain.Memory;
        var ai = brain.Ai;
        var local = body.Pos.AsBlockPos.ToLocalPosition(api);
        string walker = memory.HasAttribute(WalkerMemory.Id) ? memory.GetInt(WalkerMemory.Id).ToString(CultureInfo.InvariantCulture) : "-";
        string victim = brain.Victim == null
            ? "-"
            : $"{brain.Victim.GetName() ?? brain.Victim.Code.ToShortString()} ({(int)brain.VictimDistance} m)";
        yield return L(args, "stalker-status-body", body.Code.Path, body.EntityId, local.X, local.Y, local.Z, walker, brain.Stage, WalkerBrain.ModeName(brain.Mode), victim);
        yield return L(args, "stalker-status-gaze",
            L(args, brain.Gaze.AnyWatched ? "stalker-yes" : "stalker-no"), brain.Gaze.Watchers,
            L(args, brain.Frozen ? "stalker-yes" : "stalker-no"),
            ((int)brain.WatchedSeconds).ToString(CultureInfo.InvariantCulture), brain.Looks, brain.SlipAwayReason ?? "-",
            ai.GetString(StalkerMemory.BreakOff, "") is { Length: > 0 } breakOff ? breakOff : "-");
        yield return L(args, "stalker-status-ladder",
            ai.GetDouble(StalkerMemory.StageHours).ToString("0.0", CultureInfo.InvariantCulture),
            ai.GetInt(StalkerMemory.StageNights), ai.GetInt(StalkerMemory.StageActs), ai.GetInt(StalkerMemory.Sightings),
            ai.GetInt(StalkerMemory.HuntNights),
            ai.GetDouble(StalkerMemory.CompanyHours).ToString("0.0", CultureInfo.InvariantCulture),
            L(args, ai.GetBool(StalkerMemory.Provoked) ? "stalker-yes" : "stalker-no"));
    }

    private static string CallerName(TextCommandCallingArgs args) => args.Caller.Player?.PlayerName ?? "The console";

    private static string L(TextCommandCallingArgs args, string key, params object[] values) =>
        Lang.GetL(args.LanguageCode ?? Lang.CurrentLocale, WalkersModSystem.ModId + ":" + key, values);
}
