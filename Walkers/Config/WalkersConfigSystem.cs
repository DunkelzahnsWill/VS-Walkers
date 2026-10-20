using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Walkers.Core;

namespace Walkers.Config;

/// <summary>
/// Owns the live <see cref="WalkersConfig"/>. The server reads ModConfig/walkers.json before any other
/// Walkers system starts, so every system can read the config from Start onward, and sends a copy to
/// each client as it joins. <c>/walkers reload</c> and <c>/walkers config</c> change the values in place:
/// the root object and its section objects stay the same instances for the whole session.
/// </summary>
public class WalkersConfigSystem : ModSystem
{
    private const string ChannelName = WalkersModSystem.ModId + "-config";

    private ICoreServerAPI? sapi;
    private IServerNetworkChannel? serverChannel;

    /// <summary>The live config. On a client it holds the defaults until the server's copy arrives.</summary>
    public WalkersConfig Config { get; } = new();

    /// <summary>The full path of ModConfig/walkers.json.</summary>
    public static string FilePath => Path.Combine(GamePaths.ModConfig, WalkersConfig.FileName);

    /// <summary>
    /// Raised after the values changed: on the server after a reload or a config command, on a client
    /// when the server's copy arrives. The list holds each changed setting as "path old → new".
    /// Subscribe only if your system derives something from the values (a cached list, a timer).
    /// </summary>
    public event Action<WalkersConfig, IReadOnlyList<string>>? Changed;

    /// <summary>What the last load or reload found wrong in the file, for /walkers status. Empty when clean.</summary>
    public IReadOnlyList<string> Problems { get; private set; } = Array.Empty<string>();

    // Lower runs first; the game's default is 0.1.
    public override double ExecuteOrder() => 0.01;

    public override void StartPre(ICoreAPI api)
    {
        if (api.Side == EnumAppSide.Server)
        {
            sapi = (ICoreServerAPI)api;
            Load();
        }
    }

    public override void Start(ICoreAPI api)
    {
        api.Network.RegisterChannel(ChannelName).RegisterMessageType<ConfigPacket>();
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        serverChannel = api.Network.GetChannel(ChannelName);
        api.Event.PlayerJoin += player => serverChannel.SendPacket(new ConfigPacket { Json = WalkersConfigJson.Serialize(Config) }, player);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        api.Network.GetChannel(ChannelName).SetMessageHandler<ConfigPacket>(OnServerConfig);
    }

    /// <summary>
    /// Reads the file again and applies it. A file that cannot be read leaves the current values as
    /// they are. Returns what changed and what was wrong, for the command reply.
    /// </summary>
    public ReloadOutcome Reload()
    {
        var before = WalkersConfigJson.ToJObject(Config);
        var result = ReadFile(out bool missing, out string? text);
        if (result.Config == null)
        {
            Mod.Logger.Warning("Reload of ModConfig/{0} failed, keeping the current settings: {1}", WalkersConfig.FileName, result.FatalError);
            Problems = new[] { "The last reload failed, so the settings before it still apply: " + result.FatalError };
            return new ReloadOutcome(false, Array.Empty<string>(), Problems);
        }
        var problems = Report(result);
        WriteBackIfClean(result, missing, text);
        var changes = Apply(result.Config, before);
        Mod.Logger.Notification("Reloaded ModConfig/{0}: {1} setting(s) changed, {2} problem(s)", WalkersConfig.FileName, changes.Count, problems.Count);
        return new ReloadOutcome(true, changes, problems);
    }

    public sealed record ReloadOutcome(bool Applied, IReadOnlyList<string> Changes, IReadOnlyList<string> Problems);

    /// <summary>
    /// Sets one setting by its path: edits that key in the file, leaving the rest of the file as the
    /// admin wrote it, then reloads. Returns an error message, or null on success.
    /// </summary>
    public string? SetInFile(WalkersConfigJson.Setting setting, JToken value, out ReloadOutcome? outcome)
    {
        outcome = null;
        JObject file;
        if (File.Exists(FilePath))
        {
            var text = File.ReadAllText(FilePath);
            var read = WalkersConfigJson.Read(text);
            if (read.Config == null)
            {
                return "the file cannot be read (" + read.FatalError + "); fix it by hand, then /walkers reload";
            }
            if (read.HasComments)
            {
                return "the file has comments, which a command would remove; edit it by hand, then /walkers reload";
            }
            file = JObject.Parse(text);
        }
        else
        {
            file = WalkersConfigJson.ToJObject(Config);
        }

        WalkersConfigJson.SetPath(file, setting.Path, value);
        WalkersConfigJson.WriteFile(FilePath, file.ToString());
        outcome = Reload();
        return null;
    }

    private void Load()
    {
        var result = ReadFile(out bool missing, out string? text);
        if (result.Config == null)
        {
            // Leave the broken file for the admin to fix and run on defaults meanwhile.
            Mod.Logger.Error("Could not read ModConfig/{0}, using the defaults until it is fixed and reloaded: {1}", WalkersConfig.FileName, result.FatalError);
            Problems = new[] { result.FatalError! };
            return;
        }
        Report(result);
        WriteBackIfClean(result, missing, text);
        WalkersConfigJson.CopyInto(Config, result.Config);
        // tests/smoke/config.expect checks this line.
        Mod.Logger.Notification("Config loaded from ModConfig/{0}: {1}, {2} problem(s)", WalkersConfig.FileName, missing ? "new file" : "existing file", Problems.Count);
    }

    /// <summary>Reads the file. A missing or empty file counts as missing: the defaults, written out.</summary>
    private WalkersConfigJson.ReadResult ReadFile(out bool missing, out string? text)
    {
        text = null;
        try
        {
            if (File.Exists(FilePath))
            {
                text = File.ReadAllText(FilePath);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            missing = false;
            return new WalkersConfigJson.ReadResult { FatalError = e.Message };
        }
        missing = string.IsNullOrWhiteSpace(text);
        if (!missing)
        {
            return WalkersConfigJson.Read(text!);
        }
        var defaults = new WalkersConfigJson.ReadResult { Config = new WalkersConfig() };
        WalkersConfigJson.Check(defaults.Config, "", defaults.Fixes);
        return defaults;
    }

    /// <summary>Logs each problem as a warning and remembers them for /walkers status.</summary>
    private List<string> Report(WalkersConfigJson.ReadResult result)
    {
        var problems = new List<string>();
        problems.AddRange(result.Errors.Select(e => "Unreadable value, using its default: " + e));
        problems.AddRange(result.UnknownKeys.Select(k => "Unknown setting, ignored: " + k));
        problems.AddRange(result.Fixes);
        foreach (var problem in problems)
        {
            Mod.Logger.Warning("ModConfig/{0}: {1}", WalkersConfig.FileName, problem);
        }
        Problems = problems;
        return problems;
    }

    /// <summary>
    /// Writes the default file when there is none, and adds settings the file leaves out (new since it
    /// was written) with their defaults. Only a clean file gets keys added, and everything else in it
    /// stays as the admin wrote it; a file with problems or comments is never touched.
    /// </summary>
    private void WriteBackIfClean(WalkersConfigJson.ReadResult result, bool missing, string? text)
    {
        try
        {
            if (missing)
            {
                WalkersConfigJson.WriteFile(FilePath, WalkersConfigJson.Serialize(result.Config!));
                Mod.Logger.Notification("Wrote the default ModConfig/{0}", WalkersConfig.FileName);
            }
            else if (result.Clean && result.MissingKeys.Count > 0 && text != null)
            {
                var file = JObject.Parse(text);
                var full = WalkersConfigJson.ToJObject(result.Config!);
                foreach (var path in result.MissingKeys)
                {
                    WalkersConfigJson.SetPath(file, path, WalkersConfigJson.GetPath(full, path)!.DeepClone());
                }
                WalkersConfigJson.WriteFile(FilePath, file.ToString());
                Mod.Logger.Notification("Added new settings to ModConfig/{0}: {1}", WalkersConfig.FileName, string.Join(", ", result.MissingKeys));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Mod.Logger.Warning("Could not write ModConfig/{0}: {1}", WalkersConfig.FileName, e.Message);
        }
    }

    private List<string> Apply(WalkersConfig loaded, JObject before)
    {
        var changes = WalkersConfigJson.Diff(before, WalkersConfigJson.ToJObject(loaded));
        WalkersConfigJson.CopyInto(Config, loaded);
        if (changes.Count > 0)
        {
            serverChannel?.BroadcastPacket(new ConfigPacket { Json = WalkersConfigJson.Serialize(Config) });
            Changed?.Invoke(Config, changes);
        }
        return changes;
    }

    private void OnServerConfig(ConfigPacket packet)
    {
        var result = WalkersConfigJson.Read(packet.Json ?? "");
        if (result.Config == null)
        {
            Mod.Logger.Warning("Could not read the server's config, keeping the defaults: {0}", result.FatalError);
            return;
        }
        var before = WalkersConfigJson.ToJObject(Config);
        var changes = WalkersConfigJson.Diff(before, WalkersConfigJson.ToJObject(result.Config));
        WalkersConfigJson.CopyInto(Config, result.Config);
        Changed?.Invoke(Config, changes);
    }

    /// <summary>The server's config as JSON, sent to clients. Public because protobuf-net builds it.</summary>
    [ProtoContract]
    public class ConfigPacket
    {
        [ProtoMember(1)]
        public string? Json { get; set; }
    }
}

public static class WalkersConfigExtensions
{
    /// <summary>
    /// The live config. Fetch it once in your system's Start and keep the reference: reloads change its
    /// values in place, so read settings from it at the moment you use them, e.g.
    /// <c>config.Harass.Doors.Enabled</c>. Server side it is the file's values; client side it holds the
    /// defaults until the server's copy arrives, a moment after joining.
    /// </summary>
    public static WalkersConfig GetWalkersConfig(this ICoreAPI api) =>
        api.ModLoader.GetModSystem<WalkersConfigSystem>().Config;
}
