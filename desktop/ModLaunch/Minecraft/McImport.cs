using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;

namespace ModLaunch.Minecraft;

/// <summary>Сборка, найденная в другом лаунчере: её можно подключить без копирования.</summary>
public sealed record McFound(string Name, string Dir, string Source, string GameVersion, string Loader, string LoaderVersion, int Mods);

/// <summary>
/// Сборки из других лаунчеров: Modrinth App, Prism Launcher (и PolyMC, MultiMC), CurseForge, ATLauncher, GDLauncher — и моды в самой .minecraft.
/// Версию и загрузчик берём из их файлов, а если их нет — из последнего лога игры.
/// </summary>
public static partial class McImport
{
    static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string SourceTitle(string source) => source switch
    {
        "modrinth-app" => "Modrinth App",
        "prism" => "Prism Launcher",
        "polymc" => "PolyMC",
        "multimc" => "MultiMC",
        "atlauncher" => "ATLauncher",
        "gdlauncher" => "GDLauncher",
        "curseforge" => "CurseForge",
        "minecraft" => ".minecraft",
        "mrpack" => "Modrinth (.mrpack)",
        _ => source,
    };

    public static List<McFound> Scan()
    {
        var list = new List<McFound>();
        try { list.AddRange(ModrinthApp()); } catch { }
        try { list.AddRange(Prism()); } catch { }
        try { list.AddRange(CurseForge()); } catch { }
        try { list.AddRange(ATLauncher()); } catch { }
        try { list.AddRange(GDLauncher()); } catch { }
        try { if (RootMods() is { } root) list.Add(root); } catch { }
        var linked = Mc.Instances().Select(i => Norm(i.Dir)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return list.Where(f => !linked.Contains(Norm(f.Dir))).ToList();
    }

    static string Norm(string dir) { try { return Path.GetFullPath(dir).TrimEnd('\\', '/'); } catch { return dir; } }

    static int Mods(string dir)
    {
        try { return Directory.Exists(Path.Combine(dir, "mods")) ? Directory.EnumerateFiles(Path.Combine(dir, "mods"), "*.jar").Count() : 0; }
        catch { return 0; }
    }

    static IEnumerable<McFound> ModrinthApp()
    {
        foreach (var root in new[] { Path.Combine(AppData, "ModrinthApp", "profiles"), Path.Combine(AppData, "com.modrinth.theseus", "profiles") })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var name = Path.GetFileName(dir);
                string gv = "", loader = "vanilla", lv = "";
                // Старый Modrinth App хранил profile.json; новый — базу, поэтому запасной путь — лог игры.
                var profile = Path.Combine(dir, "profile.json");
                if (File.Exists(profile) && JsonNode.Parse(File.ReadAllText(profile))?["metadata"] is JsonNode m)
                {
                    name = m.Str("name") ?? name;
                    gv = m.Str("game_version") ?? "";
                    loader = (m.Str("loader") ?? "vanilla").ToLowerInvariant();
                    lv = m["loader_version"].Str("id") ?? "";
                }
                if (gv == "" && FromLog(dir) is { } log) (gv, loader, lv) = log;
                if (gv == "" && Directory.Exists(Path.Combine(dir, ".fabric"))) loader = "fabric";
                yield return new McFound(name, dir, "modrinth-app", gv, loader, lv, Mods(dir));
            }
        }
    }

    /// <summary>Prism Launcher и его предки с тем же форматом: PolyMC и MultiMC (тот — переносной, ищем в типичных местах).</summary>
    static IEnumerable<McFound> Prism()
    {
        var roots = new List<(string Dir, string Source)>
        {
            (Path.Combine(AppData, "PrismLauncher", "instances"), "prism"),
            (Path.Combine(AppData, "PolyMC", "instances"), "polymc"),
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "PrismLauncher", "instances"), "prism"),
            (Path.Combine(Home, "MultiMC", "instances"), "multimc"),
            (Path.Combine(Home, "Desktop", "MultiMC", "instances"), "multimc"),
            (Path.Combine(Home, "Downloads", "MultiMC", "instances"), "multimc"),
            (@"C:\MultiMC\instances", "multimc"),
        };
        foreach (var (root, source) in roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var found in MmcInstances(root, source)) yield return found;
        }
    }

    static IEnumerable<McFound> MmcInstances(string root, string source)
    {
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var pack = Path.Combine(dir, "mmc-pack.json");
            if (!File.Exists(pack)) continue;
            var cfg = Path.Combine(dir, "instance.cfg");
            var name = File.Exists(cfg) && Regex.Match(File.ReadAllText(cfg), @"(?m)^name=(.+)$") is { Success: true } nm ? nm.Groups[1].Value.Trim() : Path.GetFileName(dir);
            string gv = "", loader = "vanilla", lv = "";
            foreach (var c in (JsonNode.Parse(File.ReadAllText(pack))?["components"] as JsonArray ?? []).OfType<JsonNode>())
            {
                var v = c.Str("version") ?? "";
                switch (c.Str("uid"))
                {
                    case "net.minecraft": gv = v; break;
                    case "net.fabricmc.fabric-loader": loader = "fabric"; lv = v; break;
                    case "org.quiltmc.quilt-loader": loader = "quilt"; lv = v; break;
                    case "net.minecraftforge": loader = "forge"; lv = v; break;
                    case "net.neoforged": loader = "neoforge"; lv = v; break;
                }
            }
            var game = new[] { ".minecraft", "minecraft" }.Select(d => Path.Combine(dir, d)).FirstOrDefault(Directory.Exists) ?? Path.Combine(dir, ".minecraft");
            yield return new McFound(name, game, source, gv, loader, lv, Mods(game));
        }
    }

    static IEnumerable<McFound> CurseForge()
    {
        var root = Path.Combine(Home, "curseforge", "minecraft", "Instances");
        if (!Directory.Exists(root)) yield break;
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var file = Path.Combine(dir, "minecraftinstance.json");
            if (!File.Exists(file)) continue;
            var j = JsonNode.Parse(File.ReadAllText(file));
            var (loader, lv) = ParseCurseLoader(j?["baseModLoader"].Str("name") ?? "");
            yield return new McFound(j.Str("name") ?? Path.GetFileName(dir), dir, "curseforge", j.Str("gameVersion") ?? "", loader, lv, Mods(dir));
        }
    }

    /// <summary>ATLauncher: instances\&lt;имя&gt;\instance.json — версия игры в "id", загрузчик в launcher.loaderVersion.</summary>
    static IEnumerable<McFound> ATLauncher()
    {
        foreach (var root in new[] { Path.Combine(AppData, "ATLauncher", "instances"), Path.Combine(Home, "ATLauncher", "instances") })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var file = Path.Combine(dir, "instance.json");
                if (!File.Exists(file)) continue;
                if (ParseATLauncher(File.ReadAllText(file), Path.GetFileName(dir)) is not { } f) continue;
                yield return f with { Dir = dir, Mods = Mods(dir) };
            }
        }
    }

    public static McFound? ParseATLauncher(string json, string folder)
    {
        var j = JsonNode.Parse(json);
        if (j is null) return null;
        var gv = j.Str("id") ?? j["launcher"].Str("version") ?? "";
        var lvNode = j["launcher"]?["loaderVersion"];
        var type = (lvNode.Str("type") ?? "").ToLowerInvariant();
        var loader = type is "fabric" or "quilt" or "forge" or "neoforge" ? type : "vanilla";
        var lv = loader == "vanilla" ? "" : lvNode.Str("version") ?? "";
        return new McFound(j["launcher"].Str("name") ?? folder, "", "atlauncher", gv, loader, lv, 0);
    }

    /// <summary>GDLauncher: новый (Carbon) — data\instances\&lt;имя&gt;\instance.json и папка instance; старый — instances\&lt;имя&gt;\config.json.</summary>
    static IEnumerable<McFound> GDLauncher()
    {
        var carbon = Path.Combine(AppData, "gdlauncher_carbon", "data", "instances");
        if (Directory.Exists(carbon))
            foreach (var dir in Directory.EnumerateDirectories(carbon))
            {
                var file = Path.Combine(dir, "instance.json");
                var game = Path.Combine(dir, "instance");
                if (!File.Exists(file) || !Directory.Exists(game)) continue;
                if (ParseGdCarbon(File.ReadAllText(file), Path.GetFileName(dir)) is { } f) yield return f with { Dir = game, Mods = Mods(game) };
            }
        var legacy = Path.Combine(AppData, "gdlauncher_next", "instances");
        if (Directory.Exists(legacy))
            foreach (var dir in Directory.EnumerateDirectories(legacy))
            {
                var file = Path.Combine(dir, "config.json");
                if (!File.Exists(file)) continue;
                var j = JsonNode.Parse(File.ReadAllText(file));
                var loaderType = (j?["loader"].Str("loaderType") ?? "vanilla").ToLowerInvariant();
                var loader = loaderType is "fabric" or "quilt" or "forge" or "neoforge" ? loaderType : "vanilla";
                yield return new McFound(Path.GetFileName(dir), dir, "gdlauncher", j?["loader"].Str("mcVersion") ?? "", loader, loader == "vanilla" ? "" : j?["loader"].Str("loaderVersion") ?? "", Mods(dir));
            }
    }

    public static McFound? ParseGdCarbon(string json, string folder)
    {
        var j = JsonNode.Parse(json);
        if (j is null) return null;
        var standard = j["game_configuration"]?["version"]?["Standard"];
        var gv = standard.Str("release") ?? "";
        var first = (standard?["modloaders"] as JsonArray ?? []).OfType<JsonNode>().FirstOrDefault();
        var type = (first.Str("type_") ?? first.Str("type") ?? "").ToLowerInvariant();
        var loader = type is "fabric" or "quilt" or "forge" or "neoforge" ? type : "vanilla";
        return new McFound(j.Str("name") ?? folder, "", "gdlauncher", gv, loader, loader == "vanilla" ? "" : first.Str("version") ?? "", 0);
    }

    [SelfTest]
    static string ReadsOtherLaunchers()
    {
        var at = ParseATLauncher("""{"id":"1.20.1","launcher":{"name":"Create Above","loaderVersion":{"type":"Fabric","version":"0.15.11"}}}""", "x");
        var gd = ParseGdCarbon("""{"name":"Skyblock","game_configuration":{"version":{"Standard":{"release":"1.21.1","modloaders":[{"type_":"NeoForge","version":"21.1.77"}]}}}}""", "y");
        if (at is not { Name: "Create Above", GameVersion: "1.20.1", Loader: "fabric", LoaderVersion: "0.15.11" }) throw new Exception("ATLauncher: " + at);
        if (gd is not { Name: "Skyblock", GameVersion: "1.21.1", Loader: "neoforge", LoaderVersion: "21.1.77" }) throw new Exception("GDLauncher: " + gd);
        return "ATLauncher and GDLauncher instances read";
    }

    /// <summary>«forge-47.2.0», «neoforge-21.1.77», «fabric-0.15.7-1.20.1» → загрузчик и версия.</summary>
    public static (string Loader, string Version) ParseCurseLoader(string name)
    {
        var parts = name.Split('-');
        if (parts.Length < 2) return ("vanilla", "");
        var loader = parts[0].ToLowerInvariant();
        return loader is "forge" or "neoforge" or "fabric" or "quilt" ? (loader, parts[1]) : ("vanilla", "");
    }

    /// <summary>Моды прямо в .minecraft\mods (так их ставят вручную и через установщик Fabric).</summary>
    static McFound? RootMods()
    {
        var root = Mc.Root;
        if (Mods(root) == 0) return null;
        var (gv, loader, lv) = FromLog(root) ?? ("", Directory.Exists(Path.Combine(root, ".fabric")) ? "fabric" : "vanilla", "");
        return new McFound(I18n.T("mine.import.root"), root, "minecraft", gv, loader, lv, Mods(root));
    }

    [GeneratedRegex(@"Loading Minecraft (\S+) with (Fabric|Quilt) Loader (\S+)")] private static partial Regex FabricLine();
    [GeneratedRegex(@"--fml\.mcVersion,\s*([^,\]\s]+)")] private static partial Regex FmlMc();
    [GeneratedRegex(@"--fml\.neoForgeVersion,\s*([^,\]\s]+)")] private static partial Regex FmlNeo();
    [GeneratedRegex(@"--fml\.forgeVersion,\s*([^,\]\s]+)")] private static partial Regex FmlForge();

    /// <summary>Версия и загрузчик из logs\latest.log.</summary>
    public static (string GameVersion, string Loader, string LoaderVersion)? FromLog(string dir)
    {
        var log = Path.Combine(dir, "logs", "latest.log");
        if (!File.Exists(log)) return null;
        string text;
        try
        {
            using var f = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var r = new StreamReader(f);
            var buffer = new char[200_000];
            text = new string(buffer, 0, r.Read(buffer, 0, buffer.Length));
        }
        catch { return null; }
        return Parse(text);
    }

    public static (string GameVersion, string Loader, string LoaderVersion)? Parse(string text)
    {
        if (FabricLine().Match(text) is { Success: true } f) return (f.Groups[1].Value, f.Groups[2].Value.ToLowerInvariant(), f.Groups[3].Value);
        if (FmlMc().Match(text) is { Success: true } mc)
        {
            if (FmlNeo().Match(text) is { Success: true } neo) return (mc.Groups[1].Value, "neoforge", neo.Groups[1].Value);
            if (FmlForge().Match(text) is { Success: true } forge) return (mc.Groups[1].Value, "forge", forge.Groups[1].Value);
        }
        return null;
    }
}
