using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Headless;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

/// <summary>
/// 9.5: замер отклика без экрана (MODLAUNCH_SHOTS=Perf). Для каждой страницы — переход,
/// повторная перерисовка (как при AppState.Notify) и раскладка. Печатает строки «perf …»;
/// медленное (переход дольше 120 мс или перерисовка дольше 60 мс) — с пометкой SLOW.
/// </summary>
public static class PerfShots
{
    [DemoShots]
    static void PerfAll(Shots s)
    {
        var w = s.Window;
        MarketData.Demo(Demo.Listings(), Demo.Hub());
        var xid = XStudio.DemoProject();
        var pages = new List<(string Name, Func<Page> Make)>
        {
            ("home", () => new HomePage()),
            ("library", () => new LibraryPage()),
            ("game-installed", () => new GamePage("subnautica", "installed")),
            ("game-catalog", () => new GamePage("subnautica", "catalog")),
            ("game-config", () => new GamePage("subnautica", "config")),
            ("game-health", () => new GamePage("subnautica", "health")),
            ("game-log", () => new GamePage("subnautica", "log")),
            ("game-tools", () => new GamePage("subnautica", "tools")),
            ("game-lc", () => new GamePage("lethal-company")),
            ("mods-center", () => new ModsCenterPage()),
            ("mc-mods", () => new MinecraftPage("mods")),
            ("mc-catalog", () => new MinecraftPage("catalog")),
            ("mc-builds", () => new MinecraftPage("builds")),
            ("mc-settings", () => new MinecraftPage("settings")),
            ("mc-worlds", () => new MinecraftPage("worlds")),
            ("mc-servers", () => new MinecraftPage("servers")),
            ("market", () => new MarketPage()),
            ("market-browse", () => new MarketPage("browse")),
            ("market-workshop", () => new MarketPage("workshop")),
            ("market-studio", () => new MarketPage("studio")),
            ("creator", () => new CreatorPage()),
            ("creator-mine", () => new CreatorPage("mine")),
            ("panel", () => new ControlPanelPage()),
            ("account", () => new AccountPage()),
            ("account-wallet", () => new AccountPage("wallet")),
            ("friends", () => new CommunityPage()),
            ("settings-look", () => new SettingsPage("look")),
            ("settings-games", () => new SettingsPage("games")),
            ("settings-downloads", () => new SettingsPage("downloads")),
            ("settings-services", () => new SettingsPage("services")),
            // 17.0: каждая функция — все страницы и вкладки, до которых можно дойти из меню.
            ("settings-general", () => new SettingsPage("general")),
            ("settings-panel", () => new SettingsPage("panel")),
            ("settings-launch", () => new SettingsPage("launch")),
            ("settings-graphics", () => new SettingsPage("graphics")),
            ("settings-backups", () => new SettingsPage("backups")),
            ("settings-accounts", () => new SettingsPage("accounts")),
            ("settings-about", () => new SettingsPage("about")),
            ("game-overview", () => new GamePage("subnautica", "overview")),
            ("game-profiles", () => new GamePage("subnautica", "profiles")),
            ("game-saves", () => new GamePage("subnautica", "saves")),
            ("mod", () => new ModPage("subnautica", Demo.Many(Games.GameCatalog.ById("subnautica")!, ["2800"])[0])),
            ("profile", () => new ProfilePage()),
            ("profile-rewards", () => new ProfilePage("rewards")),
            ("profile-builds", () => new ProfilePage("builds")),
            ("ai-builds", () => new AiBuildsPage()),
            ("autopilot", () => new AutopilotPage()),
            ("steam-library", () => new SteamLibraryPage()),
            ("steam-store", () => new SteamGamesPage()),
            ("add-game", () => new AddGamePage()),
            ("community-servers", () => new CommunityPage("servers")),
            ("community-host", () => new CommunityPage("host")),
            ("creator-modscript", () => new CreatorPage("modscript")),
            ("creator-team", () => new CreatorPage("team")),
            ("creator-form", () => CreatorPage.NewForm()),
            ("xstudio", () => new XStudio(xid)),
            ("stats", () => new StatsPage()),
        };
        // Прогрев: шрифты, стили, картинки.
        // MODLAUNCH_PERF_ONLY=settings-look,profile-rewards — замерить только эти страницы.
        if (Environment.GetEnvironmentVariable("MODLAUNCH_PERF_ONLY")?.Split(',') is { Length: > 0 } only && only[0] != "")
            pages = pages.Where(x => only.Contains(x.Name)).ToList();
        foreach (var (_, make) in pages.Take(4)) { w.Navigate(make); s.Pump(150); }

        double Measure(Action a)
        {
            var sw = Stopwatch.StartNew();
            a();
            Dispatcher.UIThread.RunJobs();
            w.UpdateLayout();
            return sw.Elapsed.TotalMilliseconds;
        }
        var total = new List<(string, double, double, double, double)>();
        var passes = int.TryParse(Environment.GetEnvironmentVariable("MODLAUNCH_PERF_PASSES"), out var np) ? np : 2;
        for (var pass = 1; pass <= passes; pass++)
        foreach (var (name0, make) in pages)
        {
            var name = pass == 1 ? name0 : name0 + "*";
            double nav, first;
            try
            {
                // 17.0: «первый кадр» — страница построена и разложена, без отложенных частей (длинные списки
                // достраиваются порциями в фоне). Это то, сколько человек ждёт после клика.
                var swf = Stopwatch.StartNew();
                w.Navigate(make);
                w.UpdateLayout();
                first = swf.Elapsed.TotalMilliseconds;
                Dispatcher.UIThread.RunJobs();
                w.UpdateLayout();
                nav = swf.Elapsed.TotalMilliseconds;
            }
            catch (Exception e) { Console.WriteLine($"perf {name} FAILED {e.GetType().Name}: {e.Message}"); continue; }
            s.Pump(250);
            var sw0 = Stopwatch.StartNew();
            w.CurrentPage?.Build();
            var buildOnly = sw0.Elapsed.TotalMilliseconds;
            Dispatcher.UIThread.RunJobs();
            w.UpdateLayout();
            var rebuild = sw0.Elapsed.TotalMilliseconds;
            var count = w.CurrentPage?.GetVisualDescendants().Count() ?? 0;
            s.Pump(100);
            var notify = Measure(AppState.Notify);
            s.Pump(100);
            total.Add((name, nav, rebuild, notify, first));
            var slow = first > 120 || rebuild > 60 || notify > 60 ? "  SLOW" : "";
            Console.WriteLine($"perf {name,-20} first={first,7:0.0}ms nav={nav,7:0.0}ms rebuild={rebuild,7:0.0}ms (build {buildOnly,6:0.0}) visuals={count,5} notify={notify,6:0.0}ms{slow}");
        }
        // Прокрутка: 12 кадров с полной перерисовкой (как при прокрутке колесом) — главная, лента, каталог.
        foreach (var (name, make) in new (string, Func<Page>)[] { ("home", () => new HomePage()), ("game-catalog", () => new GamePage("subnautica", "catalog")), ("market", () => new MarketPage()) })
        {
            w.Navigate(make);
            s.Pump(800);
            var sv = w.CurrentPage?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(x => x.Extent.Height > x.Viewport.Height + 100);
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < 12; i++)
            {
                if (sv is not null) sv.Offset = new Avalonia.Vector(0, i * 60);
                Dispatcher.UIThread.RunJobs();
                w.UpdateLayout();
                w.CaptureRenderedFrame();
            }
            Console.WriteLine($"perf scroll {name,-14} {sw.Elapsed.TotalMilliseconds / 12,7:0.0}ms/frame");
        }
        Console.WriteLine($"perf TOTAL first={total.Sum(t => t.Item5):0}ms nav={total.Sum(t => t.Item2):0}ms rebuild={total.Sum(t => t.Item3):0}ms notify={total.Sum(t => t.Item4):0}ms");
    }
}
