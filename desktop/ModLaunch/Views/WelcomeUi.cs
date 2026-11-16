using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>
/// 31.0: первый запуск — «первый мод за минуту». Окно сразу показывает найденные игры, для выбранной — три самых
/// популярных мода (без библиотек и загрузчиков: их ModLaunch поставит сам), «Поставить» в один клик, затем «Играть».
/// Показывается один раз; «Пропустить» — не показывать больше. Карточка «Начало работы» на главной остаётся.
/// </summary>
public static class WelcomeUi
{
    public static bool Shown => Settings.Data.Bool("welcomeShown");

    public static void CheckLater()
    {
        if (Shown || Program.Demo || Program.Screenshot || Program.Autostarted) return;
        // Уже пользуется ModLaunch (обновился с прошлой версии): моды стоят или «Начало работы» пройдено — не мешаем.
        if (Settings.Data.Bool("onboardingDone") || AppState.Games.Any(g => g.ModCount > 0)) { Done(); return; }
        DispatcherTimer.RunOnce(Show, TimeSpan.FromSeconds(2));
    }

    [DemoShots]
    static void WelcomeShots(Shots s)
    {
        Show();
        s.Pump(2500);
        s.Save("v31-welcome");
        s.Window.CloseDialog();
    }

    static void Done()
    {
        Settings.Data["welcomeShown"] = true;
        Settings.Save();
    }

    static List<GameState> Found() => AppState.Games.Where(g => g.Path is { } p && Directory.Exists(p) && !g.Def.IsMinecraft).ToList();

    /// <summary>Моды для первого раза: популярные, не библиотека и не загрузчик (их поставят как зависимости).</summary>
    static bool Starter(GameState g, ModInfo m) =>
        !Mods.Installer.IsLoader(g.Def, m.Id) && !Catalog.IsHidden(g.Def, m)
        && !System.Text.RegularExpressions.Regex.IsMatch(m.Name, @"\b(lib|library|api|core|framework|pack|loader|bepinex|smapi)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
        && !m.Categories.Any(c => c.Contains("librar", StringComparison.OrdinalIgnoreCase) || c.Contains("modpack", StringComparison.OrdinalIgnoreCase));

    public static void Show()
    {
        var w = MainWindow.Current;
        if (w is null) return;
        var games = Found();
        var body = Ui.Col(14);
        var picks = Ui.Col(8);
        GameState? chosen = games.OrderByDescending(g => Features.PlayTime.Get(g.Def.Id).LastPlayed ?? DateTime.MinValue).FirstOrDefault();

        void LoadPicks()
        {
            picks.Children.Clear();
            if (chosen is null) return;
            var game = chosen;
            picks.Children.Add(Ui.Row(10, new ProgressBar { IsIndeterminate = true, Width = 120 }, Ui.Text(I18n.T("v31.welcome.loading", ("game", game.Def.Name)), "small muted")));
            _ = Task.Run(async () =>
            {
                List<ModInfo> mods = [];
                string? error = null;
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    var page = await Catalog.Browse(game.Def, new Query(Sort: SortBy.Popular), cts.Token);
                    mods = page.Mods.Where(m => Starter(game, m)).Take(3).ToList();
                }
                catch (Exception e) { error = Jobs.Explain(e); }
                Dispatcher.UIThread.Post(() => ShowPicks(game, mods, error));
            });
        }

        void ShowPicks(GameState game, List<ModInfo> mods, string? error)
        {
            if (!ReferenceEquals(game, chosen)) return;
            picks.Children.Clear();
            if (error is not null || mods.Count == 0)
            {
                picks.Children.Add(Ui.Text(error ?? I18n.T("v31.welcome.noMods"), "small muted", wrap: true));
                picks.Children.Add(Ui.Button(I18n.T("v31.welcome.catalog"), () => { w.CloseDialog(); Done(); w.Navigate(() => new GamePage(game.Def.Id, "catalog")); }, "", Icons.Package));
                return;
            }
            foreach (var m in mods)
            {
                var mod = m;
                var line = new DockPanel();
                var install = Ui.Button(I18n.T("v31.welcome.install"), () => { }, "primary", Icons.Download);
                install.Click += async (_, _) =>
                {
                    install.IsEnabled = false;
                    install.Content = I18n.T("v31.welcome.installing");
                    try { await Actions.Install(game, mod); } catch (Exception e) { w.Toast(Jobs.Explain(e), bad: true); }
                    if (Actions.IsInstalled(game, mod.Id))
                    {
                        Done();
                        picks.Children.Clear();
                        picks.Children.Add(Ui.Text(I18n.T("v31.welcome.ready", ("mod", mod.Name)), "h3", wrap: true));
                        picks.Children.Add(Ui.Row(8,
                            Ui.Button(I18n.T("v31.welcome.play", ("game", game.Def.Name)), () => { w.CloseDialog(); Actions.Play(game); }, "primary", Icons.Play),
                            Ui.Button(I18n.T("v31.welcome.more"), () => { w.CloseDialog(); w.Navigate(() => new GamePage(game.Def.Id, "catalog")); }, "", Icons.Package)));
                    }
                    else { install.IsEnabled = true; install.Content = I18n.T("v31.welcome.install"); }
                };
                DockPanel.SetDock(install, Dock.Right);
                line.Children.Add(install);
                var info = Ui.Col(2, Ui.Text(mod.Name, "h3"), Ui.Text(I18n.T("v31.welcome.modMeta", ("author", mod.Author), ("downloads", I18n.Compact(mod.Downloads))), "small muted"));
                info.Margin = new Thickness(0, 0, 12, 0);
                line.Children.Add(info);
                picks.Children.Add(new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10), Child = line });
            }
        }

        if (games.Count == 0)
        {
            body.Children.Add(Ui.Text(I18n.T("v31.welcome.none"), "", wrap: true));
            body.Children.Add(Ui.Button(I18n.T("starter.1.go"), () => { w.CloseDialog(); Done(); w.Navigate(() => new AddGamePage()); }, "primary", Icons.Plus));
        }
        else
        {
            body.Children.Add(Ui.Text(I18n.T("v31.welcome.found", ("n", games.Count)), "h3", wrap: true));
            var chips = new WrapPanel();
            foreach (var g in games.Take(12))
            {
                var game = g;
                var chip = Ui.Button(g.Def.Name, () => { chosen = game; RenderChips(); LoadPicks(); }, ReferenceEquals(g, chosen) ? "chip active" : "chip");
                chip.Margin = new Thickness(0, 0, 6, 6);
                chips.Children.Add(chip);
            }
            void RenderChips()
            {
                for (var i = 0; i < chips.Children.Count; i++)
                    if (chips.Children[i] is Button b) b.Classes.Set("active", ReferenceEquals(games[i], chosen));
            }
            body.Children.Add(chips);
            body.Children.Add(Ui.Text(I18n.T("v31.welcome.pick"), "muted", wrap: true));
            body.Children.Add(picks);
            LoadPicks();
        }
        w.Dialog(I18n.T("v31.welcome.title"), body, 620,
            Ui.Button(I18n.T("v31.welcome.skip"), () => { Done(); w.CloseDialog(); }));
    }
}
