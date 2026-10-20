using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Walkers.Config;

/// <summary>
/// Reads, checks and writes <see cref="WalkersConfig"/> as JSON, and finds settings by path. It knows
/// nothing about the game, so the server's file, a reload and the copy sent to clients all go through
/// the same code. It discovers settings by reflection, so feature sections need no registration here.
/// </summary>
public static class WalkersConfigJson
{
    private static readonly DefaultContractResolver Resolver = new()
    {
        NamingStrategy = new CamelCaseNamingStrategy(),
    };

    public static readonly JsonSerializerSettings Settings = new()
    {
        ContractResolver = Resolver,
        // Enums are written as their camelCase name; a number in the file is an error, not a value.
        Converters = { new StringEnumConverter(new CamelCaseNamingStrategy(), allowIntegerValues: false) },
        // A section or array in the file replaces the default instance. Reuse would merge into it,
        // and for arrays and lists that means appending to the defaults on every load.
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        Formatting = Formatting.Indented,
        Culture = CultureInfo.InvariantCulture,
    };

    private static readonly JsonLoadSettings StrictLoad = new()
    {
        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
        CommentHandling = CommentHandling.Ignore,
    };

    private static readonly JsonLoadSettings LenientLoad = new()
    {
        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Replace,
        CommentHandling = CommentHandling.Ignore,
    };

    public static string Serialize(WalkersConfig config) => JsonConvert.SerializeObject(config, Settings);

    public static JObject ToJObject(WalkersConfig config) => JObject.FromObject(config, JsonSerializer.Create(Settings));

    /// <summary>
    /// Reads a config from JSON text. A value that cannot be read keeps its default, a key the config
    /// does not have is ignored, and numbers out of range are clamped; each case is listed in the result.
    /// </summary>
    public static ReadResult Read(string json)
    {
        var result = new ReadResult();
        try
        {
            ReadInto(json, result);
        }
        catch (Exception e)
        {
            // Whatever the file holds, nothing may escape into the game's startup or a command handler.
            result.Config = null;
            result.FatalError = e.Message;
        }
        return result;
    }

    private static void ReadInto(string json, ReadResult result)
    {
        JToken token;
        try
        {
            token = JToken.Parse(json, LenientLoad);
        }
        catch (JsonException e)
        {
            result.FatalError = e.Message;
            return;
        }
        if (token is not JObject obj)
        {
            result.FatalError = "The file must hold one JSON object, { ... }.";
            return;
        }
        try
        {
            JToken.Parse(json, StrictLoad);
        }
        catch (JsonException e)
        {
            // Two copies of one key: the lenient parse kept the last, as the game would.
            result.Errors.Add(e.Message);
        }
        result.HasComments = HasComments(json);
        Inspect(obj, typeof(WalkersConfig), "", result);

        var serializer = JsonSerializer.Create(Settings);
        serializer.Error += (_, e) =>
        {
            // The event repeats for every parent object; record the innermost only.
            if (e.CurrentObject == e.ErrorContext.OriginalObject)
            {
                var message = e.ErrorContext.Error.Message;
                result.Errors.Add(message.Contains("Path '", StringComparison.Ordinal) ? message : $"{e.ErrorContext.Path}: {message}");
            }
            e.ErrorContext.Handled = true;
        };
        result.Config = obj.ToObject<WalkersConfig>(serializer) ?? new WalkersConfig();
        Check(result.Config, "", result.Fixes);
        FindMissingKeys(ToJObject(result.Config), obj, "", result.MissingKeys);
    }

    public sealed class ReadResult
    {
        /// <summary>The config read, or null when the text could not be read at all (see <see cref="FatalError"/>).</summary>
        public WalkersConfig? Config { get; set; }

        public string? FatalError { get; set; }

        /// <summary>Values that could not be read; those settings keep their defaults.</summary>
        public List<string> Errors { get; } = new();

        /// <summary>Paths in the file that match no setting.</summary>
        public List<string> UnknownKeys { get; } = new();

        /// <summary>Values that were clamped into range or otherwise corrected.</summary>
        public List<string> Fixes { get; } = new();

        /// <summary>Settings the config has and the text leaves out, such as ones added since it was written.</summary>
        public List<string> MissingKeys { get; } = new();

        public bool HasComments { get; set; }

        /// <summary>True when the text holds exactly what the config holds, so rewriting it loses nothing.</summary>
        public bool Clean => Config != null && Errors.Count == 0 && UnknownKeys.Count == 0 && Fixes.Count == 0 && !HasComments;
    }

    /// <summary>
    /// Clamps numbers into their <see cref="RangeAttribute"/>, replaces nulls with defaults and runs
    /// each section's <see cref="IWalkersConfigCheck"/>. Describes every change in <paramref name="fixes"/>.
    /// </summary>
    public static void Check(object section, string path, List<string> fixes)
    {
        object? defaults = null;
        foreach (var property in SettingsOf(section.GetType()))
        {
            var propertyPath = Join(path, property.PropertyName!);
            var value = property.ValueProvider!.GetValue(section);
            if (value == null)
            {
                defaults ??= Activator.CreateInstance(section.GetType())!;
                value = property.ValueProvider.GetValue(defaults);
                property.ValueProvider.SetValue(section, value);
                fixes.Add($"{propertyPath} was null; using the default");
            }
            var type = property.PropertyType!;
            if (IsSection(type))
            {
                Check(value!, propertyPath, fixes);
            }
            else if (type.IsEnum && !Enum.IsDefined(type, value!))
            {
                defaults ??= Activator.CreateInstance(section.GetType())!;
                property.ValueProvider.SetValue(section, property.ValueProvider.GetValue(defaults));
                fixes.Add($"{propertyPath} is not one of {string.Join(", ", Enum.GetNames(type).Select(CamelCase))}; using the default");
            }
            else if (value is string[] list && list.Any(string.IsNullOrWhiteSpace))
            {
                property.ValueProvider.SetValue(section, list.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray());
                fixes.Add($"{propertyPath} has empty entries; dropped them");
            }
            else if (RangeOf(property) is { } range && IsNumber(type))
            {
                double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                double min = Convert.ToDouble(range.Minimum, CultureInfo.InvariantCulture);
                double max = Convert.ToDouble(range.Maximum, CultureInfo.InvariantCulture);
                double clamped = double.IsNaN(number) ? min : Math.Clamp(number, min, max);
                // Compared in the setting's own type: 0.13 as a float is a hair below the double 0.13
                // of its range, and must not count as out of range.
                var converted = Convert.ChangeType(clamped, type, CultureInfo.InvariantCulture);
                if (double.IsNaN(number) || !Equals(converted, value))
                {
                    property.ValueProvider.SetValue(section, converted);
                    fixes.Add($"{propertyPath} = {Format(number)} is outside {Format(min)} to {Format(max)}; using {Format(clamped)}");
                }
            }
        }
        if (section is IWalkersConfigCheck check)
        {
            check.Check(path, fixes);
        }
    }

    /// <summary>
    /// Copies every setting from <paramref name="source"/> into <paramref name="target"/>, section by
    /// section, so code that holds the root or a section object sees the new values.
    /// </summary>
    public static void CopyInto(object target, object source)
    {
        foreach (var property in SettingsOf(target.GetType()))
        {
            var value = property.ValueProvider!.GetValue(source);
            var current = property.ValueProvider.GetValue(target);
            if (IsSection(property.PropertyType!) && value != null && current != null)
            {
                CopyInto(current, value);
            }
            else
            {
                property.ValueProvider.SetValue(target, value);
            }
        }
    }

    /// <summary>The settings whose values differ between two serialized configs, as "path old → new".</summary>
    public static List<string> Diff(JObject before, JObject after)
    {
        var changes = new List<string>();
        Diff(before, after, "", changes);
        return changes;
    }

    private static void Diff(JObject before, JObject after, string path, List<string> changes)
    {
        foreach (var property in after.Properties())
        {
            var propertyPath = Join(path, property.Name);
            var old = before[property.Name];
            if (old is JObject oldSection && property.Value is JObject newSection)
            {
                Diff(oldSection, newSection, propertyPath, changes);
            }
            else if (!JToken.DeepEquals(old, property.Value))
            {
                changes.Add($"{propertyPath} {FormatToken(old)} → {FormatToken(property.Value)}");
            }
        }
    }

    /// <summary>The token at a dotted path, ignoring case, or null.</summary>
    public static JToken? GetPath(JObject root, string path)
    {
        JToken? token = root;
        foreach (var part in path.Split('.'))
        {
            token = (token as JObject)?.Property(part, StringComparison.OrdinalIgnoreCase)?.Value;
        }
        return token;
    }

    /// <summary>
    /// Sets the token at a dotted path, ignoring case and keeping the spelling of keys that exist;
    /// creates missing sections on the way.
    /// </summary>
    public static void SetPath(JObject root, string path, JToken value)
    {
        var parts = path.Split('.');
        var parent = root;
        foreach (var part in parts[..^1])
        {
            var existing = parent.Property(part, StringComparison.OrdinalIgnoreCase);
            if (existing?.Value is JObject child)
            {
                parent = child;
                continue;
            }
            child = new JObject();
            if (existing != null)
            {
                existing.Value = child;
            }
            else
            {
                parent.Add(part, child);
            }
            parent = child;
        }
        var leaf = parent.Property(parts[^1], StringComparison.OrdinalIgnoreCase);
        if (leaf != null)
        {
            leaf.Value = value;
        }
        else
        {
            parent.Add(parts[^1], value);
        }
    }

    /// <summary>A setting found by its dotted path, with the object that holds it.</summary>
    public sealed class Setting
    {
        internal Setting(string path, JsonProperty property, object owner)
        {
            Path = path;
            Property = property;
            Owner = owner;
        }

        /// <summary>The path as the file spells it, e.g. "director.tickSeconds".</summary>
        public string Path { get; }

        public object Owner { get; }

        internal JsonProperty Property { get; }

        public Type Type => Property.PropertyType!;

        public bool IsSection => IsSection(Type);

        /// <summary>The C# property name, e.g. "TickSeconds", for comparing with nameof.</summary>
        public string MemberName => Property.UnderlyingName!;

        public object? Value => Property.ValueProvider!.GetValue(Owner);

        /// <summary>Sets the value in memory only, converting a parsed command value to the setting's type.</summary>
        public void SetValue(JToken value) => Property.ValueProvider!.SetValue(Owner, value.ToObject(Type, JsonSerializer.Create(Settings)));

        public RangeAttribute? Range => RangeOf(Property);

        public string? Description => Property.AttributeProvider?.GetAttributes(typeof(DescriptionAttribute), true)
            .OfType<DescriptionAttribute>().FirstOrDefault()?.Description;
    }

    /// <summary>
    /// Finds a setting or section of <paramref name="root"/> (the config, or any settings object such
    /// as <see cref="WalkersPlayerPrefs"/>) by its dotted path, ignoring case. Returns null for the empty path.
    /// </summary>
    public static Setting? Find(object root, string path)
    {
        object owner = root;
        JsonProperty? found = null;
        var canonical = new List<string>();
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (found != null)
            {
                if (!IsSection(found.PropertyType!))
                {
                    return null;
                }
                owner = found.ValueProvider!.GetValue(owner)!;
            }
            found = SettingsOf(owner.GetType()).FirstOrDefault(p => string.Equals(p.PropertyName, part, StringComparison.OrdinalIgnoreCase));
            if (found == null)
            {
                return null;
            }
            canonical.Add(found.PropertyName!);
        }
        return found == null ? null : new Setting(string.Join(".", canonical), found, owner);
    }

    /// <summary>The settings directly inside a section (or the root), sections included.</summary>
    public static IEnumerable<Setting> Children(object section, string path) =>
        SettingsOf(section.GetType()).Select(p => new Setting(Join(path, p.PropertyName!), p, section));

    /// <summary>
    /// Parses a value typed in a command for a setting of type <paramref name="type"/>. Lists take
    /// commas or a JSON array. Returns null and sets <paramref name="error"/> when the text does not fit.
    /// </summary>
    public static JToken? ParseValue(string text, Type type, RangeAttribute? range, out string? error)
    {
        error = null;
        text = text.Trim();
        var inv = CultureInfo.InvariantCulture;
        if (type == typeof(bool))
        {
            switch (text.ToLowerInvariant())
            {
                case "true" or "on" or "yes" or "1": return true;
                case "false" or "off" or "no" or "0": return false;
            }
            error = "expected true or false";
            return null;
        }
        if (type.IsEnum)
        {
            var names = Enum.GetNames(type);
            var name = names.FirstOrDefault(n => string.Equals(n, text, StringComparison.OrdinalIgnoreCase));
            if (name == null)
            {
                error = "expected one of " + string.Join(", ", names.Select(CamelCase));
                return null;
            }
            return CamelCase(name);
        }
        if (type == typeof(string))
        {
            return text;
        }
        if (type == typeof(string[]))
        {
            if (text.StartsWith('['))
            {
                try
                {
                    var array = JArray.Parse(text).ToObject<string[]>();
                    if (array == null || array.Any(string.IsNullOrWhiteSpace))
                    {
                        error = "expected a list of names, none of them empty";
                        return null;
                    }
                    return JArray.FromObject(array);
                }
                catch (JsonException e)
                {
                    error = e.Message;
                    return null;
                }
            }
            return new JArray(text.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray<object>());
        }
        if (IsNumber(type))
        {
            if (!double.TryParse(text, NumberStyles.Float, inv, out double number) || double.IsNaN(number) || double.IsInfinity(number))
            {
                error = "expected a number";
                return null;
            }
            bool integral = type == typeof(int) || type == typeof(long);
            if (integral && number != Math.Floor(number))
            {
                error = "expected a whole number";
                return null;
            }
            if (range != null)
            {
                double min = Convert.ToDouble(range.Minimum, inv), max = Convert.ToDouble(range.Maximum, inv);
                // As in Check: a float setting compares in float, so its own bounds are accepted.
                bool outside = type == typeof(float)
                    ? (float)number < (float)min || (float)number > (float)max
                    : number < min || number > max;
                if (outside)
                {
                    error = $"expected {Format(min)} to {Format(max)}";
                    return null;
                }
            }
            return integral ? new JValue((long)number) : new JValue(number);
        }
        error = "this setting cannot be set by command; edit the file and reload";
        return null;
    }

    /// <summary>A setting's value as the file would show it, e.g. 0.35, true, "auto" or ["a","b"].</summary>
    public static string FormatValue(object? value) =>
        value == null ? "null" : FormatToken(JToken.FromObject(value, JsonSerializer.Create(Settings)));

    /// <summary>
    /// Writes the file through a temporary file, so a crash mid-write cannot leave half a config. A
    /// symlinked file is written at its target. Where the file cannot be replaced, only written (a file
    /// bind-mounted into a container, a file locked on Windows), it is written in place instead.
    /// </summary>
    public static void WriteFile(string path, string json)
    {
        var info = new FileInfo(path);
        var target = info.LinkTarget != null ? info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path : path;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = target + ".tmp";
        try
        {
            File.WriteAllText(temp, json);
            try
            {
                File.Move(temp, target, overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                File.WriteAllText(target, json);
            }
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Left behind; harmless, and replaced by the next write.
            }
        }
    }

    public static string Join(string path, string name) => path.Length == 0 ? name : path + "." + name;

    private static string FormatToken(JToken? token) => token == null ? "(none)" : token.ToString(Formatting.None);

    private static string Format(double number) => number.ToString("0.###", CultureInfo.InvariantCulture);

    private static string CamelCase(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];

    private static IEnumerable<JsonProperty> SettingsOf(Type type) =>
        ((JsonObjectContract)Resolver.ResolveContract(type)).Properties.Where(p => !p.Ignored && p.Readable && p.Writable);

    private static bool IsSection(Type type) => Resolver.ResolveContract(type) is JsonObjectContract;

    private static bool IsNumber(Type type) =>
        type == typeof(int) || type == typeof(long) || type == typeof(float) || type == typeof(double);

    private static RangeAttribute? RangeOf(JsonProperty property) =>
        property.AttributeProvider?.GetAttributes(typeof(RangeAttribute), true).OfType<RangeAttribute>().FirstOrDefault();

    /// <summary>
    /// Finds what the deserializer would accept without a word: keys that match no setting, one key
    /// spelled twice in different case, a fraction for a whole-number setting (it would be rounded),
    /// and comma-joined enum names (they would be OR-ed together). The last two are removed from
    /// <paramref name="obj"/>, so those settings keep their defaults.
    /// </summary>
    private static void Inspect(JObject obj, Type type, string path, ReadResult result)
    {
        var settings = ((JsonObjectContract)Resolver.ResolveContract(type)).Properties;
        foreach (var group in obj.Properties().GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            result.Errors.Add($"{Join(path, group.Key)} appears {group.Count()} times ({string.Join(", ", group.Select(p => p.Name))}); only the last one counts");
        }
        foreach (var property in obj.Properties().ToList())
        {
            var propertyPath = Join(path, property.Name);
            var match = settings.GetClosestMatchProperty(property.Name);
            if (match == null || match.Ignored || !match.Writable)
            {
                result.UnknownKeys.Add(propertyPath);
                continue;
            }
            var settingType = match.PropertyType!;
            if (property.Value is JObject child && IsSection(settingType))
            {
                Inspect(child, settingType, propertyPath, result);
            }
            else if ((settingType == typeof(int) || settingType == typeof(long)) && property.Value.Type == JTokenType.Float &&
                     property.Value.Value<double>() is var number && number != Math.Floor(number))
            {
                result.Errors.Add($"{propertyPath} = {Format(number)} is not a whole number; using the default");
                property.Remove();
            }
            else if (settingType.IsEnum && property.Value.Type == JTokenType.String && property.Value.Value<string>()!.Contains(','))
            {
                result.Errors.Add($"{propertyPath} = \"{property.Value}\" names more than one value; expected one of {string.Join(", ", Enum.GetNames(settingType).Select(CamelCase))}");
                property.Remove();
            }
        }
    }

    private static void FindMissingKeys(JObject full, JObject file, string path, List<string> missing)
    {
        foreach (var property in full.Properties())
        {
            var propertyPath = Join(path, property.Name);
            var inFile = file.Property(property.Name, StringComparison.OrdinalIgnoreCase);
            if (inFile == null)
            {
                missing.Add(propertyPath);
            }
            else if (property.Value is JObject section && inFile.Value is JObject fileSection)
            {
                FindMissingKeys(section, fileSection, propertyPath, missing);
            }
        }
    }

    private static bool HasComments(string json)
    {
        try
        {
            using var reader = new JsonTextReader(new StringReader(json));
            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.Comment)
                {
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            // Already reported by the parse.
        }
        return false;
    }
}
