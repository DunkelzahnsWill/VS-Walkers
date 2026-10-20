using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Walkers.Director;

namespace Walkers.Harassment.Acts;

/// <summary>The lore bible's page triggers (section "The page queue").</summary>
public enum PageTrigger
{
    /// <summary>First sighting, stage 1; once per player.</summary>
    S1,
    /// <summary>First slip-away when watched, stage 2; once per player.</summary>
    S2,
    /// <summary>First echo, stage 3; once per player.</summary>
    S3,
    /// <summary>First one-rap knock of a cycle, stage 3; every cycle.</summary>
    K1,
    /// <summary>First two-rap knock, stage 4; once per player.</summary>
    K2,
    /// <summary>The three-rap knock at dusk, stage 5; every cycle.</summary>
    K3,
    /// <summary>The burn window closed with the body unburned or skinned; each ordinary kill.</summary>
    Gone,
    /// <summary>Half the configured return delay; each ordinary kill.</summary>
    HalfTimer,
    /// <summary>A proper kill (banish); once.</summary>
    End,
}

/// <summary>
/// The Pages act: loose pages from the lore's documents, left where the hunted player will look down
/// (lore bible, "The page queue"; creature bible, section 6). Always items, never blocks. A page lies
/// beside the door nearest the player's spawn point, or at their feet when they have no door, and drops
/// only while they are within 16 blocks of the spot, so it is found before a dropped item's despawn
/// timer runs out; one that despawns unread is left again at their feet at the next dawn.
///
/// Every stack is built to the engine rules in memory note walkers-lore-engine-rules: category
/// walkers-step, discoveryCode one of the three door journals, chapterIds an IntArrayAttribute with
/// one id below that journal's piece count, and the item name stored as resolved English text.
/// Nothing here hands out the painting journals.
///
/// Once-per-player triggers, the queue and the pages on the ground are kept in the save game under
/// walkers:harass-pages, by player uid (or stand-in id), so a killer who is offline still gets their
/// copy when they come back. Per-cycle triggers live in the walker's memory.
/// </summary>
public sealed class PagesAct
{
    public const string ActCode = "pages";

    public const string SaveKey = "walkers:harass-pages";

    public const string Maud = "walkers-maud";
    public const string Wat = "walkers-wat";
    public const string PennLate = "walkers-pennlate";
    public const string Fallback = "fallback";

    /// <summary>The escalation pages (the lore bible's Tallies of Loss); used only while its journal asset is there.</summary>
    public const string Losses = "walkers-losses";

    /// <summary>Piece counts of the three door journals (config/lore/*.json).</summary>
    private static readonly Dictionary<string, int> Pieces = new() { [Maud] = 6, [Wat] = 3, [PennLate] = 2, [Losses] = 3 };

    /// <summary>The losses chapters, by escalation act: the herd, the crops, the torn door.</summary>
    public const int LossHerd = 0, LossCrops = 1, LossDoor = 2;

    /// <summary>A page drops only while its reader is this close to the spot.</summary>
    private const double DropRange = 16;

    /// <summary>The door a page lies beside is searched this far around the reader's spawn point.</summary>
    private const int HomeDoorRadius = 12;

    private readonly HarassModSystem system;
    private ICoreServerAPI Api => system.Api;

    /// <summary>Everything kept per reader, by player uid or stand-in key.</summary>
    private Dictionary<string, ReaderData> readers = new();

    public PagesAct(HarassModSystem system)
    {
        this.system = system;
    }

    public sealed class ReaderData
    {
        /// <summary>Once-per-player triggers already fired (S1, S2, S3, K2).</summary>
        public HashSet<string> Once { get; set; } = new();

        /// <summary>Per-death triggers already fired, as "Gone:walkerId:diedDay" and "HalfTimer:...".</summary>
        public HashSet<string> Deaths { get; set; } = new();

        public List<Page> Pending { get; set; } = new();

        public List<Page> Dropped { get; set; } = new();

        /// <summary>Night at the reader at the last delivery check.</summary>
        public bool WasNight { get; set; }
    }

    public sealed class Page
    {
        public string Journal { get; set; } = Maud;
        public int Chapter { get; set; }
        /// <summary>Lang key of the item name (pagename-*).</summary>
        public string NameKey { get; set; } = "walkers:pagename-letter";
        /// <summary>Waits for the next dusk; else it goes now (with a knock).</summary>
        public bool AtDusk { get; set; }
        /// <summary>A day has passed since it was queued, so the next night is its dusk.</summary>
        public bool SawDay { get; set; }
        /// <summary>Despawned unread: dropped at the reader's feet at the next dawn.</summary>
        public bool AtDawn { get; set; }
        /// <summary>Where to drop it instead of the reader's door (beside the eyes); null for the door.</summary>
        public double[]? At { get; set; }
        public long EntityId { get; set; }
        public double DroppedHours { get; set; }
    }

    // ---- Save ----

    public void Load()
    {
        try
        {
            var data = Api.WorldManager.SaveGame.GetData(SaveKey);
            readers = data == null ? new() : JsonConvert.DeserializeObject<Dictionary<string, ReaderData>>(System.Text.Encoding.UTF8.GetString(data)) ?? new();
        }
        catch (Exception e)
        {
            system.Mod.Logger.Warning("Pages: the saved page queue could not be read, starting fresh: {0}", e.Message);
            readers = new();
        }
    }

    public void Save()
    {
        Api.WorldManager.SaveGame.StoreData(SaveKey, System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(readers)));
    }

    private ReaderData Reader(string key) => readers.TryGetValue(key, out var r) ? r : readers[key] = new ReaderData();

    public int PendingCount(string key) => readers.TryGetValue(key, out var r) ? r.Pending.Count : 0;

    // ---- Triggers ----

    /// <summary>S1, S2 and S3, from the acts and the walker's memory: the next unread Maud letter, at the next dusk.</summary>
    public void OnTrigger(HarassContext ctx, PageTrigger trigger)
    {
        if (!ctx.Config.Pages.Enabled)
        {
            return;
        }
        string key = system.VictimKey(ctx);
        var reader = Reader(key);
        if (!reader.Once.Add(trigger.ToString()))
        {
            return;
        }
        int? maud = NextUnread(key, Maud);
        if (maud != null)
        {
            Queue(reader, new Page { Journal = Maud, Chapter = maud.Value, NameKey = "walkers:pagename-letter", AtDusk = true, SawDay = !ctx.Night });
        }
        system.Log(ctx, ActCode, $"{trigger}: {(maud == null ? "nothing unread" : $"Maud {maud + 1} queued for dusk")}");
    }

    /// <summary>The first of each escalation act for this reader: its Tallies of Loss page, at their door at dusk.</summary>
    public void OnLoss(HarassContext ctx, int chapter)
    {
        if (!ctx.Config.Pages.Enabled || !HasJournal(Losses))
        {
            return;
        }
        string key = system.VictimKey(ctx);
        var reader = Reader(key);
        if (!reader.Once.Add("loss" + chapter) || IsFoundOrOutstanding(key, Losses, chapter))
        {
            return;
        }
        string name = chapter == LossDoor ? "walkers:pagename-hearsay" : "walkers:pagename-leaf";
        Queue(reader, new Page { Journal = Losses, Chapter = chapter, NameKey = name, AtDusk = true, SawDay = !ctx.Night });
        system.Log(ctx, ActCode, $"loss {chapter + 1} queued for dusk");
    }

    private bool HasJournal(string journal) => Api.Assets.Exists(new AssetLocation("walkers", $"config/lore/{journal}.json"));

    /// <summary>A counted knock: K1, K2 or K3 by its raps. Leaves its page, or the fallback letter, with the knock.</summary>
    public void OnKnock(HarassContext ctx, PageTrigger trigger)
    {
        if (!ctx.Config.Pages.Enabled)
        {
            return;
        }
        string key = system.VictimKey(ctx);
        var reader = Reader(key);
        int cycle = 1 + ctx.Memory.GetInt(WalkerMemory.Returns);
        var pages = new List<Page>();
        bool fires = trigger switch
        {
            PageTrigger.K1 => ctx.Harass.GetInt(HarassMemory.PageK1Cycle) < cycle,
            PageTrigger.K3 => ctx.Harass.GetInt(HarassMemory.PageK3Cycle) < cycle,
            _ => !reader.Once.Contains(trigger.ToString()),
        };
        if (fires)
        {
            switch (trigger)
            {
                case PageTrigger.K1:
                    ctx.Harass.SetInt(HarassMemory.PageK1Cycle, cycle);
                    if (cycle >= 2)
                    {
                        AddMaud(key, pages);
                    }
                    AddIfUnread(key, pages, Wat, 0, "walkers:pagename-scrap");
                    if (cycle >= 3)
                    {
                        AddIfUnread(key, pages, PennLate, 1, "walkers:pagename-leaf");
                    }
                    break;
                case PageTrigger.K2:
                    reader.Once.Add(trigger.ToString());
                    AddMaud(key, pages);
                    break;
                default:
                    ctx.Harass.SetInt(HarassMemory.PageK3Cycle, cycle);
                    if (cycle >= 2)
                    {
                        AddMaud(key, pages);
                    }
                    AddIfUnread(key, pages, Wat, 1, "walkers:pagename-scrap");
                    break;
            }
        }
        if (pages.Count == 0)
        {
            // The step is never empty after a knock.
            pages.Add(new Page { Journal = Fallback, NameKey = "walkers:walkers-parch-step-title" });
        }
        foreach (var page in pages)
        {
            Queue(reader, page);
        }
        system.Log(ctx, ActCode, $"{trigger} (cycle {cycle}): {string.Join(", ", pages.Select(Describe))} with the knock");
        DeliverNow(ctx.Victim, key, reader);
    }

    /// <summary>Gone: the body went unburned (or was skinned). Wat 3 if unread, at the victim's step and the killer's, at the next dusk.</summary>
    public void OnReleased(LedgerEntry entry)
    {
        if (!system.Config.Harass.Pages.Enabled || entry.Release is not (WalkerRelease.Skinned or WalkerRelease.LeftAlone))
        {
            return;
        }
        string death = $"{PageTrigger.Gone}:{entry.WalkerId}:{entry.DiedTotalDays:0.###}";
        foreach (var key in ReadersOfDeath(entry))
        {
            var reader = Reader(key);
            if (!reader.Deaths.Add(death))
            {
                continue;
            }
            if (!IsFoundOrOutstanding(key, Wat, 2))
            {
                Queue(reader, new Page { Journal = Wat, Chapter = 2, NameKey = "walkers:pagename-scrap", AtDusk = true });
            }
        }
    }

    /// <summary>Banished: Later Leaves 2, and Maud 6 if unread, beside the eyes at once, both named "A folded leaf, still warm".</summary>
    public void OnBanished(LedgerEntry entry)
    {
        if (!system.Config.Harass.Pages.Enabled || entry.VictimUid == null)
        {
            return;
        }
        string key = entry.VictimUid;
        var reader = Reader(key);
        if (!reader.Once.Add(PageTrigger.End.ToString()))
        {
            return;
        }
        var at = new[] { entry.Pos.X + 0.5, entry.Pos.Y + 0.5, entry.Pos.Z + 0.5 };
        var pages = new List<Page>();
        AddIfUnread(key, pages, PennLate, 1, "walkers:pagename-knotleaf");
        AddIfUnread(key, pages, Maud, 5, "walkers:pagename-knotleaf");
        foreach (var page in pages)
        {
            page.At = at;
            Drop(key, reader, page, new Vec3d(at[0], at[1], at[2]), entry.Pos.dimension);
        }
        system.Mod.Logger.Debug("Pages: End for walker {0}: {1} beside the eyes", entry.WalkerId, pages.Count);
    }

    /// <summary>
    /// ½T, checked on the delivery tick: for each released death in the ledger past half its return
    /// delay, Later Leaves 1 if unread at both steps, at the next dusk.
    /// </summary>
    private void CheckHalfTimers()
    {
        var director = system.Director;
        if (director == null)
        {
            return;
        }
        double today = Api.World.Calendar.TotalDays;
        foreach (var entry in director.Ledger)
        {
            if (entry.Outcome != LedgerOutcome.Released || today < entry.DiedTotalDays + (entry.ReturnTotalDays - entry.DiedTotalDays) / 2)
            {
                continue;
            }
            string death = $"{PageTrigger.HalfTimer}:{entry.WalkerId}:{entry.DiedTotalDays:0.###}";
            foreach (var key in ReadersOfDeath(entry))
            {
                var reader = Reader(key);
                if (reader.Deaths.Add(death) && !IsFoundOrOutstanding(key, PennLate, 0))
                {
                    Queue(reader, new Page { Journal = PennLate, Chapter = 0, NameKey = "walkers:pagename-leaf", AtDusk = true });
                }
            }
        }
    }

    /// <summary>The hunted player, and the killer when they are someone else.</summary>
    private IEnumerable<string> ReadersOfDeath(LedgerEntry entry)
    {
        if (entry.VictimUid != null)
        {
            yield return entry.VictimUid;
        }
        if (entry.KillerName != null)
        {
            var killer = Api.World.AllPlayers.FirstOrDefault(p => p.PlayerName == entry.KillerName);
            if (killer != null && killer.PlayerUID != entry.VictimUid)
            {
                yield return killer.PlayerUID;
            }
        }
    }

    // ---- Choosing chapters ----

    private void AddMaud(string key, List<Page> pages)
    {
        int? maud = NextUnread(key, Maud, pages);
        if (maud != null)
        {
            pages.Add(new Page { Journal = Maud, Chapter = maud.Value, NameKey = "walkers:pagename-letter" });
        }
    }

    private void AddIfUnread(string key, List<Page> pages, string journal, int chapter, string nameKey)
    {
        if (!IsFoundOrOutstanding(key, journal, chapter) && !pages.Any(p => p.Journal == journal && p.Chapter == chapter))
        {
            pages.Add(new Page { Journal = journal, Chapter = chapter, NameKey = nameKey });
        }
    }

    /// <summary>The lowest chapter of a journal the reader has not found and that is not already on its way; null when none is left.</summary>
    private int? NextUnread(string key, string journal, List<Page>? alsoTaken = null)
    {
        for (int chapter = 0; chapter < Pieces[journal]; chapter++)
        {
            if (!IsFoundOrOutstanding(key, journal, chapter) && alsoTaken?.Any(p => p.Journal == journal && p.Chapter == chapter) != true)
            {
                return chapter;
            }
        }
        return null;
    }

    /// <summary>True when the reader already found the chapter, or a page with it is queued or lies on the ground.</summary>
    private bool IsFoundOrOutstanding(string key, string journal, int chapter)
    {
        if (Api.ModLoader.GetModSystem<ModJournal>()?.DidDiscoverLore(key, journal, chapter) == true)
        {
            return true;
        }
        return readers.TryGetValue(key, out var r) && r.Pending.Concat(r.Dropped).Any(p => p.Journal == journal && p.Chapter == chapter);
    }

    private static void Queue(ReaderData reader, Page page) => reader.Pending.Add(page);

    private static string Describe(Page page) => page.Journal == Fallback ? "the fallback letter" : $"{page.Journal} {page.Chapter + 1}";

    // ---- Delivery ----

    /// <summary>
    /// Every few seconds: for each reader who is here (an online player, or a walker's stand-in victim),
    /// pages due at this dusk drop by their door once they are within 16 blocks of it; at dawn whatever
    /// is still waiting, and pages that despawned unread, drop at their feet.
    /// </summary>
    public void DeliveryTick(IEnumerable<(string Key, Entity Entity)> present)
    {
        if (!system.Config.Harass.Pages.Enabled)
        {
            return;
        }
        CheckHalfTimers();
        foreach (var (key, entity) in present)
        {
            if (!readers.TryGetValue(key, out var reader) || reader.Pending.Count == 0)
            {
                if (reader != null)
                {
                    reader.WasNight = Stalker.StalkerModSystem.IsNight(Api.World, entity.Pos.XYZ);
                }
                continue;
            }
            bool night = Stalker.StalkerModSystem.IsNight(Api.World, entity.Pos.XYZ);
            bool dawn = reader.WasNight && !night;
            reader.WasNight = night;
            foreach (var page in reader.Pending)
            {
                if (!night)
                {
                    page.SawDay = true;
                }
            }
            if (dawn)
            {
                foreach (var page in reader.Pending.Where(p => p.AtDusk || p.AtDawn).ToList())
                {
                    Drop(key, reader, page, entity.Pos.XYZ.AddCopy(0, 0.5, 0), entity.Pos.Dimension);
                }
                continue;
            }
            var due = reader.Pending.Where(p => p.AtDusk && p.SawDay && night).ToList();
            if (due.Count > 0)
            {
                var spot = HomeSpot(key, entity);
                if (spot == null)
                {
                    foreach (var page in due)
                    {
                        Drop(key, reader, page, entity.Pos.XYZ.AddCopy(0, 0.5, 0), entity.Pos.Dimension);
                    }
                }
                else if (entity.Pos.DistanceTo(spot) <= DropRange)
                {
                    foreach (var page in due)
                    {
                        Drop(key, reader, page, spot, entity.Pos.Dimension);
                    }
                }
            }
        }
    }

    /// <summary>Drops every page waiting to go "now" (with a knock): by the door nearest the reader within 16 blocks, else at their feet.</summary>
    private void DeliverNow(Entity reader, string key, ReaderData data)
    {
        var now = data.Pending.Where(p => !p.AtDusk && !p.AtDawn).ToList();
        if (now.Count == 0)
        {
            return;
        }
        var doors = BlockRules.FindNear(Api.World, reader, BlockRules.VictimReach, be => be.GetBehavior<BEBehaviorDoor>());
        var spot = doors.Count > 0 ? Outside(doors[0].Pos, reader.Pos.XYZ) : reader.Pos.XYZ.AddCopy(0, 0.5, 0);
        foreach (var page in now)
        {
            Drop(key, data, page, spot, reader.Pos.Dimension);
        }
    }

    /// <summary>Beside the door nearest the reader's spawn point (within 12 blocks), on the outside; null when there is no door there.</summary>
    private Vec3d? HomeSpot(string key, Entity entity)
    {
        BlockPos anchor = entity.Pos.AsBlockPos;
        if (Api.World.PlayerByUid(key) is IServerPlayer player)
        {
            try
            {
                var spawn = player.GetSpawnPosition(false);
                if (spawn != null)
                {
                    anchor = spawn.AsBlockPos;
                }
            }
            catch (Exception)
            {
                // No spawn point yet: the player's own position will do.
            }
        }
        if (Api.World.BlockAccessor.GetChunkAtBlockPos(anchor) == null)
        {
            return null;
        }
        var doors = BlockRules.FindNear(Api.World, anchor, HomeDoorRadius, be => be.GetBehavior<BEBehaviorDoor>());
        return doors.Count == 0 ? null : Outside(doors[0].Pos, anchor.ToVec3d().Add(0.5, 0, 0.5));
    }

    /// <summary>One block out from the door, on the side away from <paramref name="inside"/>.</summary>
    private static Vec3d Outside(BlockPos door, Vec3d inside)
    {
        double dx = door.X + 0.5 - inside.X, dz = door.Z + 0.5 - inside.Z;
        double len = Math.Max(0.001, Math.Sqrt(dx * dx + dz * dz));
        return new Vec3d(door.X + 0.5 + dx / len * 1.2, door.Y + 0.5, door.Z + 0.5 + dz / len * 1.2);
    }

    private void Drop(string key, ReaderData reader, Page page, Vec3d at, int dimension)
    {
        reader.Pending.Remove(page);
        var stack = MakeStack(page);
        if (stack == null)
        {
            return;
        }
        var pos = at.Clone();
        pos.Y += dimension * Vintagestory.API.Config.GlobalConstants.DimensionSizeInChunks * Vintagestory.API.Config.GlobalConstants.ChunkSize;
        var item = Api.World.SpawnItemEntity(stack, pos, new Vec3d(0, 0.05, 0));
        if (page.Journal != Fallback && item != null)
        {
            page.EntityId = item.EntityId;
            page.DroppedHours = Api.World.Calendar.TotalHours;
            page.AtDusk = false;
            page.AtDawn = false;
            reader.Dropped.Add(page);
        }
        system.Mod.Logger.Debug("Pages: {0} for {1} dropped at {2}", Describe(page), key, EchoAct.Fmt(at));
        system.NotePageDropped(key, Describe(page));
    }

    /// <summary>A page item despawned: picked up, it is done; expired unread, it is left again at the reader's feet at the next dawn.</summary>
    public void OnItemDespawn(Entity entity, EntityDespawnData? reason)
    {
        if (entity is not EntityItem)
        {
            return;
        }
        foreach (var (key, reader) in readers)
        {
            var page = reader.Dropped.FirstOrDefault(p => p.EntityId == entity.EntityId);
            if (page == null)
            {
                continue;
            }
            reader.Dropped.Remove(page);
            if (reason?.Reason == EnumDespawnReason.Expire && !IsFound(key, page))
            {
                page.EntityId = 0;
                page.AtDawn = true;
                page.At = null;
                reader.Pending.Add(page);
            }
            return;
        }
    }

    private bool IsFound(string key, Page page) => Api.ModLoader.GetModSystem<ModJournal>()?.DidDiscoverLore(key, page.Journal, page.Chapter) == true;

    /// <summary>
    /// The page's stack, checked against every rule that would otherwise throw when a player reads it;
    /// null (and a warning) when one fails.
    /// </summary>
    public ItemStack? MakeStack(Page page, bool quiet = false)
    {
        var world = Api.World;
        if (page.Journal == Fallback)
        {
            var parchment = world.GetItem(new AssetLocation("game", "paper-parchment"));
            if (parchment == null)
            {
                return null;
            }
            var letter = new ItemStack(parchment);
            letter.Attributes.SetString("title", Lang.Get("walkers:walkers-parch-step-title"));
            letter.Attributes.SetString("titleCode", "walkers:walkers-parch-step-title");
            letter.Attributes["textCodes"] = new StringArrayAttribute(new[] { "walkers:walkers-parch-step-p1" });
            letter.Attributes.SetString("signedby", "");
            return letter;
        }
        if (!Pieces.TryGetValue(page.Journal, out int count) || page.Chapter < 0 || page.Chapter >= count || !HasJournal(page.Journal))
        {
            if (!quiet)
            {
                system.Mod.Logger.Warning("Pages: refused a page for {0} chapter {1}, which does not exist", page.Journal, page.Chapter);
            }
            return null;
        }
        var item = world.GetItem(new AssetLocation("game", page.Journal == Maud ? "envelope-normal-blue" : "lore-paper"));
        if (item is not ItemRandomLore)
        {
            system.Mod.Logger.Warning("Pages: the vanilla lore item for {0} is missing or changed", page.Journal);
            return null;
        }
        var stack = new ItemStack(item);
        stack.Attributes.SetString("category", "walkers-step");
        stack.Attributes.SetString("discoveryCode", page.Journal);
        stack.Attributes["chapterIds"] = new IntArrayAttribute(new[] { page.Chapter });
        stack.Attributes.SetString("title", Lang.Get(page.NameKey));
        return stack;
    }
}
