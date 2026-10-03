using System.IO.Compression;
using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Games;

namespace ModLaunch.Mods;

/// <summary>
/// Раскладка архива для BepInEx, как в r2modman: plugins — обычные моды, patchers и monomod —
/// патчеры, которые BepInEx грузит до игры (им место в BepInEx/patchers), config — настройки,
/// core — сам BepInEx (его мод трогать не должен).
/// </summary>
public static class BepInExLayout
{
    /// <summary>Часть архива: что это (plugins, patchers, monomod, config, core) и путь к ней внутри архива.</summary>
    public sealed record Part(string Kind, string Prefix);

    static readonly string[] Kinds = ["plugins", "patchers", "monomod", "config", "core"];

    /// <summary>Части, которые ставятся отдельно от plugins, — у записи мода поле target.</summary>
    public static bool IsRouted(string kind) => kind is "patchers" or "monomod";

    /// <summary>Найти части: сразу в корне, под общей папкой-обёрткой или под BepInEx/.</summary>
    public static List<Part> Find(IReadOnlyList<string> files)
    {
        var top = files.Select(f => f.Split('/')[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var wrapper = top.Count == 1 && files.All(f => f.Contains('/')) && !Kinds.Contains(top[0], StringComparer.OrdinalIgnoreCase)
            && !top[0].Equals("BepInEx", StringComparison.OrdinalIgnoreCase) ? top[0] + "/" : "";
        // Core/ и config/ бывают и своими папками мода: берём их, только если архив разложен как BepInEx
        // (рядом manifest.json Thunderstore, plugins/, patchers/ или monomod/). Путь с BepInEx/ — всегда.
        var package = files.Any(f => f.Equals(wrapper + "manifest.json", StringComparison.OrdinalIgnoreCase)
            || new[] { "plugins/", "patchers/", "monomod/" }.Any(k => f.StartsWith(wrapper + k, StringComparison.OrdinalIgnoreCase)));
        var parts = new List<Part>();
        foreach (var kind in Kinds)
        foreach (var prefix in package ? new[] { wrapper + kind + "/", wrapper + "BepInEx/" + kind + "/" } : [wrapper + "BepInEx/" + kind + "/"])
        {
            // Регистр в архивах бывает любой (Patchers/, bepinex/): берём, как написано в архиве.
            var hit = files.FirstOrDefault(f => f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) parts.Add(new Part(kind, hit[..(prefix.Length - 1)]));
        }
        return parts;
    }

    /// <summary>Лежит ли файл архива в одной из частей (чтобы не положить его ещё и в plugins).</summary>
    public static bool Inside(string entry, IEnumerable<Part> parts) =>
        parts.Any(p => entry.StartsWith(p.Prefix + "/", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Настройки из архива — в BepInEx/config. Обычный мод не трогает уже настроенное;
    /// сборка (modpack) заменяет, но сначала кладёт старые файлы в ModHub/config-backup.
    /// </summary>
    public static int ApplyConfig(string archive, Part part, string gamePath, bool overwrite)
    {
        var target = Path.Combine(gamePath, "BepInEx", "config");
        if (!overwrite) return Archive.Extract(archive, target, part.Prefix, ignoreCase: true, keepExisting: true).Count;
        var backup = Path.Combine(gamePath, "ModHub", "config-backup", DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        var prefix = part.Prefix + "/";
        foreach (var entry in Archive.Files(archive).Where(f => f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            var relative = entry[prefix.Length..];
            var existing = Path.GetFullPath(Path.Combine(target, relative));
            // Как в Archive.Extract: путь наружу config (../) не трогаем — иначе копия легла бы куда угодно.
            if (!existing.StartsWith(Path.GetFullPath(target) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(existing)) continue;
            var copy = Path.Combine(backup, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(existing, copy, true);
        }
        return Archive.Extract(archive, target, part.Prefix, ignoreCase: true).Count;
    }

    /// <summary>Архив с plugins, patchers и config: патчер — в BepInEx/patchers, настройки не затираются, выкл/вкл работают.</summary>
    [SelfTest]
    static string RoutesPatchersAndConfig()
    {
        var game = Path.Combine(Paths.DataDir, "layout-test", "Lethal Company");
        Directory.CreateDirectory(Path.Combine(game, "BepInEx", "config"));
        File.WriteAllText(Path.Combine(game, "BepInEx", "config", "mine.cfg"), "user value");
        var zip = Path.Combine(Paths.DataDir, "layout-test", "pack.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            void Add(string name, string text) { using var w = new StreamWriter(archive.CreateEntry(name).Open()); w.Write(text); }
            Add("manifest.json", "{\"name\":\"HookGen\",\"version_number\":\"1.0.0\",\"dependencies\":[]}");
            Add("plugins/HookGen.dll", "plugin");
            Add("patchers/HookGenPatcher/HookGenPatcher.dll", "patcher");
            Add("config/mine.cfg", "pack value");
            Add("config/new.cfg", "pack value");
            Add("core/BepInEx.dll", "must not be touched");
        }
        var registry = new ModRegistry(GameCatalog.ById("lethal-company")!, game);
        var main = Installer.InstallArchive(registry, zip, new JsonObject { ["id"] = "Owner-HookGen", ["name"] = "HookGen", ["source"] = "thunderstore" });
        var patcher = Path.Combine(game, "BepInEx", "patchers", "HookGen", "HookGenPatcher", "HookGenPatcher.dll");
        if (!File.Exists(patcher)) throw new Exception("patcher is not in BepInEx/patchers");
        if (!File.Exists(Path.Combine(registry.FolderFor(main), "HookGen.dll"))) throw new Exception("plugin is not in plugins");
        if (Directory.EnumerateFiles(registry.ModsDir, "HookGenPatcher.dll", SearchOption.AllDirectories).Any()) throw new Exception("patcher also landed in plugins");
        if (File.ReadAllText(Path.Combine(game, "BepInEx", "config", "mine.cfg")) != "user value") throw new Exception("user config was overwritten");
        if (!File.Exists(Path.Combine(game, "BepInEx", "config", "new.cfg"))) throw new Exception("new config not added");
        if (File.Exists(Path.Combine(game, "BepInEx", "core", "BepInEx.dll"))) throw new Exception("core was touched");
        registry.SetEnabled("Owner-HookGen", false);
        if (File.Exists(patcher)) throw new Exception("patcher still active after disabling the mod");
        registry.SetEnabled("Owner-HookGen", true);
        if (!File.Exists(patcher)) throw new Exception("patcher not back after enabling");
        registry.Remove("Owner-HookGen");
        if (Directory.Exists(Path.GetDirectoryName(Path.GetDirectoryName(patcher))) || registry.Get("Owner-HookGen#patchers") is not null) throw new Exception("remove left the patcher behind");
        // Папка мода со своими Core/ и config/ — это не BepInEx, а пакет Thunderstore в папке (с manifest.json) — да.
        if (Find(["MyMod/MyMod.dll", "MyMod/Core/MyMod.Core.dll", "MyMod/config/defaults.json"]).Count != 0
            || Find(["MyMod.dll", "Core/MyMod.Core.dll"]).Count != 0) throw new Exception("mod's own Core/ and config/ taken for BepInEx parts");
        if (Find(["Owner-Mod/manifest.json", "Owner-Mod/plugins/Mod.dll", "Owner-Mod/config/Mod.cfg"]).Count != 2
            || Find(["plugins/Mod.dll", "config/Mod.cfg"]).Count != 2 || Find(["Mod/BepInEx/config/Mod.cfg", "Mod/Mod.dll"]).Count != 1) throw new Exception("BepInEx-shaped archive not routed");
        return "plugins/, patchers/ and config/ routed; user config kept; core ignored; off/on/remove cascade";
    }
}
