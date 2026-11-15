using System.Text.Json.Nodes;
using ModLaunch.Core;

namespace ModLaunch.Games;

/// <summary>Сколько занимает игра и на каком диске; Measured = посчитали по файлам (а не взяли из лаунчера).</summary>
public sealed record GameSpace(LibGame Game, long Bytes, string Drive, bool Measured);

/// <summary>Диск: всего, свободно и сколько из занятого — игры из библиотеки.</summary>
public sealed record DriveSpace(string Root, string Label, long Total, long Free, long Games);

/// <summary>
/// 30.2: «Место на дисках» — как менеджер хранилища Steam, но для игр из всех лаунчеров. Размер берём у лаунчера
/// (Steam, Epic, EA, Ubisoft, Battle.net пишут его сами); нет — считаем файлы папки в фоне (по одной игре,
/// ссылки и точки повторной обработки пропускаем) и помним три дня в cache/sizes.json.
/// </summary>
public static class DiskUsage
{
    static readonly TimeSpan Fresh = TimeSpan.FromDays(3);
    static readonly Lazy<JsonFile> Cache = new(() => new JsonFile(Path.Combine(Paths.CacheDir, "sizes.json"), () => new JsonObject()));
    static readonly SemaphoreSlim Gate = new(1);

    /// <summary>Посчитан ещё один размер — страница перерисуется.</summary>
    public static event Action? Changed;

    public static string DriveOf(string? dir)
    {
        if (string.IsNullOrEmpty(dir)) return "";
        try { return Path.GetPathRoot(Path.GetFullPath(dir))?.ToUpperInvariant() ?? ""; }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return ""; }
    }

    static string? FolderOf(LibGame g) => g.Source?.AltDir is { } alt && Directory.Exists(alt) ? alt : g.Dir;

    /// <summary>Размер из лаунчера или из кэша; null — ещё не считали.</summary>
    static long? Known(LibGame g, string? dir)
    {
        if (g.Size > 0) return g.Size;
        if (dir is null || Cache.Value.Data[dir.ToLowerInvariant()] is not JsonObject hit) return null;
        var at = DateTime.TryParse(hit.Str("at"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t : DateTime.MinValue;
        return DateTime.UtcNow - at < Fresh ? hit.Long("bytes") : null;
    }

    /// <summary>Установленные игры с местом (известным сейчас), самые большие сверху.</summary>
    public static List<GameSpace> Games(IEnumerable<LibGame> games) =>
        games.Where(g => g.Installed)
            .Select(g => (Game: g, Dir: FolderOf(g)))
            .Select(x => new GameSpace(x.Game, Known(x.Game, x.Dir) ?? -1, DriveOf(x.Dir), x.Game.Size <= 0))
            .OrderByDescending(s => s.Bytes).ThenBy(s => s.Game.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>Диски с играми (и системный), с местом на них.</summary>
    public static List<DriveSpace> Drives(IReadOnlyList<GameSpace> spaces)
    {
        var roots = spaces.Select(s => s.Drive).Where(r => r != "").ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (DriveOf(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) is { Length: > 0 } system) roots.Add(system);
        var list = new List<DriveSpace>();
        foreach (var root in roots.Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var d = new DriveInfo(root);
                if (!d.IsReady) continue;
                var games = spaces.Where(s => string.Equals(s.Drive, root, StringComparison.OrdinalIgnoreCase) && s.Bytes > 0).Sum(s => s.Bytes);
                list.Add(new DriveSpace(root, d.VolumeLabel, d.TotalSize, d.AvailableFreeSpace, games));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        return list;
    }

    /// <summary>Сколько байт в папке (все вложенные файлы; недоступное и ссылки пропускаем).</summary>
    public static long Measure(string dir, CancellationToken ct = default)
    {
        long total = 0;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var file in new DirectoryInfo(dir).EnumerateFiles("*", options))
        {
            ct.ThrowIfCancellationRequested();
            try { total += file.Length; } catch (IOException) { }
        }
        return total;
    }

    /// <summary>Досчитать в фоне игры без размера (по одной; после каждой — Changed).</summary>
    public static async Task MeasureMissing(IEnumerable<LibGame> games, CancellationToken ct)
    {
        if (Program.Screenshot) return;
        if (!await Gate.WaitAsync(0, ct)) return;
        try
        {
            foreach (var g in games.Where(g => g.Installed && g.Size <= 0).ToList())
            {
                ct.ThrowIfCancellationRequested();
                var dir = FolderOf(g);
                if (dir is null || !Directory.Exists(dir) || Known(g, dir) is not null) continue;
                var bytes = await Task.Run(() => Measure(dir, ct), ct);
                lock (Cache.Value)
                {
                    Cache.Value.Data[dir.ToLowerInvariant()] = new JsonObject { ["bytes"] = bytes, ["at"] = DateTime.UtcNow.ToString("o") };
                    Cache.Value.Save();
                }
                Changed?.Invoke();
            }
        }
        catch (OperationCanceledException) { }
        finally { Gate.Release(); }
    }

    /// <summary>Игры, которые давно не запускали (или никогда) и которые занимают много: их удаление освободит больше всего.</summary>
    public static List<GameSpace> Idle(IEnumerable<GameSpace> spaces, Func<LibGame, long> lastPlayed, DateTimeOffset now, int days = 60, long minBytes = 2L << 30) =>
        spaces.Where(s => s.Bytes >= minBytes && lastPlayed(s.Game) is var last && (last == 0 || now.ToUnixTimeSeconds() - last >= days * 86400L))
            .OrderByDescending(s => s.Bytes).ToList();

    /// <summary>Где удалять игру: Steam — сам спросит и удалит; остальные — страница игры в её лаунчере или «Приложения» Windows.</summary>
    public static string? UninstallUri(LibGame g) => g.Store switch
    {
        "steam" when g.SteamAppId > 0 => $"steam://uninstall/{g.SteamAppId}",
        "other" => null,
        _ => AllGames.LauncherUri(g) ?? "ms-settings:appsfeatures",
    };

    [SelfTest]
    static string DiskUsageMeasuresAndPicksIdleGames()
    {
        var dir = Path.Combine(Path.GetTempPath(), "modlaunch-du-" + Environment.ProcessId);
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "a", "b"));
            File.WriteAllBytes(Path.Combine(dir, "x.bin"), new byte[1000]);
            File.WriteAllBytes(Path.Combine(dir, "a", "b", "y.bin"), new byte[234]);
            if (Measure(dir) != 1234) throw new Exception("measure " + Measure(dir));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }

        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        GameSpace S(string key, long gb) => new(new LibGame(key, "steam", key, null, 1, true, gb << 30), gb << 30, "C:\\", false);
        var last = new Dictionary<string, long> { ["old"] = now.AddDays(-90).ToUnixTimeSeconds(), ["new"] = now.AddDays(-3).ToUnixTimeSeconds(), ["never"] = 0, ["small"] = 0 };
        var idle = Idle([S("old", 40), S("new", 80), S("never", 10), S("small", 1)], g => last[g.Key], now);
        if (string.Join(",", idle.Select(s => s.Game.Key)) != "old,never") throw new Exception("idle " + string.Join(",", idle.Select(s => s.Game.Key)));
        if (UninstallUri(new LibGame("steam:10", "steam", "X", null, 10, true, 0)) != "steam://uninstall/10") throw new Exception("steam uninstall");
        if (UninstallUri(new LibGame("other:x", "other", "X", null, 0, true, 0)) is not null) throw new Exception("own copies are not uninstalled for you");
        if (DriveOf(@"d:\Games\X") != @"D:\") throw new Exception("drive");
        return "folder size counted recursively, idle = 60+ days unplayed and ≥ 2 GB, Steam uninstall link";
    }
}
