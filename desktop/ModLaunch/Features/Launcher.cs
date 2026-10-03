using System.Diagnostics;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Games;

namespace ModLaunch.Features;

/// <summary>
/// Запуск игры: копия сохранений перед стартом, параметры запуска, учёт
/// времени и возврат окна, когда игра закрылась. Всё как в 3.x.
/// </summary>
public static partial class Launcher
{
    public static event Action<string, bool, long>? Exited; // игра, засчитано, мс

    /// <summary>Запущенные из ModLaunch игры — для кнопки «Остановить» (как в Modrinth App).</summary>
    static readonly Dictionary<string, (System.Diagnostics.Process Process, DateTime Started)> Running = [];

    /// <summary>Как закончился последний запуск — для помощника при вылете.</summary>
    public sealed record ExitInfo(DateTime Started, long Ms, int? Code, bool ByUser);
    static readonly Dictionary<string, ExitInfo> Last = [];
    static readonly HashSet<string> StopRequested = [];
    /// <summary>Снимки экранов: «запущенная» игра без настоящего процесса.</summary>
    static readonly Dictionary<string, DateTime> Demo = [];

    public static bool IsRunning(string gameId) { lock (Running) return Running.ContainsKey(gameId) || Demo.ContainsKey(gameId); }
    public static bool AnyRunning() { lock (Running) return Running.Count > 0; }

    public static DateTime? StartedAt(string gameId)
    {
        lock (Running) return Running.TryGetValue(gameId, out var r) ? r.Started : Demo.TryGetValue(gameId, out var d) ? d : null;
    }

    public static ExitInfo? LastExit(string gameId) { lock (Running) return Last.GetValueOrDefault(gameId); }

    public static void DemoRunning(string gameId, bool on, TimeSpan? ago = null)
    {
        lock (Running) { if (on) Demo[gameId] = DateTime.UtcNow - (ago ?? TimeSpan.Zero); else Demo.Remove(gameId); }
    }

    /// <summary>Остановить игру (вместе с дочерними процессами: SMAPI запускает саму игру).</summary>
    public static void Stop(string gameId)
    {
        System.Diagnostics.Process? p;
        lock (Running)
        {
            p = Running.TryGetValue(gameId, out var r) ? r.Process : null;
            if (p is not null) StopRequested.Add(gameId);
        }
        try { p?.Kill(entireProcessTree: true); } catch { }
    }

    [GeneratedRegex("\"([^\"]*)\"|'([^']*)'|(\\S+)")] private static partial Regex Arg();

    public static List<string> SplitArgs(string? text) =>
        Arg().Matches(text ?? "").Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value)
            .Take(40).Select(a => a.Length > 400 ? a[..400] : a).ToList();

    public static string Args(string gameId) => Settings.Data.Obj("launchArgs").Str(gameId) ?? "";

    public static void SetArgs(string gameId, string args)
    {
        Settings.Data.Obj("launchArgs")[gameId] = args.Trim();
        Settings.Save();
    }

    public static string? SavesDir(GameDef game, string gamePath)
    {
        try { return game.SavesDir?.Invoke(gamePath); } catch { return null; }
    }

    /// <summary>Запустить. Возвращает имя резервной копии, если она сделана. vanilla — без модов, на один раз.</summary>
    public static string? Launch(GameDef game, string gamePath, bool vanilla = false)
    {
        // Загрузчик, отложенный «на один запуск без модов», возвращаем перед обычным запуском.
        if (!vanilla && Vanilla.Mode(game.Id) == "temp") try { Vanilla.Unpark(game); } catch { }
        var exe = vanilla ? Vanilla.Exe(game, gamePath) : game.LaunchExe(gamePath);
        if (!File.Exists(exe))
        {
            if (game.Loader == LoaderKind.Smapi) throw new FileNotFoundException(I18n.T("err.loaderExeMissing", ("loader", game.LoaderName), ("path", exe)));
            var present = Directory.Exists(gamePath) ? Directory.EnumerateFiles(gamePath, "*.exe").Select(Path.GetFileName).Take(10).ToList() : [];
            throw new FileNotFoundException(present.Count > 0
                ? I18n.T("err.gameExeMissing", ("path", exe), ("present", string.Join(", ", present)))
                : I18n.T("err.gameExeMissingEmpty", ("path", exe)));
        }

        string? backup = null;
        if (Backups.OnLaunch)
        {
            try { backup = Backups.Create(game.Id, SavesDir(game, gamePath), "launch")?.Name; } catch { }
        }

        var parked = false;
        if (vanilla && !Vanilla.IsParked(game.Id) && game.Loader != LoaderKind.Smapi)
        {
            Vanilla.Park(game, gamePath, "temp");
            parked = true;
        }
        var psi = new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = false };
        foreach (var a in SplitArgs(Args(game.Id))) psi.ArgumentList.Add(a);
        Process process;
        try { process = Process.Start(psi) ?? throw new InvalidOperationException(exe); }
        catch
        {
            if (parked) try { Vanilla.Unpark(game); } catch { }
            throw;
        }
        Watch(game, process, parked);
        return backup;
    }

    /// <summary>
    /// Следить за процессом игры, запущенной не нами напрямую (Minecraft — через свой лаунчер):
    /// «Запущено», кнопка «Остановить» и время в игре работают так же.
    /// </summary>
    public static void Track(GameDef game, Process process)
    {
        lock (Running) if (Running.ContainsKey(game.Id)) return;
        Watch(game, process, false);
        Avalonia.Threading.Dispatcher.UIThread.Post(AppState.Notify);
    }

    static void Watch(GameDef game, Process process, bool parked)
    {
        var track = PlayTime.Track;
        if (track) PlayTime.Start(game.Id);
        process.EnableRaisingEvents = true;
        lock (Running) { Running[game.Id] = (process, DateTime.UtcNow); StopRequested.Remove(game.Id); }
        process.Exited += (_, _) =>
        {
            int? code = null;
            try { code = process.ExitCode; } catch { }
            var ran = TimeSpan.Zero;
            lock (Running)
            {
                var started = Running.TryGetValue(game.Id, out var r) ? r.Started : DateTime.UtcNow;
                Running.Remove(game.Id);
                ran = DateTime.UtcNow - started;
                Last[game.Id] = new ExitInfo(started, (long)ran.TotalMilliseconds, code, StopRequested.Remove(game.Id));
            }
            var (counted, ms) = track ? PlayTime.Stop(game.Id) : (false, 0L);
            if (parked) _ = Vanilla.RestoreAfterExit(game, ran);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Exited?.Invoke(game.Id, counted, ms));
        };
        // Процесс мог закончиться раньше, чем подписались на Exited.
        try { if (process.HasExited) process.Refresh(); } catch { }
    }
}
