using System.Diagnostics;
using ModLaunch.Core;
using ModLaunch.Mods;

namespace ModLaunch.Minecraft;

/// <summary>
/// «Играть»: поставить загрузчик (если нужно), записать установку в лаунчер, открыть лаунчер.
/// Окно игры ловим по процессу Java с заголовком «Minecraft» — так работают «Запущено» и время в игре.
/// </summary>
public static class McLaunch
{
    /// <summary>Started — лаунчер открыт; AlreadyOpen — он уже был открыт и мог не заметить новую установку.</summary>
    public enum Result { Started, NoLauncher, AlreadyOpen }

    /// <summary>
    /// 24.0: какая сборка запущена последней — её и разбирает «Почему игра упала» (с главной и из друзей
    /// запускают сборку, не делая её выбранной).
    /// </summary>
    public static string? LaunchedId { get; private set; }

    public static McInstance? Launched => (LaunchedId is { } id ? Mc.Get(id) : null) ?? Mc.Active;

    public static async Task<Result> Play(McInstance i, IProgress<InstallStep>? progress = null, CancellationToken ct = default)
    {
        LaunchedId = i.Id;
        await McInstall.EnsureVersion(i, progress, ct);
        // Открытый лаунчер читает установки при старте — новую он может не увидеть, пока его не перезапустят.
        var wasOpen = !Program.Demo && LauncherRunning();
        i.LastPlayed = DateTime.UtcNow;
        Mc.Save(i);
        McProfiles.Sync(i, played: true);
        if (Program.Demo) return Result.Started;
        var launcher = Mc.FindLauncher();
        if (launcher is null) return Result.NoLauncher;
        if (launcher.Kind == "classic" && launcher.Exe is not null)
            Process.Start(new ProcessStartInfo(launcher.Exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(launcher.Exe)! });
        else
            Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{Mc.StorePackage}!Minecraft") { UseShellExecute = true });
        Watch(i);
        return wasOpen ? Result.AlreadyOpen : Result.Started;
    }

    static bool LauncherRunning() => SafeProcesses("MinecraftLauncher").Length > 0;

    static CancellationTokenSource? _watch;

    /// <summary>Ждём окно игры до 10 минут (вход, загрузка версии), потом — пока оно не закроется.</summary>
    static void Watch(McInstance i)
    {
        _watch?.Cancel();
        var cts = _watch = new CancellationTokenSource();
        var since = DateTime.Now;
        _ = Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested && DateTime.Now - since < TimeSpan.FromMinutes(10))
                {
                    await Task.Delay(3000, cts.Token);
                    // 24.0: игра упала ещё до окна (нет Fabric API, не та Java, моды несовместимы) — окна «Minecraft»
                    // не будет, но «Почему игра упала» должно открыться и тут.
                    if (FindGame(since) is null && EarlyCrash(i, since))
                    {
                        Features.Launcher.ReportCrash(Mc.Def!, since.ToUniversalTime(), (long)(DateTime.Now - since).TotalMilliseconds);
                        return;
                    }
                    if (FindGame(since) is { } game)
                    {
                        var jars = 0;
                        try { jars = Directory.Exists(Path.Combine(i.Dir, "mods")) ? Directory.GetFiles(Path.Combine(i.Dir, "mods"), "*.jar").Length : 0; } catch { }
                        Features.BuildTime.Use(Mc.Def!.Id, Mc.Def!.Id + "|mc:" + i.Id, i.Name, jars);
                        Features.Launcher.Track(Mc.Def!, game);
                        // 9.4: время в игре — и у самой сборки.
                        var started = DateTime.Now;
                        try { await game.WaitForExitAsync(); } catch { return; }
                        var played = (long)(DateTime.Now - started).TotalMilliseconds;
                        if (played > 0)
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                if (Mc.Get(i.Id) is not { } fresh) return;
                                fresh.PlayMs += played;
                                Mc.Save(fresh);
                                Mc.Notify();
                            });
                        return;
                    }
                }
            }
            catch { }
        }, cts.Token);
    }

    /// <summary>Что пишут загрузчики, когда игра не запустилась.</summary>
    static readonly string[] EarlyMarkers =
    [
        "Mod resolution encountered", "Incompatible mods found", "UnsupportedClassVersionError", "Missing or unsupported mandatory dependencies",
        "MixinApplyError", "Mixin apply failed", "java.lang.OutOfMemoryError", "Mod loading has failed", "Failed to load mods",
    ];

    /// <summary>Вылет до окна игры: свежий отчёт о вылете или свежий latest.log с ошибкой загрузки модов.</summary>
    internal static bool EarlyCrash(McInstance i, DateTime since)
    {
        try
        {
            var reports = Path.Combine(i.Dir, "crash-reports");
            if (Directory.Exists(reports) && new DirectoryInfo(reports).EnumerateFiles("crash-*.txt").Any(f => f.LastWriteTime > since)) return true;
            var log = Path.Combine(i.Dir, "logs", "latest.log");
            if (!File.Exists(log) || File.GetLastWriteTime(log) <= since) return false;
            var text = string.Join('\n', Features.Logs.Tail(log, 600));
            return EarlyMarkers.Any(m => text.Contains(m, StringComparison.Ordinal));
        }
        catch { return false; }
    }

    /// <summary>Процесс игры: javaw/java с окном «Minecraft …», запущенный после нажатия «Играть».</summary>
    static Process? FindGame(DateTime since)
    {
        foreach (var name in new[] { "javaw", "java" })
        {
            foreach (var p in SafeProcesses(name))
            {
                try
                {
                    if (p.StartTime < since.AddSeconds(-5)) continue;
                    if (p.MainWindowTitle.StartsWith("Minecraft", StringComparison.OrdinalIgnoreCase)) return p;
                }
                catch { }
            }
        }
        return null;
    }

    static Process[] SafeProcesses(string name)
    {
        try { return Process.GetProcessesByName(name); } catch { return []; }
    }
}
