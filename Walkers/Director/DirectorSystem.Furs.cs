using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Walkers.Commands;
using Walkers.Core;

namespace Walkers.Director;

// Unnatural furs (creature bible, section 7): whoever carries walker furs in their own inventories is
// marked. Furs carry no walker's identity (the owner's ruling, 20 Oct: players cannot tell one walker
// from another), so a mark counts for every walker. Marks change only while some walker that is not
// banished has had its furs taken. A mark takes after furs.markThresholdHours of carrying while online,
// eligible and not idle, grows to furs.markCapHours and fades by furs.decayPerHour once they stop. Any
// due return goes to the most-marked carrier, and a marked player rolls more often while a walker rests.
// Also the escalation level (section 7, the escalation list), which the harassment kit reads.
public partial class DirectorSystem
{
    private const string FurCode = "walkerfur";

    /// <summary>The owner's five-level ladder (20 Oct): 1 Interloping, 2 Grudge, 3 Rage, 4 Hate, 5 Wrath.</summary>
    private const int MaxEscalation = 5;

    /// <summary>At Wrath a walker that dies improperly returns in half the time.</summary>
    private const int WrathLevel = 5;

    /// <summary>A player's own inventories that count: hotbar, backpack with its bags, crafting grid and cursor. Never a chest.</summary>
    private static readonly string[] FurInventories =
    {
        GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName, GlobalConstants.craftingInvClassName, GlobalConstants.mousecursorInvClassName,
    };

    // ---- Marks ----

    /// <summary>This walker's furs were taken at one of its deaths.</summary>
    private bool FursTakenFrom(int walkerId) => data.Ledger.Any(e => e.WalkerId == walkerId && e.FursTaken);

    /// <summary>Some walker that is not banished has furs out: only then do furs mark anyone.</summary>
    private bool FursOut() => walkers.Values.Any(w => w.State != WalkerState.Banished && FursTakenFrom(w.Id));

    /// <summary>On each director tick: scans online players for furs, and grows or fades their marks.</summary>
    private void UpdateFurs()
    {
        double now = sapi.World.Calendar.TotalHours;
        // A long gap (a frozen calendar, a restart) never counts for more than two days.
        double dt = data.FursUpdatedTotalHours <= 0 ? 0 : Math.Clamp(now - data.FursUpdatedTotalHours, 0, 48);
        data.FursUpdatedTotalHours = now;
        if (!config.Furs.MarkTarget || !FursOut())
        {
            // A banish does not cool the furs; marks only hold still until another walker is skinned.
            return;
        }
        // Offline players keep their marks as they are: they neither carry nor put the furs down.
        foreach (var candidate in candidates.Values.Where(c => c.Player != null))
        {
            var record = playerRecords[candidate.Uid];
            bool carrying = CarriesFurs(candidate.Player!);
            if (carrying)
            {
                record.FurMark ??= new FurMark();
            }
            if (record.FurMark != null)
            {
                UpdateMark(candidate, record, record.FurMark, carrying, now, dt);
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
                record.FurMark = null;
            }
            else if (mark.Hours < furs.MarkThresholdHours)
            {
                mark.ToldMarked = false;
            }
            return;
        }

        if (!mark.Carrying)
        {
            // Picked up. Furs another player carried before do not count for a while.
            if (data.FurCarriers.Any(uid => uid != carrier.Uid))
            {
                mark.GraceUntilTotalHours = Math.Max(mark.GraceUntilTotalHours, now + furs.ForeignGraceHours);
            }
            if (!data.FurCarriers.Contains(carrier.Uid))
            {
                data.FurCarriers.Add(carrier.Uid);
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
            Mod.Logger.Notification("{0} is marked by the walker furs they carry", carrier.Name);
        }
    }

    /// <summary>Whether a player has walker furs in their own inventories.</summary>
    private static bool CarriesFurs(IServerPlayer player)
    {
        foreach (var name in FurInventories)
        {
            var inventory = player.InventoryManager.GetOwnInventory(name);
            if (inventory == null)
            {
                continue;
            }
            foreach (var slot in inventory)
            {
                if (slot?.Itemstack?.Collectible?.Code is { } code && code.Domain == Core.WalkersModSystem.ModId && code.Path == FurCode)
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>One of the lore bible's fur lines (game:ingamediscovery-{code}), shown once to the carrier.</summary>
    private void Tell(IServerPlayer player, string code)
    {
        try
        {
            sapi.SendWalkersDiscovery(player, code);
        }
        catch (Exception e)
        {
            Mod.Logger.Warning("Could not show {0} to {1}: {2}", code, player.PlayerName, e.Message);
        }
    }

    private bool IsMarked(FurMark? mark) => mark != null && config.Furs.MarkTarget && mark.Hours > 0 && mark.Hours >= config.Furs.MarkThresholdHours;

    /// <summary>Online players marked by walker furs, the most marked first: the first targets of any return.</summary>
    private IEnumerable<Candidate> MarkedCarriers() =>
        candidates.Values
            .Where(c => c.Player != null && IsMarked(playerRecords[c.Uid].FurMark))
            .OrderByDescending(c => playerRecords[c.Uid].FurMark!.Hours);

    /// <summary>A marked player whose roll a resting walker doubles (furs.restingRollMultiplier).</summary>
    private bool MarkedWhileResting(string uid) =>
        IsMarked(playerRecords[uid].FurMark) && walkers.Values.Any(w => w.State == WalkerState.Resting);

    public void ClearFurMarks(string playerUid)
    {
        if (playerRecords.TryGetValue(playerUid, out var record) && record.FurMark != null)
        {
            record.FurMark = null;
            Mod.Logger.Notification("{0}'s fur mark is cleared", NameOf(playerUid));
        }
    }

    /// <summary>Forms saw furs burn: the player who dropped them loses their mark (furs.burnClearsMark).</summary>
    private void OnFursBurned(IPlayer? byPlayer, int count)
    {
        if (byPlayer != null && config.Furs.BurnClearsMark)
        {
            ClearFurMarks(byPlayer.PlayerUID);
        }
    }

    private void ClearAllFurMarks()
    {
        foreach (var record in playerRecords.Values)
        {
            record.FurMark = null;
        }
        data.FurCarriers.Clear();
    }

    // ---- Escalation ----

    /// <summary>Deaths that sent the walker to the ledger rather than ending it.</summary>
    private int ImproperDeaths(int walkerId) =>
        data.Ledger.Count(e => e.WalkerId == walkerId && e.Outcome is LedgerOutcome.Released or LedgerOutcome.Returned);

    /// <summary>
    /// The owner's five-level ladder: 1 on its first haunt, plus its improper deaths, plus one for every
    /// escalation.monthsPerLevel in-game months of live haunting since the first improper death, at most 5;
    /// 0 for a banished walker.
    /// </summary>
    internal int EscalationLevel(WalkerRecord walker)
    {
        if (walker.State == WalkerState.Banished)
        {
            return 0;
        }
        // A walker's first haunt is level 1 (Interloping).
        int improper = ImproperDeaths(walker.Id);
        int level = 1 + improper;
        float months = config.Escalation.MonthsPerLevel;
        if (improper > 0 && months > 0 && sapi.World.Calendar is { } calendar)
        {
            level += (int)Math.Floor(walker.EscalationHauntHours / (months * calendar.DaysPerMonth * calendar.HoursPerDay));
        }
        return Math.Min(MaxEscalation, level);
    }

    /// <summary>At Wrath, the top of the ladder, groups no longer hold it back (the owner's ladder, 20 Oct).</summary>
    private bool IsWrath(WalkerRecord walker) => EscalationLevel(walker) >= WrathLevel;

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
        foreach (var record in records.Where(r => r.FurMark != null).OrderByDescending(r => r.FurMark!.Hours))
        {
            var mark = record.FurMark!;
            lines.Add("  " + L(args, IsMarked(mark) ? "director-furs-marked" : "director-furs-carrying",
                record.Name, mark.Hours, mark.Carrying ? L(args, "director-furs-now") : ""));
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
            ClearAllFurMarks();
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
