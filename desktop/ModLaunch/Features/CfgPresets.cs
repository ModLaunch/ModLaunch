using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Mods;

namespace ModLaunch.Features;

/// <summary>Готовый набор настроек модов: встроенный (правила) или свой (снимок файлов).</summary>
public sealed record CfgPreset(string Id, string Title, string Text, string Icon, bool BuiltIn, DateTime? Saved = null, int Files = 0);

/// <summary>
/// Готовые настройки модов (8.4). Встроенные наборы — правила «что поставить»:
/// «Рекомендуемые», «Производительность», «Поиск проблем», «По умолчанию». Свои наборы — снимок
/// всех файлов настроек игры в zip (как у профилей), их можно сохранить, применить,
/// выгрузить файлом .mlcfg и загрузить у друга. Перед любым применением делается снимок
/// «как было» — одна кнопка возвращает всё назад.
/// </summary>
public static partial class CfgPresets
{
    public const string Defaults = "defaults", Recommended = "recommended", Performance = "performance", Debug = "debug";

    /// <summary>Встроенные наборы для загрузчика игры (у SMAPI и Hollow Knight «поиска проблем» нет — нечего включать).</summary>
    public static List<CfgPreset> BuiltIn(GameDef def)
    {
        var list = new List<CfgPreset>
        {
            new(Recommended, I18n.T("cfgp.recommended"), I18n.T(def.Loader == LoaderKind.Bepinex ? "cfgp.recommended.text" : "cfgp.recommended.text.json"), "star", true),
            new(Performance, I18n.T("cfgp.performance"), I18n.T("cfgp.performance.text"), "zap", true),
        };
        if (def.Loader == LoaderKind.Bepinex) list.Add(new(Debug, I18n.T("cfgp.debug"), I18n.T("cfgp.debug.text"), "bug", true));
        list.Add(new(Defaults, I18n.T("cfgp.defaults"), I18n.T(def.Loader == LoaderKind.Bepinex ? "cfgp.defaults.text" : "cfgp.defaults.text.json"), "undo", true));
        return list;
    }

    // ---------------------------------------------------------------- правила встроенных наборов

    /// <summary>Отладочные переключатели модов («Debug», «VerboseLogging», «DevMode»…).</summary>
    [GeneratedRegex(@"debug|verbose|trace|devmode|developermode|showfps|logging", RegexOptions.IgnoreCase)] private static partial Regex DebugFlag();

    static readonly Dictionary<string, (string Section, string Key, string Value)[]> BepInExRules = new()
    {
        [Recommended] =
        [
            ("Logging.Console", "Enabled", "false"),
            ("Logging.Disk", "Enabled", "true"),
            ("Logging.Disk", "LogLevels", "Fatal, Error, Warning, Message, Info"),
            ("Logging", "UnityLogListening", "true"),
            ("Caching", "EnableAssemblyCache", "true"),
        ],
        [Performance] =
        [
            ("Logging.Console", "Enabled", "false"),
            ("Logging.Disk", "Enabled", "true"),
            ("Logging.Disk", "LogLevels", "Fatal, Error, Warning"),
            ("Logging", "UnityLogListening", "true"),
            ("Logging", "LogConsoleToUnityLog", "false"),
            ("Caching", "EnableAssemblyCache", "true"),
            ("Harmony.Logger", "LogChannels", "Warn, Error"),
        ],
        [Debug] =
        [
            ("Logging.Console", "Enabled", "true"),
            ("Logging.Console", "LogLevels", "All"),
            ("Logging.Disk", "Enabled", "true"),
            ("Logging.Disk", "LogLevels", "All"),
            ("Logging.Disk", "WriteUnityLog", "true"),
            ("Logging", "UnityLogListening", "true"),
        ],
    };

    static bool IsCore(string path) => Path.GetFileName(path).Equals("BepInEx.cfg", StringComparison.OrdinalIgnoreCase);

    /// <summary>Что поменяет набор: файл → (настройка, новое значение). Ничего не пишет.</summary>
    public static Dictionary<string, List<(CfgEntry Entry, string Value)>> Plan(ModRegistry registry, string preset)
    {
        var plan = new Dictionary<string, List<(CfgEntry, string)>>();
        foreach (var file in CfgFile.Files(registry))
        {
            var entries = CfgFile.Parse(file);
            var changes = new List<(CfgEntry, string)>();
            if (preset == Defaults)
            {
                // У .cfg значение по умолчанию записано в комментарии. У json его нет —
                // такие файлы удаляются (с копией), и мод при запуске создаёт их заново.
                changes.AddRange(entries.Where(e => e.Changed).Select(e => (e, e.Default)));
            }
            else if (IsCore(file) && BepInExRules.TryGetValue(preset, out var rules))
            {
                foreach (var (section, key, value) in rules)
                    if (entries.FirstOrDefault(e => e.Section == section && e.Key == key) is { } e && !SameValue(e.Value, value))
                        changes.Add((e, value));
            }
            else if (preset is Recommended or Performance)
            {
                // Моды: выключить отладку — меньше спама в логе и быстрее игра.
                foreach (var e in entries.Where(e => e.IsBool && DebugFlag().IsMatch(e.Key) && e.Value.Equals("true", StringComparison.OrdinalIgnoreCase)))
                    changes.Add((e, "false"));
            }
            if (changes.Count > 0) plan[file] = changes;
        }
        return plan;
    }

    static bool SameValue(string a, string b) => string.Equals(a.Replace(" ", ""), b.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);

    /// <summary>json-файлы, которые «По умолчанию» удалит (мод создаст их заново).</summary>
    public static List<string> ResetFiles(ModRegistry registry) =>
        registry.Game.Loader == LoaderKind.Bepinex ? [] : CfgFile.Files(registry);

    /// <summary>Применить встроенный набор. Возвращает (сколько настроек, сколько файлов).</summary>
    public static (int Values, int Files) Apply(GameState g, string preset)
    {
        var registry = g.Registry ?? throw new InvalidOperationException("no game");
        Snapshot(registry, g.Def.Id);
        var values = 0;
        var files = 0;
        foreach (var (file, changes) in Plan(registry, preset))
        {
            var n = CfgFile.SetMany(file, changes);
            values += n;
            if (n > 0) files++;
        }
        if (preset == Defaults)
        {
            foreach (var file in ResetFiles(registry))
            {
                try { File.Delete(file); files++; } catch { }
            }
        }
        Settings.Data.Obj("cfgPreset")[g.Def.Id] = preset;
        Settings.Save();
        return (values, files);
    }

    /// <summary>Какой набор применяли последним (для отметки на карточке).</summary>
    public static string? Last(string gameId) => Settings.Data.Obj("cfgPreset").Str(gameId);

    // ---------------------------------------------------------------- «как было»

    static string Dir(string gameId) => Directory.CreateDirectory(Path.Combine(Paths.DataDir, "cfg-presets", gameId)).FullName;
    static string UndoZip(string gameId) => Path.Combine(Dir(gameId), "_undo.zip");

    static void Snapshot(ModRegistry registry, string gameId)
    {
        try { if (!ModSetup.SaveConfig(registry, UndoZip(gameId))) File.Delete(UndoZip(gameId)); } catch { }
    }

    public static bool CanUndo(string gameId) => File.Exists(UndoZip(gameId));

    /// <summary>Вернуть настройки, какими они были до последнего набора.</summary>
    public static int Undo(GameState g)
    {
        var zip = UndoZip(g.Def.Id);
        if (!File.Exists(zip) || g.Registry is null) return 0;
        int n;
        using (var archive = ZipFile.OpenRead(zip)) n = ModSetup.RestoreConfig(g.Registry, archive);
        File.Delete(zip);
        Settings.Data.Obj("cfgPreset").Remove(g.Def.Id);
        Settings.Save();
        return n;
    }

    // ---------------------------------------------------------------- свои наборы

    static string Index(string gameId) => Path.Combine(Dir(gameId), "index.json");

    static JsonArray Load(string gameId)
    {
        try { return JsonNode.Parse(File.ReadAllText(Index(gameId))) as JsonArray ?? []; } catch { return []; }
    }

    static void Store(string gameId, JsonArray list) => File.WriteAllText(Index(gameId), list.ToJsonString());

    public static string Clean(string? name)
    {
        var s = Regex.Replace((name ?? "").Trim(), @"\s+", " ");
        return s.Length > 40 ? s[..40] : s;
    }

    public static List<CfgPreset> Mine(string gameId) => Load(gameId).OfType<JsonObject>()
        .Where(o => File.Exists(Path.Combine(Dir(gameId), o.Str("file") ?? "-")))
        .Select(o => new CfgPreset(o.Str("file")!, o.Str("name") ?? "?", "", "save", false,
            DateTime.TryParse(o.Str("saved"), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var d) ? d : null, (int)o.Long("files")))
        .OrderByDescending(p => p.Saved).ToList();

    /// <summary>Сохранить текущие настройки как свой набор (с тем же именем — перезаписать).</summary>
    public static CfgPreset Save(GameState g, string name)
    {
        name = Clean(name);
        if (name == "") throw new InvalidOperationException(I18n.T("cfgp.err.name"));
        var registry = g.Registry ?? throw new InvalidOperationException("no game");
        var list = Load(g.Def.Id);
        var existing = list.OfType<JsonObject>().FirstOrDefault(o => string.Equals(o.Str("name"), name, StringComparison.OrdinalIgnoreCase));
        var file = existing?.Str("file") ?? $"p{DateTime.UtcNow:yyyyMMddHHmmssfff}.zip";
        var zip = Path.Combine(Dir(g.Def.Id), file);
        if (!ModSetup.SaveConfig(registry, zip)) throw new InvalidOperationException(I18n.T("cfgp.err.empty"));
        var count = ModSetup.ConfigFiles(registry).Count;
        if (existing is not null) list.Remove(existing);
        list.Add(new JsonObject { ["name"] = name, ["file"] = file, ["saved"] = DateTime.UtcNow.ToString("o"), ["files"] = count });
        Store(g.Def.Id, list);
        return new CfgPreset(file, name, "", "save", false, DateTime.UtcNow, count);
    }

    public static int ApplyMine(GameState g, string id)
    {
        var registry = g.Registry ?? throw new InvalidOperationException("no game");
        var zip = Path.Combine(Dir(g.Def.Id), Path.GetFileName(id));
        if (!File.Exists(zip)) throw new FileNotFoundException(zip);
        Snapshot(registry, g.Def.Id);
        int n;
        using (var archive = ZipFile.OpenRead(zip)) n = ModSetup.RestoreConfig(registry, archive);
        Settings.Data.Obj("cfgPreset")[g.Def.Id] = "mine:" + id;
        Settings.Save();
        return n;
    }

    public static void Delete(string gameId, string id)
    {
        var list = Load(gameId);
        foreach (var o in list.OfType<JsonObject>().Where(o => o.Str("file") == id).ToList()) list.Remove(o);
        Store(gameId, list);
        try { File.Delete(Path.Combine(Dir(gameId), Path.GetFileName(id))); } catch { }
    }

    public static void Rename(string gameId, string id, string name)
    {
        name = Clean(name);
        if (name == "") return;
        var list = Load(gameId);
        foreach (var o in list.OfType<JsonObject>().Where(o => o.Str("file") == id)) o["name"] = name;
        Store(gameId, list);
    }

    /// <summary>Выгрузить набор файлом .mlcfg (это zip с файлами настроек и подписью игры).</summary>
    public static void Export(string gameId, string id, string target)
    {
        var src = Path.Combine(Dir(gameId), Path.GetFileName(id));
        var name = Mine(gameId).FirstOrDefault(p => p.Id == id)?.Title ?? "preset";
        File.Copy(src, target, true);
        using var zip = ZipFile.Open(target, ZipArchiveMode.Update);
        zip.GetEntry("modlaunch-preset.json")?.Delete();
        using var w = new StreamWriter(zip.CreateEntry("modlaunch-preset.json").Open());
        w.Write(new JsonObject { ["game"] = gameId, ["name"] = name, ["app"] = Http.Version }.ToJsonString());
    }

    /// <summary>Загрузить набор из файла: проверить, что он для этой игры, и сохранить в «Мои наборы».</summary>
    public static CfgPreset Import(GameState g, string file)
    {
        if (new FileInfo(file).Length > 32 * 1024 * 1024) throw new InvalidDataException(I18n.T("cfgp.err.file"));
        string name;
        using (var zip = ZipFile.OpenRead(file))
        {
            var meta = zip.GetEntry("modlaunch-preset.json");
            JsonNode? info = null;
            if (meta is not null) using (var r = new StreamReader(meta.Open())) info = JsonNode.Parse(r.ReadToEnd());
            if (info.Str("game") is { } game && game != g.Def.Id)
                throw new InvalidDataException(I18n.T("cfgp.err.game", ("game", GameCatalog.ById(game)?.Name ?? game)));
            if (!zip.Entries.Any(e => e.FullName.StartsWith("config/", StringComparison.OrdinalIgnoreCase) || e.FullName.StartsWith("Mods/", StringComparison.OrdinalIgnoreCase) || e.FullName.StartsWith("settings/", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException(I18n.T("cfgp.err.file"));
            name = Clean(info.Str("name") ?? Path.GetFileNameWithoutExtension(file));
        }
        var list = Load(g.Def.Id);
        var unique = name;
        for (var i = 2; list.OfType<JsonObject>().Any(o => string.Equals(o.Str("name"), unique, StringComparison.OrdinalIgnoreCase)); i++) unique = $"{name} ({i})";
        var id = $"p{DateTime.UtcNow:yyyyMMddHHmmssfff}.zip";
        File.Copy(file, Path.Combine(Dir(g.Def.Id), id), true);
        list.Add(new JsonObject { ["name"] = unique, ["file"] = id, ["saved"] = DateTime.UtcNow.ToString("o"), ["files"] = 0 });
        Store(g.Def.Id, list);
        return new CfgPreset(id, unique, "", "save", false, DateTime.UtcNow);
    }

    [SelfTest]
    static string PlansBuiltInPresets()
    {
        string[] core =
        [
            "[Logging.Console]", "## Enables", "# Setting type: Boolean", "# Default value: false", "Enabled = true", "",
            "[Logging.Disk]", "# Setting type: LogLevel", "# Default value: Fatal, Error, Warning, Message, Info", "LogLevels = All", "",
        ];
        var root = Path.Combine(Paths.DataDir, "cfgp-test", "BepInEx", "config");
        Directory.CreateDirectory(root);
        File.WriteAllLines(Path.Combine(root, "BepInEx.cfg"), core);
        File.WriteAllLines(Path.Combine(root, "Cool.cfg"), ["[General]", "# Setting type: Boolean", "# Default value: false", "DebugMode = true", "# Setting type: Boolean", "# Default value: true", "Fancy = true"]);
        var def = GameCatalog.All.First(d => d.Loader == LoaderKind.Bepinex);
        var registry = new ModRegistry(def, Path.Combine(Paths.DataDir, "cfgp-test"));
        var perf = Plan(registry, Performance);
        if (perf.Count != 2) throw new Exception("performance should touch 2 files, got " + perf.Count);
        foreach (var (file, changes) in perf) CfgFile.SetMany(file, changes);
        var after = CfgFile.Parse(Path.Combine(root, "BepInEx.cfg"));
        if (after[0].Value != "false" || after[1].Value != "Fatal, Error, Warning") throw new Exception("core rules not applied");
        if (CfgFile.Parse(Path.Combine(root, "Cool.cfg"))[0].Value != "false") throw new Exception("debug flag not turned off");
        var defaults = Plan(registry, Defaults);
        if (!defaults.Values.SelectMany(v => v).Any(c => c.Entry.Key == "LogLevels")) throw new Exception("defaults plan wrong");
        Directory.Delete(Path.Combine(Paths.DataDir, "cfgp-test"), true);
        return "performance: core + debug flags; defaults: back to comments";
    }
}
