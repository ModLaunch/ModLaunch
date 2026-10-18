using System.Text.Json;
using System.Text.RegularExpressions;

namespace ModScriptX;

/// <summary>Определение движка по папке игры и обзор её кода: что можно читать и к чему цепляться.</summary>
public static class Engines
{
    /// <summary>Папки и файлы античитов. С такими играми инжектор не работает: он только для одиночных модов.</summary>
    static readonly string[] AntiCheat = ["EasyAntiCheat", "BattlEye", "PunkBuster", "XIGNCODE", "GameGuard", "nProtect", "EAAntiCheat", "mhyprot", "vgk.sys", "FACEIT"];

    public static string? FindAntiCheat(string gameDir)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(gameDir, "*", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true }))
        {
            var name = Path.GetFileName(entry);
            var hit = AntiCheat.FirstOrDefault(a => name.StartsWith(a, StringComparison.OrdinalIgnoreCase));
            if (hit != null) return hit;
        }
        return null;
    }

    public static Target? Detect(string gameDir)
    {
        bool Dir(params string[] p) => Directory.Exists(Path.Combine([gameDir, .. p]));
        bool File_(params string[] p) => File.Exists(Path.Combine([gameDir, .. p]));
        bool Any(string pattern) => Directory.EnumerateFiles(gameDir, pattern).Any();

        if (Dir("renpy") || Dir("game") && Directory.EnumerateFiles(Path.Combine(gameDir, "game"), "*.rpy*", SearchOption.AllDirectories).Any()) return Target.Find("renpy");
        if (File_("www", "js", "plugins.js") || File_("js", "plugins.js")) return Target.Find("rpgmaker");
        if (Directory.EnumerateDirectories(gameDir, "*_Data").Any(d => Directory.Exists(Path.Combine(d, "Managed"))) || Any("UnityPlayer.dll")) return Target.Find("unity");
        if (Any("*.pck") || File_("project.godot")) return Target.Find("godot");
        if (Any("*.love") || File_("main.lua") || Dir("Mods") || Dir("mods")) return Target.Find("lua");
        return null;
    }

    /// <summary>Обзор кода игры для автора мода: скрипты, переменные, точки подключения.</summary>
    public static List<string> Inspect(string gameDir, Target t)
    {
        var o = new List<string> { $"engine: {t.Title} · language: {t.Language}" };
        void List(string title, IEnumerable<string> items, int max = 25)
        {
            var all = items.Distinct().ToList();
            o.Add($"{title} ({all.Count}):");
            o.AddRange(all.Take(max).Select(i => "  " + i));
            if (all.Count > max) o.Add($"  … and {all.Count - max} more");
        }
        string Rel(string p) => Path.GetRelativePath(gameDir, p).Replace('\\', '/');
        IEnumerable<string> Files(string dir, string pattern) =>
            Directory.Exists(dir) ? Directory.EnumerateFiles(dir, pattern, new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }) : [];
        IEnumerable<string> Grep(IEnumerable<string> files, string regex) =>
            files.SelectMany(f => File.ReadLines(f).Select(l => Regex.Match(l, regex)).Where(m => m.Success).Select(m => m.Groups[1].Value));

        switch (t.Id)
        {
            case "renpy":
            {
                var game = Path.Combine(gameDir, "game");
                var rpy = Files(game, "*.rpy").ToList();
                List("scripts", rpy.Select(Rel));
                if (rpy.Count == 0 && Files(game, "*.rpyc").Any()) o.Add("  only compiled .rpyc/.rpa found: decompile with unrpyc to read the story code");
                List("labels (jump/call targets)", Grep(rpy, @"^\s*label\s+([\w.]+)"));
                List("variables (use in set/add)", Grep(rpy, @"^\s*(?:default|define)\s+([\w.]+)\s*="));
                break;
            }
            case "rpgmaker":
            {
                var js = Directory.Exists(Path.Combine(gameDir, "www")) ? Path.Combine(gameDir, "www") : gameDir;
                var plugins = File.ReadAllText(Path.Combine(js, "js", "plugins.js"));
                List("plugins", Regex.Matches(plugins, "\"name\"\\s*:\\s*\"([^\"]+)\"\\s*,\\s*\"status\"\\s*:\\s*(true|false)").Select(m => m.Groups[1].Value + (m.Groups[2].Value == "true" ? "" : " (off)")));
                var system = Path.Combine(js, "data", "System.json");
                if (File.Exists(system))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(system));
                    IEnumerable<string> Named(string key, string api) => doc.RootElement.TryGetProperty(key, out var arr)
                        ? arr.EnumerateArray().Select((e, i) => (Name: e.GetString() ?? "", i)).Where(x => x.Name != "").Select(x => $"{api}({x.i}) — {x.Name}")
                        : [];
                    List("variables (set $gameVariables.setValue / call)", Named("variables", "$gameVariables.value"));
                    List("switches", Named("switches", "$gameSwitches.value"));
                }
                break;
            }
            case "unity":
            {
                var managed = Directory.EnumerateDirectories(gameDir, "*_Data").Select(d => Path.Combine(d, "Managed")).FirstOrDefault(Directory.Exists);
                o.Add(managed != null && File.Exists(Path.Combine(managed, "Assembly-CSharp.dll"))
                    ? $"game code: {Rel(Path.Combine(managed, "Assembly-CSharp.dll"))} (open in ILSpy or dnSpy to read classes)"
                    : "game code: IL2CPP build (no Assembly-CSharp.dll); use BepInEx 6 IL2CPP + Il2CppDumper");
                o.Add(Directory.Exists(Path.Combine(gameDir, "BepInEx", "core")) ? "BepInEx: installed" : "BepInEx: not installed (needed to load .modx mods)");
                List("plugins", Files(Path.Combine(gameDir, "BepInEx", "plugins"), "*.dll").Select(Rel));
                break;
            }
            case "godot":
            {
                List("game packs", Directory.EnumerateFiles(gameDir, "*.pck").Select(Rel));
                List("scripts", Files(gameDir, "*.gd").Select(Rel));
                List("mods", Files(Path.Combine(gameDir, "mods"), "*.zip").Select(Rel));
                o.Add("Godot Mod Loader must be set up in the game for mods/*.zip to load");
                break;
            }
            default:
            {
                var lua = Files(gameDir, "*.lua").ToList();
                List("scripts", lua.Select(Rel));
                List("global functions (hook by wrapping)", Grep(lua, @"^\s*function\s+([\w.:]+)\s*\("));
                break;
            }
        }
        return o;
    }
}
