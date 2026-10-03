using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Mods;

namespace ModLaunch.Features;

/// <summary>Настройки модов SMAPI (Stardew Valley): config.json в папке мода. Правим только простые значения верхнего уровня.</summary>
public static class CfgJson
{
    static readonly JsonDocumentOptions Lenient = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static List<string> Files(ModRegistry registry)
    {
        if (registry.Game.Loader != LoaderKind.Smapi || !Directory.Exists(registry.ModsDir)) return [];
        try { return Directory.EnumerateFiles(registry.ModsDir, "config.json", SearchOption.AllDirectories).OrderBy(Label).ToList(); }
        catch { return []; }
    }

    /// <summary>Подпись в списке: имя папки мода (сами файлы все зовутся config.json).</summary>
    public static string Label(string path) => Path.GetFileName(Path.GetDirectoryName(path)) ?? path;

    /// <summary>Настройки модов Hollow Knight: *.GlobalSettings.json в папке сохранений игры.</summary>
    public static List<string> HkFiles(ModRegistry registry)
    {
        var dir = Launcher.SavesDir(registry.Game, registry.GamePath);
        if (dir is null || !Directory.Exists(dir)) return [];
        try { return Directory.EnumerateFiles(dir, "*.GlobalSettings.json", SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName).ToList(); }
        catch { return []; }
    }

    static string SectionLabel(string path) => CfgFile.Label(path);

    /// <summary>Простые значения верхнего уровня и одного уровня вложенности («Раздел → ключ»).</summary>
    public static List<CfgEntry> Parse(string path)
    {
        var result = new List<CfgEntry>();
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path), documentOptions: Lenient) is not JsonObject obj) return result;
            var label = SectionLabel(path);
            foreach (var (key, node) in obj)
            {
                if (node is JsonValue value) { if (Entry(label, [key], value) is { } e) result.Add(e); }
                else if (node is JsonObject inner)
                    foreach (var (k2, n2) in inner)
                        if (n2 is JsonValue v2 && Entry($"{label} · {key}", [key, k2], v2) is { } e2) result.Add(e2);
            }
            ApplySchema(path, label, result);
            foreach (var e in result) e.Title ??= Humanize(e.Key);
        }
        catch { }
        // Сначала основные настройки мода, потом вложенные разделы.
        return result.Where(e => !IsVersionStamp(e)).OrderBy(e => e.JsonPath?.Length ?? 1).ToList();
    }

    static readonly System.Text.RegularExpressions.Regex WordBreak = new(@"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])");
    static readonly System.Text.RegularExpressions.Regex VersionText = new(@"^v?\d+(\.\d+)+([-+].*)?$");

    /// <summary>«EnchantedGroveWarpX» → «Enchanted Grove Warp X», «saveSkins» → «Save Skins». Null — если читать и так удобно.</summary>
    public static string? Humanize(string key)
    {
        if (key.Length < 2 || key.All(char.IsDigit)) return null;
        var text = WordBreak.Replace(key.Replace('_', ' '), " ");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        if (text.Length > 0) text = char.ToUpperInvariant(text[0]) + text[1..];
        return text == key ? null : text;
    }

    /// <summary>Служебная отметка версии («Version»: "3.5.0.0") — мод пишет её сам, править нечего.</summary>
    static bool IsVersionStamp(CfgEntry e) =>
        (e.Key.Equals("Version", StringComparison.OrdinalIgnoreCase) || e.Key.Equals("ConfigVersion", StringComparison.OrdinalIgnoreCase))
        && VersionText.IsMatch(e.Value.Trim());

    static readonly System.Text.RegularExpressions.Regex NumberText = new(@"^-?\d+(\.\d+)?$");

    static CfgEntry? Entry(string section, string[] path, JsonValue value)
    {
        string kind, text;
        var asText = false;
        if (value.TryGetValue<bool>(out var b)) { kind = "Boolean"; text = b ? "true" : "false"; }
        else if (value.TryGetValue<string>(out var s))
        {
            text = s;
            // Content Patcher хранит всё строками: "true"/"false" — это переключатель, "48" — число.
            if (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s.Equals("false", StringComparison.OrdinalIgnoreCase)) { kind = "Boolean"; asText = true; text = s.ToLowerInvariant(); }
            else if (NumberText.IsMatch(s.Trim())) { kind = "Number"; asText = true; }
            else kind = "String";
        }
        else if (value.GetValueKind() == JsonValueKind.Number) { kind = "Number"; text = value.ToJsonString(); }
        else return null;
        return new CfgEntry { Section = section, Key = path[^1], Value = text, Type = kind, JsonPath = path, AsText = asText };
    }

    static string? Text(JsonNode? node) => node is JsonValue v ? (v.TryGetValue<string>(out var s) ? s : v.ToJsonString()) : null;

    static JsonNode? Field(JsonObject obj, string name) => obj.FirstOrDefault(kv => kv.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    /// <summary>
    /// Пакеты Content Patcher описывают свои настройки в content.json («ConfigSchema»): варианты,
    /// значение по умолчанию, описание и раздел. Названия и описания на языке программы берём из i18n.
    /// </summary>
    static void ApplySchema(string path, string label, List<CfgEntry> entries)
    {
        var dir = Path.GetDirectoryName(path)!;
        var content = Path.Combine(dir, "content.json");
        if (!File.Exists(content)) return;
        JsonObject? schema;
        try { schema = JsonNode.Parse(File.ReadAllText(content), documentOptions: Lenient) is JsonObject root ? Field(root, "ConfigSchema") as JsonObject : null; }
        catch { return; }
        if (schema is null) return;
        var tr = Translations(dir);
        string? T(string key) => tr.TryGetValue(key, out var v) && v.Trim() != "" ? v : null;
        foreach (var e in entries)
        {
            if (e.JsonPath is not { Length: 1 } || Field(schema, e.Key) is not JsonObject field) continue;
            var choices = (Text(Field(field, "AllowValues")) ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            e.Multi = (Text(Field(field, "AllowMultiple")) ?? "").Equals("true", StringComparison.OrdinalIgnoreCase);
            e.Default = Text(Field(field, "Default")) ?? e.Default;
            var yesNo = choices.Count == 2 && choices.All(c => c.Equals("true", StringComparison.OrdinalIgnoreCase) || c.Equals("false", StringComparison.OrdinalIgnoreCase));
            if (yesNo && !e.Multi)
            {
                e.Type = "Boolean";
                e.AsText = true;
                e.Default = e.Default.ToLowerInvariant();
            }
            else if (choices.Count > 0)
            {
                // Варианты из списка: выпадающий список (или галочки, если можно несколько).
                e.Choices = choices;
                if (e.IsBool || e.IsNumber) { e.Type = "String"; e.AsText = true; }
            }
            e.Title = T($"config.{e.Key}.name");
            e.Description = T($"config.{e.Key}.description") ?? Text(Field(field, "Description")) ?? e.Description;
            if (Text(Field(field, "Section")) is { Length: > 0 } section)
                e.Section = $"{label} · {T($"config.section.{section}.name") ?? section}";
        }
    }

    /// <summary>Переводы пакета: i18n/default.json, поверх — файл (или папка) языка программы.</summary>
    static Dictionary<string, string> Translations(string dir)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var root = Path.Combine(dir, "i18n");
        if (!Directory.Exists(root)) return map;
        void Load(string name)
        {
            var files = new List<string>();
            if (File.Exists(Path.Combine(root, name + ".json"))) files.Add(Path.Combine(root, name + ".json"));
            if (Directory.Exists(Path.Combine(root, name))) files.AddRange(Directory.EnumerateFiles(Path.Combine(root, name), "*.json").Order());
            foreach (var file in files)
            {
                try
                {
                    if (JsonNode.Parse(File.ReadAllText(file), documentOptions: Lenient) is JsonObject obj)
                        foreach (var (k, v) in obj)
                            if (k.StartsWith("config.", StringComparison.OrdinalIgnoreCase) && Text(v) is { } s) map[k] = s;
                }
                catch { }
            }
        }
        Load("default");
        var lang = I18n.Lang.Split('-')[0].ToLowerInvariant();
        if (lang != "en") Load(lang);
        return map;
    }

    static JsonNode Typed(CfgEntry entry, string value) => entry.AsText ? (JsonNode)(entry.IsBool ? value.ToLowerInvariant() : value) : entry.Type switch
    {
        "Boolean" => (JsonNode)value.Equals("true", StringComparison.OrdinalIgnoreCase),
        "Number" => long.TryParse(value, out var l) ? (JsonNode)l : double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? (JsonNode)d : throw new FormatException(value),
        _ => (JsonNode)value,
    };

    static void Put(JsonObject root, CfgEntry entry, string value)
    {
        var path = entry.JsonPath ?? [entry.Key];
        var target = root;
        foreach (var part in path[..^1]) target = target[part] as JsonObject ?? throw new InvalidOperationException(string.Join(".", path));
        target[path[^1]] = Typed(entry, value);
    }

    static void Write(string path, JsonObject obj)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })) obj.WriteTo(writer);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, stream.ToArray());
        File.Move(temp, path, true);
    }

    public static void Set(string path, CfgEntry entry, string value)
    {
        var obj = JsonNode.Parse(File.ReadAllText(path), documentOptions: Lenient) as JsonObject ?? throw new InvalidOperationException(path);
        Put(obj, entry, value);
        Write(path, obj);
        entry.Value = value;
    }

    public static int SetMany(string path, List<(CfgEntry Entry, string Value)> changes)
    {
        var obj = JsonNode.Parse(File.ReadAllText(path), documentOptions: Lenient) as JsonObject ?? throw new InvalidOperationException(path);
        var n = 0;
        foreach (var (entry, value) in changes)
        {
            try { Put(obj, entry, value); entry.Value = value; n++; } catch { }
        }
        if (n > 0) Write(path, obj);
        return n;
    }

    [SelfTest]
    static string EditsSimpleValues()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Paths.DataDir, "cfgjson-test", "CoolMod")).FullName;
        var path = Path.Combine(dir, "config.json");
        File.WriteAllText(path, "{ // comment\n \"Enabled\": true, \"Radius\": 5, \"Key\": \"K\", \"Nested\": { \"a\": 1 } }");
        var list = Parse(path);
        if (list.Count != 4 || list[0].Type != "Boolean" || list[1].Type != "Number" || list[2].Value != "K" || list[3].Key != "a") throw new Exception("parse wrong: " + list.Count);
        Set(path, list[0], "false");
        Set(path, list[1], "9");
        var after = Parse(path);
        if (after[0].Value != "false" || after[1].Value != "9") throw new Exception("write wrong");
        if (!File.ReadAllText(path).Contains("\"Nested\"")) throw new Exception("nested value lost");
        Set(path, after[3], "7");
        if (Parse(path)[3].Value != "7") throw new Exception("nested write wrong");
        Directory.Delete(Path.Combine(Paths.DataDir, "cfgjson-test"), true);
        return "bool/number/string and nested read, three values written";
    }

    [SelfTest]
    static string ReadsContentPatcherSchema()
    {
        var root = Path.Combine(Paths.DataDir, "cfgjson-cp-test");
        var dir = Directory.CreateDirectory(Path.Combine(root, "[CP] Big Pack")).FullName;
        Directory.CreateDirectory(Path.Combine(dir, "i18n"));
        var path = Path.Combine(dir, "config.json");
        File.WriteAllText(path, "{ \"SeasonalEdits\": \"true\", \"Warp\": \"default\", \"Size\": \"48\", \"Pets\": \"cat, dog\", \"Plain\": \"hello\" }");
        File.WriteAllText(Path.Combine(dir, "content.json"), """
            {
              // комментарии и лишние запятые встречаются в настоящих пакетах
              "Format": "2.0.0",
              "ConfigSchema": {
                "SeasonalEdits": { "AllowValues": "true, false", "Default": "true", },
                "Warp": { "AllowValues": "default, farm, town", "Default": "default", "Section": "Travel" },
                "Size": { "Default": "32", "Description": "Map size" },
                "Pets": { "AllowValues": "cat, dog, bird", "AllowMultiple": true, "Default": "cat" },
              },
            }
            """);
        File.WriteAllText(Path.Combine(dir, "i18n", "default.json"), "{ \"config.Warp.name\": \"Warp point\", \"config.section.Travel.name\": \"Travel\" }");
        File.WriteAllText(Path.Combine(dir, "i18n", "ru.json"), "{ \"config.Warp.name\": \"Точка входа\", \"config.section.Travel.name\": \"Путешествия\" }");
        try
        {
            var list = Parse(path).ToDictionary(e => e.Key);
            var seasonal = list["SeasonalEdits"];
            if (!seasonal.IsBool || !seasonal.AsText || seasonal.Default != "true") throw new Exception("string bool not a toggle");
            var warp = list["Warp"];
            if (warp.Choices.Count != 3 || warp.Title is null || !warp.Section.Contains('·')) throw new Exception("choices/title/section: " + warp.Choices.Count + " " + warp.Title + " " + warp.Section);
            if (!list["Size"].IsNumber || list["Size"].Default != "32" || list["Size"].Description != "Map size") throw new Exception("number schema");
            if (!list["Pets"].Multi || list["Pets"].Choices.Count != 3) throw new Exception("multi");
            if (list["Plain"].Type != "String" || list["Plain"].AsText) throw new Exception("plain string");
            SetMany(path, [(seasonal, "false"), (list["Size"], "50")]);
            var text = File.ReadAllText(path);
            if (!text.Contains("\"SeasonalEdits\": \"false\"") || !text.Contains("\"Size\": \"50\"")) throw new Exception("strings not kept: " + text);
            if (!Parse(path).First(e => e.Key == "Size").Changed) throw new Exception("changed vs schema default");
            foreach (var (key, want) in new[] { ("EnchantedGroveWarpX", "Enchanted Grove Warp X"), ("saveSkins", "Save Skins"), ("HUDScale", "HUD Scale"), ("show_hp", "Show hp") })
                if (Humanize(key) != want) throw new Exception($"humanize {key} → {Humanize(key)}");
            if (Humanize("Enabled") is not null || Humanize("0") is not null) throw new Exception("humanize should skip plain keys");
            File.WriteAllText(path, "{ \"Nested\": { \"a\": 1 }, \"Version\": \"3.5.0.0-486610\", \"Top\": true }");
            var order = Parse(path);
            if (order.Count != 2 || order[0].Key != "Top") throw new Exception("top-level first, version stamp hidden: " + string.Join(",", order.Select(e => e.Key)));
            return "string bools/numbers, AllowValues, AllowMultiple, Default, Section and i18n names; written back as strings";
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
