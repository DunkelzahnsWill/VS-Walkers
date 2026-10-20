using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Walkers.Commands;

namespace Walkers.Director;

// /walkers director: the admin commands that skip the waiting (creature bible, section 9, "Commands"
// and "Testing without a client"). They hang off the shared /walkers root and its controlserver
// privilege; replies go through Lang and WalkersCommandsSystem.SafeReply.
public partial class DirectorSystem
{
    private const int MaxListedLedgerEntries = 20;

    private void RegisterCommands()
    {
        var parsers = sapi.ChatCommands.Parsers;
        sapi.ChatCommands.GetOrCreate(Core.WalkersModSystem.ModId)
            .BeginSubCommand("director")
                .WithDescription(Lang.Get("walkers:director-cmd-desc"))
                .HandleWith(WalkersCommandsSystem.SafeReply(OnStatusCommand))
                .BeginSubCommand("status")
                    .WithDescription(Lang.Get("walkers:director-cmd-status-desc"))
                    .WithExamples("/walkers director status", "/walkers director status 1", "/walkers director status SomePlayer")
                    .WithArgs(parsers.OptionalWord("walker or player"))
                    .HandleWith(WalkersCommandsSystem.SafeReply(OnStatusCommand))
                .EndSubCommand()
                .BeginSubCommand("haunt")
                    .WithDescription(Lang.Get("walkers:director-cmd-haunt-desc"))
                    .WithExamples("/walkers director haunt SomePlayer", "/walkers director haunt SomePlayer 3", "/walkers director haunt standin-1")
                    .WithArgs(parsers.Word("player"), parsers.OptionalIntRange("stage", 1, 5))
                    .HandleWith(WalkersCommandsSystem.SafeReply(OnHauntCommand))
                .EndSubCommand()
                .BeginSubCommand("end")
                    .WithDescription(Lang.Get("walkers:director-cmd-end-desc"))
                    .WithExamples("/walkers director end 1", "/walkers director end SomePlayer ledger", "/walkers director end 1 banish")
                    .WithArgs(parsers.Word("walker or player"), parsers.OptionalWordRange("how", "rest", "ledger", "banish"))
                    .HandleWith(WalkersCommandsSystem.SafeReply(OnEndCommand))
                .EndSubCommand()
                .BeginSubCommand("return")
                    .WithDescription(Lang.Get("walkers:director-cmd-return-desc"))
                    .WithExamples("/walkers director return 1", "/walkers director return 1 now")
                    .WithArgs(parsers.Int("walker"), parsers.OptionalWordRange("when", "now"))
                    .HandleWith(WalkersCommandsSystem.SafeReply(OnReturnCommand))
                .EndSubCommand()
                .BeginSubCommand("ledger")
                    .WithDescription(Lang.Get("walkers:director-cmd-ledger-desc"))
                    .HandleWith(WalkersCommandsSystem.SafeReply(OnLedgerCommand))
                .EndSubCommand()
                .BeginSubCommand("tick")
                    .WithDescription(Lang.Get("walkers:director-cmd-tick-desc"))
                    .HandleWith(WalkersCommandsSystem.SafeReply(OnTickCommand))
                .EndSubCommand()
                .BeginSubCommand("standin")
                    .WithDescription(Lang.Get("walkers:director-cmd-standin-desc"))
                    .HandleWith(WalkersCommandsSystem.SafeReply(OnStandInListCommand))
                    .BeginSubCommand("add")
                        .WithDescription(Lang.Get("walkers:director-cmd-standin-add-desc"))
                        .WithExamples("/walkers director standin add", "/walkers director standin add ~10 ~ ~5")
                        .WithArgs(parsers.OptionalWorldPosition("position"))
                        .HandleWith(WalkersCommandsSystem.SafeReply(OnStandInAddCommand))
                    .EndSubCommand()
                    .BeginSubCommand("remove")
                        .WithDescription(Lang.Get("walkers:director-cmd-standin-remove-desc"))
                        .WithArgs(parsers.Word("stand-in"))
                        .HandleWith(WalkersCommandsSystem.SafeReply(OnStandInRemoveCommand))
                    .EndSubCommand()
                    .BeginSubCommand("list")
                        .WithDescription(Lang.Get("walkers:director-cmd-standin-list-desc"))
                        .HandleWith(WalkersCommandsSystem.SafeReply(OnStandInListCommand))
                    .EndSubCommand()
                .EndSubCommand()
            .EndSubCommand();
    }

    // ---- status ----

    private TextCommandResult OnStatusCommand(TextCommandCallingArgs args)
    {
        UpdateCandidates();
        var target = args.Parsers.Count > 0 && !args.Parsers[0].IsMissing ? (string)args[0] : null;
        var lines = new List<string>();
        if (target == null)
        {
            lines.Add(L(args, "director-status-header", DescribeStates(), ActiveCount(), ActiveCap(), data.Ledger.Count, data.Restore.Count,
                ladder?.GetType().Name ?? L(args, "director-none")));
            foreach (var walker in walkers.Values.OrderBy(w => w.Id))
            {
                lines.Add("  " + DescribeWalker(args, walker));
            }
            foreach (var candidate in candidates.Values.OrderBy(c => c.Name))
            {
                lines.Add("  " + DescribeCandidate(args, candidate));
            }
            return TextCommandResult.Success(string.Join("\n", lines));
        }

        if (int.TryParse(target, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            if (!walkers.TryGetValue(id, out var walker))
            {
                return TextCommandResult.Error(L(args, "director-no-walker", id));
            }
            lines.Add(DescribeWalker(args, walker));
            var memory = memories[id];
            var subtrees = string.Join(", ", new[] { WalkerMemory.Ai, WalkerMemory.Harass, WalkerMemory.Form }.Where(memory.HasAttribute));
            lines.Add(L(args, "director-status-memory", memory.GetFloat(WalkerMemory.Pace, 1), memory.GetInt(WalkerMemory.Returns),
                subtrees.Length == 0 ? L(args, "director-none") : subtrees));
            if (LatestEntry(id) is { } entry)
            {
                lines.Add(DescribeLedgerEntry(args, entry));
            }
            return TextCommandResult.Success(string.Join("\n", lines));
        }

        var uid = ResolveUid(target);
        if (uid == null)
        {
            return TextCommandResult.Error(L(args, "director-no-player", target));
        }
        if (candidates.TryGetValue(uid, out var found))
        {
            lines.Add(DescribeCandidate(args, found));
        }
        if (playerRecords.TryGetValue(uid, out var record))
        {
            double now = sapi.World.Calendar.TotalDays;
            lines.Add(L(args, "director-status-record", NameOf(uid), Day(record.FirstSeenTotalDays), Day(record.RestUntilTotalDays),
                Math.Max(0, record.RestUntilTotalDays - now), record.MarkedByWalker));
        }
        foreach (var walker in walkers.Values.Where(w => w.VictimUid == uid))
        {
            lines.Add(DescribeWalker(args, walker));
        }
        if (lines.Count == 0)
        {
            lines.Add(L(args, "director-status-unknown-player", NameOf(uid)));
        }
        return TextCommandResult.Success(string.Join("\n", lines));
    }

    private string DescribeWalker(TextCommandCallingArgs args, WalkerRecord walker)
    {
        var stage = memories[walker.Id].GetInt(WalkerMemory.Stage);
        var victim = walker.VictimUid == null ? "" : NameOf(walker.VictimUid);
        var text = walker.State switch
        {
            WalkerState.Haunting => L(args, "director-walker-haunting", walker.Id, victim, stage, walker.HauntClockHours / sapi.World.Calendar.HoursPerDay,
                config.Director.HauntMaxDays, walker.GuiseType ?? "?", walker.Returns),
            WalkerState.Paused => L(args, "director-walker-paused", walker.Id, victim, stage, pauseReasons.GetValueOrDefault(walker.Id, "?"),
                sapi.World.Calendar.TotalDays - walker.PausedSinceTotalDays, config.Director.PausedHauntMaxDays),
            WalkerState.Down => L(args, "director-walker-down", walker.Id),
            WalkerState.Waiting => L(args, "director-walker-waiting", walker.Id, Day(LatestEntry(walker.Id)?.ReturnTotalDays ?? 0),
                Math.Max(0, (LatestEntry(walker.Id)?.ReturnTotalDays ?? 0) - sapi.World.Calendar.TotalDays)),
            WalkerState.Banished => L(args, "director-walker-banished", walker.Id),
            _ => L(args, walker.ReturnPending ? "director-walker-resting-return" : "director-walker-resting", walker.Id, walker.Returns),
        };
        if (BodyOf(walker) is { } body)
        {
            var local = body.Pos.AsBlockPos.ToLocalPosition(sapi);
            text += L(args, "director-walker-body", body.Code.Path, body.EntityId, local.X, local.Y, local.Z, body.Alive ? "" : L(args, "director-dead"));
            if (walker.VictimUid != null && VictimEntity(walker.VictimUid) is { } victimEntity && victimEntity.Pos.Dimension == body.Pos.Dimension)
            {
                text += L(args, "director-walker-distance", (int)victimEntity.Pos.DistanceTo(body.Pos.XYZ));
            }
        }
        return text;
    }

    private string DescribeCandidate(TextCommandCallingArgs args, Candidate c)
    {
        var facts = new List<string>();
        if (c.IsStandIn) facts.Add(L(args, "director-fact-standin"));
        facts.Add(L(args, c.Eligible ? "director-fact-eligible" : "director-fact-not-eligible"));
        if (c.Surface) facts.Add(L(args, "director-fact-surface"));
        if (c.NearForest) facts.Add(L(args, "director-fact-forest"));
        if (c.Alone) facts.Add(L(args, "director-fact-alone"));
        if (c.Night) facts.Add(L(args, "director-fact-night"));
        if (c.Asleep) facts.Add(L(args, "director-fact-asleep"));
        return L(args, "director-candidate", c.Name, string.Join(", ", facts));
    }

    // ---- haunt ----

    private TextCommandResult OnHauntCommand(TextCommandCallingArgs args)
    {
        var target = (string)args[0];
        int? stage = args.Parsers[1].IsMissing ? null : (int)args[1];
        UpdateCandidates();
        var uid = ResolveUid(target);
        if (uid == null || !candidates.TryGetValue(uid, out var victim))
        {
            return TextCommandResult.Error(L(args, "director-not-online", target));
        }
        if (IsStandInUid(uid) && !config.Debug.StandInVictims)
        {
            return TextCommandResult.Error(L(args, "director-standins-off"));
        }

        var current = walkers.Values.FirstOrDefault(w => w.VictimUid == uid && w.State is WalkerState.Haunting or WalkerState.Paused);
        if (current != null)
        {
            if (stage == null)
            {
                return TextCommandResult.Success(L(args, "director-haunt-already", current.Id, victim.Name, memories[current.Id].GetInt(WalkerMemory.Stage)));
            }
            SetStage(current, stage.Value);
            Audit(args, "set walker {0}'s stage on {1} to {2}", current.Id, victim.Name, stage.Value);
            return TextCommandResult.Success(L(args, "director-haunt-stage", current.Id, victim.Name, stage.Value));
        }

        var walker = walkers.Values.Where(w => w.State == WalkerState.Resting).OrderBy(w => w.Id).FirstOrDefault() ?? CreateWalker();
        StartHaunt(walker, victim, walker.ReturnPending, stage);
        Audit(args, "started walker {0}'s haunt on {1}", walker.Id, victim.Name);
        return TextCommandResult.Success(L(args, "director-haunt-started", walker.Id, victim.Name, memories[walker.Id].GetInt(WalkerMemory.Stage), walker.GuiseType ?? "?"));
    }

    /// <summary>Moves a haunting walker to a stage, as an admin asks: the ladder starts that stage afresh.</summary>
    private void SetStage(WalkerRecord walker, int stage)
    {
        var memory = memories[walker.Id];
        memory.SetInt(WalkerMemory.Stage, stage);
        try
        {
            ladder?.Begin(memory, stage, sapi.World.Calendar.TotalHours);
        }
        catch (Exception e)
        {
            Mod.Logger.Error("The ladder failed to begin stage {0} for walker {1}: {2}", stage, walker.Id, e);
        }
        CheckStage(walker);
    }

    // ---- end ----

    private TextCommandResult OnEndCommand(TextCommandCallingArgs args)
    {
        var target = (string)args[0];
        var how = args.Parsers[1].IsMissing ? "rest" : (string)args[1];
        UpdateCandidates();
        var walker = ResolveWalker(target);
        if (walker == null)
        {
            return TextCommandResult.Error(L(args, "director-no-walker-for", target));
        }
        var body = BodyOf(walker);
        switch (how)
        {
            case "banish":
                if (walker.State == WalkerState.Banished)
                {
                    return TextCommandResult.Error(L(args, "director-already-banished", walker.Id));
                }
                Banish(walker.Id, args.Caller.Player, AdminCause);
                break;
            case "ledger":
                if (walker.State is WalkerState.Waiting or WalkerState.Banished)
                {
                    return TextCommandResult.Error(L(args, "director-already-ledger", walker.Id));
                }
                if (walker.State != WalkerState.Down)
                {
                    GoDown(walker, body?.Pos, AdminCause, CallerName(args));
                }
                ReleaseCore(walker, WalkerRelease.LeftAlone, false);
                break;
            default:
                if (walker.State is not (WalkerState.Haunting or WalkerState.Paused))
                {
                    return TextCommandResult.Error(L(args, "director-not-haunting", walker.Id));
                }
                EndHauntCore(walker, HauntEndReason.Command);
                break;
        }
        // An admin's removal does not wait until nobody looks.
        if (body != null && body.Alive && walker.State != WalkerState.Haunting)
        {
            RemoveBody(walker, body);
        }
        Audit(args, "ended walker {0} ({1})", walker.Id, how);
        return TextCommandResult.Success(L(args, "director-ended-" + how, walker.Id));
    }

    private const string AdminCause = "removed by an admin";

    // ---- return ----

    private TextCommandResult OnReturnCommand(TextCommandCallingArgs args)
    {
        int id = (int)args[0];
        bool now = !args.Parsers[1].IsMissing;
        if (!walkers.TryGetValue(id, out var walker))
        {
            return TextCommandResult.Error(L(args, "director-no-walker", id));
        }
        var entry = LatestEntry(id);
        if (walker.State != WalkerState.Waiting || entry == null)
        {
            return TextCommandResult.Error(L(args, "director-not-waiting", id));
        }
        double today = sapi.World.Calendar.TotalDays;
        if (!now)
        {
            return TextCommandResult.Success(L(args, "director-return-when", id, Day(entry.ReturnTotalDays), Math.Max(0, entry.ReturnTotalDays - today),
                entry.VictimUid == null ? L(args, "director-none") : NameOf(entry.VictimUid)));
        }
        entry.ReturnTotalDays = Math.Min(entry.ReturnTotalDays, today);
        Audit(args, "brought walker {0}'s return forward to now", id);
        returnNow = id;
        try
        {
            Tick();
        }
        finally
        {
            returnNow = 0;
        }
        return walker.State == WalkerState.Haunting
            ? TextCommandResult.Success(L(args, "director-returned", id, NameOf(walker.VictimUid!), memories[id].GetInt(WalkerMemory.Stage)))
            : TextCommandResult.Success(L(args, "director-return-waits", id, entry.VictimUid == null ? L(args, "director-none") : NameOf(entry.VictimUid)));
    }

    // ---- ledger ----

    private TextCommandResult OnLedgerCommand(TextCommandCallingArgs args)
    {
        if (data.Ledger.Count == 0)
        {
            return TextCommandResult.Success(L(args, "director-ledger-empty"));
        }
        var lines = new List<string> { L(args, "director-ledger-header", data.Ledger.Count) };
        foreach (var entry in data.Ledger.Skip(Math.Max(0, data.Ledger.Count - MaxListedLedgerEntries)))
        {
            lines.Add("  " + DescribeLedgerEntry(args, entry));
        }
        return TextCommandResult.Success(string.Join("\n", lines));
    }

    private string DescribeLedgerEntry(TextCommandCallingArgs args, LedgerRecord e)
    {
        var local = new BlockPos(e.X, e.Y, e.Z, e.Dimension).ToLocalPosition(sapi);
        var outcome = e.Outcome switch
        {
            LedgerOutcome.Released => L(args, "director-outcome-released", e.Release?.ToString() ?? "?", e.FursTaken ? L(args, "director-furs-taken") : "", Day(e.ReturnTotalDays)),
            LedgerOutcome.Banished => L(args, "director-outcome-banished"),
            LedgerOutcome.Returned => L(args, "director-outcome-returned"),
            _ => L(args, "director-outcome-pending", Day(e.ReturnTotalDays)),
        };
        return L(args, "director-ledger-entry", e.WalkerId, Day(e.DiedTotalDays), local.X, local.Y, local.Z, e.Cause,
            e.KillerName == null ? "" : L(args, "director-by", e.KillerName), e.VictimName ?? L(args, "director-none"), outcome);
    }

    // ---- tick ----

    private TextCommandResult OnTickCommand(TextCommandCallingArgs args)
    {
        Tick();
        return TextCommandResult.Success(L(args, "director-ticked", DescribeStates(), ActiveCount(), ActiveCap()));
    }

    // ---- stand-ins ----

    private TextCommandResult OnStandInAddCommand(TextCommandCallingArgs args)
    {
        if (!config.Debug.StandInVictims)
        {
            return TextCommandResult.Error(L(args, "director-standins-off"));
        }
        Vec3d? at = args.Parsers[0].IsMissing ? null : (Vec3d)args[0];
        BlockPos start;
        if (at != null)
        {
            start = at.AsBlockPos;
        }
        else if (args.Caller.Pos != null)
        {
            start = args.Caller.Pos.AsBlockPos;
        }
        else
        {
            // From the console: the world spawn, on the ground.
            start = sapi.World.DefaultSpawnPosition.AsBlockPos;
            start.Y = sapi.World.BlockAccessor.GetTerrainMapheightAt(start) + 1;
        }
        if (sapi.World.BlockAccessor.GetChunkAtBlockPos(start) == null)
        {
            return TextCommandResult.Error(L(args, "director-standin-not-loaded"));
        }
        var spot = FindStandingSpot(start);
        if (spot == null)
        {
            return TextCommandResult.Error(L(args, "director-standin-no-spot"));
        }
        var uid = AddStandIn(spot, out var error);
        if (uid == null)
        {
            return TextCommandResult.Error(L(args, "director-standin-failed", error ?? ""));
        }
        var local = spot.AsBlockPos.ToLocalPosition(sapi);
        Audit(args, "added stand-in victim {0}", uid);
        return TextCommandResult.Success(L(args, "director-standin-added", uid, local.X, local.Y, local.Z));
    }

    private TextCommandResult OnStandInRemoveCommand(TextCommandCallingArgs args)
    {
        var uid = (string)args[0];
        if (!RemoveStandIn(uid))
        {
            return TextCommandResult.Error(L(args, "director-no-standin", uid));
        }
        Audit(args, "removed stand-in victim {0}", uid);
        return TextCommandResult.Success(L(args, "director-standin-removed", uid));
    }

    private TextCommandResult OnStandInListCommand(TextCommandCallingArgs args)
    {
        if (standIns.Count == 0)
        {
            return TextCommandResult.Success(L(args, config.Debug.StandInVictims ? "director-standin-none" : "director-standins-off"));
        }
        var lines = new List<string> { L(args, "director-standin-header", standIns.Count) };
        foreach (var (uid, record) in standIns.OrderBy(s => s.Key))
        {
            var local = new BlockPos((int)record.X, (int)record.Y, (int)record.Z).ToLocalPosition(sapi);
            bool loaded = sapi.World.GetEntityById(record.EntityId) is { Alive: true };
            lines.Add("  " + L(args, loaded ? "director-standin-entry" : "director-standin-entry-unloaded", uid, local.X, local.Y, local.Z));
        }
        return TextCommandResult.Success(string.Join("\n", lines));
    }

    /// <summary>Where a creature can stand in the column of <paramref name="start"/>, searching up and down from it.</summary>
    private Vec3d? FindStandingSpot(BlockPos start)
    {
        var accessor = sapi.World.BlockAccessor;
        for (int offset = 0; offset <= 16; offset++)
        {
            foreach (int dy in offset == 0 ? new[] { 0 } : new[] { offset, -offset })
            {
                var pos = start.AddCopy(0, dy, 0);
                if (pos.Y < 1 || accessor.GetChunkAtBlockPos(pos) == null)
                {
                    continue;
                }
                if (StandingHeight(accessor, pos) is { } y)
                {
                    return new Vec3d(pos.X + 0.5, y, pos.Z + 0.5);
                }
            }
        }
        return null;
    }

    // ---- Helpers ----

    /// <summary>A uid for a stand-in uid, an online player's name or a known player's name; null when there is none.</summary>
    private string? ResolveUid(string target)
    {
        if (standIns.ContainsKey(target))
        {
            return target;
        }
        var online = sapi.World.AllOnlinePlayers.FirstOrDefault(p => string.Equals(p.PlayerName, target, StringComparison.OrdinalIgnoreCase));
        if (online != null)
        {
            return online.PlayerUID;
        }
        var known = playerRecords.Values.FirstOrDefault(r => string.Equals(r.Name, target, StringComparison.OrdinalIgnoreCase));
        return known?.Uid ?? sapi.PlayerData.GetPlayerDataByLastKnownName(target)?.PlayerUID;
    }

    /// <summary>A walker by id, or the one haunting (or last down for) a player or stand-in.</summary>
    private WalkerRecord? ResolveWalker(string target)
    {
        if (int.TryParse(target, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            return walkers.GetValueOrDefault(id);
        }
        var uid = ResolveUid(target);
        return uid == null ? null : walkers.Values.FirstOrDefault(w => w.VictimUid == uid && w.State is WalkerState.Haunting or WalkerState.Paused or WalkerState.Down);
    }

    private static string Day(double totalDays) => totalDays.ToString("0.#", CultureInfo.InvariantCulture);

    private static string CallerName(TextCommandCallingArgs args) => args.Caller.Player?.PlayerName ?? "the console";

    private void Audit(TextCommandCallingArgs args, string what, params object[] values) =>
        Mod.Logger.Audit("{0} {1}", CallerName(args), string.Format(CultureInfo.InvariantCulture, what, values));

    private static string L(TextCommandCallingArgs args, string key, params object[] values) =>
        Lang.GetL(args.LanguageCode ?? Lang.CurrentLocale, Core.WalkersModSystem.ModId + ":" + key, values);
}
