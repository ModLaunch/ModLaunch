using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Mods;

namespace ModLaunch.Features;

/// <summary>
/// Конфликт двух включённых модов. Kind: duplicate — один и тот же мод стоит дважды (разные версии или копии);
/// overlap — разные моды кладут файл с одним именем (загрузится только один). Loser — id мода, которого
/// разумно выключить (старшая копия дубля; у overlap — тот, чей файл старее), WinnerName — кто остаётся.
/// </summary>
public sealed record Conflict(string A, string B, int Files, string Kind = "overlap", string? Loser = null, string? WinnerName = null, List<string>? Shared = null);

/// <summary>
/// 23.0: конфликты файлов, как в Vortex, но умнее. Общие библиотеки (Harmony, MonoMod, Newtonsoft.Json,
/// System.*, …) и файлы, которые кладут три и больше модов, — не конфликт: так моды обычно и устроены, и
/// выключать из-за них ничего нельзя. Оставшиеся пары делятся на «один мод дважды» (чинится сам: старшая
/// копия выключается) и «разные моды спорят за файл» (предлагаем оставить более новый — только по кнопке).
/// </summary>
public static partial class Conflicts
{
    /// <summary>Библиотеки, которые моды носят с собой: совпадение таких файлов — не конфликт.</summary>
    [GeneratedRegex(@"^(0?harmony.*|monomod.*|mono\.cecil.*|newtonsoft\.json.*|system\..*|microsoft\..*|netstandard|bepinex.*|unityengine.*|assembly-csharp.*|mmhook_.*|.*\.publicized|r2api.*|csync|lethalconfig|lethallib|nautilus|smlhelper|configurationmanager|hookgenpatcher|xunity.*|mscorlib)\.dll$", RegexOptions.IgnoreCase)]
    private static partial Regex SharedLib();

    public static bool IsSharedLibrary(string file) => SharedLib().IsMatch(file);

    public static List<Conflict> Find(ModRegistry registry)
    {
        var mods = new List<(JsonObject Mod, string Name, Dictionary<string, string> Files)>();
        foreach (var mod in registry.List())
        {
            if (!mod.Bool("enabled", true) || mod.Bool("missing") || mod.Str("kind") == "preset") continue;
            var folder = registry.FolderFor(mod);
            if (!Directory.Exists(folder)) continue;
            Dictionary<string, string> files;
            try
            {
                files = Directory.EnumerateFiles(folder, "*.dll", SearchOption.AllDirectories).Take(500)
                    .GroupBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            mods.Add((mod, mod.Str("name") ?? mod.Str("id") ?? "", files));
        }
        // Сколько модов носят каждый файл: три и больше — это общая библиотека, даже если её нет в списке.
        var carriers = mods.SelectMany(m => m.Files.Keys).GroupBy(f => f, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var result = new List<Conflict>();
        for (var i = 0; i < mods.Count; i++)
            for (var j = i + 1; j < mods.Count; j++)
            {
                var (a, b) = (mods[i], mods[j]);
                var same = a.Files.Keys.Where(b.Files.ContainsKey).ToList();
                if (same.Count == 0) continue;
                var duplicate = SameMod(a.Mod, b.Mod);
                // У дубля считаем все общие файлы; у разных модов — только «свои», не библиотеки.
                var real = duplicate ? same : same.Where(f => !IsSharedLibrary(f) && carriers[f] < 3).ToList();
                if (real.Count == 0) continue;
                if (duplicate)
                {
                    var older = Older(a.Mod, b.Mod);
                    var winner = ReferenceEquals(older, a.Mod) ? b : a;
                    result.Add(new Conflict(a.Name, b.Name, real.Count, "duplicate", older.Str("id"), winner.Name, real));
                }
                else
                {
                    // Разные моды: «проигрывает» тот, чей общий файл старее (по версии файла, потом по дате).
                    var f = real[0];
                    var loser = Compare(a.Files[f], b.Files[f]) < 0 ? a : b;
                    var win = ReferenceEquals(loser.Mod, a.Mod) ? b : a;
                    result.Add(new Conflict(a.Name, b.Name, real.Count, "overlap", loser.Mod.Str("id"), win.Name, real));
                }
            }
        return result.OrderBy(c => c.Kind == "duplicate" ? 0 : 1).ThenByDescending(c => c.Files).ToList();
    }

    [GeneratedRegex(@"[-_ .]?v?\d+(\.\d+)+.*$")] private static partial Regex VersionTail();

    /// <summary>Один ли это мод: тот же мод Nexus, тот же id без версии, то же имя или та же папка без версии.</summary>
    internal static bool SameMod(JsonObject a, JsonObject b)
    {
        static string Base(string? s) => Deps.Norm(VersionTail().Replace(s ?? "", "")).TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        static string? Nexus(JsonObject m) => m.Str("id") is { } id && id.StartsWith("nexus:", StringComparison.Ordinal) ? id : null;
        if (Nexus(a) is { } na && na == Nexus(b)) return true;
        var (ia, ib) = (Base(a.Str("id")?.Split(':')[^1]), Base(b.Str("id")?.Split(':')[^1]));
        if (ia.Length >= 3 && ia == ib) return true;
        var (sa, sb) = (Base(a.Str("name")), Base(b.Str("name")));
        if (sa.Length >= 3 && sa == sb) return true;
        var (fa, fb) = (Base(a.Str("folder")), Base(b.Str("folder")));
        return fa.Length >= 3 && fa == fb;
    }

    /// <summary>Какая из двух копий старше: по версии, при равенстве — кто поставлен раньше.</summary>
    static JsonObject Older(JsonObject a, JsonObject b)
    {
        static Version? V(JsonObject m) => Version.TryParse(Regex.Match(m.Str("version") ?? "", @"\d+(\.\d+){1,3}").Value, out var v) ? v : null;
        if (V(a) is { } va && V(b) is { } vb && va != vb) return va < vb ? a : b;
        return string.CompareOrdinal(a.Str("installedAt") ?? "", b.Str("installedAt") ?? "") <= 0 ? a : b;
    }

    /// <summary>Сравнить два файла: по версии сборки, потом по дате изменения.</summary>
    static int Compare(string a, string b)
    {
        try
        {
            var (va, vb) = (System.Diagnostics.FileVersionInfo.GetVersionInfo(a), System.Diagnostics.FileVersionInfo.GetVersionInfo(b));
            var c = new Version(va.FileMajorPart, va.FileMinorPart, va.FileBuildPart, va.FilePrivatePart).CompareTo(new Version(vb.FileMajorPart, vb.FileMinorPart, vb.FileBuildPart, vb.FilePrivatePart));
            if (c != 0) return c;
            return File.GetLastWriteTimeUtc(a).CompareTo(File.GetLastWriteTimeUtc(b));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }
    }

    /// <summary>Размер папки мода — для сортировки «по размеру».</summary>
    public static long FolderSize(string path)
    {
        try
        {
            if (File.Exists(path)) return new FileInfo(path).Length;
            return Directory.Exists(path) ? new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;
        }
        catch { return 0; }
    }

    [SelfTest]
    static string ConflictsKnowLibrariesFromDuplicates()
    {
        var dir = Path.Combine(Paths.DataDir, "conflict-test", "Lethal Company");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        var plugins = Directory.CreateDirectory(Path.Combine(dir, "BepInEx", "plugins")).FullName;
        var registry = new ModRegistry(Games.GameCatalog.ById("lethal-company")!, dir);
        void Mod(string id, string name, string folder, string version, string installed, params string[] dlls)
        {
            var d = Directory.CreateDirectory(Path.Combine(plugins, folder)).FullName;
            foreach (var f in dlls) File.WriteAllText(Path.Combine(d, f), id);
            registry.Add(new JsonObject { ["id"] = id, ["name"] = name, ["folder"] = folder, ["version"] = version, ["installedAt"] = installed });
        }
        // Два разных мода с Harmony внутри — не конфликт; одна и та же библиотека у троих — тоже.
        Mod("A-Alpha", "Alpha", "Alpha", "1.0.0", "2026-01-01", "Alpha.dll", "0Harmony.dll", "Common.dll");
        Mod("B-Beta", "Beta", "Beta", "1.0.0", "2026-01-02", "Beta.dll", "0Harmony.dll", "Common.dll");
        Mod("C-Gamma", "Gamma", "Gamma", "1.0.0", "2026-01-03", "Gamma.dll", "Common.dll");
        // Один мод дважды: старшая версия уступает, даже если её поставили позже.
        Mod("D-MoreSuits-1.4.1", "More Suits", "MoreSuits-1.4.1", "1.4.1", "2026-01-15", "MoreSuits.dll");
        Mod("D-MoreSuits-1.2.0", "More Suits", "MoreSuits-1.2.0", "1.2.0", "2026-02-01", "MoreSuits.dll");
        // Разные моды спорят за свой файл.
        Mod("E-Radio", "Radio", "Radio", "1.0.0", "2026-03-01", "Music.dll");
        Mod("F-Jukebox", "Jukebox", "Jukebox", "1.0.0", "2026-03-02", "Music.dll");
        try
        {
            var found = Find(registry);
            if (found.Any(c => c.Kind != "duplicate" && c.Shared!.Any(f => IsSharedLibrary(f) || f == "Common.dll")))
                throw new Exception("libraries reported as conflicts: " + string.Join("; ", found.Select(c => $"{c.A}/{c.B} {string.Join(",", c.Shared!)}")));
            var dup = found.SingleOrDefault(c => c.Kind == "duplicate") ?? throw new Exception("duplicate not found: " + string.Join("; ", found.Select(c => $"{c.A}/{c.B} {c.Kind}")));
            if (dup.Loser != "D-MoreSuits-1.2.0") throw new Exception("kept the older copy: loser " + dup.Loser);
            var overlap = found.SingleOrDefault(c => c.Kind == "overlap") ?? throw new Exception("overlap not found");
            if (overlap.Shared!.Single() != "Music.dll") throw new Exception("overlap files");
            return $"{found.Count} real conflicts among 7 mods: duplicate keeps 1.4.1, Radio and Jukebox share Music.dll; Harmony and a common library ignored";
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
