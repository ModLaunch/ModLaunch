using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>Главная: все игры плитками и короткий рассказ о том, что умеет программа.</summary>
public sealed class HomePage : Page
{
    public override string Title => I18n.T("nav.menu");

    int _hero;
    IDisposable? _timer;

    public HomePage()
    {
        DetachedFromVisualTree += (_, _) => _timer?.Dispose();
        AttachedToVisualTree += (_, _) =>
        {
            if (Program.Screenshot) return;
            _timer = DispatcherTimer.Run(() => { _hero = (_hero + 1) % 3; Build(); return true; }, TimeSpan.FromSeconds(8));
        };
    }

    public override void Search(string text)
    {
        var game = AppState.Games.FirstOrDefault(g => g.Status == Detect.Found);
        if (game is null) { MainWindow.Current?.Toast(I18n.T("search.noGames")); return; }
        MainWindow.Current?.Navigate(() => new GamePage(game.Def.Id, "catalog", text));
    }

    public override void Build()
    {
        var content = new StackPanel { Spacing = 22, Margin = new Thickness(34, 26, 34, 34), MaxWidth = 1280 };

        var header = new DockPanel();
        var all = Ui.Button(I18n.T("home.allGames") + "  ›", () => MainWindow.Current?.Navigate(() => new SettingsPage("games")), "ghost");
        all.Foreground = Ui.Res("Brand2");
        DockPanel.SetDock(all, Dock.Right);
        header.Children.Add(all);
        header.Children.Add(Ui.Text(I18n.T("home.yourGames"), "h2"));
        content.Children.Add(header);

        var grid = new UniformGrid { Columns = 4 };
        foreach (var g in AppState.Games) grid.Children.Add(Tile(g));
        content.Children.Add(grid);

        content.Children.Add(Hero());

        Content = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    static string StatusText(GameState g) => g.Status switch
    {
        Detect.Searching => I18n.T("games.searching"),
        Detect.Found when !g.LoaderInstalled => I18n.T("home.loaderNeeded", ("loader", g.Def.LoaderName)),
        Detect.Found when g.ModCount > 0 => I18n.T("aside.mods." + I18n.Plural(g.ModCount, "one", "few", "many"), ("n", g.ModCount)),
        Detect.Found => I18n.T("games.loaderReady", ("loader", g.Def.LoaderName)),
        Detect.NotFound => I18n.T("games.notDetected"),
        _ => I18n.T("games.notSearched"),
    };

    static Control Tile(GameState g)
    {
        var art = g.Def.Art is null ? null : Images.Asset(g.Def.Art, 520);
        Control face = art is not null
            ? new Image { Source = art, Stretch = Stretch.UniformToFill }
            : new Border
            {
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse(g.Def.Accent), 0), new GradientStop(Color.Parse("#2A1B0A"), 1) },
                },
            };

        var shade = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.2, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#000F1116"), 0), new GradientStop(Color.Parse("#E60F1116"), 1) },
            },
        };

        var dot = Ui.Dot(g.Status == Detect.Found ? (g.LoaderInstalled ? Ui.Res("Good") : Ui.Res("Warn")) : Ui.Res("Faint"));
        var info = Ui.Col(4,
            new TextBlock { Text = g.Def.Name, FontSize = 19, FontWeight = FontWeight.Bold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, MaxLines = 2 },
            Ui.Row(8, dot, Ui.Text(StatusText(g), "small", color: Ui.Hex("#C9CFDB"))));
        info.VerticalAlignment = VerticalAlignment.Bottom;
        info.Margin = new Thickness(14, 0, 14, 14);

        var tile = new Button
        {
            Classes = { "tile" },
            Height = 136,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 14, 14),
            Content = new Panel { Children = { face, shade, info } },
        };
        var id = g.Def.Id;
        tile.Click += (_, _) => MainWindow.Current?.Navigate(() => new GamePage(id));
        return tile;
    }

    Control Hero()
    {
        string[] banners = ["banner-hero.jpg", "banner-deps.jpg", "banner-broken.jpg"];
        string[] keys = ["home.hero", "home.hero2", "home.hero3"];
        var i = _hero % 3;

        var text = Ui.Col(12,
            new TextBlock { Text = I18n.T(keys[i] + ".title"), FontSize = 30, FontWeight = FontWeight.Bold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = I18n.T(keys[i] + ".text"), FontSize = 15, Foreground = Ui.Hex("#D5DAE5"), TextWrapping = TextWrapping.Wrap, MaxWidth = 560 });
        var dots = Ui.Row(6, Enumerable.Range(0, 3).Select(n =>
        {
            var d = new Border { Width = n == i ? 22 : 8, Height = 8, CornerRadius = new CornerRadius(4), Background = n == i ? Ui.Res("Brand2") : Ui.Hex("#55FFFFFF"), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
            var target = n;
            d.PointerPressed += (_, _) => { _hero = target; Build(); };
            return (Control)d;
        }).ToArray());
        text.Children.Add(dots);
        foreach (var c in text.Children) c.HorizontalAlignment = HorizontalAlignment.Left;
        text.Margin = new Thickness(36, 32);
        text.VerticalAlignment = VerticalAlignment.Center;

        return new Border
        {
            Height = 260,
            CornerRadius = new CornerRadius(20),
            ClipToBounds = true,
            BorderBrush = Ui.Res("Line"),
            BorderThickness = new Thickness(1),
            Child = new Panel
            {
                Children =
                {
                    new Image { Source = Images.Asset(banners[i], 1600), Stretch = Stretch.UniformToFill },
                    new Border
                    {
                        Background = new LinearGradientBrush
                        {
                            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                            GradientStops = { new GradientStop(Color.Parse("#F20F1116"), 0), new GradientStop(Color.Parse("#990F1116"), 0.55), new GradientStop(Color.Parse("#000F1116"), 1) },
                        },
                    },
                    text,
                },
            },
        };
    }
}
