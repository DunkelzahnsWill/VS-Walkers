using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Walkers.Commands;
using Walkers.Config;
using Walkers.Core;

namespace Walkers.Proto;

/// <summary>
/// The 1.2 prototype (record/road-bible.md, part 2): its switch, proto.enabled, and <c>/walkers proto</c>.
/// While the switch is on, the values of proto.fight and proto.return stand in for the fight settings of the stalker AI (two
/// of them in the gaze rule, as R34 allows) and the forms, and for the return of the director, in the server's live config. The file is never written with them: each time the config is
/// loaded or changed they are laid over it again, and switching off reloads the file, which puts the
/// admin's own values back. Nothing outside Walkers/Proto/ refers to this system or its section.
/// </summary>
public class ProtoModSystem : ModSystem
{
    /// <summary>Each value of proto.fight and proto.return and the setting it stands in for.</summary>
    internal static readonly (string Proto, string Target)[] Overlay =
    {
        ("proto.fight.trueHealth", "forms.trueHealth"),
        ("proto.fight.trueDamage", "forms.trueDamage"),
        ("proto.fight.attackCooldownMs", "forms.attackCooldownMs"),
        ("proto.fight.retreatHealth", "forms.retreatHealth"),
        ("proto.fight.stalkSpeed", "stalk.stalkSpeed"),
        ("proto.fight.chaseSpeed", "stalk.chaseSpeed"),
        ("proto.fight.lungeCooldownSeconds", "stalk.lungeCooldownSeconds"),
        ("proto.fight.lungeRange", "gaze.lungeRange"),
        ("proto.fight.stareAggroSeconds", "gaze.stareAggroSeconds"),
        ("proto.return.afterSeasons", "return.afterSeasons"),
        ("proto.return.paceMultiplier", "return.paceMultiplier"),
    };

    private ICoreServerAPI sapi = null!;
    private WalkersConfigSystem configSystem = null!;

    /// <summary>The values the overlay replaced, by target path: the file's own, for the status reply. Empty while off.</summary>
    private readonly Dictionary<string, object?> replaced = new(StringComparer.Ordinal);

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    // After the config (0.01) and the feature systems, which read the config only when they use a value.
    public override double ExecuteOrder() => 0.5;

    private WalkersConfig Config => configSystem.Config;

    public bool Applied => replaced.Count > 0;

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;
        configSystem = api.ModLoader.GetModSystem<WalkersConfigSystem>();
        configSystem.Changed += (_, _) => Refresh();
        api.ModLoader.GetModSystem<WalkersCommandsSystem>().Status += lines => lines.Add(StatusLine());
        Register(api);
        Refresh();
        // tests/smoke/proto.expect checks this line.
        Mod.Logger.Notification("Prototype loaded, switched {0}", Config.Proto.Enabled ? "on" : "off");
    }

    /// <summary>Lays the overlay over the live config if the switch is on. After a load or change the live config holds the file's values again.</summary>
    private void Refresh()
    {
        replaced.Clear();
        if (!Config.Proto.Enabled)
        {
            return;
        }
        foreach (var (protoPath, targetPath) in Overlay)
        {
            var source = WalkersConfigJson.Find(Config, protoPath);
            var target = WalkersConfigJson.Find(Config, targetPath);
            if (source == null || target == null || target.Type != source.Type)
            {
                Mod.Logger.Error("Prototype: cannot lay {0} over {1}", protoPath, targetPath);
                continue;
            }
            replaced[target.Path] = target.Value;
            target.Property.ValueProvider!.SetValue(target.Owner, source.Value);
        }
        Mod.Logger.Notification("Prototype: {0} setting(s) stand in for the file's", replaced.Count);
    }

    private string StatusLine() => Lang.Get("walkers:proto-status-line", Lang.Get(Config.Proto.Enabled ? "walkers:proto-on" : "walkers:proto-off"));

    private void Register(ICoreServerAPI api)
    {
        api.ChatCommands.GetOrCreate(WalkersModSystem.ModId)
            .BeginSubCommand("proto")
                .WithDescription(Lang.Get("walkers:proto-desc"))
                .HandleWith(WalkersCommandsSystem.SafeReply(OnStatus))
                .BeginSubCommand("status")
                    .WithDescription(Lang.Get("walkers:proto-status-desc"))
                    .HandleWith(WalkersCommandsSystem.SafeReply(OnStatus))
                .EndSubCommand()
                .BeginSubCommand("on")
                    .WithDescription(Lang.Get("walkers:proto-on-desc"))
                    .HandleWith(WalkersCommandsSystem.SafeReply(args => Switch(args, true)))
                .EndSubCommand()
                .BeginSubCommand("off")
                    .WithDescription(Lang.Get("walkers:proto-off-desc"))
                    .HandleWith(WalkersCommandsSystem.SafeReply(args => Switch(args, false)))
                .EndSubCommand()
            .EndSubCommand();
    }

    private TextCommandResult OnStatus(TextCommandCallingArgs args)
    {
        var lines = new List<string> { L(args, "proto-status-header", L(args, Config.Proto.Enabled ? "proto-on" : "proto-off")) };
        foreach (var (protoPath, targetPath) in Overlay)
        {
            var target = WalkersConfigJson.Find(Config, targetPath)!;
            var source = WalkersConfigJson.Find(Config, protoPath)!;
            string fileValue = WalkersConfigJson.FormatValue(replaced.TryGetValue(target.Path, out var old) ? old : target.Value);
            lines.Add(Applied
                ? L(args, "proto-status-fight-on", target.Path, WalkersConfigJson.FormatValue(target.Value), fileValue)
                : L(args, "proto-status-fight-off", target.Path, fileValue, WalkersConfigJson.FormatValue(source.Value)));
        }
        return TextCommandResult.Success(string.Join("\n", lines));
    }

    private TextCommandResult Switch(TextCommandCallingArgs args, bool on)
    {
        if (Config.Proto.Enabled == on)
        {
            return TextCommandResult.Success(L(args, on ? "proto-already-on" : "proto-already-off"));
        }
        var setting = WalkersConfigJson.Find(Config, "proto.enabled")!;
        var error = configSystem.SetInFile(setting, new JValue(on), out _);
        if (error != null)
        {
            return TextCommandResult.Error(L(args, "proto-not-saved", error));
        }
        Mod.Logger.Audit("{0} switched the prototype {1}", args.Caller.Player?.PlayerName ?? "The console", on ? "on" : "off");
        return TextCommandResult.Success(L(args, on ? "proto-switched-on" : "proto-switched-off", replaced.Count));
    }

    private static string L(TextCommandCallingArgs args, string key, params object[] values) =>
        Lang.GetL(args.LanguageCode ?? Lang.CurrentLocale, WalkersModSystem.ModId + ":" + key, values);
}
