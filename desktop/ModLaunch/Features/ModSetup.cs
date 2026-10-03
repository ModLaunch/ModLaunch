using System.IO.Compression;
using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Mods;

namespace ModLaunch.Features;

/// <summary>Мод в составе сборки: номер, версия, откуда и включён ли.</summary>
public sealed record SetupMod(string Id, string Name, string Version, string Source, bool Enabled);

/// <summary>
/// «Фотография» модов игры: какие стоят, каких версий, какие включены, и настройки модов
/// (BepInEx/config или config.json модов SMAPI) в zip. На ней стоят профили, снимки «как было»
/// и коды сборок.
/// </summary>
public static class ModSetup
{
    public static List<SetupMod> Capture(ModRegistry registry)
    {
        registry.Reconcile();
        return registry.List()
            .Where(r => !r.Bool("missing") && r.Str("target") is null && r.Str("kind") != "preset")
            .Select(r => new SetupMod(r.Str("id")!, r.Str("name") ?? r.Str("id")!, r.Str("version") ?? "", r.Str("source") ?? "file", r.Bool("enabled", true)))
            .ToList();
    }

    public static JsonArray ToJson(IEnumerable<SetupMod> mods) => new(mods.Select(m => (JsonNode)new JsonObject
    {
        ["id"] = m.Id, ["name"] = m.Name, ["version"] = m.Version, ["source"] = m.Source, ["enabled"] = m.Enabled,
    }).ToArray());

    public static List<SetupMod> FromJson(JsonArray? list) => (list ?? []).OfType<JsonObject>()
        .Where(m => m.Str("id") is { Length: > 0 })
        .Select(m => new SetupMod(m.Str("id")!, m.Str("name") ?? m.Str("id")!, m.Str("version") ?? "", m.Str("source") ?? "file", m.Bool("enabled", true)))
        .ToList();

    // ---------------------------------------------------------------- настройки модов

    /// <summary>Файлы настроек: у BepInEx — всё в BepInEx/config, у SMAPI — config.json каждого мода.</summary>
    public static List<(string Path, string Entry)> ConfigFiles(ModRegistry registry)
    {
        var result = new List<(string, string)>();
        try
        {
            if (registry.Game.Loader == LoaderKind.Bepinex)
            {
                var dir = Path.Combine(registry.GamePath, "BepInEx", "config");
                if (Directory.Exists(dir))
                    foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                        if (new FileInfo(file).Length < 4 * 1024 * 1024)
                            result.Add((file, "config/" + Path.GetRelativePath(dir, file).Replace('\\', '/')));
            }
            else if (registry.Game.Loader == LoaderKind.Smapi && Directory.Exists(registry.ModsDir))
            {
                foreach (var file in Directory.EnumerateFiles(registry.ModsDir, "config.json", SearchOption.AllDirectories))
                    result.Add((file, "Mods/" + Path.GetRelativePath(registry.ModsDir, file).Replace('\\', '/')));
            }
            else if (registry.Game.Loader == LoaderKind.HkApi)
            {
                // Hollow Knight: настройки модов — *.GlobalSettings.json рядом с сохранениями.
                foreach (var file in CfgJson.HkFiles(registry))
                    result.Add((file, "settings/" + Path.GetFileName(file)));
            }
        }
        catch { }
        return result;
    }

    /// <summary>Сложить настройки в zip. false — настроек нет.</summary>
    public static bool SaveConfig(ModRegistry registry, string zipPath)
    {
        var files = ConfigFiles(registry);
        if (files.Count == 0) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        var temp = zipPath + ".tmp";
        File.Delete(temp); // остаток прошлого раза (программу закрыли посреди записи) — иначе новый zip не создать
        using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
            foreach (var (path, entry) in files)
                try { zip.CreateEntryFromFile(path, entry, CompressionLevel.Optimal); } catch { }
        File.Move(temp, zipPath, true);
        return true;
    }

    /// <summary>Куда в игре ляжет файл из архива настроек (или null, если путь странный).</summary>
    static string? Target(ModRegistry registry, string entry)
    {
        entry = entry.Replace('\\', '/').TrimStart('/');
        string root, relative;
        if (entry.StartsWith("config/", StringComparison.OrdinalIgnoreCase)) { root = Path.Combine(registry.GamePath, "BepInEx", "config"); relative = entry["config/".Length..]; }
        else if (entry.StartsWith("Mods/", StringComparison.OrdinalIgnoreCase) && registry.Game.Loader == LoaderKind.Smapi) { root = registry.ModsDir; relative = entry["Mods/".Length..]; }
        else if (entry.StartsWith("settings/", StringComparison.OrdinalIgnoreCase) && registry.Game.Loader == LoaderKind.HkApi
            && Launcher.SavesDir(registry.Game, registry.GamePath) is { } saves && entry.EndsWith(".GlobalSettings.json", StringComparison.OrdinalIgnoreCase))
        { root = saves; relative = entry["settings/".Length..]; if (relative.Contains('/')) return null; }
        else return null;
        // У SMAPI настройки лежат в папке мода: мода сейчас нет (удалён, выключен) — папку ради одного config.json не создаём.
        if (root == registry.ModsDir && !Directory.Exists(Path.Combine(root, relative.Split('/')[0]))) return null;
        var full = Path.GetFullPath(Path.Combine(root, relative));
        return relative != "" && full.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    /// <summary>
    /// Разложить настройки из zip (или из потока архива кода сборки). Прежние файлы, которые
    /// заменяются, сначала копируются в ModHub/config-backup. Возвращает, сколько файлов записано.
    /// </summary>
    public static int RestoreConfig(ModRegistry registry, ZipArchive zip)
    {
        var backup = Path.Combine(registry.GamePath, "ModHub", "config-backup", DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        var written = 0;
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/') || Target(registry, entry.FullName) is not { } target) continue;
            try
            {
                if (File.Exists(target))
                {
                    var rel = Path.GetRelativePath(registry.GamePath, target);
                    if (rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel)) rel = Path.Combine("settings", Path.GetFileName(target));
                    var copy = Path.Combine(backup, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                    File.Copy(target, copy, true);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, true);
                written++;
            }
            catch { }
        }
        return written;
    }

    public static int RestoreConfig(ModRegistry registry, string zipPath)
    {
        if (!File.Exists(zipPath)) return 0;
        using var zip = ZipFile.OpenRead(zipPath);
        return RestoreConfig(registry, zip);
    }

    // ---------------------------------------------------------------- привести к составу

    /// <summary>Чем состав отличается от того, что стоит: чего нет и что другой версии.</summary>
    public static (List<SetupMod> Missing, List<SetupMod> OtherVersion) Differences(ModRegistry registry, IEnumerable<SetupMod> wanted)
    {
        var missing = new List<SetupMod>();
        var other = new List<SetupMod>();
        foreach (var w in wanted)
        {
            if (registry.Get(w.Id) is not { } have || have.Bool("missing")) { missing.Add(w); continue; }
            if (w.Version != "" && have.Str("version") is { Length: > 0 } v && !Versions.Same(v, w.Version)) other.Add(w);
        }
        return (missing, other);
    }

    /// <summary>Включить и выключить моды, как в составе (моды не из состава выключаются). Возвращает, сколько переключено.</summary>
    public static int ApplyEnabled(ModRegistry registry, IEnumerable<SetupMod> wanted)
    {
        registry.Reconcile();
        var on = wanted.Where(m => m.Enabled).Select(m => m.Id).ToHashSet();
        var listed = wanted.Select(m => m.Id).ToHashSet();
        var changed = 0;
        foreach (var mod in registry.List().Where(r => r.Str("target") is null && r.Str("kind") != "preset" && !r.Bool("missing")))
        {
            var id = mod.Str("id")!;
            // Вторая папка того же мода («мод#папка») идёт за своим модом, если в составе её нет отдельно.
            var key = !listed.Contains(id) && id.IndexOf('#') is > 0 and var at ? id[..at] : id;
            var want = on.Contains(key);
            if (mod.Bool("enabled", true) == want) continue;
            try { registry.SetEnabled(id, want); changed++; } catch { }
        }
        return changed;
    }

    /// <summary>Поставить нужную версию из архива загрузок (без интернета). false — такой версии в архиве нет.</summary>
    public static async Task<bool> FromArchive(GameState g, SetupMod mod)
    {
        var registry = g.Registry!;
        if (DownloadArchive.For(g.Def.Id, mod.Id).FirstOrDefault(f => Versions.Same(f.Version, mod.Version)) is not { } file) return false;
        var meta = new JsonObject { ["id"] = mod.Id, ["name"] = mod.Name, ["version"] = mod.Version, ["source"] = mod.Source };
        if (registry.Get(mod.Id) is { } old)
            foreach (var key in new[] { "author", "url", "icon", "requestedBy" }) meta[key] = old[key]?.DeepClone();
        if (registry.Has(mod.Id)) await ModUpdates.Replace(g, mod.Id, () => Task.FromResult(Installer.InstallArchive(registry, file.Path, meta)));
        else Installer.InstallArchive(registry, file.Path, meta);
        return true;
    }

    [SelfTest]
    static string ConfigRoundTrip()
    {
        var game = Path.Combine(Paths.DataDir, "setup-test", "Lethal Company");
        var config = Directory.CreateDirectory(Path.Combine(game, "BepInEx", "config")).FullName;
        File.WriteAllText(Path.Combine(config, "a.cfg"), "one");
        Directory.CreateDirectory(Path.Combine(config, "sub"));
        File.WriteAllText(Path.Combine(config, "sub", "b.cfg"), "two");
        var registry = new ModRegistry(GameCatalog.ById("lethal-company")!, game);
        var zip = Path.Combine(Paths.DataDir, "setup-test", "config.zip");
        if (!SaveConfig(registry, zip)) throw new Exception("config not saved");
        File.WriteAllText(Path.Combine(config, "a.cfg"), "changed");
        if (RestoreConfig(registry, zip) != 2 || File.ReadAllText(Path.Combine(config, "a.cfg")) != "one") throw new Exception("config not restored");
        var backups = Directory.GetDirectories(Path.Combine(game, "ModHub", "config-backup"));
        if (backups.Length != 1 || File.ReadAllText(Path.Combine(backups[0], "BepInEx", "config", "a.cfg")) != "changed") throw new Exception("replaced config was not backed up");
        if (Target(registry, "config/../../evil.dll") is not null) throw new Exception("path escape not blocked");
        return "2 config files saved, restored, old ones backed up";
    }

    /// <summary>SMAPI: настройки удалённого мода не создают в Mods папку с одним config.json.</summary>
    [SelfTest]
    static string SmapiSkipsRemovedMods()
    {
        var game = Path.Combine(Paths.DataDir, "setup-smapi-test", "Stardew Valley");
        var registry = new ModRegistry(GameCatalog.ById("stardew-valley")!, game);
        foreach (var mod in new[] { "Lookup Anything", "Gone" })
            File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(registry.ModsDir, mod)).FullName, "config.json"), "{}");
        var zip = Path.Combine(Paths.DataDir, "setup-smapi-test", "config.zip");
        SaveConfig(registry, zip);
        Directory.Delete(Path.Combine(registry.ModsDir, "Gone"), true);
        if (RestoreConfig(registry, zip) != 1 || Directory.Exists(Path.Combine(registry.ModsDir, "Gone"))) throw new Exception("settings of a removed mod recreated its folder");
        return "settings of removed SMAPI mods skipped";
    }

    /// <summary>Остаток прошлой записи (.tmp, программу закрыли посреди сохранения) не мешает сохранить настройки.</summary>
    [SelfTest]
    static string SavesOverLeftoverTemp()
    {
        var game = Path.Combine(Paths.DataDir, "setup-tmp-test", "ROUNDS");
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(game, "BepInEx", "config")).FullName, "a.cfg"), "one");
        var registry = new ModRegistry(GameCatalog.ById("rounds")!, game);
        var zip = Path.Combine(Paths.DataDir, "setup-tmp-test", "config.zip");
        File.WriteAllText(zip + ".tmp", "half-written");
        if (!SaveConfig(registry, zip)) throw new Exception("config not saved");
        return "leftover .tmp replaced";
    }

    /// <summary>Вторая папка мода («мод#папка») идёт за своим модом: код r2modman её отдельно не знает.</summary>
    [SelfTest]
    static string PartsFollowTheirMod()
    {
        var game = Path.Combine(Paths.DataDir, "setup-parts-test", "PEAK");
        var registry = new ModRegistry(GameCatalog.ById("peak")!, game);
        foreach (var (id, folder, source) in new[] { ("A-Mod", "Mod", "thunderstore"), ("A-Mod#Extra", "Extra", "thunderstore"), ("B-Other", "Other", "file") })
        {
            Directory.CreateDirectory(Path.Combine(registry.ModsDir, folder));
            registry.Add(new JsonObject { ["id"] = id, ["name"] = folder, ["folder"] = folder, ["version"] = "1.0.0", ["source"] = source, ["requestedBy"] = id == "A-Mod#Extra" ? "A-Mod" : null });
        }
        ApplyEnabled(registry, [new SetupMod("A-Mod", "Mod", "1.0.0", "thunderstore", true)]);
        if (!registry.Get("A-Mod#Extra")!.Bool("enabled", true)) throw new Exception("second folder of an enabled mod was turned off");
        if (registry.Get("B-Other")!.Bool("enabled", true)) throw new Exception("mod outside the setup stayed on");
        var (inCode, left) = ProfileCode.Pick(registry);
        if (inCode.Count != 1 || left.Count != 1 || left[0] != "Other") throw new Exception("code: " + string.Join(", ", inCode.Select(m => m.Id)) + " | left: " + string.Join(", ", left));
        return "part follows its mod, file mod left out of the code";
    }
}
