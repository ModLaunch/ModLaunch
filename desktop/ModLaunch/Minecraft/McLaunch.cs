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

    public static async Task<Result> Play(McInstance i, IProgress<InstallStep>? progress = null, CancellationToken ct = default)
    {
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
                    if (FindGame(since) is { } game)
                    {
                        Features.Launcher.Track(Mc.Def!, game);
                        return;
                    }
                }
            }
            catch { }
        }, cts.Token);
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
