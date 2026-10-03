using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;

namespace ModLaunch.Minecraft;

/// <summary>Сборка, найденная в другом лаунчере: её можно подключить без копирования.</summary>
public sealed record McFound(string Name, string Dir, string Source, string GameVersion, string Loader, string LoaderVersion, int Mods);

/// <summary>
/// Сборки из других лаунчеров: Modrinth App, Prism Launcher, CurseForge — и моды в самой .minecraft.
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

    static IEnumerable<McFound> Prism()
    {
        var root = Path.Combine(AppData, "PrismLauncher", "instances");
        if (!Directory.Exists(root)) yield break;
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
            yield return new McFound(name, game, "prism", gv, loader, lv, Mods(game));
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
