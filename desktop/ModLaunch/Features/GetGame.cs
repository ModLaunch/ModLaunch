using System.Diagnostics;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Mods;

namespace ModLaunch.Features;

/// <summary>
/// 9.4: «Игры нет — поставьте её». Игру из Steam ставит сам Steam (steam://install), а ModLaunch
/// следит за его файлом appmanifest: показывает, сколько скачано, и находит игру, как только
/// Steam закончит. Лаунчер Minecraft качается с сайта Mojang и ставится его же установщиком.
/// </summary>
public static class GetGame
{
    /// <summary>Как идёт установка в Steam: скачано и всего (байты), готово ли.</summary>
    public sealed record SteamProgress(long Done, long Total, bool Installed, bool Started);

    static readonly Dictionary<string, SteamProgress> Watching = [];
    static DispatcherTimer? _timer;
    static DateTime _until;

    /// <summary>Стоит ли Steam на этом компьютере.</summary>
    public static bool SteamInstalled => Program.Demo || Locator.SteamRoots().Any();

    public static string StoreUrl(GameDef d) => $"https://store.steampowered.com/app/{d.SteamAppId}/";

    /// <summary>Попросить Steam поставить игру (откроется окно установки Steam) и ждать её.</summary>
    public static void InstallViaSteam(GameState g)
    {
        if (g.Def.SteamAppId <= 0) return;
        if (!Program.Demo)
            try { Process.Start(new ProcessStartInfo($"steam://install/{g.Def.SteamAppId}") { UseShellExecute = true }); } catch { }
        Watch(g);
    }

    /// <summary>Нет Steam — страница загрузки Steam.</summary>
    public static void GetSteam() => Views.Ui.OpenUrl("https://store.steampowered.com/about/");

    /// <summary>Сейчас ждём эту игру из Steam (и что известно о загрузке).</summary>
    public static SteamProgress? Progress(string gameId)
    {
        lock (Watching) return Watching.TryGetValue(gameId, out var p) ? p : null;
    }

    /// <summary>Следить за установкой: раз в 4 секунды читать appmanifest, до трёх часов.</summary>
    public static void Watch(GameState g)
    {
        lock (Watching) Watching[g.Def.Id] = new SteamProgress(0, 0, false, false);
        _until = DateTime.UtcNow.AddHours(3);
        if (_timer is not null) return;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
        AppState.Notify();
    }

    public static void StopWatching(string gameId)
    {
        lock (Watching) Watching.Remove(gameId);
        AppState.Notify();
    }

    static void Poll()
    {
        List<string> ids;
        lock (Watching) ids = [.. Watching.Keys];
        if (ids.Count == 0 || DateTime.UtcNow > _until)
        {
            _timer?.Stop();
            _timer = null;
            lock (Watching) Watching.Clear();
            return;
        }
        foreach (var id in ids)
        {
            var g = AppState.Games.FirstOrDefault(x => x.Def.Id == id);
            if (g is null || g.Status == Detect.Found) { StopWatching(id); continue; }
            var p = Read(g.Def.SteamAppId);
            lock (Watching) Watching[id] = p;
            if (p.Installed)
            {
                StopWatching(id);
                _ = Found(g);
            }
        }
        AppState.Notify();
    }

    static async Task Found(GameState g)
    {
        await AppState.DetectOne(g);
        if (g.Status == Detect.Found) Views.MainWindow.Current?.Toast(Core.I18n.T("get.found", ("game", g.Def.Name)));
    }

    /// <summary>
    /// appmanifest_&lt;id&gt;.acf из библиотек Steam: StateFlags 4 — игра целиком на диске;
    /// BytesDownloaded / BytesToDownload — сколько скачано.
    /// </summary>
    public static SteamProgress Read(int appId)
    {
        foreach (var lib in Locator.SteamLibraries())
        {
            var file = Path.Combine(lib, "steamapps", $"appmanifest_{appId}.acf");
            if (!File.Exists(file)) continue;
            try
            {
                var vdf = Vdf.Parse(File.ReadAllText(file));
                var flags = long.TryParse(Vdf.Get(vdf, "AppState", "StateFlags") as string, out var f) ? f : 0;
                long Num(string key) => long.TryParse(Vdf.Get(vdf, "AppState", key) as string, out var n) ? n : 0;
                var total = Num("BytesToDownload");
                var done = Num("BytesDownloaded");
                // 4 — установлена, без флагов «обновляется/качается» (1024 и выше).
                var installed = (flags & 4) != 0 && (flags & ~(4L | 2L)) == 0;
                return new SteamProgress(done, total, installed, true);
            }
            catch { }
        }
        return new SteamProgress(0, 0, false, false);
    }

    // ---------------------------------------------------------------- Minecraft

    /// <summary>Официальный установщик лаунчера Minecraft (тот же, что в winget: Mojang.MinecraftLauncher).</summary>
    const string LauncherMsi = "https://launcher.mojang.com/download/MinecraftInstaller.msi";

    /// <summary>Лаунчер в Microsoft Store (Xbox) — второй путь.</summary>
    public const string LauncherStore = "https://apps.microsoft.com/detail/9PGW18NPBZV5";

    /// <summary>Скачать и запустить установщик лаунчера Minecraft; когда он закончит — найти Minecraft.</summary>
    public static Job InstallMinecraftLauncher() =>
        Jobs.Run("Minecraft Launcher", "Minecraft", async (_, progress, ct) =>
        {
            progress.Report(new InstallStep("loader.download", "Minecraft Launcher"));
            if (Program.Demo) { await Task.Delay(300, ct); return; }
            var msi = await Http.Download([LauncherMsi], "MinecraftInstaller.msi",
                new Progress<double>(r => progress.Report(new InstallStep("loader.download", "Minecraft Launcher", Ratio: r))), null, ct);
            progress.Report(new InstallStep("loader.install", "Minecraft Launcher"));
            using (var p = Process.Start(new ProcessStartInfo("msiexec.exe") { ArgumentList = { "/i", msi, "/passive" }, UseShellExecute = false }))
                if (p is not null) await p.WaitForExitAsync(ct);
            try { File.Delete(msi); } catch { }
            if (Minecraft.Mc.State is { } mc)
                await Dispatcher.UIThread.InvokeAsync(() => AppState.DetectOne(mc));
            progress.Report(new InstallStep("loader.done", "Minecraft Launcher"));
        });

    [SelfTest]
    static string ReadsSteamManifest()
    {
        var acf = "\"AppState\"\n{\n\t\"appid\"\t\t\"264710\"\n\t\"StateFlags\"\t\t\"1026\"\n\t\"BytesToDownload\"\t\t\"8000\"\n\t\"BytesDownloaded\"\t\t\"2000\"\n}\n";
        var vdf = Vdf.Parse(acf);
        if (Vdf.Get(vdf, "AppState", "BytesDownloaded") as string != "2000") throw new Exception("manifest not parsed");
        var flags = long.Parse((string)Vdf.Get(vdf, "AppState", "StateFlags")!);
        var installed = (flags & 4) != 0 && (flags & ~(4L | 2L)) == 0;
        if (installed) throw new Exception("1026 (downloading) taken for installed");
        if (!((4 & 4) != 0 && (4 & ~(4L | 2L)) == 0)) throw new Exception("4 not taken for installed");
        return "StateFlags 1026 = downloading, 4 = installed, bytes 2000/8000";
    }
}
