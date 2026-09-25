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

    public static ModLaunch.Setup.SetupMode SetupMode { get; private set; }

    /// <summary>Из окна установки — «Запустить без установки»: открыть обычное окно в этом же процессе.</summary>
    public static void StartMain(string[] args)
    {
        SetupMode = ModLaunch.Setup.SetupMode.None;
        Features.Nxm.Claim(args);
        var window = new MainWindow();
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = window;
        window.Show();
    }

    /// <summary>Ссылка nxm://, с которой программу запустили.</summary>
    public static string? StartupLink { get; private set; }

    /// <summary>Запущена вместе с Windows (--autostart).</summary>
    public static bool Autostarted { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--catalog-report")) return CatalogReport.Run().GetAwaiter().GetResult();
        if (args.Contains("--selfcheck")) return SelfCheck.Run().GetAwaiter().GetResult();
        var shot = Array.IndexOf(args, "--screenshot");
        if (shot >= 0 && shot + 1 < args.Length) return Screenshots(args[shot + 1]);

        // Установка, обновление, удаление — та же программа в другом режиме.
        SetupMode = ModLaunch.Setup.Installer.Detect(args);
        if (SetupMode != ModLaunch.Setup.SetupMode.None)
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 0;
        }

        // Один экземпляр: второй запуск (в том числе по ссылке nxm://) передаёт ссылку первому.
        if (!Features.Nxm.Claim(args)) return 0;
        Autostarted = args.Contains("--autostart");
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
        window.Navigate(() => new LibraryPage());
        Save("1a-library");
        // Наведение на обложку: подъём, тень, зум картинки и кнопка «Играть».
        Pump(1200);
        window.MouseMove(new Point(190, 280));
        Pump(700);
        Save("1b-library-hover");
        window.MouseMove(new Point(5, 5));
        window.Navigate(() => new GamePage("subnautica", "installed"));
        Save("2-installed");
        window.Navigate(() => new GamePage("subnautica", "catalog"));
        Save("3-picks");
        window.Navigate(() => new GamePage("subnautica", "tools"));
        Save("3a-tools");
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
        window.Navigate(() => new ModPage("subnautica", Core.Demo.Many(Games.GameCatalog.ById("subnautica")!, ["2800"])[0]));
        Save("9a-mod");
        window.Navigate(() => new FriendsPage());
        Save("9b-friends");
        window.Navigate(() => new SettingsPage("accounts"));
        Save("9c-account");
        Settings.Data["ownerStats"] = true;
        window.Navigate(() => new StatsPage());
        Save("9d-stats");
        window.Navigate(() => new SettingsPage("about"));
        Save("9e-about");
        window.Navigate(() => new SettingsPage("downloads"));
        Save("9h-downloads");

        // 5.0: Creator Hub, «+», новые настройки, светлая тема, быстрый переход.
        var seeds = Creator.Projects.Create("Дешёвые семена", Creator.Templates.ById("sdv-seeds")!.Code);
        window.Navigate(() => new CreatorPage("mine", seeds.Id));
        Save("10a-creator-editor");
        window.Navigate(() => new CreatorPage("mine"));
        Save("10b-creator-mine");
        window.Navigate(() => new CreatorPage("examples"));
        Save("10c-creator-examples");
        window.Navigate(() => new CreatorPage("docs"));
        Save("10d-creator-docs");
        AddGamePage.Demo(
        [
            new Games.FoundGame("Hades", @"D:\SteamLibrary\steamapps\common\Hades", @"x64\Hades.exe", 1145360, "steam", "other"),
            new Games.FoundGame("Muck", @"D:\SteamLibrary\steamapps\common\Muck", "Muck.exe", 1625450, "steam", "unity"),
            new Games.FoundGame("Deep Rock Galactic", @"D:\SteamLibrary\steamapps\common\Deep Rock Galactic", "FSD.exe", 548430, "steam", "unreal"),
            new Games.FoundGame("Outward Definitive Edition", @"C:\Program Files (x86)\Steam\steamapps\common\Outward", "Outward Definitive Edition.exe", 1758860, "steam", "unity"),
            new Games.FoundGame("Terraria", @"C:\GOG Games\Terraria", "Terraria.exe", 0, "gog", "xna"),
        ]);
        window.Navigate(() => new AddGamePage());
        Save("10e-add-game");
        window.Navigate(() => new SettingsPage("look"));
        Save("10f-look");
        window.Navigate(() => new SettingsPage("interface"));
        Save("10g-interface");
        window.Navigate(() => new SettingsPage("system"));
        Save("10h-system");
        window.Navigate(() => new GamePage("peak", "catalog"));
        Save("10i-peak");
        Look.SetTheme("light");
        window.Navigate(() => new HomePage());
        Save("10j-home-light");
        Look.SetTheme("black");
        Look.SetAccent("#22C55E");
        window.Navigate(() => new CreatorPage("examples"));
        Save("10k-black-green");
        Look.SetTheme("dark");
        Look.SetAccent(Look.Accents[0]);
        window.Navigate(() => new HomePage());
        window.CommandPalette();
        Save("10l-palette");
        window.CloseDialog();

        // 5.5: ModLaunch Hub, публикация, страница мода из Hub, новые игры.
        CreatorPage.DemoHub([]);
        window.Navigate(() => new CreatorPage("hub"));
        Save("11a-hub-empty");
        var demoHub = Core.Demo.Hub();
        CreatorPage.DemoHub(demoHub);
        window.Navigate(() => new CreatorPage("hub"));
        Save("11b-hub");
        window.Navigate(() => new ModPage(demoHub[0].Game, Creator.Hub.ToModInfo(demoHub[0])));
        Save("11c-hub-mod");
        window.Navigate(() => new CreatorPage("examples"));
        Save("11d-examples");
        HubPublish.Show(new Creator.HubDraft { Name = "Больше слотов", Summary = "Ещё 8 быстрых слотов", Game = "valheim", Version = "1.2.0", Tags = ["qol", "ui"] }, fromProject: false, pack: null);
        Save("11e-publish");
        window.CloseDialog();
        window.Navigate(() => new GamePage("hollow-knight-silksong", "catalog"));
        Save("11f-silksong");
        var demoMod = Core.Demo.Many(Games.GameCatalog.ById("subnautica")!, ["2800"])[0];
        window.Navigate(() => new ModPage("subnautica", demoMod, "files"));
        Save("11g-mod-versions");
        window.Navigate(() => new ModPage("subnautica", demoMod, "stats"));
        Save("12a-mod-stats");
        window.Navigate(() => new ModPage("subnautica", demoMod, "changes"));
        Save("12b-mod-changes");
        window.Navigate(() => new ModsCenterPage("updates"));
        Save("12c-mods-center");
        window.Navigate(() => new CreatorPage("docs"));
        Save("11h-docs");

        var big = new BigPictureWindow(windowed: true);
        big.Show();
        Pump();
        big.CaptureRenderedFrame()?.Save(Path.Combine(outDir, "13a-bigpicture.png"));
        Console.WriteLine("saved 13a-bigpicture");
        big.DemoMods();
        Pump();
        big.CaptureRenderedFrame()?.Save(Path.Combine(outDir, "13b-bigpicture-mods.png"));
        Console.WriteLine("saved 13b-bigpicture-mods");
        big.Close();

        var setup = new SetupWindow(ModLaunch.Setup.SetupMode.Install);
        setup.Show();
        Pump();
        setup.CaptureRenderedFrame()?.Save(Path.Combine(outDir, "9f-setup.png"));
        Console.WriteLine("saved 9f-setup");
        setup.Close();

        OverlayWindow.GameId = "subnautica";
        OverlayWindow.StartedAt = DateTime.UtcNow.AddMinutes(-42);
        var overlay = new OverlayWindow { Width = 1366, Height = 800 };
        OverlayWindow.Toggle();
        Pump();
        overlay.CaptureRenderedFrame()?.Save(Path.Combine(outDir, "9g-overlay.png"));
        Console.WriteLine("saved 9g-overlay");
        overlay.Hide();

        I18n.Set("en");
        window.Navigate(() => new HomePage());
        Save("8-home-en");
        return 0;
    }
}
