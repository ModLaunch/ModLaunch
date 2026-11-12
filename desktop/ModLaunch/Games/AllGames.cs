using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Games;

/// <summary>
/// Игра единой библиотеки. Key — «магазин:номер» (steam:570, epic:Fortnite, xbox:Семейство, ml:id-игры-ModLaunch).
/// SteamAppId — для обложек (у игр не из Steam — найденный по имени, 0 — не нашёлся). ManagedId — игра уже в ModLaunch (с модами).
/// SteamLast/SteamMinutes — то, что знает о ней Steam; своё время ModLaunch добавляет в Played.
/// </summary>
public sealed record LibGame(string Key, string Store, string Name, string? Dir, int SteamAppId, bool Installed, long Size,
    long SteamLast = 0, long SteamMinutes = 0, StoreGame? Source = null, string? ManagedId = null)
{
    /// <summary>Ключ учёта времени: игра ModLaunch считает своё время сама, остальные — под lib:Key.</summary>
    public string TimeId => ManagedId ?? "lib:" + Key;
}

/// <summary>Игра ModLaunch с папкой на диске — снимок для фонового потока (AppState меняется только в UI).</summary>
public sealed record ManagedCopy(string Id, string Name, string Path, int SteamAppId);

/// <summary>
/// 27.0: единая библиотека — Steam (весь аккаунт), Epic, GOG, Xbox, EA app, Ubisoft Connect, Battle.net
/// и игры, добавленные в ModLaunch вручную (репаки, свои папки). Одна игра — одна строка: копия Steam,
/// которая уже есть в ModLaunch, не дублируется. Сканирование — в фоне, кеш на минуту.
/// Часы: Steam считает свои, остальным ModLaunch считает сам — замечает процесс игры в её папке после запуска.
/// </summary>
public static partial class AllGames
{
    static List<LibGame>? _games;
    static DateTime _at;
    static Task? _loading;

    public static event Action? Changed;

    /// <summary>Последний собранный список (null — ещё не собран).</summary>
    public static IReadOnlyList<LibGame>? Current => _games;

    public static bool Stale => _games is null || DateTime.UtcNow - _at > TimeSpan.FromMinutes(1);

    /// <summary>Пересобрать библиотеку в фоне. Вызывать из UI-потока; повторный вызов во время сборки ждёт ту же сборку.</summary>
    public static Task Refresh()
    {
        if (_loading is { IsCompleted: false }) return _loading;
        var managed = AppState.Games.Where(g => g.Path is not null && g.Status == Detect.Found)
            .Select(g => new ManagedCopy(g.Def.Id, g.Def.Name, g.Path!, g.Def.SteamAppId)).ToList();
        var demo = Program.Demo || Program.Screenshot;
        var done = new TaskCompletionSource();
        _loading = done.Task;
        _ = Task.Run(async () =>
        {
            List<LibGame>? games = null;
            try
            {
                await SteamCatalog.EnsureLoaded();
                games = Merge(SteamAccount.All, demo ? DemoStores() : Stores.ScanAll(), managed, SteamIdByName);
            }
            catch { }
            Dispatcher.UIThread.Post(() =>
            {
                if (games is not null) { _games = games; _at = DateTime.UtcNow; }
                done.TrySetResult();
                Changed?.Invoke();
            });
        });
        return _loading;
    }

    /// <summary>Номер Steam по имени: справочник игр ModLaunch, потом весь каталог Steam (только точное имя).</summary>
    static int SteamIdByName(string name)
    {
        if (GameDirectory.ByName(name) is { SteamAppId: > 0 } dir) return dir.SteamAppId;
        var folded = SteamCatalog.Fold(name);
        return folded == "" ? 0 : SteamCatalog.Search(name, 3).FirstOrDefault(d => SteamCatalog.Fold(d.Name) == folded)?.SteamAppId ?? 0;
    }

    static string Norm(string path)
    {
        try { return Path.GetFullPath(path).TrimEnd('\\', '/'); } catch { return path.TrimEnd('\\', '/'); }
    }

    /// <summary>
    /// Склейка: Steam → остальные лаунчеры → игры ModLaunch без пары. Игра ModLaunch, лежащая в папке игры Steam
    /// или другого лаунчера, становится её ManagedId (играть — через ModLaunch, с модами), а не отдельной строкой.
    /// </summary>
    internal static List<LibGame> Merge(IEnumerable<SteamGame> steam, IEnumerable<StoreGame> other, IReadOnlyList<ManagedCopy> managed, Func<string, int> steamIdByName)
    {
        var byPath = new Dictionary<string, ManagedCopy>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in managed) byPath.TryAdd(Norm(m.Path), m);
        var used = new HashSet<string>();
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<LibGame>();

        string? Claim(string? dir)
        {
            if (dir is null || !byPath.TryGetValue(Norm(dir), out var m) || !used.Add(m.Id)) return null;
            return m.Id;
        }

        foreach (var s in steam)
        {
            var dir = s.Installed?.Path is { Length: > 0 } p ? p : null;
            if (dir is not null) dirs.Add(Norm(dir));
            result.Add(new LibGame("steam:" + s.AppId, "steam", s.Name, dir, s.AppId, s.IsInstalled, s.Size, s.LastPlayed, s.Minutes, null, Claim(dir)));
        }
        foreach (var g in other)
        {
            var key = Norm(g.Dir);
            if (!dirs.Add(key)) continue;
            result.Add(new LibGame(g.Store + ":" + g.Id, g.Store, g.Name, g.Dir, steamIdByName(g.Name), true, g.Size, 0, 0, g, Claim(g.Dir)));
        }
        foreach (var m in managed)
        {
            if (used.Contains(m.Id) || !dirs.Add(Norm(m.Path))) continue;
            result.Add(new LibGame("ml:" + m.Id, "other", m.Name, m.Path, m.SteamAppId > 0 ? m.SteamAppId : steamIdByName(m.Name), true, 0, 0, 0, null, m.Id));
        }
        return result;
    }

    // ---------------------------------------------------------------- время

    /// <summary>Последний запуск (unix, 0 — не запускали) и минуты в игре: у Steam — его счёт или наш, если наш больше.</summary>
    public static (long Last, long Minutes) Played(LibGame g)
    {
        var own = PlayTime.Get(g.TimeId);
        var last = own.LastPlayed is { } at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)).ToUnixTimeSeconds() : 0;
        var minutes = own.TotalMs / 60_000;
        return (Math.Max(g.SteamLast, last), g.Store == "steam" ? Math.Max(g.SteamMinutes, minutes) : minutes);
    }

    public static bool IsRunning(LibGame g) => g.ManagedId is { } id ? Launcher.IsRunning(id) : PlayTime.IsRunning(g.TimeId);

    // ---------------------------------------------------------------- запуск

    /// <summary>
    /// Запустить игру не из ModLaunch (у игр ModLaunch — Actions.Play с модами). Возвращает текст ошибки для человека или null.
    /// Steam считает время сам; у остальных ModLaunch ждёт процесс из папки игры и считает время, пока он жив.
    /// </summary>
    public static string? Play(LibGame g)
    {
        if (Program.Screenshot) return null;
        var s = g.Source;
        try
        {
            if (g.Store == "steam") { SteamLibrary.Play(g.SteamAppId); return null; }
            if (s?.Uri is { } uri)
            {
                if (uri.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                    Process.Start(new ProcessStartInfo("explorer.exe", uri) { UseShellExecute = true });
                else Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            }
            else if ((s?.Exe ?? MainExe(g)) is { } exe && File.Exists(exe))
                Process.Start(new ProcessStartInfo(exe, s?.Args ?? "") { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) ?? "" });
            else return I18n.T("v27.lib.noExe");
        }
        catch (Win32Exception e) when (e.NativeErrorCode is 2 or 1155)
        {
            // 1155 — у протокола нет программы: лаунчер удалили, а игра осталась.
            return I18n.T("v27.lib.noLauncher", ("store", Stores.Title(g.Store)));
        }
        catch (Exception e) { return I18n.T("v27.lib.cantStart", ("error", e.Message)); }
        Watch(g);
        return null;
    }

    static string? MainExe(LibGame g) => g.Dir is { } dir && CustomGames.MainExe(dir, g.Name) is { } rel ? Path.Combine(dir, rel) : null;

    /// <summary>Открыть игру в её лаунчере (страница игры), если у лаунчера есть такая ссылка.</summary>
    public static string? LauncherUri(LibGame g) => g.Store switch
    {
        "steam" => $"steam://nav/games/details/{g.SteamAppId}",
        "epic" => "com.epicgames.launcher://store",
        "gog" when g.Source is { } s => $"goggalaxy://openGameView/{s.Id}",
        "ubisoft" when g.Source is { } s => $"uplay://open/game/{s.Id}",
        "battlenet" when g.Source is { } s => "battlenet://" + s.Id,
        _ => null,
    };

    static readonly HashSet<string> Watching = [];

    static void Watch(LibGame g)
    {
        if (g.Store == "steam") return;
        var id = g.TimeId;
        var dirs = new[] { g.Dir, g.Source?.AltDir }.OfType<string>().Select(d => Norm(d) + "\\").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        lock (Watching) if (dirs.Length == 0 || !Watching.Add(id)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                // Лаунчер может сначала обновить игру или попросить войти — ждём процесс до пяти минут.
                var until = DateTime.UtcNow.AddMinutes(5);
                while (!ProcessPaths.AnyUnder(dirs))
                {
                    if (DateTime.UtcNow > until)
                    {
                        Dispatcher.UIThread.Post(() => { PlayTime.Start(id); PlayTime.Stop(id); Changed?.Invoke(); });
                        return;
                    }
                    await Task.Delay(3000);
                }
                Dispatcher.UIThread.Post(() => { PlayTime.Start(id); Changed?.Invoke(); });
                // Два промаха подряд: игра могла перезапуститься (лаунчер → игра), это не конец сеанса.
                for (var miss = 0; miss < 2;)
                {
                    await Task.Delay(5000);
                    miss = ProcessPaths.AnyUnder(dirs) ? 0 : miss + 1;
                }
                Dispatcher.UIThread.Post(() => { PlayTime.Stop(id); Changed?.Invoke(); });
            }
            finally { lock (Watching) Watching.Remove(id); }
        });
    }

    /// <summary>--list-games: библиотека этого ПК текстом (без окна и без сети). Игры ModLaunch здесь не склеиваются — только лаунчеры.</summary>
    public static int Cli()
    {
        SteamCatalog.EnsureLoaded().GetAwaiter().GetResult();
        var games = Merge(SteamAccount.All.Where(g => g.IsInstalled), Stores.ScanAll(), [], SteamIdByName);
        foreach (var g in games.OrderBy(g => Array.IndexOf(Stores.Order, g.Store)).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
            Console.WriteLine($"{g.Store,-10} {g.Name} | steam={g.SteamAppId} | {g.Dir} | {g.Source?.Uri ?? g.Source?.Exe ?? (g.Store == "steam" ? "steam://rungameid/" + g.SteamAppId : "exe from folder")}");
        Console.WriteLine($"{games.Count} installed games, stores: {string.Join(", ", games.GroupBy(g => g.Store).Select(x => x.Key + " " + x.Count()))}");
        return 0;
    }

    // ---------------------------------------------------------------- демо

    static List<StoreGame> DemoStores() =>
    [
        new("epic", "Fortnite", "Fortnite", @"D:\Epic Games\Fortnite", Size: 32_000_000_000),
        new("epic", "Hades", "Hades", @"D:\Epic Games\Hades", Size: 15_000_000_000),
        new("gog", "1207664643", "The Witcher 3: Wild Hunt", @"D:\GOG Games\The Witcher 3", Size: 0),
        new("gog", "1495134320", "Cyberpunk 2077", @"D:\GOG Games\Cyberpunk 2077"),
        new("xbox", "Microsoft.624F8B84B80_8wekyb3d8bbwe", "Forza Horizon 5", @"C:\XboxGames\Forza Horizon 5\Content"),
        new("xbox", "Microsoft.SunriseBaseGame_8wekyb3d8bbwe", "Starfield", @"C:\XboxGames\Starfield\Content"),
        new("ea", "Origin.OFR.50.0004657", "Battlefield 2042", @"D:\EA Games\Battlefield 2042", Size: 100_000_000_000),
        new("ubisoft", "4923", "Anno 1800", @"D:\Ubisoft\Anno 1800", Size: 60_000_000_000),
        new("battlenet", "prometheus", "Overwatch", @"D:\Battle.net\Overwatch", Size: 45_000_000_000),
        new("battlenet", "fenris", "Diablo IV", @"D:\Battle.net\Diablo IV", Size: 90_000_000_000),
    ];

    [SelfTest]
    static string LibraryMergesStores()
    {
        var steamApp = new SteamApp(413150, "Stardew Valley", "Stardew Valley", @"C:\Steam\steamapps\common\Stardew Valley", 1_700_000_000, 700);
        SteamGame[] steam = [new(413150, "Stardew Valley", steamApp, 1_700_000_000, 600), new(570, "Dota 2", null, 1_600_000_000, 64_800)];
        StoreGame[] other =
        [
            new("epic", "Hades", "Hades", @"D:\Epic\Hades"),
            new("gog", "1", "Stardew Copy", @"C:\Steam\steamapps\common\Stardew Valley\"), // та же папка, что у Steam — второй строкой не нужна
            new("xbox", "Fam_x", "Forza Horizon 5", @"C:\XboxGames\Forza\Content"),
        ];
        ManagedCopy[] managed =
        [
            new("stardew", "Stardew Valley", @"C:\Steam\steamapps\common\Stardew Valley", 413150),
            new("hades", "Hades", @"D:\Epic\Hades", 0),
            new("repack", "Hollow Knight", @"E:\Repacks\Hollow Knight", 367520),
        ];
        var list = Merge(steam, other, managed, n => n == "Hades" ? 1145350 : 0);
        var keys = string.Join(",", list.Select(g => g.Key));
        if (keys != "steam:413150,steam:570,epic:Hades,xbox:Fam_x,ml:repack") throw new Exception(keys);
        if (list[0].ManagedId != "stardew" || list[1].ManagedId is not null || list[1].Installed || list[2].ManagedId != "hades" || list[2].SteamAppId != 1145350)
            throw new Exception("links: " + string.Join("; ", list));
        if (list[4] is not { Store: "other", SteamAppId: 367520, ManagedId: "repack", Installed: true }) throw new Exception("repack: " + list[4]);
        if (list[3].TimeId != "lib:xbox:Fam_x" || list[0].TimeId != "stardew") throw new Exception("time ids");
        return "Steam + Epic + Xbox + ModLaunch copy: one row per folder, ModLaunch copies linked, covers by name";
    }
}

/// <summary>Есть ли запущенный процесс из папки игры. Путь — QueryFullProcessImageName с минимальными правами (работает и для игр от администратора).</summary>
static partial class ProcessPaths
{
    const uint QueryLimited = 0x1000;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool QueryFullProcessImageName(IntPtr process, int flags, char* name, ref int size);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);

    static unsafe string? PathOf(int pid)
    {
        var h = OpenProcess(QueryLimited, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var buffer = stackalloc char[1024];
            var size = 1024;
            return QueryFullProcessImageName(h, 0, buffer, ref size) ? new string(buffer, 0, size) : null;
        }
        finally { CloseHandle(h); }
    }

    /// <summary>dirs — полные пути папок с «\» на конце.</summary>
    public static bool AnyUnder(string[] dirs)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var self = Environment.ProcessId;
        var all = Process.GetProcesses();
        try
        {
            foreach (var p in all)
            {
                if (p.Id == self || p.Id <= 4) continue;
                if (PathOf(p.Id) is { } path && dirs.Any(d => path.StartsWith(d, StringComparison.OrdinalIgnoreCase))) return true;
            }
            return false;
        }
        finally { foreach (var p in all) p.Dispose(); }
    }
}
