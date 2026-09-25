using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Views;

namespace ModLaunch;

public static class Program
{
    /// <summary>Режим снимков: без окна на экране, данные — поддельные (см. Demo).</summary>
    public static bool Screenshot { get; private set; }
    public static bool Demo { get; private set; }

    /// <summary>Ссылка nxm://, с которой программу запустили.</summary>
    public static string? StartupLink { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--selfcheck")) return SelfCheck.Run().GetAwaiter().GetResult();
        var shot = Array.IndexOf(args, "--screenshot");
        if (shot >= 0 && shot + 1 < args.Length) return Screenshots(args[shot + 1]);

        // Один экземпляр: второй запуск (в том числе по ссылке nxm://) передаёт ссылку первому.
        if (!Features.Nxm.Claim(args)) return 0;
        StartupLink = args.FirstOrDefault(a => a.StartsWith("nxm://", StringComparison.OrdinalIgnoreCase));
        try { if (!Features.Nxm.IsRegistered()) Features.Nxm.Register(); } catch { }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();

    /// <summary>Отрисовать главные экраны в PNG — для проверки вида на CI и без Windows.</summary>
    static int Screenshots(string outDir)
    {
        Screenshot = true;
        Demo = true;
        Directory.CreateDirectory(outDir);
        var root = Path.Combine(Path.GetTempPath(), "modlaunch-demo-" + Environment.ProcessId);
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Core.Demo.Prepare(root);

        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont()
            .SetupWithoutStarting();

        var window = new MainWindow { Width = 1366, Height = 800 };
        window.Show();

        void Pump(int ms = 600)
        {
            var until = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < until)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Thread.Sleep(15);
            }
        }

        void Save(string name)
        {
            Pump();
            window.CaptureRenderedFrame()?.Save(Path.Combine(outDir, name + ".png"));
            Console.WriteLine("saved " + name);
        }

        Save("1-home");
        window.Navigate(() => new GamePage("subnautica", "installed"));
        Save("2-installed");
        window.Navigate(() => new GamePage("subnautica", "catalog"));
        Save("3-picks");
        window.Navigate(() => new GamePage("subnautica", "profiles"));
        Save("3b-profiles");
        window.Navigate(() => new GamePage("subnautica", "saves"));
        Save("3c-saves");
        window.Navigate(() => new GamePage("subnautica", "log"));
        Save("3d-log");
        var packs = new GamePage("subnautica", "catalog");
        window.Navigate(() => packs);
        Pump(300);
        packs.ShowSection("packs");
        Save("3e-collections");
        window.Navigate(() => new GamePage("lethal-company", "catalog", "More"));
        Save("4-catalog");
        window.Navigate(() => new GamePage("valheim"));
        Save("5-loader");
        window.Navigate(() => new GamePage("hollow-knight"));
        Save("6-notfound");
        window.Navigate(() => new SettingsPage("games"));
        Save("7-settings");
        window.Navigate(() => new SettingsPage("launch"));
        Save("7b-launch");
        window.Navigate(() => new SettingsPage("graphics"));
        Save("7c-graphics");
        window.Navigate(() => new SettingsPage("backups"));
        Save("7d-backups");
        I18n.Set("en");
        window.Navigate(() => new HomePage());
        Save("8-home-en");
        return 0;
    }
}
