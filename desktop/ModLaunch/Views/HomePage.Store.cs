using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// Главная 9.2 «Store» — как главная Microsoft Store: большой баннер-карусель с играми слева,
/// три плитки справа, ниже — полки «Ваши игры», «Популярные моды», «Выбор ModLaunch».
/// Всё стоит по одной сетке: одинаковые поля, отступы и высоты — ничего не «скачет».
/// </summary>
public sealed partial class HomePage
{
    /// <summary>Какой слайд карусели показан (живёт весь сеанс: перерисовка не сбрасывает его).</summary>
    static int _slide;
    DispatcherTimer? _carouselTimer;

    void BuildStore()
    {
        var col = StoreKit.Column();
        var mine = MainWindow.OrderedGames().Where(g => g.Status == Detect.Found && !Features.GameCollections.IsHidden(g.Def.Id)).ToList();
        // Недавние — первыми: карусель начинается с игры, в которую играли последней.
        // «Продолжить игру» выключено в настройках — карусель просто по порядку игр.
        var byRecent = Settings.Data.Bool("homeContinue", true)
            ? mine.OrderByDescending(g => Features.PlayTime.Get(g.Def.Id).LastPlayed ?? DateTime.MinValue).ToList()
            : mine;

        if (Starter(mine) is { } starter) col.Children.Add(Intro(starter, 0));
        col.Children.Add(Intro(HeroRow(byRecent), 0));

        // Ваши игры — обложками на полке.
        var searching = AppState.Games.Any(g => g.Status == Detect.Searching);
        var posters = new List<Control>();
        var n = 1;
        foreach (var g in mine) posters.Add(Intro(StoreKit.Poster(g), n++));
        posters.Add(Intro(StoreKit.AddPoster(), n++));
        var games = Ui.Col(14, StoreKit.Header(I18n.T("home.yourGames"), () => MainWindow.Current?.Navigate(() => new LibraryPage()), count: mine.Count > 0 ? mine.Count.ToString() : null));
        if (mine.Count == 0) games.Children.Add(Ui.Text(searching ? I18n.T("games.searching") : I18n.T("home.noGames"), "muted", wrap: true));
        games.Children.Add(StoreKit.Shelf(posters));
        col.Children.Add(games);

        // Загрузка подборок — один раз за сеанс; по готовности главная перерисуется.
        if (_featured is null && !_featuredLoading) { _featuredLoading = true; Dispatcher.UIThread.Post(() => _ = LoadFeatured(mine)); }

        // Популярные моды — по очереди из каждой игры, чтобы список не был про одну.
        if (mine.Count > 0 && Settings.Data.Bool("homePopular", true))
        {
            var lists = mine.Where(g => g.Def.HasExternalCatalog && !g.Def.IsMinecraft).Select(g => (Game: g, Mods: Views.Aside.Popular(g))).ToList();
            var pool = new List<(GameState Game, Sources.ModInfo Mod)>();
            for (var i = 0; pool.Count < 9 && lists.Any(l => l.Mods is { } m && m.Count > i); i++)
                foreach (var (g, mods) in lists)
                    if (mods is { } m && m.Count > i && pool.Count < 9) pool.Add((g, m[i]));
            Control body = pool.Count > 0
                ? StoreKit.ModGrid(pool.Select(p => StoreKit.ModItem(p.Game, p.Mod)), 3)
                : StoreKit.ModGrid(Enumerable.Range(0, 6).Select(_ => StoreKit.Placeholder(double.NaN, 72)), 3);
            col.Children.Add(Ui.Col(10, StoreKit.Header(I18n.T("v92.home.popular"), () => MainWindow.Current?.Navigate(() => new ModsCenterPage())), body));
        }

        // Выбор ModLaunch — широкими карточками.
        if (_featured is { Count: > 0 } featured && Settings.Data.Bool("homePopular", true))
            col.Children.Add(Ui.Col(14, StoreKit.Header(I18n.T("v91.home.pick")), StoreKit.Shelf(featured.Select(f => StoreKit.ModCard(f.Game, f.Mod)))));

        // Недавно смотрели и избранное.
        var recent = Features.Recent.All().Where(r => AppState.Games.Any(g => g.Def.Id == r.Game && g.Status == Detect.Found)).Take(9).ToList();
        if (recent.Count > 0)
            col.Children.Add(Ui.Col(10, StoreKit.Header(I18n.T("mc.recent")), StoreKit.ModGrid(recent.Select(r => StoreKit.ModItem(AppState.Game(r.Game), r.Mod)), 3)));
        var favorites = Favorites.All().Where(f => AppState.Games.Any(g => g.Def.Id == f.GameId && g.Status == Detect.Found)).Take(20).ToList();
        if (favorites.Count > 0 && Settings.Data.Bool("homeFavorites", true))
            col.Children.Add(Ui.Col(14, StoreKit.Header(I18n.T("home.favorites")), StoreKit.Shelf(favorites.Select(f => StoreKit.ModCard(AppState.Game(f.GameId), f.Mod, 248)))));

        // 8.4: место под рекламу.
        if (Ads.Enabled)
        {
            _ad ??= AdSlot.Banner();
            if (_ad.Parent is Panel was) was.Children.Remove(_ad);
            col.Children.Add(_ad);
        }

        Content = new ScrollViewer { Content = col, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    // ---------------------------------------------------------------- баннер: карусель и три плитки

    Control HeroRow(List<GameState> games)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*"), ColumnSpacing = StoreKit.Gap, Height = 392 };
        grid.Children.Add(Carousel(games));

        var right = new Grid { RowDefinitions = new RowDefinitions("*,*"), RowSpacing = StoreKit.Gap };
        right.Children.Add(FeaturedTile());
        var small = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = StoreKit.Gap };
        var accent = Color.Parse(Look.Accent);
        small.Children.Add(StoreKit.LinkTile(
            new Panel { Children = { StoreKit.AssetImage("banner-hero.jpg", 700), new Border { Background = new SolidColorBrush(Color.FromArgb(90, accent.R, accent.G, accent.B)) } } },
            "", "Creator Hub", I18n.T("v92.home.creator.text"), () => MainWindow.Current?.Navigate(() => new CreatorPage()), Icons.Creator));
        var updates = Features.ModUpdates.Found.Sum(kv => kv.Value.Count) + Features.Tracking.Updates.Count;
        var center = StoreKit.LinkTile(StoreKit.AssetImage("banner-broken.jpg", 700), "", I18n.T("mc.title"),
            updates > 0 ? I18n.T("v92.home.center.updates", ("n", updates)) : I18n.T("v92.home.center.text"),
            () => MainWindow.Current?.Navigate(() => new ModsCenterPage()), Icons.Package);
        Grid.SetColumn(center, 1);
        small.Children.Add(center);
        Grid.SetRow(small, 1);
        right.Children.Add(small);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    /// <summary>Правая верхняя плитка: мод из «Выбора ModLaunch» на арте его игры.</summary>
    Control FeaturedTile()
    {
        var list = _featured ?? [];
        var p = list.FirstOrDefault(f => !Actions.IsInstalled(f.Game, f.Mod.Id));
        if (p.Game is null) p = list.FirstOrDefault();
        if (p.Game is null)
        {
            // Пока подборка грузится (или игр нет) — плитка с библиотекой игр.
            var art = AppState.Games.FirstOrDefault(g => g.Status == Detect.Found)?.Def ?? AppState.Games[0].Def;
            return StoreKit.LinkTile(Ui.GameImage(art, 900, art: Images.Art.Hero), "", I18n.T("lib.title"), I18n.T("v92.home.library.text"),
                () => MainWindow.Current?.Navigate(() => new LibraryPage()), Icons.Gamepad);
        }
        var icon = new Border
        {
            Width = 96, Height = 96, CornerRadius = new CornerRadius(20), ClipToBounds = true, Margin = new Thickness(0, 0, 22, 0),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), BorderThickness = new Thickness(1),
            Child = Ui.Thumb(p.Mod.Icon, p.Mod.Name, 96, 0, 240),
        };
        var background = new Panel { Children = { Ui.GameImage(p.Game.Def, 900, art: Images.Art.Hero), new Border { Background = new SolidColorBrush(Color.FromArgb(70, 8, 8, 10)) }, icon } };
        var mod = p.Mod;
        var id = p.Game.Def.Id;
        return StoreKit.LinkTile(background, I18n.T("v91.home.pick"), mod.Name, p.Game.Def.Name, () => MainWindow.Current?.Navigate(() => new ModPage(id, mod)));
    }

    /// <summary>
    /// Карусель игр: арт во всю плитку, логотип, состояние и «Играть»; слайды сменяются сами
    /// каждые 7 секунд (пока мышь над баннером — стоят), точки внизу — переключить вручную.
    /// </summary>
    Control Carousel(List<GameState> games)
    {
        var slides = games.Take(5).ToList();
        if (slides.Count == 0) return Welcome();
        var host = new Panel();
        var views = slides.Select(Slide).ToList();
        foreach (var v in views) host.Children.Add(v);
        var dots = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 22, 22) };
        var pips = new List<Border>();
        for (var i = 0; i < views.Count; i++)
        {
            var k = i;
            var pip = new Border { Classes = { "dot" }, Height = 6, Width = 6, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
            pip.PointerPressed += (_, e) => { e.Handled = true; Show(k); };
            pips.Add(pip);
            dots.Children.Add(new Border { Padding = new Thickness(0, 6), Background = Brushes.Transparent, Child = pip });
        }
        if (views.Count > 1) host.Children.Add(dots);
        _slide %= views.Count;

        void Show(int index, bool animate = true)
        {
            _slide = index;
            for (var i = 0; i < views.Count; i++)
            {
                var on = i == index;
                views[i].Transitions = animate && Animate.On ? [new Avalonia.Animation.DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(520) }] : null;
                views[i].Opacity = on ? 1 : 0;
                views[i].IsHitTestVisible = on;
                views[i].ZIndex = on ? 1 : 0;
                pips[i].Width = on ? 22 : 6;
                pips[i].Background = on ? Brushes.White : new SolidColorBrush(Color.FromArgb(120, 255, 255, 255));
            }
            if (ArtOf(views[index]) is { } art && animate && Animate.On)
                Animate.From(art, "scale(1.06)", 1400, 0, new Avalonia.Animation.Easings.QuadraticEaseOut(), 1);
        }
        static Control? ArtOf(Control slide) => slide is Panel { Children.Count: > 0 } p ? p.Children[0] : null;
        Show(_slide, animate: false);

        var hover = false;
        host.PointerEntered += (_, _) => hover = true;
        host.PointerExited += (_, _) => hover = false;
        _carouselTimer?.Stop();
        if (views.Count > 1 && !Program.Screenshot)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
            timer.Tick += (_, _) => { if (!hover) Show((_slide + 1) % views.Count); };
            _carouselTimer = timer;
            host.AttachedToVisualTree += (_, _) => timer.Start();
            host.DetachedFromVisualTree += (_, _) => timer.Stop();
        }
        return new Border { Classes = { "store-tile", "hero" }, Child = host };
    }

    /// <summary>Один слайд: арт игры, слева — «Продолжить игру», логотип, состояние и кнопки.</summary>
    static Control Slide(GameState g)
    {
        var id = g.Def.Id;
        var played = Features.PlayTime.Get(id);
        var running = Features.Launcher.IsRunning(id);
        var white = Brushes.White;

        var logo = Images.GameAsset(g.Def, Images.Art.Logo, 768);
        Control title = logo is null
            ? new TextBlock { Text = g.Def.Name, FontSize = 34, FontWeight = FontWeight.Bold, LetterSpacing = -0.8, Foreground = white, TextWrapping = TextWrapping.Wrap, MaxWidth = 480 }
            : new Image { Source = logo, MaxHeight = 92, MaxWidth = 380, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
        if (logo is not null) ToolTip.SetTip(title, g.Def.Name);

        var line = running ? I18n.T("time.running")
            : played.LastPlayed is not null ? $"{I18n.T("v4.continue")} · {I18n.T("time.last", ("when", Ui.Ago(played.LastPlayed)))}"
            : I18n.T("home.yourGames");
        var eyebrow = new TextBlock { Text = line.ToUpper(I18n.Culture), FontSize = 11.5, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.8, Foreground = Ui.Hex("#DADCE4") };

        var (dot, state) = StoreKit.GameState(g);
        var chips = Ui.Row(8, StoreKit.Pill(state, dot));
        if (played.TotalMs > 0) chips.Children.Add(StoreKit.Pill(I18n.T("time.total", ("time", Features.PlayTime.Format(played.TotalMs)))));

        var buttons = Ui.Row(10);
        if (running)
        {
            buttons.Children.Add(PlayControls.RunningPill(id));
            buttons.Children.Add(Ui.Button(I18n.T("v4.stop"), () => Features.Launcher.Stop(id), "hero-ghost", Icons.Stop));
        }
        else if (g.LoaderInstalled || g.Def.Loader is Games.LoaderKind.None) buttons.Children.Add(PlayControls.PlayButton(g));
        else
        {
            var install = Ui.Button(I18n.T("games.installLoader", ("loader", g.Def.LoaderName)), () => Actions.InstallLoader(g), "primary", Icons.Download);
            install.Padding = new Thickness(24, 13);
            buttons.Children.Add(install);
        }
        var mods = Ui.Button(I18n.T("v92.home.toGame"), () => MainWindow.Current?.Navigate(() => new GamePage(id)), "hero-ghost");
        mods.Padding = new Thickness(20, 13);
        buttons.Children.Add(mods);

        var left = Ui.Col(16, eyebrow, title, chips, buttons);
        left.VerticalAlignment = VerticalAlignment.Bottom;
        left.HorizontalAlignment = HorizontalAlignment.Left;
        left.Margin = new Thickness(34, 0, 34, 30);

        var art = Ui.GameImage(g.Def, 1600, art: Images.Art.Hero);
        var slide = new Panel
        {
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Children =
            {
                art,
                new Border { Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse("#E6060608"), 0), new GradientStop(Color.Parse("#A6060608"), 0.38), new GradientStop(Color.Parse("#00060608"), 0.75) } } },
                new Border { Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse("#00060608"), 0.5), new GradientStop(Color.Parse("#B3060608"), 1) } } },
                left,
            },
        };
        StoreKit.OnClick(slide, () => MainWindow.Current?.Navigate(() => new GamePage(id)));
        slide.ContextFlyout = GameCard.Menu(g);
        return slide;
    }

    /// <summary>Баннер, когда игр ещё нет: приветствие и «Добавить игру».</summary>
    static Control Welcome()
    {
        var add = Ui.Button(I18n.T("add.title"), () => MainWindow.Current?.Navigate(() => new AddGamePage()), "primary", Icons.Plus);
        add.Padding = new Thickness(24, 13);
        var library = Ui.Button(I18n.T("lib.title"), () => MainWindow.Current?.Navigate(() => new LibraryPage()), "hero-ghost");
        library.Padding = new Thickness(20, 13);
        var words = Ui.Col(14,
            new TextBlock { Text = I18n.T("v92.hero.welcome"), FontSize = 32, FontWeight = FontWeight.Bold, LetterSpacing = -0.8, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 },
            new TextBlock { Text = I18n.T("v92.hero.welcome.text"), FontSize = 15, Foreground = Ui.Hex("#D5D8E0"), TextWrapping = TextWrapping.Wrap, MaxWidth = 480 },
            Ui.Row(10, add, library));
        words.VerticalAlignment = VerticalAlignment.Bottom;
        words.Margin = new Thickness(34, 0, 34, 30);
        return new Border
        {
            Classes = { "store-tile", "hero" },
            Child = new Panel
            {
                Children =
                {
                    StoreKit.AssetImage("banner-deps.jpg", 1600),
                    new Border { Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                        GradientStops = { new GradientStop(Color.Parse("#E6060608"), 0), new GradientStop(Color.Parse("#00060608"), 0.8) } } },
                    words,
                },
            },
        };
    }
}
