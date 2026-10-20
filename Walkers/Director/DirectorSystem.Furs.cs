using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Walkers.Commands;

namespace Walkers.Director;

// Unnatural furs (creature bible, section 7): whoever carries a walker's furs in their own inventories
// is marked as its target. The mark takes after furs.markThresholdHours of carrying while online,
// eligible and not idle, grows to furs.markCapHours and fades by furs.decayPerHour once they stop.
// A due return goes to the most-marked carrier, and a resting walker's marked player rolls more often.
// Also the escalation level (section 7, the escalation list), which the harassment kit reads.
public partial class DirectorSystem
{
    private const string FurCode = "walkerfur";

    /// <summary>The top tier of the escalation list.</summary>
    private const int MaxEscalation = 3;

    /// <summary>A player's own inventories that count: hotbar, backpack with its bags, crafting grid and cursor. Never a chest.</summary>
    private static readonly string[] FurInventories =
    {
        GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName, GlobalConstants.craftingInvClassName, GlobalConstants.mousecursorInvClassName,
    };

    // ---- Marks ----

    /// <summary>Some walker that is not banished has furs out: only then are inventories scanned.</summary>
    private bool FursOut() =>
        data.Ledger.Any(e => e.FursTaken && walkers.TryGetValue(e.WalkerId, out var w) && w.State != WalkerState.Banished);

    /// <summary>On each director tick: scans online players for furs, and grows or fades their marks.</summary>
    private void UpdateFurs()
    {
        double now = sapi.World.Calendar.TotalHours;
        // A long gap (a frozen calendar, a restart) never counts for more than two days.
        double dt = data.FursUpdatedTotalHours <= 0 ? 0 : Math.Clamp(now - data.FursUpdatedTotalHours, 0, 48);
        data.FursUpdatedTotalHours = now;
        if (!config.Furs.MarkTarget)
        {
            return;
        }
        bool scan = FursOut();
        if (!scan && !playerRecords.Values.Any(r => r.FurMarks.Count > 0))
        {
            return;
        }
        // Offline players keep their marks as they are: they neither carry nor put the furs down.
        foreach (var candidate in candidates.Values.Where(c => c.Player != null))
        {
            var carried = scan ? CarriedFurs(candidate.Player!) : new HashSet<int>();
            var record = playerRecords[candidate.Uid];
            foreach (int walkerId in carried.Where(id => record.FurMarks.All(m => m.WalkerId != id)))
            {
                record.FurMarks.Add(new FurMark { WalkerId = walkerId });
            }
            foreach (var mark in record.FurMarks.ToList())
            {
                UpdateMark(candidate, record, mark, carried.Contains(mark.WalkerId), now, dt);
            }
        }
    }

    private void UpdateMark(Candidate carrier, PlayerRecord record, FurMark mark, bool carrying, double now, double dt)
    {
        var furs = config.Furs;
        if (!carrying)
        {
            mark.Carrying = false;
            mark.Hours -= furs.DecayPerHour * dt;
            if (mark.Hours <= 0)
            {
                record.FurMarks.Remove(mark);
            }
            else if (mark.Hours < furs.MarkThresholdHours)
            {
                mark.ToldMarked = false;
            }
            return;
        }

        var walker = walkers[mark.WalkerId];
        if (!mark.Carrying)
        {
            // Picked up. Furs someone else carried before do not count for a while.
            if (walker.FurCarriers.Any(uid => uid != carrier.Uid))
            {
                mark.GraceUntilTotalHours = Math.Max(mark.GraceUntilTotalHours, now + furs.ForeignGraceHours);
            }
            if (!walker.FurCarriers.Contains(carrier.Uid))
            {
                walker.FurCarriers.Add(carrier.Uid);
            }
            if (!mark.ToldHeld)
            {
                mark.ToldHeld = true;
                Tell(carrier.Player!, "walkers-fur-held");
            }
        }
        mark.Carrying = true;
        if (carrier.Eligible && now >= mark.GraceUntilTotalHours)
        {
            mark.Hours = Math.Min(furs.MarkCapHours, mark.Hours + dt);
        }
        if (IsMarked(mark) && !mark.ToldMarked)
        {
            mark.ToldMarked = true;
            Tell(carrier.Player!, "walkers-fur-marked");
            Mod.Logger.Notification("{0} is marked by walker {1}'s furs", carrier.Name, mark.WalkerId);
        }
    }

    /// <summary>The walker ids of the furs in a player's own inventories, banished walkers' furs aside (they have gone cold).</summary>
    private HashSet<int> CarriedFurs(IServerPlayer player)
    {
        var found = new HashSet<int>();
        foreach (var name in FurInventories)
        {
            var inventory = player.InventoryManager.GetOwnInventory(name);
            if (inventory == null)
            {
                continue;
            }
            foreach (var slot in inventory)
            {
                var stack = slot?.Itemstack;
                if (stack?.Collectible?.Code is { } code && code.Domain == Core.WalkersModSystem.ModId && code.Path == FurCode &&
                    stack.Attributes.GetInt(WalkerFur.WalkerIdKey) is var id and > 0 &&
                    walkers.TryGetValue(id, out var walker) && walker.State != WalkerState.Banished)
                {
                    found.Add(id);
                }
            }
        }
        return found;
    }

    /// <summary>One of the lore bible's fur lines (game:ingamediscovery-{code}), shown once to the carrier.</summary>
    private void Tell(IServerPlayer player, string code)
    {
        try
        {
            sapi.SendIngameDiscovery(player, code);
        }
        catch (Exception e)
        {
            Mod.Logger.Warning("Could not show {0} to {1}: {2}", code, player.PlayerName, e.Message);
        }
    }

    private bool IsMarked(FurMark mark) => config.Furs.MarkTarget && mark.Hours > 0 && mark.Hours >= config.Furs.MarkThresholdHours;

    /// <summary>Online players marked by this walker's furs, the most marked first.</summary>
    private IEnumerable<Candidate> MarkedCarriers(int walkerId) =>
        candidates.Values
            .Where(c => c.Player != null)
            .Select(c => (Candidate: c, Mark: playerRecords[c.Uid].FurMarks.FirstOrDefault(m => m.WalkerId == walkerId)))
            .Where(t => t.Mark != null && IsMarked(t.Mark))
            .OrderByDescending(t => t.Mark!.Hours)
            .Select(t => t.Candidate);

    /// <summary>The resting walker whose furs mark this player most, or null.</summary>
    private WalkerRecord? RestingMarker(string uid) =>
        playerRecords[uid].FurMarks
            .Where(IsMarked)
            .OrderByDescending(m => m.Hours)
            .Select(m => walkers.GetValueOrDefault(m.WalkerId))
            .FirstOrDefault(w => w?.State == WalkerState.Resting);

    public void ClearFurMarks(string playerUid, int walkerId = 0)
    {
        if (playerRecords.TryGetValue(playerUid, out var record) &&
            record.FurMarks.RemoveAll(m => walkerId == 0 || m.WalkerId == walkerId) > 0)
        {
            Mod.Logger.Notification("{0}'s fur marks{1} are cleared", NameOf(playerUid), walkerId == 0 ? "" : " from walker " + walkerId);
        }
    }

    /// <summary>A banished walker's furs turn cold: every mark it made is gone.</summary>
    private void ClearMarksOf(int walkerId)
    {
        foreach (var record in playerRecords.Values)
        {
            record.FurMarks.RemoveAll(m => m.WalkerId == walkerId);
        }
        walkers[walkerId].FurCarriers.Clear();
    }

    // ---- Escalation ----

    /// <summary>Deaths that sent the walker to the ledger rather than ending it.</summary>
    private int ImproperDeaths(int walkerId) =>
        data.Ledger.Count(e => e.WalkerId == walkerId && e.Outcome is LedgerOutcome.Released or LedgerOutcome.Returned);

    /// <summary>
    /// Improper deaths, plus one for every escalation.monthsPerLevel in-game months of live haunting since
    /// the first, at most the top tier; 0 for a walker that never came back or is banished.
    /// </summary>
    internal int EscalationLevel(WalkerRecord walker)
    {
        if (walker.State == WalkerState.Banished)
        {
            return 0;
        }
        int level = ImproperDeaths(walker.Id);
        float months = config.Escalation.MonthsPerLevel;
        if (level > 0 && months > 0 && sapi.World.Calendar is { } calendar)
        {
            level += (int)Math.Floor(walker.EscalationHauntHours / (months * calendar.DaysPerMonth * calendar.HoursPerDay));
        }
        return Math.Min(MaxEscalation, level);
    }

    // ---- /walkers furs ----

    private void RegisterFursCommands()
    {
        var parsers = sapi.ChatCommands.Parsers;
        sapi.ChatCommands.GetOrCreate(Core.WalkersModSystem.ModId)
            .BeginSubCommand("furs")
                .WithDescription(Lang.Get("walkers:director-cmd-furs-desc"))
                .HandleWith(WalkersCommandsSystem.SafeReply(OnFursStatusCommand))
                .BeginSubCommand("status")
                    .WithDescription(Lang.Get("walkers:director-cmd-furs-status-desc"))
                    .WithExamples("/walkers furs status", "/walkers furs status SomePlayer")
                    .WithArgs(parsers.OptionalWord("player"))
                    .HandleWith(WalkersCommandsSystem.SafeReply(OnFursStatusCommand))
                .EndSubCommand()
                .BeginSubCommand("clear")
                    .WithDescription(Lang.Get("walkers:director-cmd-furs-clear-desc"))
                    .WithExamples("/walkers furs clear", "/walkers furs clear SomePlayer")
                    .WithArgs(parsers.OptionalWord("player"))
                    .HandleWith(WalkersCommandsSystem.SafeReply(OnFursClearCommand))
                .EndSubCommand()
            .EndSubCommand();
    }

    private TextCommandResult OnFursStatusCommand(TextCommandCallingArgs args)
    {
        var target = args.Parsers.Count > 0 && !args.Parsers[0].IsMissing ? (string)args[0] : null;
        var records = playerRecords.Values.AsEnumerable();
        if (target != null)
        {
            var uid = ResolveUid(target);
            if (uid == null || !playerRecords.ContainsKey(uid))
            {
                return TextCommandResult.Error(L(args, "director-no-player", target));
            }
            records = new[] { playerRecords[uid] };
        }
        var lines = new List<string> { L(args, config.Furs.MarkTarget ? "director-furs-header" : "director-furs-header-off", config.Furs.MarkThresholdHours) };
        foreach (var record in records.OrderBy(r => r.Name))
        {
            foreach (var mark in record.FurMarks.OrderByDescending(m => m.Hours))
            {
                lines.Add("  " + L(args, IsMarked(mark) ? "director-furs-marked" : "director-furs-carrying",
                    record.Name, mark.WalkerId, mark.Hours, mark.Carrying ? L(args, "director-furs-now") : ""));
            }
        }
        if (lines.Count == 1)
        {
            lines.Add("  " + L(args, "director-furs-none"));
        }
        return TextCommandResult.Success(string.Join("\n", lines));
    }

    private TextCommandResult OnFursClearCommand(TextCommandCallingArgs args)
    {
        var target = args.Parsers.Count > 0 && !args.Parsers[0].IsMissing ? (string)args[0] : null;
        if (target == null)
        {
            foreach (var record in playerRecords.Values)
            {
                record.FurMarks.Clear();
            }
            Audit(args, "cleared every fur mark");
            return TextCommandResult.Success(L(args, "director-furs-cleared-all"));
        }
        var uid = ResolveUid(target);
        if (uid == null || !playerRecords.ContainsKey(uid))
        {
            return TextCommandResult.Error(L(args, "director-no-player", target));
        }
        ClearFurMarks(uid);
        Audit(args, "cleared the fur marks of {0}", NameOf(uid));
        return TextCommandResult.Success(L(args, "director-furs-cleared", NameOf(uid)));
    }
}
