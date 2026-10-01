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
        Core.CrashLog.Install();
        if (args.Contains("--catalog-report")) return CatalogReport.Run().GetAwaiter().GetResult();
        if (args.Contains("--creator-check")) return CreatorCheck();
        var bpShots = Array.IndexOf(args, "--bp-shots");
        if (bpShots >= 0 && bpShots + 1 < args.Length) return BigPictureShots(args[bpShots + 1]);
        var creatorShots = Array.IndexOf(args, "--creator-shots");
        if (creatorShots >= 0 && creatorShots + 1 < args.Length) return CreatorShots(args[creatorShots + 1]);
        if (args.Contains("--installer-check")) return SelfCheck.InstallerCheck().GetAwaiter().GetResult();
        if (args.Contains("--selfcheck")) return SelfCheck.Run().GetAwaiter().GetResult();
        var shot = Array.IndexOf(args, "--screenshot");
        if (shot >= 0 && shot + 1 < args.Length) return Screenshots(args[shot + 1]);
        var setupShots = Array.IndexOf(args, "--setup-shots");
        if (setupShots >= 0 && setupShots + 1 < args.Length) return SetupShots(args[setupShots + 1]);

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

    static void PumpUi(int ms = 600)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(15);
        }
    }

    static void StartHeadless(string outDir)
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
    }

    /// <summary>Creator Hub без сети: нужен Avalonia (значок пакета), поэтому в безголовом режиме и на тестовых данных.</summary>
    static int CreatorCheck()
    {
        StartHeadless(Path.Combine(Path.GetTempPath(), "modlaunch-creator-shots"));
        return Task.Run(SelfCheck.CreatorCheck).GetAwaiter().GetResult(); // не на потоке интерфейса: там await ждал бы сам себя
    }

    /// <summary>Все разделы Creator Hub: чужие файлы для примера (картинка, модель, звук) создаются на лету.</summary>
    static void CreatorScreens(MainWindow window, Action<string> save)
    {
        // Ассеты для витрины: настоящая картинка и пара файлов других видов.
        var tmp = Path.Combine(Path.GetTempPath(), "modlaunch-creator-demo-" + Environment.ProcessId);
        Directory.CreateDirectory(tmp);
        using (var icon = Avalonia.Platform.AssetLoader.Open(new Uri("avares://ModLaunch/Assets/icon.png"))) using (var f = File.Create(Path.Combine(tmp, "sword-icon.png"))) icon.CopyTo(f);
        File.WriteAllBytes(Path.Combine(tmp, "sword.fbx"), new byte[420_000]);
        File.WriteAllBytes(Path.Combine(tmp, "swing.ogg"), new byte[86_000]);
        File.WriteAllBytes(Path.Combine(tmp, "mymod.bundle"), new byte[1_900_000]);
        foreach (var file in Directory.GetFiles(tmp)) Creator.AssetLibrary.Import(file, "");

        window.Navigate(() => new CreatorPage("studio"));
        save("15a-creator-studio");
        window.Navigate(() => new CreatorPage("code"));
        save("15b-creator-code-empty");
        var lethal = Games.GameCatalog.ById("lethal-company")!;
        var sample = Creator.CodeProjects.Create("Быстрые ноги", lethal, null, "Maks");
        Creator.CodeProjects.Create("Больше слотов", Games.GameCatalog.ById("valheim")!, null, "Maks");
        Creator.CodeProjects.Create("Весенний рынок", Games.GameCatalog.ById("stardew-valley")!, null, "Maks");
        window.Navigate(() => new CreatorPage("code"));
        save("15c-creator-code-list");
        var page = new CreatorPage("code");
        page.ShowWizard(lethal.Id);
        window.Navigate(() => page);
        save("15d-creator-code-wizard");
        var detail = new CreatorPage("code");
        detail.ShowCode(sample);
        window.Navigate(() => detail);
        save("15e-creator-code-project");
        window.Navigate(() => new CreatorPage("snippets"));
        save("15f-creator-snippets");
        window.Navigate(() => new CreatorPage("assets"));
        save("15g-creator-assets");
        var pack = new CreatorPage("pack");
        pack.DemoPack(sample);
        window.Navigate(() => pack);
        save("15h-creator-pack");
        window.Navigate(() => new CreatorPage("guides"));
        save("15i-creator-guides");
        var model = new CreatorPage("guides");
        model.ShowGuide("model");
        window.Navigate(() => model);
        save("15j-creator-guide-model");
        var check = new CreatorPage("guides");
        check.ShowGuide("check");
        window.Navigate(() => check);
        save("15k-creator-checklist");
        window.Navigate(() => new CreatorPage("tools"));
        save("15l-creator-tools");
        try { Directory.Delete(tmp, true); } catch { }
    }

    /// <summary>Big Picture: главный экран и все окна-меню (каталог, профили, обновления, друзья, загрузки) на поддельных данных.</summary>
    static void BigPictureScreens(Action<Avalonia.Controls.Window, string> save)
    {
        foreach (var g in AppState.Games.Where(g => g.Status == Detect.Found && g.Registry is not null))
            try { Features.Profiles.Save(g.Def.Id, "Выживание", g.Registry!); Features.Profiles.Save(g.Def.Id, "Только визуал", g.Registry!); } catch { }
        Jobs.All.Insert(0, new Job { Title = "Better Sprint", GameName = "Subnautica", Step = "Скачиваю", Ratio = 0.42 });
        Jobs.All.Insert(1, new Job { Title = "Nautilus", GameName = "Subnautica", Step = "Готово", Ratio = 1, Status = JobStatus.Done });
        var big = new BigPictureWindow(windowed: true);
        big.Show();
        save(big, "13a-bigpicture");
        big.DemoMods();
        save(big, "13b-bigpicture-mods");
        foreach (var (what, file) in new[]
        {
            ("sections", "13c-bp-catalog-sections"), ("catalog", "13d-bp-catalog"), ("profiles", "13e-bp-profiles"),
            ("more", "13f-bp-more"), ("friends", "13g-bp-friends"), ("downloads", "13h-bp-downloads"),
        })
        {
            big.Demo(what);
            save(big, file);
        }
        big.Close();
    }

    static int BigPictureShots(string outDir)
    {
        StartHeadless(outDir);
        BigPictureScreens((w, name) =>
        {
            PumpUi(900);
            w.CaptureRenderedFrame()?.Save(Path.Combine(outDir, name + ".png"));
            Console.WriteLine("saved " + name);
        });
        return 0;
    }

    static int CreatorShots(string outDir)
    {
        StartHeadless(outDir);
        var window = new MainWindow { Width = 1500, Height = 900 };
        window.Show();
        window.CloseDialog();
        CreatorScreens(window, name =>
        {
            PumpUi(700);
            window.CaptureRenderedFrame()?.Save(Path.Combine(outDir, name + ".png"));
            Console.WriteLine("saved " + name);
        });
        return 0;
    }

    /// <summary>Только окна установщика — все экраны, без установки на самом деле.</summary>
    static int SetupShots(string outDir)
    {
        StartHeadless(outDir);
        foreach (var (mode, screen) in new[]
        {
            (ModLaunch.Setup.SetupMode.Install, "welcome"), (ModLaunch.Setup.SetupMode.Install, "welcome-update"), (ModLaunch.Setup.SetupMode.Install, "welcome-legacy"),
            (ModLaunch.Setup.SetupMode.Install, "progress"), (ModLaunch.Setup.SetupMode.Install, "done"), (ModLaunch.Setup.SetupMode.Install, "error"), (ModLaunch.Setup.SetupMode.Install, "license"),
            (ModLaunch.Setup.SetupMode.Uninstall, "uninstall"), (ModLaunch.Setup.SetupMode.Uninstall, "uninstalled"),
        })
        {
            var w = new SetupWindow(mode);
            w.Show();
            w.Preview(screen);
            PumpUi(900);
            w.CaptureRenderedFrame()?.Save(Path.Combine(outDir, "setup-" + screen + ".png"));
            Console.WriteLine("saved setup-" + screen);
            w.Close();
        }
        return 0;
    }

    /// <summary>Отрисовать главные экраны в PNG — для проверки вида на CI и без Windows.</summary>
    static int Screenshots(string outDir)
    {
        StartHeadless(outDir);

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
        CreatorScreens(window, Save);
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

        // Широкий монитор 2560×1440: авто-масштаб и раскладка без пустых полей.
        window.Width = 2560;
        window.Height = 1440;
        Pump(400);
        window.Navigate(() => new HomePage());
        Save("14a-wide-home");
        window.Navigate(() => new LibraryPage());
        Save("14b-wide-library");
        window.Navigate(() => new SettingsPage("look"));
        Save("14c-wide-settings");
        window.Width = 1366;
        window.Height = 800;
        Pump(400);

        BigPictureScreens((w, name) =>
        {
            Pump();
            w.CaptureRenderedFrame()?.Save(Path.Combine(outDir, name + ".png"));
            Console.WriteLine("saved " + name);
        });

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
