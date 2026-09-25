using System.Text.Json;
using Microsoft.Win32;

namespace ModLaunch.Games;

public sealed record Located(string Path, string Source);

/// <summary>
/// Поиск игры на диске: Steam (реестр → libraryfolders.vdf → appmanifest),
/// Epic (манифесты лаунчера), GOG (реестр) и, наконец, обход правдоподобных
/// папок с опознаванием по содержимому — у репаков имя папки любое.
/// </summary>
public static class Locator
{
    const int MaxDirsPerRoot = 400, MaxTotalDirs = 6000;
    const int DeepMaxDirsPerRoot = 1200, DeepMaxTotalDirs = 40000;

    static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "windows", "windows.old", "$recycle.bin", "system volume information", "recovery", "perflogs", "appdata",
        "programdata", "node_modules", "msocache", "config.msi", "inetpub", "windowsapps", "packagecache",
    };

    public static IEnumerable<string> SteamRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (OperatingSystem.IsWindows())
        {
            foreach (var (hive, key, value) in new[]
            {
                (Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
                (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
                (Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
            })
            {
                string? found = null;
                try { found = hive.OpenSubKey(key)?.GetValue(value) as string; } catch { }
                if (found is not null) found = Path.GetFullPath(found.Replace('/', '\\'));
                if (found is not null && Directory.Exists(found) && seen.Add(found)) yield return found;
            }
            foreach (var drive in new[] { "C:", "D:", "E:" })
            foreach (var suffix in new[] { @"\Program Files (x86)\Steam", @"\Program Files\Steam", @"\Steam" })
            {
                var p = drive + suffix;
                if (Directory.Exists(p) && seen.Add(p)) yield return p;
            }
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var p in new[] { ".steam/steam", ".local/share/Steam", ".var/app/com.valvesoftware.Steam/data/Steam" })
            {
                var full = Path.Combine(home, p);
                if (Directory.Exists(full) && seen.Add(full)) yield return full;
            }
        }
    }

    public static List<string> SteamLibraries()
    {
        var libraries = new List<string>();
        foreach (var root in SteamRoots())
        {
            Add(root);
            try
            {
                var vdf = Vdf.Parse(File.ReadAllText(Path.Combine(root, "steamapps", "libraryfolders.vdf")));
                var list = Vdf.Get(vdf, "libraryfolders") as Dictionary<string, object> ?? vdf;
                foreach (var entry in list.Values)
                {
                    if (entry is Dictionary<string, object> d && d.TryGetValue("path", out var p) && p is string s) Add(s);
                    else if (entry is string old && (old.Contains('\\') || old.Contains('/'))) Add(old);
                }
            }
            catch { }
        }
        return libraries;

        void Add(string p)
        {
            if (!libraries.Contains(p, StringComparer.OrdinalIgnoreCase)) libraries.Add(p);
        }
    }

    public static string? FindSteamApp(int appId, List<string> libraries)
    {
        foreach (var library in libraries)
        {
            try
            {
                var manifest = Vdf.Parse(File.ReadAllText(Path.Combine(library, "steamapps", $"appmanifest_{appId}.acf")));
                if (Vdf.Get(manifest, "AppState", "installdir") is not string dir || dir == "") continue;
                var gamePath = Path.Combine(library, "steamapps", "common", dir);
                if (Directory.Exists(gamePath)) return gamePath;
            }
            catch { }
        }
        return null;
    }

    static string Norm(string s) => new(s.ToLowerInvariant().Where(c => !char.IsWhiteSpace(c)).ToArray());

    static string? FindInEpic(GameDef game)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var dir = Path.Combine(programData, "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(dir)) return null;
        var wanted = game.FolderNames.Append(game.Name).Select(Norm).ToHashSet();
        foreach (var file in Directory.EnumerateFiles(dir, "*.item"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                string? Prop(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                var names = new[] { Prop("DisplayName"), Prop("InstallationGuid"), Prop("MandatoryAppFolderName") };
                if (!names.Any(n => n is not null && wanted.Contains(Norm(n)))) continue;
                var location = Prop("InstallLocation");
                if (location is not null && Directory.Exists(location)) return location;
            }
            catch { }
        }
        return null;
    }

    static string? FindInGog(GameDef game)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var wanted = game.FolderNames.Append(game.Name).Select(Norm).ToHashSet();
        foreach (var hive in new[] { @"SOFTWARE\WOW6432Node\GOG.com\Games", @"SOFTWARE\GOG.com\Games" })
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(hive);
                if (key is null) continue;
                foreach (var sub in key.GetSubKeyNames())
                {
                    using var g = key.OpenSubKey(sub);
                    if (g?.GetValue("gameName") is not string name || !wanted.Contains(Norm(name))) continue;
                    if (g.GetValue("path") is string p && Directory.Exists(p)) return p;
                }
            }
            catch { }
        }
        return null;
    }

    static IEnumerable<string> Drives()
    {
        if (!OperatingSystem.IsWindows()) return ["/"];
        return DriveInfo.GetDrives()
            .Where(d => { try { return d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable; } catch { return false; } })
            .Select(d => d.Name.TrimEnd('\\'));
    }

    static List<(string Dir, int Depth)> ScanRoots(List<string> libraries, bool deep)
    {
        var roots = libraries.Select(l => (Path.Combine(l, "steamapps", "common"), 1)).ToList();
        var templates = new (string, int)[]
        {
            ("", deep ? 3 : 1), ("Games", deep ? 4 : 2), ("Игры", deep ? 4 : 2), ("Game", 2),
            (@"SteamLibrary\steamapps\common", 1), (@"Steam\steamapps\common", 1),
            ("Program Files", deep ? 3 : 2), ("Program Files (x86)", deep ? 3 : 2),
            (@"Program Files\Steam\steamapps\common", 1), (@"Program Files (x86)\Steam\steamapps\common", 1),
            ("GOG Games", 2), (@"GOG Galaxy\Games", 2), ("Epic Games", 2), (@"Program Files\Epic Games", 2),
            (@"Program Files (x86)\Epic Games", 2), ("XboxGames", 2), ("Downloads", 2), ("Загрузки", 2),
            ("Torrents", 2), ("Торренты", 2), ("Repacks", 2), ("Репаки", 2), ("Igruha", 2),
        };
        foreach (var drive in Drives())
        foreach (var (suffix, depth) in templates)
            roots.Add((suffix == "" ? drive + Path.DirectorySeparatorChar : Path.Combine(drive + Path.DirectorySeparatorChar, suffix), depth));

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var folder in new[] { "Desktop", "Downloads", "Documents", "Games", "Рабочий стол", "Загрузки", @"OneDrive\Desktop", @"OneDrive\Рабочий стол" })
            roots.Add((Path.Combine(home, folder), 2));

        return roots
            .GroupBy(r => r.Item1, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, g.Max(r => r.Item2)))
            .ToList();
    }

    static string? Scan(GameDef game, List<string> libraries, bool deep, IProgress<string>? progress, CancellationToken ct)
    {
        var maxTotal = deep ? DeepMaxTotalDirs : MaxTotalDirs;
        var maxPerRoot = deep ? DeepMaxDirsPerRoot : MaxDirsPerRoot;
        var checkedCount = 0;
        foreach (var (rootDir, maxDepth) in ScanRoots(libraries, deep))
        {
            if (checkedCount >= maxTotal || ct.IsCancellationRequested) break;
            if (!Directory.Exists(rootDir)) continue;
            progress?.Report(rootDir);
            var queue = new Queue<(string Dir, int Depth)>();
            queue.Enqueue((rootDir, 0));
            while (queue.Count > 0 && checkedCount < maxTotal)
            {
                var (dir, depth) = queue.Dequeue();
                if (depth > 0)
                {
                    checkedCount++;
                    if (game.MatchesSignature(dir)) return dir;
                }
                if (depth >= maxDepth) continue;
                IEnumerable<string> children;
                try { children = Directory.EnumerateDirectories(dir).Take(maxPerRoot).ToList(); } catch { continue; }
                foreach (var child in children)
                {
                    var name = Path.GetFileName(child);
                    if (name.StartsWith('$') || name.StartsWith('.') || SkipDirs.Contains(name)) continue;
                    queue.Enqueue((child, depth + 1));
                }
            }
        }
        return null;
    }

    public static Task<Located?> Locate(GameDef game, bool deep = false, IProgress<string>? progress = null, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            var libraries = SteamLibraries();
            if (FindSteamApp(game.SteamAppId, libraries) is string steam) return new Located(steam, "steam");
            foreach (var library in libraries)
            foreach (var folder in game.FolderNames)
            {
                var candidate = Path.Combine(library, "steamapps", "common", folder);
                if (Directory.Exists(candidate)) return new Located(candidate, "steam-folder");
            }
            if (FindInEpic(game) is string epic) return new Located(epic, "epic");
            if (FindInGog(game) is string gog) return new Located(gog, "gog");
            return Scan(game, libraries, deep, progress, ct) is string found ? new Located(found, "scan") : null;
        }, ct);

    /// <summary>Проверка папки, выбранной вручную. null — всё хорошо, иначе ключ текста ошибки.</summary>
    public static string? Validate(GameDef game, string dir)
    {
        if (!Directory.Exists(dir)) return "err.pathNotExists";
        if (game.MatchesSignature(dir)) return null;
        if (GameCatalog.All.Any(g => g != game && g.MatchesSignature(dir))) return "err.notGameFolder";
        if (game.Executables.Any(e => File.Exists(Path.Combine(dir, e)))) return null;
        return "err.notGameFolder";
    }
}
