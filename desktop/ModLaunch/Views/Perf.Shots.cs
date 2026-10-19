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
            ("friends", () => new FriendsPage()),
            ("settings-look", () => new SettingsPage("look")),
            ("settings-games", () => new SettingsPage("games")),
            ("settings-downloads", () => new SettingsPage("downloads")),
            ("settings-services", () => new SettingsPage("services")),
        };
        // Прогрев: шрифты, стили, картинки.
        foreach (var (_, make) in pages.Take(4)) { w.Navigate(make); s.Pump(150); }

        double Measure(Action a)
        {
            var sw = Stopwatch.StartNew();
            a();
            Dispatcher.UIThread.RunJobs();
            w.UpdateLayout();
            return sw.Elapsed.TotalMilliseconds;
        }
        var total = new List<(string, double, double, double)>();
        var passes = int.TryParse(Environment.GetEnvironmentVariable("MODLAUNCH_PERF_PASSES"), out var np) ? np : 2;
        for (var pass = 1; pass <= passes; pass++)
        foreach (var (name0, make) in pages)
        {
            var name = pass == 1 ? name0 : name0 + "*";
            double nav;
            try { nav = Measure(() => w.Navigate(make)); }
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
            total.Add((name, nav, rebuild, notify));
            var slow = nav > 120 || rebuild > 60 || notify > 60 ? "  SLOW" : "";
            Console.WriteLine($"perf {name,-20} nav={nav,7:0.0}ms rebuild={rebuild,7:0.0}ms (build {buildOnly,6:0.0}) visuals={count,5} notify={notify,6:0.0}ms{slow}");
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
        Console.WriteLine($"perf TOTAL nav={total.Sum(t => t.Item2):0}ms rebuild={total.Sum(t => t.Item3):0}ms notify={total.Sum(t => t.Item4):0}ms");
    }
}
