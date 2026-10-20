using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Cake.Common;
using Cake.Common.Diagnostics;
using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Clean;
using Cake.Common.Tools.DotNet.Publish;
using Cake.Core;
using Cake.Frosting;
using Cake.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace CakeBuild;

public static class Program
{
    public static int Main(string[] args)
    {
        return new CakeHost()
            .UseContext<BuildContext>()
            .Run(args);
    }
}

public class BuildContext : FrostingContext
{
    public const string ProjectName = "Walkers";
    public string BuildConfiguration { get; }
    public string Version { get; }
    public string Name { get; }
    public bool SkipJsonValidation { get; }

    public BuildContext(ICakeContext context)
        : base(context)
    {
        BuildConfiguration = context.Argument("configuration", "Release");
        SkipJsonValidation = context.Argument("skipJsonValidation", false);
        var modInfo = context.DeserializeJsonFromFile<ModInfo>($"../{ProjectName}/modinfo.json");
        Version = modInfo.Version;
        Name = modInfo.ModID;
    }
}

[TaskName("ValidateJson")]
public sealed class ValidateJsonTask : FrostingTask<BuildContext>
{
    // The game parses assets with Newtonsoft as well and keeps the LAST of two equal keys without a word.
    private static readonly JsonLoadSettings StrictLoad = new()
    {
        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
    };

    public override void Run(BuildContext context)
    {
        if (context.SkipJsonValidation)
        {
            return;
        }
        var errors = new List<string>();
        var assetsRoot = Path.GetFullPath($"../{BuildContext.ProjectName}/assets");
        // Lang files can be split per feature (lang/<feature>/en.json) and the game merges them, so a
        // key in two files of one language silently keeps only one value.
        var langKeyFiles = new Dictionary<string, string>();
        var jsonFiles = context.GetFiles($"../{BuildContext.ProjectName}/assets/**/*.json")
            .Concat(context.GetFiles($"../{BuildContext.ProjectName}/*.json"));
        foreach (var file in jsonFiles)
        {
            var text = File.ReadAllText(file.FullPath);
            var parts = Path.GetRelativePath(assetsRoot, file.FullPath).Replace('\\', '/').Split('/');
            var isLang = parts.Length >= 3 && parts[1] == "lang";

            JToken token;
            try
            {
                token = JToken.Parse(text);
            }
            catch (JsonException ex)
            {
                errors.Add($"{file.FullPath}: {ex.Message}");
                continue;
            }
            try
            {
                JToken.Parse(text, StrictLoad);
            }
            catch (JsonException ex)
            {
                // In a lang file a repeated key is a mistake. Vanilla assets copied as a starting point
                // sometimes repeat a key on purpose, so elsewhere it is only a warning.
                var message = $"{file.FullPath}: {ex.Message} The game uses the last value.";
                if (isLang)
                    errors.Add(message);
                else
                    context.Warning("{0}", message);
            }

            if (isLang && token is JObject lang)
            {
                // Keyed the way the game keys them: <lang>.json and worldconfig-<lang>.json share one
                // table, and a key without a domain gets the folder's domain in front.
                var langCode = Path.GetFileNameWithoutExtension(parts[^1]);
                if (langCode.StartsWith("worldconfig-"))
                    langCode = langCode["worldconfig-".Length..];
                foreach (var key in lang.Properties().Select(p => p.Name))
                {
                    var fullKey = key.Contains(':') ? key : $"{parts[0]}:{key}";
                    var id = $"{langCode}|{fullKey}";
                    if (langKeyFiles.TryGetValue(id, out var firstFile))
                        errors.Add($"{file.FullPath}: lang key \"{fullKey}\" is also in {firstFile}");
                    else
                        langKeyFiles[id] = file.FullPath;
                }
            }
        }
        if (errors.Count > 0)
        {
            throw new Exception($"JSON validation failed:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}");
        }
    }
}

[TaskName("ValidateModInfo")]
public sealed class ValidateModInfoTask : FrostingTask<BuildContext>
{
    // The version pattern of https://moddbcdn.vintagestory.at/schema/modinfo.latest.json
    private static readonly Regex VersionPattern = new(@"^\d+\.\d+\.\d+(-(pre|rc|dev)\.\d+)?$");

    public override void Run(BuildContext context)
    {
        var projectDir = $"../{BuildContext.ProjectName}";
        var modInfo = JObject.Parse(File.ReadAllText($"{projectDir}/modinfo.json"));
        var modId = (string?)(modInfo["modid"] ?? modInfo["modId"]) ?? "";
        var errors = new List<string>();

        if (!Regex.IsMatch(modId, "^[a-z0-9]+$"))
            errors.Add($"modid \"{modId}\" must be lowercase letters and digits only");
        if (!VersionPattern.IsMatch((string?)modInfo["version"] ?? ""))
            errors.Add($"version \"{modInfo["version"]}\" must look like 1.2.3 or 1.2.3-pre.1 (pre, rc or dev)");
        if ((string?)modInfo["type"] != "Code")
            errors.Add("type must be \"Code\": the mod ships a DLL");
        if ((string?)modInfo["side"] != "Universal")
            errors.Add("side must be \"Universal\": clients need the shapes, textures and sounds");
        if (modInfo["dependencies"]?["game"] == null)
            errors.Add("dependencies must name the game version the mod was built against");

        // Every asset folder is a domain. Only our own and "game" (for worldconfig lang files) belong here,
        // so a misspelt folder fails here instead of loading as a stray domain.
        var assetsDir = $"{projectDir}/assets";
        if (context.DirectoryExists(assetsDir))
        {
            if (!context.DirectoryExists($"{assetsDir}/{modId}"))
                errors.Add($"assets/{modId} is missing; the asset folder must match the modid");
            foreach (var dir in Directory.GetDirectories(assetsDir).Select(Path.GetFileName))
            {
                if (dir != modId && dir != "game")
                    errors.Add($"assets/{dir} is not the mod's domain ({modId}); move it under assets/{modId}");
            }
        }

        if (errors.Count > 0)
        {
            throw new Exception($"modinfo.json check failed:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}");
        }
    }
}

[TaskName("Build")]
[IsDependentOn(typeof(ValidateJsonTask))]
[IsDependentOn(typeof(ValidateModInfoTask))]
public sealed class BuildTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        context.DotNetClean($"../{BuildContext.ProjectName}/{BuildContext.ProjectName}.csproj",
            new DotNetCleanSettings
            {
                Configuration = context.BuildConfiguration
            });


        context.DotNetPublish($"../{BuildContext.ProjectName}/{BuildContext.ProjectName}.csproj",
            new DotNetPublishSettings
            {
                Configuration = context.BuildConfiguration
            });
    }
}

[TaskName("Package")]
[IsDependentOn(typeof(BuildTask))]
public sealed class PackageTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        var projectDir = $"../{BuildContext.ProjectName}";
        var packageDir = $"../Releases/{context.Name}";
        context.EnsureDirectoryExists("../Releases");
        context.CleanDirectory("../Releases");
        context.EnsureDirectoryExists(packageDir);
        context.CopyFiles($"{projectDir}/bin/{context.BuildConfiguration}/Mods/mod/publish/*", packageDir);
        if (context.DirectoryExists($"{projectDir}/assets"))
        {
            context.CopyDirectory($"{projectDir}/assets", $"{packageDir}/assets");
            // .gitkeep files only hold empty folders in git; the game would see them as assets.
            context.DeleteFiles($"{packageDir}/assets/**/.gitkeep");
        }
        context.CopyFile($"{projectDir}/modinfo.json", $"{packageDir}/modinfo.json");
        if (context.FileExists($"{projectDir}/modicon.png"))
        {
            context.CopyFile($"{projectDir}/modicon.png", $"{packageDir}/modicon.png");
        }
        // A zip mod reads world-config attributes only from worldconfig.json at the zip root.
        if (context.FileExists($"{projectDir}/worldconfig.json"))
        {
            context.CopyFile($"{projectDir}/worldconfig.json", $"{packageDir}/worldconfig.json");
        }
        context.Zip(packageDir, $"../Releases/{context.Name}_{context.Version}.zip");
    }
}

[TaskName("Default")]
[IsDependentOn(typeof(PackageTask))]
public class DefaultTask : FrostingTask
{
}
