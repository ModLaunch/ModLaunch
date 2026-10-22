using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>
/// 10.0: главная — действительно главная. Сверху «сцена», как на консоли: арт выбранной игры во всю
/// ширину, логотип, «Играть», оценка автопилота, а внизу сцены — полка обложек ваших игр (нажатие
/// выбирает игру, двойное — открывает). Под сценой — приветствие и виджеты, которые можно менять,
/// двигать и растягивать, как на iPhone. Вид сцены задаёт конфиг: stage — большая, compact — низкая,
/// off — без неё.
/// </summary>
public sealed partial class HomePage
{
    static string? _stage;
    static bool _widgetsHooked;
    Border? _stageHost;

    void BuildTen()
    {
        _stageHost = null;
        if (!_widgetsHooked)
        {
            _widgetsHooked = true;
            Widgets.Changed += () => { if (MainWindow.Current?.CurrentPage is HomePage hp) hp.Build(); };
        }
        var mine = MyGames();
        if (_featured is null && !_featuredLoading && mine.Count > 0) { _featuredLoading = true; Dispatcher.UIThread.Post(() => _ = LoadFeatured(mine)); }
        CheckHealthLater(mine);
        if (!Shown) Dispatcher.UIThread.Post(ProfilePage.AnnounceNew, DispatcherPriority.Background);

        var col = StoreKit.Column(spacing: 18, top: 18);
        var n = 0;
        if (Starter(mine) is { } starter) col.Children.Add(Intro(starter, n++));
        var hero = Settings.Data.Str("homeHero") ?? "stage";
        if (hero != "off" && mine.Count > 0)
        {
            _stageHost = new Border { Child = Stage(mine, hero == "compact") };
            col.Children.Add(Intro(_stageHost, n++));
        }
        col.Children.Add(Intro(Greet(), n++));
        col.Children.Add(Intro(WidgetsArea(mine), n++));
        if (Ads.Enabled)
        {
            _ad ??= AdSlot.Banner();
            if (_ad.Parent is Panel was) was.Children.Remove(_ad);
            col.Children.Add(_ad);
        }
        // 12.0: правая кнопка по пустому месту главной — добавить виджет.
        col.Background = Brushes.Transparent;
        col.ContextFlyout = AreaMenu();
        Content = new ScrollViewer { Content = col, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    /// <summary>Приветствие одной строкой и две кнопки: «Изменить виджеты» и «Стиль».</summary>
    Control Greet()
    {
        var name = ProfileKit.PlayerName();
        var hello = new TextBlock { Text = Greeting() + ", " + name, FontFamily = Look.Display, FontSize = 22, FontWeight = FontWeight.Bold, LetterSpacing = -0.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var edit = Ui.Button(_editWidgets ? I18n.T("v10.w.done") : I18n.T("v10.w.edit"), () => { _editWidgets = !_editWidgets; Build(); }, _editWidgets ? "primary" : "ghost", _editWidgets ? Icons.Check : Icons.Grid);
        var style = Ui.Button(I18n.T("v10.cfg.short"), () => MainWindow.Current?.Navigate(() => new SettingsPage("configs")), "ghost", Icons.Palette);
        // 12.0: быстрые переходы одной кнопкой (у каждой — свой Ctrl-ярлык в подсказке).
        Button Quick(string text, string icon, string keys, Func<Page> make) => Ui.Button(text, () => MainWindow.Current?.Navigate(make), "ghost", icon, keys);
        var dock = Ui.Row(6,
            Quick(I18n.T("lib.title"), Icons.Grid, "Ctrl+L", () => new LibraryPage()),
            Quick(I18n.T("mc.title"), Icons.Package, "Ctrl+U", () => new ModsCenterPage()));
        var right = Ui.Row(6, dock, new Border { Width = 1, Height = 22, Background = Ui.Res("Line"), Margin = new Thickness(4, 0), VerticalAlignment = VerticalAlignment.Center }, style, edit);
        right.VerticalAlignment = VerticalAlignment.Center;
        var row = new DockPanel { Margin = new Thickness(2, 6, 0, 0) };
        DockPanel.SetDock(right, Dock.Right);
        row.Children.Add(right);
        row.Children.Add(hello);
        return row;
    }

    // ---------------------------------------------------------------- сцена

    Control Stage(List<GameState> mine, bool compact)
    {
        var g = mine.FirstOrDefault(x => x.Def.Id == _stage) ?? mine[0];
        _stage = g.Def.Id;
        var id = g.Def.Id;
        var played = PlayTime.Get(id);
        var running = Launcher.IsRunning(id);

        var art = new Border { Child = Ui.GameImage(g.Def, 1600, art: Images.Art.Hero), ClipToBounds = true };
        if (Animate.On && !Program.Screenshot)
        {
            // Медленный наезд камеры — едва заметный, как на экране консоли.
            art.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("scale(1.0)");
            art.Transitions = [new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromSeconds(16), Easing = new SineEaseOut() }];
            art.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => art.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("scale(1.06)"), DispatcherPriority.Background);
        }

        var logo = Images.GameAsset(g.Def, Images.Art.Logo, 900);
        Control title = logo is null
            ? new TextBlock { Text = g.Def.Name, FontFamily = Look.Display, FontSize = compact ? 28 : 42, FontWeight = FontWeight.Bold, LetterSpacing = -1, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, MaxWidth = 620 }
            : new Image { Source = logo, MaxHeight = compact ? 64 : 120, MaxWidth = compact ? 300 : 480, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
        if (logo is not null) ToolTip.SetTip(title, g.Def.Name);

        var when = running ? I18n.T("time.running") : played.LastPlayed is not null ? I18n.T("time.last", ("when", Ui.Ago(played.LastPlayed))) : I18n.T("v93.home.notPlayed");
        var meta = new TextBlock { Text = string.Join("  ·  ", new[] { when, g.ModCount > 0 ? StoreKit.Mods(g.ModCount) : null, played.TotalMs >= 3_600_000 ? I18n.T("v93.home.hours", ("n", played.TotalMs / 3_600_000)) : null }.OfType<string>()), FontSize = 13.5, Foreground = Ui.Hex("#D5D9E2") };
        var accent = Color.TryParse(g.Def.Accent, out var ac) ? ac : Gx.Accent;

        var buttons = Ui.Row(10);
        if (running)
        {
            buttons.Children.Add(PlayControls.RunningPill(id));
            buttons.Children.Add(Ui.Button(I18n.T("v4.stop"), () => Launcher.Stop(id), "hero-ghost", Icons.Stop));
        }
        else if (g.LoaderInstalled || g.Def.Loader is Games.LoaderKind.None or Games.LoaderKind.Minecraft) buttons.Children.Add(PlayControls.PlayButton(g));
        else
        {
            var install = Ui.Button(I18n.T("games.installLoader", ("loader", g.Def.LoaderName)), () => Actions.InstallLoader(g), "primary", Icons.Download);
            install.Padding = new Thickness(24, 13);
            buttons.Children.Add(install);
        }
        var mods = Ui.Button(I18n.T("tab.mods"), () => MainWindow.Current?.Navigate(() => new GamePage(id, "installed")), "hero-ghost", Icons.Package);
        mods.Padding = new Thickness(18, 13);
        buttons.Children.Add(mods);
        if (!g.Def.IsMinecraft && g.Registry is not null)
        {
            var score = Autopilot.Plan(g, quick: true).Score;
            var pilot = Ui.Button($"{I18n.T("v10.ap.short")} · {score}", () => MainWindow.Current?.Navigate(() => new AutopilotPage(id)), "hero-ghost", Icons.Wand, I18n.T("v10.ap.grade." + Autopilot.Grade(score)));
            pilot.Padding = new Thickness(18, 13);
            buttons.Children.Add(pilot);
        }

        var left = Ui.Col(compact ? 10 : 14, title, meta, buttons);
        left.VerticalAlignment = VerticalAlignment.Bottom;
        left.HorizontalAlignment = HorizontalAlignment.Left;
        left.Margin = new Thickness(compact ? 28 : 40, 0, 0, compact ? 24 : 36);
        left.MaxWidth = 660;

        // Полка обложек внизу справа: выбранная — крупнее, с кольцом акцента.
        var shelf = Ui.Row(10);
        foreach (var x in mine.Where(x => !x.Def.IsMinecraft || true).Take(compact ? 6 : 7))
            shelf.Children.Add(StageCover(x, x.Def.Id == id, compact));
        shelf.HorizontalAlignment = HorizontalAlignment.Right;
        shelf.VerticalAlignment = VerticalAlignment.Bottom;
        shelf.Margin = new Thickness(0, 0, compact ? 24 : 32, compact ? 22 : 34);

        var stage = new Panel
        {
            Height = compact ? 230 : 470, ClipToBounds = true,
            Children =
            {
                art,
                new Border { Background = Gx.ShadeLeft(0.78, 240) },
                new Border { Background = Gx.ShadeUp(0.45, 230) },
                left,
                shelf,
            },
        };

        var radius = new CornerRadius(Look.Radius == "sharp" ? 4 : 22);
        var frame = new Border { CornerRadius = radius, ClipToBounds = true, Child = stage, ContextFlyout = GameCard.Menu(g) };
        // Если места мало, полка прячется — сцена не налезает сама на себя.
        frame.SizeChanged += (_, e) => shelf.IsVisible = e.NewSize.Width > 1080;
        // 12.0: мягкое свечение цветом игры под сценой — на обёртке: у Border с ClipToBounds тень обрезается.
        // 15.0: во «взрослом» виде свечения нет — оно расплывалось за край сцены.
        if (Look.Mature) return new Border { CornerRadius = radius, Child = frame };
        return new Border { CornerRadius = radius, Child = frame, BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 18, Blur = 60, Spread = -18, Color = Color.FromArgb(110, accent.R, accent.G, accent.B) }) };
    }

    Control StageCover(GameState g, bool on, bool compact)
    {
        var w = compact ? (on ? 74 : 62) : (on ? 96 : 78);
        var cover = new Border
        {
            Width = w, Height = w * 1.5, CornerRadius = new CornerRadius(10), ClipToBounds = true, VerticalAlignment = VerticalAlignment.Bottom,
            BorderBrush = on ? Ui.Res("Brand") : new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), BorderThickness = new Thickness(on ? 2.5 : 1),
            BoxShadow = on ? Gx.Glow(140, 22) : default, Opacity = on ? 1 : 0.82,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Child = Ui.GameImage(g.Def, 300, art: Images.Art.Cover),
            Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(160) }],
        };
        ToolTip.SetTip(cover, g.Def.Name);
        cover.PointerEntered += (_, _) => cover.Opacity = 1;
        cover.PointerExited += (_, _) => cover.Opacity = on ? 1 : 0.82;
        cover.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return;
            if (on) { MainWindow.Current?.Navigate(() => new GamePage(g.Def.Id)); return; }
            _stage = g.Def.Id;
            if (_stageHost is not null)
            {
                _stageHost.Child = Stage(MyGames(), Settings.Data.Str("homeHero") == "compact");
                if (Animate.On) Animate.From(_stageHost, "scale(0.995)", 260, 0, new CubicEaseOut(), 0.6);
            }
        };
        return cover;
    }
}
