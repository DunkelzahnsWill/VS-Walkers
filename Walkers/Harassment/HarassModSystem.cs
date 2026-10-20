using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Walkers.Commands;
using Walkers.Config;
using Walkers.Director;
using Walkers.Harassment.Acts;
using Walkers.Stalker;

namespace Walkers.Harassment;

/// <summary>
/// What the kit remembers about one victim between ticks, in memory only: timers in real
/// milliseconds, the drain's tracking, the last five acts. Counters that must survive a body or a
/// restart are in the walker's memory instead (<see cref="HarassMemory"/>).
/// </summary>
public sealed class VictimState
{
    /// <summary>World.ElapsedMilliseconds before which the scheduler draws no act.</summary>
    public long NextActMs;
    public long LastWakeMs = -100000;
    public long LastPanicMs;
    public long LastHurtMs = -100000;
    public int LastHurtCounter = -1;
    public long SeenHitMs = -1;
    public bool WasGuise;
    public bool DrainTracking;
    public double HandBackPerSecond;
    public bool SeenReveal;
    public long RevealUntilMs;
    public float TirednessAtBed;
    public bool WasAsleep;
    public readonly LinkedList<string> Recent = new();
}

/// <summary>
/// The harassment kit, on the server (creature bible, section 6): once a second, for every walker body
/// the stalker AI tracks that haunts someone, it updates the titles, the page triggers, the stability
/// drain and the livestock panic, and lets the scheduler spend tonight's act budget. The acts live in
/// <c>Acts/</c>, one class each. It needs the stalker AI (bodies, victims, the gaze) and works with or
/// without the director (restore list, act reports, the ledger's page triggers).
/// </summary>
public sealed class HarassModSystem : ModSystem
{
    private const int TickMs = 1000;

    /// <summary>The page delivery runs every this many ticks.</summary>
    private const int PageTickEvery = 5;

    /// <summary>No act while the victim was hurt within this many real milliseconds (a fight).</summary>
    private const long FightMs = 10000;

    /// <summary>From stage 4, no act while another counting player is within this many blocks of the victim.</summary>
    private const double CompanyRange = 24;

    /// <summary>The quiet stretch the scheduler keeps once a night, in real milliseconds.</summary>
    private const long QuietMs = 300000;

    /// <summary>When every drawn act skipped, the scheduler tries again after this long.</summary>
    private const long RetryMs = 20000;

    private const int RecentKept = 5;

    public ICoreServerAPI Api { get; private set; } = null!;

    public WalkersConfig Config { get; private set; } = null!;

    public Random Rand { get; } = new();

    public StalkerModSystem Stalker { get; private set; } = null!;

    public HarassRestore Restore { get; private set; } = null!;

    public PagesAct Pages { get; private set; } = null!;

    public IReadOnlyList<HarassAct> Acts => acts;

    public KnockAct Knocks { get; } = new();

    public OfferingAct Offering { get; } = new();

    public TitleAct Titles { get; } = new();

    public StabilityDrain Drain { get; } = new();

    public LivestockPanic Panic { get; } = new();

    public Escalation Escalation { get; private set; } = null!;

    private readonly List<HarassAct> acts = new();
    private readonly Dictionary<string, VictimState> victims = new();
    private readonly Dictionary<string, EchoBuffer> echoes = new();
    private readonly HashSet<long> failedBodies = new();
    private IWalkerDirector? director;
    private int tickCount;

    /// <summary>The director, once the server runs; null without one.</summary>
    public IWalkerDirector? Director => director;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    // After the stalker AI and the director, which this system reads.
    public override double ExecuteOrder() => 0.6;

    public override void StartServerSide(ICoreServerAPI api)
    {
        Api = api;
        Config = api.GetWalkersConfig();
        Stalker = api.ModLoader.GetModSystem<StalkerModSystem>();
        Restore = new HarassRestore(this);
        Pages = new PagesAct(this);
        Escalation = new Escalation(this);
        acts.AddRange(new HarassAct[]
        {
            new EchoAct(), Knocks, new WrongCallAct(), new WhisperAct(), new DouseTorchAct(),
            new DoorAct(), new ChestLidAct(), new SleepDisturbAct(), new BreakInAct(),
        });

        api.Event.DidUseBlock += OnUseBlock;
        api.Event.DidBreakBlock += OnBreakBlock;
        api.Event.DidPlaceBlock += OnPlaceBlock;
        api.Event.OnEntityDespawn += (entity, reason) => Pages.OnItemDespawn(entity, reason);
        api.Event.SaveGameLoaded += Pages.Load;
        api.Event.GameWorldSave += Pages.Save;
        api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, FindDirector);
        api.Event.RegisterGameTickListener(OnTick, TickMs, 2000);

        HarassCommands.Register(api, this);
        api.ModLoader.GetModSystem<WalkersCommandsSystem>().Status += lines => HarassCommands.AddStatusLine(this, lines);
    }

    private void FindDirector()
    {
        director = Api.GetWalkersDirector();
        if (director == null)
        {
            Mod.Logger.Notification("Harassment kit: no director; walkers it did not place keep their own restore list");
            return;
        }
        Restore.RegisterWith(director);
        director.WalkerReleased += (_, entry) => Guard("pages (released)", () => Pages.OnReleased(entry));
        director.WalkerBanished += (_, entry) => Guard("pages (banished)", () => Pages.OnBanished(entry));
        Mod.Logger.Notification("Harassment kit: restore handler and page triggers registered with the director");
    }

    private void Guard(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Mod.Logger.Error("Harassment kit: {0} failed: {1}", what, e);
        }
    }

    // ---- Who and what ----

    /// <summary>The key a victim's state, echoes and pages are kept under: the player uid, else the director's stand-in id, else the entity id.</summary>
    public string VictimKey(HarassContext ctx) => KeyOf(ctx.Victim, ctx.Memory);

    public static string KeyOf(Entity victim, Vintagestory.API.Datastructures.ITreeAttribute? memory)
    {
        if (victim is EntityPlayer ep && ep.PlayerUID != null)
        {
            return ep.PlayerUID;
        }
        string? standIn = memory?.GetString(WalkerMemory.Victim);
        return !string.IsNullOrEmpty(standIn) && memory?.GetLong(WalkerMemory.VictimEntity) == victim.EntityId ? standIn! : "entity-" + victim.EntityId;
    }

    public VictimState StateOf(string key) => victims.TryGetValue(key, out var s) ? s : victims[key] = new VictimState();

    public EchoBuffer? EchoesOf(string playerUid) => echoes.TryGetValue(playerUid, out var b) ? b : null;

    /// <summary>The director's id of the walker, or 0 for a body the director did not place.</summary>
    public int WalkerIdOf(HarassContext ctx) => director != null && director.TryGetWalkerId(ctx.Body, out int id) ? id : 0;

    /// <summary>The context of a loaded walker body that haunts someone; null otherwise.</summary>
    public HarassContext? ContextOf(WalkerBrain brain) => ContextOf(brain, null);

    /// <summary>As above; <paramref name="night"/> overrides the time of day (the self-test).</summary>
    public HarassContext? ContextOf(WalkerBrain brain, bool? night)
    {
        var victim = brain.Victim;
        if (!brain.Body.Alive || victim == null || !victim.Alive || brain.Stage <= 0)
        {
            return null;
        }
        return new HarassContext(this, brain, victim, StateOf(KeyOf(victim, brain.Memory)), night);
    }

    // ---- The tick ----

    private void OnTick(float dt)
    {
        Restore.Tick(Api);
        if (!Config.Enabled)
        {
            return;
        }
        tickCount++;
        var present = new Dictionary<string, Entity>();
        foreach (var brain in Stalker.Brains.ToList())
        {
            var ctx = ContextOf(brain);
            if (ctx == null || failedBodies.Contains(brain.Body.EntityId))
            {
                continue;
            }
            present[VictimKey(ctx)] = ctx.Victim;
            try
            {
                Update(ctx);
            }
            catch (Exception e)
            {
                failedBodies.Add(brain.Body.EntityId);
                Mod.Logger.Error("Harassment kit: the update of {0} #{1} failed, so it does nothing more for this body: {2}", brain.Body.Code, brain.Body.EntityId, e);
            }
        }
        if (tickCount % PageTickEvery == 0)
        {
            foreach (var player in Api.World.AllOnlinePlayers)
            {
                if (player.Entity != null)
                {
                    present[player.PlayerUID] = player.Entity;
                }
            }
            Guard("page delivery", () => Pages.DeliveryTick(present.Select(p => (p.Key, p.Value))));
        }
    }

    private void Update(HarassContext ctx)
    {
        NewHauntCheck(ctx);
        NightCheck(ctx);
        WatchVictim(ctx);
        bool paused = IsStorm() || ctx.Memory.GetBool(WalkerMemory.Withdraw);

        Titles.Update(ctx);
        WatchPageTriggers(ctx);
        Drain.Tick(ctx, paused);
        if (paused)
        {
            return;
        }
        foreach (var (code, result) in Escalation.Tick(ctx))
        {
            ReportEscalation(ctx, code, result);
        }
        int bolted = Panic.Tick(ctx);
        if (bolted > 0)
        {
            Mod.Logger.Debug("Harassment: {0} animal(s) bolted from walker #{1}", bolted, ctx.Body.EntityId);
        }
        Schedule(ctx);
    }

    /// <summary>A new haunt (the stalker AI's hauntBegan changed): the once-per-haunt flags start again.</summary>
    private static void NewHauntCheck(HarassContext ctx)
    {
        double began = ctx.Brain.Ai.GetDouble(StalkerMemory.HauntBegan, -1);
        var harass = ctx.Harass;
        if (harass.GetDouble(HarassMemory.Haunt, double.NaN) == began)
        {
            return;
        }
        harass.SetDouble(HarassMemory.Haunt, began);
        foreach (var key in new[] { HarassMemory.TitleWatching, HarassMemory.TitleForestSaw, HarassMemory.Offerings, HarassMemory.BreakIns, HarassMemory.Tears, HarassMemory.DrainTaken, HarassMemory.EntryUntilMs,
                     HarassMemory.OtherTears, HarassMemory.BreakInNight, HarassMemory.BreakInTonight, HarassMemory.HerdDone, HarassMemory.HerdPending,
                     HarassMemory.HerdCount, HarassMemory.CropsEaten, HarassMemory.LivestockDone })
        {
            harass.RemoveAttribute(key);
        }
    }

    /// <summary>Dusk resets tonight's budget and brings the stage-5 knock; dawn brings the stage-4 offering.</summary>
    private void NightCheck(HarassContext ctx)
    {
        var harass = ctx.Harass;
        bool known = harass.HasAttribute(HarassMemory.IsNight);
        bool wasNight = harass.GetBool(HarassMemory.IsNight);
        harass.SetBool(HarassMemory.IsNight, ctx.Night);
        if (!known)
        {
            HarassMemory.NewNight(harass, Api.World.Calendar.TotalHours);
            return;
        }
        if (ctx.Night && !wasNight)
        {
            HarassMemory.NewNight(harass, Api.World.Calendar.TotalHours);
            var knock = Knocks.RunDusk(ctx);
            if (knock.Done)
            {
                Report(ctx, KnockAct.ActCode, knock.Detail);
            }
            if (ctx.Stage >= 5 && Enraged.Is(ctx) && Config.Harass.Whispers.Enabled && Lang.HasTranslation("game:ingamediscovery-" + WhisperAct.CodeOf(WhisperAct.IronLine)))
            {
                Log(ctx, WhisperAct.ActCode, WhisperAct.Say(ctx, WhisperAct.IronLine).Detail);
            }
        }
        else if (!ctx.Night && wasNight)
        {
            var offering = Offering.RunDawn(ctx);
            if (offering.Done)
            {
                Report(ctx, OfferingAct.ActCode, offering.Detail);
            }
            foreach (var (code, result) in Escalation.OnDawn(ctx))
            {
                ReportEscalation(ctx, code, result);
            }
        }
    }

    private void ReportEscalation(HarassContext ctx, string code, ActResult result)
    {
        if (result.Done)
        {
            Report(ctx, code, result.Detail);
        }
        else
        {
            Mod.Logger.Debug("Harassment: {0} skipped for {1}: {2}", code, VictimKey(ctx), result.Detail);
        }
    }

    /// <summary>Hurt lately (a fight), and the tiredness they lay down with.</summary>
    private void WatchVictim(HarassContext ctx)
    {
        var state = ctx.State;
        int counter = ctx.Victim.WatchedAttributes.GetInt("onHurtCounter");
        if (state.LastHurtCounter >= 0 && counter != state.LastHurtCounter)
        {
            state.LastHurtMs = Api.World.ElapsedMilliseconds;
        }
        state.LastHurtCounter = counter;
        bool asleep = StalkerModSystem.IsAsleep(ctx.Victim);
        if (asleep && !state.WasAsleep)
        {
            state.TirednessAtBed = SleepDisturbAct.ReadTiredness(ctx.Victim);
        }
        else if (!asleep && state.WasAsleep)
        {
            state.LastWakeMs = Api.World.ElapsedMilliseconds;
        }
        state.WasAsleep = asleep;
        ctx.Harass.SetBool(HarassMemory.Enraged, Enraged.Is(ctx));
    }

    /// <summary>S1 (first sighting, stage 1) and S2 (first slip-away when watched, stage 2), read from the stalker AI.</summary>
    private void WatchPageTriggers(HarassContext ctx)
    {
        if (ctx.Stage == 1 && ctx.Brain.Ai.GetInt(StalkerMemory.Sightings) > 0)
        {
            Pages.OnTrigger(ctx, PageTrigger.S1);
        }
        else if (ctx.Stage == 2 && ctx.Brain.SlipAwayReason is "watched" or "approached")
        {
            Pages.OnTrigger(ctx, PageTrigger.S2);
        }
    }

    private bool IsStorm() => Api.ModLoader.GetModSystem<SystemTemporalStability>()?.StormData?.nowStormActive == true;

    /// <summary>Why no act may fire now, or null: a fight, a storm, or company from stage 4.</summary>
    public string? Blocked(HarassContext ctx)
    {
        long now = Api.World.ElapsedMilliseconds;
        if (now - ctx.State.LastHurtMs < FightMs || ctx.Brain.MsSincePlayerHit < FightMs)
        {
            return "the victim is in a fight";
        }
        if (IsStorm())
        {
            return "a temporal storm";
        }
        if (ctx.Stage >= 4 && Stalker.CountingNear(ctx.Victim.Pos.XYZ, ctx.Victim.Pos.Dimension, CompanyRange).Any(e => e.EntityId != ctx.Victim.EntityId))
        {
            return "the victim has company";
        }
        return null;
    }

    // ---- The scheduler ----

    /// <summary>
    /// Spends tonight's budget (harass.actsPerNight for the stage), at least harass.minGapSeconds plus a
    /// random 0 to 120 seconds apart, drawn from the acts the stage allows, weighted toward acts new at
    /// this stage, never the same act twice in a row, with one quiet stretch of 5 real minutes a night.
    /// </summary>
    private void Schedule(HarassContext ctx)
    {
        var harass = ctx.Harass;
        int budget = Config.Harass.ActsPerNight.For(ctx.Stage);
        int spent = harass.GetInt(HarassMemory.Spent);
        long now = Api.World.ElapsedMilliseconds;
        if (spent >= budget || now < ctx.State.NextActMs || Blocked(ctx) != null)
        {
            return;
        }
        string last = harass.GetString(HarassMemory.LastAct, "");
        var candidates = acts.Where(a => a.Scheduled && a.Code != last && a.Allowed(ctx) && a.Weight(ctx) > 0).ToList();
        for (int attempt = 0; attempt < 3 && candidates.Count > 0; attempt++)
        {
            var act = Draw(ctx, candidates);
            candidates.Remove(act);
            var result = act.Run(ctx);
            if (!result.Done)
            {
                Mod.Logger.Debug("Harassment: {0} skipped for {1}: {2}", act.Code, VictimKey(ctx), result.Detail);
                continue;
            }
            Count(ctx, act.Code, result.Detail, spendBudget: true);
            long gap = Config.Harass.MinGapSeconds * 1000L + Rand.Next(120001);
            if (!harass.GetBool(HarassMemory.QuietDone) && Rand.NextDouble() < 1.0 / Math.Max(1, budget - spent - 1))
            {
                harass.SetBool(HarassMemory.QuietDone, true);
                gap = Math.Max(gap, QuietMs + Rand.Next(60001));
            }
            ctx.State.NextActMs = now + gap;
            return;
        }
        ctx.State.NextActMs = now + RetryMs;
    }

    private HarassAct Draw(HarassContext ctx, List<HarassAct> candidates)
    {
        double total = candidates.Sum(a => a.Weight(ctx));
        double roll = Rand.NextDouble() * total;
        foreach (var act in candidates)
        {
            roll -= act.Weight(ctx);
            if (roll <= 0)
            {
                return act;
            }
        }
        return candidates[^1];
    }

    /// <summary>
    /// Runs one act now, outside the timer but under its own rules: its switch, stage, time of day and
    /// cap, and whatever the act itself checks. For <c>/walkers harass act</c> and the self-test.
    /// </summary>
    public ActResult RunNow(HarassContext ctx, HarassAct act)
    {
        if (!act.Enabled(ctx.Config))
        {
            return ActResult.Skip("switched off");
        }
        if (!act.Allowed(ctx))
        {
            return ActResult.Skip(HarassMemory.Count(ctx.Harass, act.Code) >= act.Cap(ctx.Config)
                ? $"tonight's cap ({act.Cap(ctx.Config)}) is reached"
                : $"not allowed at stage {ctx.Stage} {(ctx.Night ? "at night" : "by day")}");
        }
        var result = act.Run(ctx);
        if (result.Done)
        {
            Count(ctx, act.Code, result.Detail, spendBudget: true);
        }
        return result;
    }

    private void Count(HarassContext ctx, string code, string detail, bool spendBudget)
    {
        HarassMemory.AddCount(ctx.Harass, code);
        if (spendBudget)
        {
            ctx.Harass.SetInt(HarassMemory.Spent, ctx.Harass.GetInt(HarassMemory.Spent) + 1);
            ctx.Harass.SetString(HarassMemory.LastAct, code);
        }
        Report(ctx, code, detail);
    }

    /// <summary>An act reached the victim: the ladder counts it (ladder.stageMinActs), and it is logged.</summary>
    private void Report(HarassContext ctx, string code, string detail)
    {
        if (director != null && director.TryGetWalkerId(ctx.Body, out int id))
        {
            director.ReportAct(id, code);
        }
        else
        {
            Stalker.Ladder.OnAct(ctx.Memory, code);
        }
        Log(ctx, code, detail);
    }

    /// <summary>Writes an act to the server log at debug level and keeps the last five per victim for the status command.</summary>
    public void Log(HarassContext ctx, string code, string detail)
    {
        Mod.Logger.Debug("Harassment: {0} on {1} (stage {2}): {3}", code, VictimKey(ctx), ctx.Stage, detail);
        AddRecent(ctx.State, $"{code}: {detail}");
    }

    public void NotePageDropped(string key, string what)
    {
        if (victims.TryGetValue(key, out var state))
        {
            AddRecent(state, $"{PagesAct.ActCode}: {what} dropped");
        }
    }

    private void AddRecent(VictimState state, string line)
    {
        var hours = Api.World.Calendar.TotalHours;
        state.Recent.AddLast($"day {Api.World.Calendar.TotalDays:0.0}, {line}");
        while (state.Recent.Count > RecentKept)
        {
            state.Recent.RemoveFirst();
        }
    }

    // ---- Echo buffers ----

    private void Remember(IServerPlayer? player, EchoEntry entry)
    {
        if (player == null || !Config.Harass.Echoes.Enabled)
        {
            return;
        }
        if (!echoes.TryGetValue(player.PlayerUID, out var buffer))
        {
            echoes[player.PlayerUID] = buffer = new EchoBuffer();
        }
        buffer.Add(entry, Config.Harass.Echoes.Buffer);
    }

    private void OnUseBlock(IServerPlayer byPlayer, BlockSelection blockSel)
    {
        var pos = blockSel.Position;
        var block = Api.World.BlockAccessor.GetBlock(pos);
        var be = Api.World.BlockAccessor.GetBlockEntity(pos);
        AssetLocation? sound = null;
        if (Doors.From(be) is { } door)
        {
            sound = door.Door != null
                ? (door.Opened ? door.Door.doorBh?.OpenSound : door.Door.doorBh?.CloseSound)
                : HarassSounds.DoorSound;
        }
        else if (be is BlockEntityOpenableContainer container)
        {
            sound = container.OpenSound.Location;
        }
        if (sound != null)
        {
            Remember(byPlayer, new EchoEntry(EchoKind.Used, block.Id, pos.Copy(), sound, Api.World.Calendar.TotalHours));
        }
    }

    private void OnBreakBlock(IServerPlayer byPlayer, int oldBlockId, BlockSelection blockSel)
    {
        var block = Api.World.GetBlock(oldBlockId);
        var sound = block?.Sounds?.Break.Location ?? HarassSounds.DestructFallback;
        Remember(byPlayer, new EchoEntry(EchoKind.Broke, oldBlockId, blockSel.Position.Copy(), sound, Api.World.Calendar.TotalHours));
    }

    private void OnPlaceBlock(IServerPlayer byPlayer, int oldBlockId, BlockSelection blockSel, ItemStack withItemStack)
    {
        var block = Api.World.BlockAccessor.GetBlock(blockSel.Position);
        var sound = block.Sounds?.Place.Location ?? HarassSounds.BuildFallback;
        Remember(byPlayer, new EchoEntry(EchoKind.Placed, block.Id, blockSel.Position.Copy(), sound, Api.World.Calendar.TotalHours));
    }

    // ---- For the commands ----

    /// <summary>The state of a victim, if the kit has seen them.</summary>
    public VictimState? FindState(string key) => victims.TryGetValue(key, out var s) ? s : null;

    public int EchoBuffers => echoes.Count;
}
