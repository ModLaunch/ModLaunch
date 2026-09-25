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

    public static bool IsRunning(string gameId) { lock (Running) return Running.ContainsKey(gameId); }

    public static DateTime? StartedAt(string gameId) { lock (Running) return Running.TryGetValue(gameId, out var r) ? r.Started : null; }

    /// <summary>Остановить игру (вместе с дочерними процессами: SMAPI запускает саму игру).</summary>
    public static void Stop(string gameId)
    {
        System.Diagnostics.Process? p;
        lock (Running) p = Running.TryGetValue(gameId, out var r) ? r.Process : null;
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

    /// <summary>Запустить. Возвращает имя резервной копии, если она сделана.</summary>
    public static string? Launch(GameDef game, string gamePath)
    {
        var exe = game.LaunchExe(gamePath);
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

        var psi = new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = false };
        foreach (var a in SplitArgs(Args(game.Id))) psi.ArgumentList.Add(a);
        var process = Process.Start(psi) ?? throw new InvalidOperationException(exe);
        var track = PlayTime.Track;
        if (track) PlayTime.Start(game.Id);
        process.EnableRaisingEvents = true;
        lock (Running) Running[game.Id] = (process, DateTime.UtcNow);
        process.Exited += (_, _) =>
        {
            lock (Running) Running.Remove(game.Id);
            var (counted, ms) = track ? PlayTime.Stop(game.Id) : (false, 0L);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Exited?.Invoke(game.Id, counted, ms));
        };
        return backup;
    }
}
