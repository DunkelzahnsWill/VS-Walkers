using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Walkers.Config;
using Walkers.Core;

namespace Walkers.Commands;

/// <summary>
/// The /walkers chat and console command. Admin subcommands need the controlserver privilege;
/// <c>/walkers me</c> is for every player.
///
/// Other features add their own subcommands from their own ModSystem, without touching this file:
/// <code>
/// api.ChatCommands.GetOrCreate(WalkersModSystem.ModId)
///     .BeginSubCommand("ledger").WithDescription("...").HandleWith(OnLedger).EndSubCommand();
/// </code>
/// A subcommand that sets no privilege inherits controlserver from the root. To add a line to
/// <c>/walkers status</c>, subscribe to <see cref="Status"/>.
/// </summary>
public class WalkersCommandsSystem : ModSystem
{
    /// <summary>Every creature of the mod has an entity code in the walkers domain starting with this.</summary>
    public const string CreaturePrefix = "skinwalker-";

    /// <summary>How far in front of the player /walkers spawn puts the creature, in blocks.</summary>
    private const int SpawnDistance = 8;

    private const int MaxListedWalkers = 20;

    private ICoreServerAPI api = null!;
    private WalkersConfigSystem configSystem = null!;

    /// <summary>
    /// Raised by /walkers status. Add one short line per fact worth showing an admin, such as
    /// "Director: 2 haunts active". Runs on the server main thread.
    /// </summary>
    public event Action<List<string>>? Status;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        this.api = api;
        configSystem = api.ModLoader.GetModSystem<WalkersConfigSystem>();
        var parsers = api.ChatCommands.Parsers;

        api.ChatCommands.GetOrCreate(WalkersModSystem.ModId)
            .WithDescription(Lang.Get("walkers:commands-desc"))
            .RequiresPrivilege(Privilege.controlserver)
            .BeginSubCommand("status")
                .WithDescription(Lang.Get("walkers:commands-status-desc"))
                .HandleWith(SafeReply(OnStatus))
            .EndSubCommand()
            .BeginSubCommand("spawn")
                .WithDescription(Lang.Get("walkers:commands-spawn-desc"))
                .WithExamples("/walkers spawn true", "/walkers spawn animal", "/walkers spawn animal-whitetail SomePlayer")
                .WithArgs(parsers.Word("form", new[] { "animal", "true" }), parsers.OptionalWord("player"))
                .HandleWith(SafeReply(OnSpawn))
            .EndSubCommand()
            .BeginSubCommand("despawn")
                .WithDescription(Lang.Get("walkers:commands-despawn-desc"))
                .WithExamples("/walkers despawn", "/walkers despawn 32")
                .WithArgs(parsers.OptionalIntRange("radius", 1, 4096))
                .HandleWith(SafeReply(OnDespawn))
            .EndSubCommand()
            .BeginSubCommand("reload")
                .WithDescription(Lang.Get("walkers:commands-reload-desc"))
                .HandleWith(SafeReply(OnReload))
            .EndSubCommand()
            .BeginSubCommand("config")
                .WithDescription(Lang.Get("walkers:commands-config-desc"))
                .WithExamples("/walkers config", "/walkers config harass", "/walkers config harass.doors.enabled false")
                .WithArgs(parsers.OptionalWord("setting"), parsers.OptionalAll("value"))
                .HandleWith(SafeReply(OnConfig))
            .EndSubCommand()
            .BeginSubCommand("me")
                .WithDescription(Lang.Get("walkers:commands-me-desc"))
                .WithExamples("/walkers me", "/walkers me noName on", "/walkers me scareVolume 0.5")
                .RequiresPrivilege(Privilege.chat)
                .RequiresPlayer()
                .WithArgs(parsers.OptionalWord("setting"), parsers.OptionalAll("value"))
                .HandleWith(SafeReply(OnMe))
            .EndSubCommand();
    }

    private TextCommandResult OnStatus(TextCommandCallingArgs args)
    {
        var config = configSystem.Config;
        var lines = new List<string>
        {
            L(args, "commands-status-header", Mod.Info.Version, L(args, config.Enabled ? "commands-on" : "commands-off"), WalkersConfigJson.FormatValue(config.MundaneMode)),
            L(args, "commands-status-config", WalkersConfig.FileName, configSystem.Problems.Count),
        };
        lines.AddRange(configSystem.Problems.Select(p => "  " + p));

        var walkers = LoadedWalkers().ToList();
        lines.Add(L(args, "commands-status-walkers", walkers.Count));
        var from = args.Caller.Pos;
        foreach (var entity in walkers.OrderBy(e => from == null ? 0 : e.Pos.DistanceTo(from)).Take(MaxListedWalkers))
        {
            var local = entity.Pos.AsBlockPos.ToLocalPosition(api);
            var line = $"  {entity.Code.Path} #{entity.EntityId} at {local.X}, {local.Y}, {local.Z}";
            if (from != null)
            {
                line += L(args, "commands-status-distance", (int)entity.Pos.DistanceTo(from));
            }
            lines.Add(line);
        }
        if (walkers.Count > MaxListedWalkers)
        {
            lines.Add(L(args, "commands-status-more", walkers.Count - MaxListedWalkers));
        }

        if (Status != null)
        {
            foreach (Action<List<string>> handler in Status.GetInvocationList())
            {
                try
                {
                    handler(lines);
                }
                catch (Exception e)
                {
                    Mod.Logger.Error("A /walkers status handler failed: {0}", e);
                }
            }
        }
        return TextCommandResult.Success(string.Join("\n", lines));
    }

    private TextCommandResult OnSpawn(TextCommandCallingArgs args)
    {
        var form = ((string)args[0]).ToLowerInvariant();
        var playerName = (string?)args[1];

        var candidates = api.World.EntityTypes
            .Where(t => t.Code.Domain == WalkersModSystem.ModId &&
                        (t.Code.Path == CreaturePrefix + form || t.Code.Path.StartsWith(CreaturePrefix + form + "-", StringComparison.Ordinal)))
            .ToList();
        if (candidates.Count == 0)
        {
            var known = api.World.EntityTypes
                .Where(t => t.Code.Domain == WalkersModSystem.ModId && t.Code.Path.StartsWith(CreaturePrefix, StringComparison.Ordinal))
                .Select(t => t.Code.Path[CreaturePrefix.Length..])
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();
            return TextCommandResult.Error(known.Count == 0
                ? L(args, "commands-spawn-none-loaded")
                : L(args, "commands-spawn-unknown-form", form, string.Join(", ", known)));
        }
        var type = candidates[api.World.Rand.Next(candidates.Count)];

        IServerPlayer? near;
        if (playerName != null)
        {
            near = api.World.AllOnlinePlayers.OfType<IServerPlayer>()
                .FirstOrDefault(p => string.Equals(p.PlayerName, playerName, StringComparison.OrdinalIgnoreCase));
            if (near?.Entity == null)
            {
                return TextCommandResult.Error(L(args, "commands-no-such-player", playerName));
            }
        }
        else
        {
            near = args.Caller.Player as IServerPlayer;
        }

        EntityPos origin;
        BlockPos start;
        if (near?.Entity != null)
        {
            // A few blocks in front of the player, where they are looking.
            origin = near.Entity.Pos;
            var view = origin.GetViewVector();
            var flat = new Vec3d(view.X, 0, view.Z);
            flat = flat.Length() < 0.01 ? new Vec3d(0, 0, 1) : flat.Normalize();
            start = new BlockPos((int)Math.Floor(origin.X + flat.X * SpawnDistance), (int)origin.Y, (int)Math.Floor(origin.Z + flat.Z * SpawnDistance), origin.Dimension);
        }
        else
        {
            // From the console without a player: the world's spawn point, on the ground.
            origin = api.World.DefaultSpawnPosition;
            start = origin.AsBlockPos;
            if (api.World.BlockAccessor.GetChunkAtBlockPos(start) == null)
            {
                return TextCommandResult.Error(L(args, "commands-spawn-not-loaded"));
            }
            start.Y = api.World.BlockAccessor.GetTerrainMapheightAt(start) + 1;
        }

        var spot = FindStandingSpot(start);
        if (spot == null)
        {
            return TextCommandResult.Error(L(args, "commands-spawn-no-spot"));
        }

        var entity = api.World.ClassRegistry.CreateEntity(type);
        entity.Pos.Dimension = origin.Dimension;
        entity.Pos.X = spot.X;
        entity.Pos.Y = spot.Y;
        entity.Pos.Z = spot.Z;
        entity.Pos.Pitch = 0;
        // Face whoever it was spawned for (the game's yaw is atan2 of x over z).
        entity.Pos.Yaw = near?.Entity == null ? 0 : (float)Math.Atan2(origin.X - entity.Pos.X, origin.Z - entity.Pos.Z);
        api.World.SpawnEntity(entity);

        var local = entity.Pos.AsBlockPos.ToLocalPosition(api);
        Mod.Logger.Audit("{0} spawned {1} #{2} at {3}", CallerName(args), type.Code, entity.EntityId, entity.Pos.AsBlockPos);
        return TextCommandResult.Success(L(args, "commands-spawned", type.Code.Path, entity.EntityId, local.X, local.Y, local.Z));
    }

    private TextCommandResult OnDespawn(TextCommandCallingArgs args)
    {
        int? radius = args.Parsers[0].IsMissing ? null : (int)args[0];
        var from = args.Caller.Pos;
        if (radius != null && from == null)
        {
            return TextCommandResult.Error(L(args, "commands-despawn-radius-needs-position"));
        }
        int count = 0;
        foreach (var entity in LoadedWalkers().ToList())
        {
            if (radius != null && entity.Pos.DistanceTo(from!) > radius.Value)
            {
                continue;
            }
            // Removed, not a death: no drops, no death event, so nothing counts it as a kill.
            entity.Die(EnumDespawnReason.Removed);
            count++;
        }
        Mod.Logger.Audit("{0} despawned {1} walker(s)", CallerName(args), count);
        return TextCommandResult.Success(radius == null
            ? L(args, "commands-despawned-all", count)
            : L(args, "commands-despawned-near", count, radius.Value));
    }

    private TextCommandResult OnReload(TextCommandCallingArgs args)
    {
        var outcome = configSystem.Reload();
        Mod.Logger.Audit("{0} reloaded the config", CallerName(args));
        return Outcome(args, outcome, L(args, "commands-reloaded", WalkersConfig.FileName, outcome.Changes.Count));
    }

    private TextCommandResult OnConfig(TextCommandCallingArgs args)
    {
        var path = (string?)args[0] ?? "";
        var text = (string?)args[1];
        var config = configSystem.Config;
        if (path.Length == 0)
        {
            return TextCommandResult.Success(L(args, "commands-config-header", WalkersConfig.FileName) + "\n" + DescribeChildren(config, "", new WalkersConfig()));
        }
        var setting = WalkersConfigJson.Find(config, path);
        if (setting == null)
        {
            return TextCommandResult.Error(L(args, "commands-config-unknown", path));
        }
        var defaults = WalkersConfigJson.Find(new WalkersConfig(), setting.Path);
        if (text == null)
        {
            return TextCommandResult.Success(setting.IsSection
                ? DescribeSetting(setting, defaults) + "\n" + DescribeChildren(setting.Value!, setting.Path, defaults?.Value)
                : DescribeSetting(setting, defaults));
        }
        if (setting.IsSection)
        {
            return TextCommandResult.Error(L(args, "commands-config-section", setting.Path));
        }
        var value = WalkersConfigJson.ParseValue(text, setting.Type, setting.Range, out var error);
        if (value == null)
        {
            return TextCommandResult.Error(L(args, "commands-config-bad-value", setting.Path, error ?? ""));
        }
        error = configSystem.SetInFile(setting, value, out var outcome);
        if (error != null)
        {
            return TextCommandResult.Error(L(args, "commands-config-not-saved", error));
        }
        Mod.Logger.Audit("{0} set {1} to {2}", CallerName(args), setting.Path, value.ToString(Newtonsoft.Json.Formatting.None));
        return Outcome(args, outcome!, L(args, "commands-config-saved", setting.Path, WalkersConfigJson.FormatValue(WalkersConfigJson.Find(config, setting.Path)!.Value)));
    }

    private TextCommandResult OnMe(TextCommandCallingArgs args)
    {
        var player = (IServerPlayer)args.Caller.Player;
        var path = (string?)args[0];
        var text = (string?)args[1];
        var prefs = player.GetWalkersPrefs();
        if (path == null)
        {
            return TextCommandResult.Success(L(args, "commands-me-header") + "\n" + DescribeChildren(prefs, "", new WalkersPlayerPrefs()));
        }
        var setting = WalkersConfigJson.Find(prefs, path);
        if (setting == null || setting.IsSection)
        {
            return TextCommandResult.Error(L(args, "commands-me-unknown", path, string.Join(", ", WalkersConfigJson.Children(prefs, "").Select(s => s.Path))));
        }
        if (text == null)
        {
            return TextCommandResult.Success(DescribeSetting(setting, WalkersConfigJson.Find(new WalkersPlayerPrefs(), setting.Path)));
        }
        var value = WalkersConfigJson.ParseValue(text, setting.Type, setting.Range, out var error);
        if (value == null)
        {
            return TextCommandResult.Error(L(args, "commands-config-bad-value", setting.Path, error ?? ""));
        }
        setting.SetValue(value);
        player.SetWalkersPrefs(prefs);
        return TextCommandResult.Success(L(args, "commands-me-saved", setting.Path, WalkersConfigJson.FormatValue(setting.Value)));
    }

    /// <summary>Every loaded creature of the mod, alive.</summary>
    private IEnumerable<Entity> LoadedWalkers() =>
        api.World.LoadedEntities.Values.Where(e =>
            e is EntityAgent and not EntityPlayer && e.Alive && e.Code?.Domain == WalkersModSystem.ModId);

    /// <summary>
    /// Where a creature can stand in the column of <paramref name="start"/>, searching up and down from
    /// it: the centre of the block at the height of the ground. Null when the column has no such place.
    /// </summary>
    private Vec3d? FindStandingSpot(BlockPos start)
    {
        var accessor = api.World.BlockAccessor;
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

    /// <summary>
    /// The height at which a creature stands with its feet in this block, or null. Either the block is
    /// open and the one below carries it (solid on top, or a slab or path at least half a block high),
    /// or the block holds thin cover it stands on: snow layers, loose stones and sticks. The blocks it
    /// would reach into above must be open.
    /// </summary>
    private static double? StandingHeight(IBlockAccessor accessor, BlockPos pos)
    {
        var block = accessor.GetBlock(pos);
        if (block.IsLiquid())
        {
            return null;
        }
        float top = TopOf(accessor, block, pos);
        if (top > ThinCover || !IsOpen(accessor, pos.UpCopy()) || (top > 0 && !IsOpen(accessor, pos.UpCopy(2))))
        {
            return null;
        }
        if (top > 0)
        {
            return pos.Y + top;
        }
        var belowPos = pos.DownCopy();
        var below = accessor.GetBlock(belowPos);
        if (below.SideSolid[BlockFacing.UP.Index])
        {
            return pos.Y;
        }
        float belowTop = TopOf(accessor, below, belowPos);
        return belowTop >= ThinCover ? belowPos.Y + belowTop : null;
    }

    /// <summary>Blocks whose collision reaches no higher than this are cover to stand on.</summary>
    private const float ThinCover = 0.5f;

    private static float TopOf(IBlockAccessor accessor, Block block, BlockPos pos)
    {
        var boxes = block.GetCollisionBoxes(accessor, pos);
        return boxes == null || boxes.Length == 0 ? 0 : boxes.Max(b => b.Y2);
    }

    private static bool IsOpen(IBlockAccessor accessor, BlockPos pos)
    {
        var block = accessor.GetBlock(pos);
        return !block.IsLiquid() && TopOf(accessor, block, pos) == 0;
    }

    private TextCommandResult Outcome(TextCommandCallingArgs args, WalkersConfigSystem.ReloadOutcome outcome, string headline)
    {
        if (!outcome.Applied)
        {
            return TextCommandResult.Error(L(args, "commands-reload-failed", WalkersConfig.FileName, outcome.Problems.FirstOrDefault() ?? ""));
        }
        var lines = new List<string> { headline };
        lines.AddRange(outcome.Changes.Select(c => "  " + c));
        if (outcome.Problems.Count > 0)
        {
            lines.Add(L(args, "commands-problems", outcome.Problems.Count));
            lines.AddRange(outcome.Problems.Select(p => "  " + p));
        }
        return TextCommandResult.Success(string.Join("\n", lines));
    }

    private static string DescribeSetting(WalkersConfigJson.Setting setting, WalkersConfigJson.Setting? defaults)
    {
        var sb = new StringBuilder(setting.Path);
        if (!setting.IsSection)
        {
            sb.Append(" = ").Append(WalkersConfigJson.FormatValue(setting.Value));
            var details = new List<string>();
            if (defaults != null)
            {
                details.Add("default " + WalkersConfigJson.FormatValue(defaults.Value));
            }
            if (setting.Range is { } range)
            {
                details.Add(string.Format(CultureInfo.InvariantCulture, "{0} to {1}", range.Minimum, range.Maximum));
            }
            else if (setting.Type.IsEnum)
            {
                details.Add(string.Join(" | ", Enum.GetValues(setting.Type).Cast<object>().Select(WalkersConfigJson.FormatValue)));
            }
            if (details.Count > 0)
            {
                sb.Append(" (").Append(string.Join(", ", details)).Append(')');
            }
        }
        if (setting.Description != null)
        {
            sb.Append(setting.IsSection ? ": " : ". ").Append(setting.Description);
        }
        return sb.ToString();
    }

    /// <summary>One line per setting in a section: values for settings, a count for nested sections.</summary>
    private static string DescribeChildren(object section, string path, object? defaults)
    {
        var lines = new List<string>();
        foreach (var child in WalkersConfigJson.Children(section, path))
        {
            if (child.IsSection)
            {
                int count = WalkersConfigJson.Children(child.Value!, child.Path).Count();
                lines.Add($"  {child.Path}: {count} settings");
            }
            else
            {
                var line = $"  {child.Path} = {WalkersConfigJson.FormatValue(child.Value)}";
                var defaultValue = defaults == null ? null : WalkersConfigJson.Find(defaults, child.Path[(path.Length == 0 ? 0 : path.Length + 1)..]);
                if (defaultValue != null && WalkersConfigJson.FormatValue(defaultValue.Value) != WalkersConfigJson.FormatValue(child.Value))
                {
                    line += " (default " + WalkersConfigJson.FormatValue(defaultValue.Value) + ")";
                }
                lines.Add(line);
            }
        }
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Wraps a command handler so its reply reaches the caller exactly as written. The server treats a
    /// reply as a format string twice: it logs a console reply through string.Format, and it runs a
    /// one-line player reply through Lang.GetL. A brace in the text (from a JSON error message, or a
    /// player's own argument) would garble the reply and log an error. Feature subcommands can use it too.
    /// </summary>
    public static OnCommandDelegate SafeReply(OnCommandDelegate handler) => args =>
    {
        var result = handler(args);
        var message = result.StatusMessage;
        if (string.IsNullOrEmpty(message) || result.MessageParams != null)
        {
            return result;
        }
        if (args.Caller.Type == EnumCallerType.Console)
        {
            result.StatusMessage = message.Replace("{", "{{").Replace("}", "}}");
        }
        else if (args.Caller.Player != null && !message.Contains('\n'))
        {
            // Multi-line player replies are sent as they are; one-liners go through Lang.GetL(text, params).
            result.StatusMessage = "{0}";
            result.MessageParams = new object[] { message };
        }
        return result;
    };

    private static string CallerName(TextCommandCallingArgs args) => args.Caller.Player?.PlayerName ?? "The console";

    private static string L(TextCommandCallingArgs args, string key, params object[] values) =>
        Lang.GetL(args.LanguageCode ?? Lang.CurrentLocale, WalkersModSystem.ModId + ":" + key, values);
}
