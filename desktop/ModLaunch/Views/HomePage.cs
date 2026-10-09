using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// Главная в духе Modrinth App: продолжить игру, ваши (найденные) игры,
/// популярные моды и избранное. Все остальные игры — в «Библиотеке».
/// </summary>
public sealed partial class HomePage : Page
{
    public override string Title => I18n.T("nav.menu");
    public override Control? Aside() => Views.Aside.Home();

    public override void Search(string text)
    {
        // Поиск сразу по всем своим играм.
        MainWindow.Current?.Navigate(() => new SearchPage(text));
    }

    Control Intro(Control c, int index) { if (!Shown) Animate.Rise(c, index); return c; }

    public override void Build()
    {
        var content = new StackPanel { Spacing = 30, Margin = new Thickness(32, 26, 32, 32), MaxWidth = 1680 };

        // Новичку: три шага «найти игру, поставить мод, играть».
        // (карточка строится ниже, когда известен список игр)
        // Продолжить игру — последние запущенные.
        var recent = AppState.Games
            .Select(g => (Game: g, Played: Features.PlayTime.Get(g.Def.Id)))
            .Where(x => x.Game.Status == Detect.Found && x.Played.LastPlayed is not null)
            .OrderByDescending(x => x.Played.LastPlayed).Take(3).ToList();
        if (recent.Count > 0 && Settings.Data.Bool("homeContinue", true))
        {
            // 9.0 «Витрина»: последняя игра — большой картой во всю ширину, остальные — строками под ней.
            if (Look.Vitrina)
            {
                content.Children.Add(Intro(ContinueHero(recent[0].Game, recent[0].Played), 0));
                if (recent.Count > 1) content.Children.Add(Continue(recent.Skip(1).ToList(), false));
            }
            else content.Children.Add(Continue(recent));
        }

        // Ваши игры — только те, что есть на компьютере.
        var mine = MainWindow.OrderedGames().Where(g => g.Status == Detect.Found && !Features.GameCollections.IsHidden(g.Def.Id)).ToList();
        var searching = AppState.Games.Any(g => g.Status == Detect.Searching);
        var games = new WrapPanel();
        var n = 0;
        var coverWidth = Look.Vitrina ? 144 : 132;
        foreach (var g in mine) games.Children.Add(Intro(GameCard.Cover(g, coverWidth), n++));
        games.Children.Add(Intro(GameCard.AddCover(coverWidth), n++));
        var section = Ui.Col(12, Header(I18n.T("home.yourGames"), I18n.T("lib.open"), () => MainWindow.Current?.Navigate(() => new LibraryPage())));
        if (mine.Count == 0)
            section.Children.Add(Ui.Text(searching ? I18n.T("games.searching") : I18n.T("home.noGames"), "muted", wrap: true));
        section.Children.Add(games);
        content.Children.Add(section);

        if (Starter(mine) is { } starter) content.Children.Insert(0, Intro(starter, 0));

        // 8.4: место под рекламу (пока нет своей рекламы — объявления ModLaunch).
        if (Ads.Enabled)
        {
            _ad ??= AdSlot.Banner();
            if (_ad.Parent is Panel was) was.Children.Remove(_ad);
            content.Children.Add(Intro(_ad, 3));
        }

        // «Выбор ModLaunch» — карусель лучших модов для ваших игр.
        if (_featured is null) { if (!_featuredLoading) { _featuredLoading = true; Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = LoadFeatured(mine)); } }
        else if (_featured.Count > 0 && Settings.Data.Bool("homePopular", true))
        {
            _featuredView ??= new Featured(_featured);
            if (_featuredView.Parent is Panel old) old.Children.Remove(_featuredView);
            content.Children.Add(_featuredView);
        }

        // Топ модов: все ваши игры вместе или одна.
        if (mine.Count > 0 && Settings.Data.Bool("homePopular", true)) content.Children.Add(TopMods(mine));

        var favorites = Favorites.All().Where(f => AppState.Games.Any(g => g.Def.Id == f.GameId && g.Status == Detect.Found)).Take(20).ToList();
        if (favorites.Count > 0 && Settings.Data.Bool("homeFavorites", true)) content.Children.Add(Shelf(I18n.T("home.favorites"), favorites, null));

        Content = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    static Control Header(string title, string? link, Action? open)
    {
        var row = new DockPanel();
        if (link is not null && open is not null)
        {
            var more = Ui.Button(link + "  ›", open, "ghost");
            more.Foreground = Ui.Res("Muted");
            more.Padding = new Thickness(8, 4);
            DockPanel.SetDock(more, Dock.Right);
            row.Children.Add(more);
        }
        var t = Ui.Text(title, "h2");
        t.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(t);
        return row;
    }

    static Control Continue(List<(GameState Game, Features.Played Played)> recent, bool title = true)
    {
        var row = new WrapPanel();
        foreach (var (g, played) in recent)
        {
            var running = Features.Launcher.IsRunning(g.Def.Id);
            row.Children.Add(GameCard.Row(g, running ? I18n.T("time.running") : I18n.T("time.last", ("when", Ui.Ago(played.LastPlayed)))));
        }
        return title ? Ui.Col(12, Header(I18n.T("v4.continue"), null, null), row) : row;
    }

    /// <summary>
    /// «Витрина» (9.0): последняя игра во всю ширину — арт, логотип, сведения о модах, «Играть»
    /// и, если есть, готовые обновления модов с кнопкой «Обновить всё».
    /// </summary>
    static Control ContinueHero(GameState g, Features.Played played)
    {
        var id = g.Def.Id;
        var running = Features.Launcher.IsRunning(id);
        var white = Brushes.White;
        var soft = Ui.Hex("#D2D6DF");

        var art = Ui.GameImage(g.Def, 1600, art: Images.Art.Hero);
        var logo = Images.GameAsset(g.Def, Images.Art.Logo, 768);
        Control title = logo is null
            ? new TextBlock { Text = g.Def.Name, FontFamily = Look.Display, FontSize = 34, FontWeight = FontWeight.Bold, Foreground = white, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 }
            : new Image { Source = logo, MaxHeight = 96, MaxWidth = 440, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
        if (logo is not null) ToolTip.SetTip(title, g.Def.Name);

        var when = running ? I18n.T("time.running") : I18n.T("time.last", ("when", Ui.Ago(played.LastPlayed)));
        var eyebrow = Ui.Row(8, Ui.Icon(Icons.Clock, 14, Ui.Hex("#B8E3F0")),
            new TextBlock { Text = $"{I18n.T("v4.continue")} · {when}".ToUpper(I18n.Culture), FontSize = 12, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.8, Foreground = Ui.Hex("#B8E3F0"), VerticalAlignment = VerticalAlignment.Center });

        Control Chip(string text, IBrush? dot) => new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), CornerRadius = new CornerRadius(8), Padding = new Thickness(11, 5), Margin = new Thickness(0, 0, 8, 0),
            Child = dot is null
                ? new TextBlock { Text = text, FontSize = 13, Foreground = white }
                : Ui.Row(7, Ui.Dot(dot, 7), new TextBlock { Text = text, FontSize = 13, Foreground = white, VerticalAlignment = VerticalAlignment.Center }),
        };
        var chips = new WrapPanel();
        chips.Children.Add(Chip(GameCard.Status(g), g.LoaderInstalled ? Ui.Res("Good") : Ui.Res("Warn")));
        if (played.TotalMs > 0) chips.Children.Add(Chip(I18n.T("time.total", ("time", Features.PlayTime.Format(played.TotalMs))), null));

        var buttons = Ui.Row(10);
        if (running)
        {
            buttons.Children.Add(PlayControls.RunningPill(id));
            buttons.Children.Add(Ui.Button(I18n.T("v4.stop"), () => Features.Launcher.Stop(id), "", Icons.Stop));
        }
        else if (g.LoaderInstalled) buttons.Children.Add(PlayControls.PlayButton(g));
        else
        {
            var install = Ui.Button(I18n.T("games.installLoader", ("loader", g.Def.LoaderName)), () => Actions.InstallLoader(g), "primary", Icons.Download);
            install.Padding = new Thickness(24, 13);
            buttons.Children.Add(install);
        }
        var mods = Ui.Button(I18n.T("tab.mods"), () => MainWindow.Current?.Navigate(() => new GamePage(id, "installed")), "hero-ghost");
        mods.Padding = new Thickness(20, 13);
        buttons.Children.Add(mods);

        var left = Ui.Col(18, eyebrow, Ui.Col(16, title, chips), buttons);
        left.VerticalAlignment = VerticalAlignment.Bottom;
        left.MaxWidth = 640;
        left.HorizontalAlignment = HorizontalAlignment.Left;

        var layers = new Panel
        {
            Children =
            {
                art,
                new Border { Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse("#F0080A0E"), 0), new GradientStop(Color.Parse("#CC080A0E"), 0.34), new GradientStop(Color.Parse("#18080A0E"), 0.68), new GradientStop(Color.Parse("#00080A0E"), 1) } } },
                new Border { Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse("#00080A0E"), 0.55), new GradientStop(Color.Parse("#99080A0E"), 1) } } },
                new Border { Padding = new Thickness(40, 32), Child = left },
            },
        };

        // Готовые обновления модов этой игры — карточка справа внизу.
        if (Features.ModUpdates.Found.TryGetValue(id, out var updates) && updates.Count > 0)
        {
            var list = Ui.Col(7);
            foreach (var u in updates.Take(3))
            {
                var row = new DockPanel();
                var ver = new TextBlock { Text = $"{u.Current} → {u.Latest}", FontSize = 13, Foreground = Ui.Hex("#9AA2B4"), VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(ver, Dock.Right);
                row.Children.Add(ver);
                row.Children.Add(new TextBlock { Text = u.Name, FontSize = 13, Foreground = soft, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 10, 0) });
                list.Children.Add(row);
            }
            var all = Ui.Button(I18n.T("upd.all"), () => UpdateReview.Show(updates.Select(u => (g, u)).ToList()), "hero-light", Icons.ArrowUp);
            all.HorizontalAlignment = HorizontalAlignment.Stretch;
            all.HorizontalContentAlignment = HorizontalAlignment.Center;
            layers.Children.Add(new Border
            {
                Width = 300, Margin = new Thickness(28), Padding = new Thickness(16), CornerRadius = new CornerRadius(16),
                Background = new SolidColorBrush(Color.FromArgb(205, 9, 11, 15)), BorderBrush = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)), BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Child = Ui.Col(12,
                    new TextBlock { Text = I18n.T("upd.review.title." + I18n.Plural(updates.Count, "one", "few", "many"), ("n", updates.Count)), FontWeight = FontWeight.SemiBold, Foreground = white },
                    list, all),
            });
        }

        var card = new Border { Height = 372, CornerRadius = new CornerRadius(20), ClipToBounds = true, Background = Ui.Res("Surface"), Child = layers, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
        // Клик по арту (не по кнопкам) — страница игры.
        card.PointerPressed += (_, e) =>
        {
            if (e.Source is Avalonia.Visual v && Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Button>(v, true) is null)
                MainWindow.Current?.Navigate(() => new GamePage(id));
        };
        card.ContextFlyout = GameCard.Menu(g);
        return card;
    }

    // Карусель и её данные живут весь сеанс: перерисовка главной не сбрасывает прокрутку.
    static List<(GameState Game, Sources.ModInfo Mod)>? _featured;
    Featured? _featuredView;
    Control? _ad;
    static bool _featuredLoading;
    string _topGame = "all";

    async Task LoadFeatured(List<GameState> mine)
    {
        var lists = new List<List<(GameState, Sources.ModInfo)>>();
        foreach (var g in mine.Where(g => g.Def.Picks.Length > 0).Take(6))
        {
            try
            {
                var ids = g.Def.Picks.Take(4).ToList();
                var mods = Program.Demo ? Demo.Many(g.Def, ids) : await Sources.Catalog.Many(g.Def, ids);
                lists.Add(mods.Where(m => !Actions.IsInstalled(g, m.Id)).Concat(mods.Where(m => Actions.IsInstalled(g, m.Id))).Select(m => (g, m)).ToList());
            }
            catch { }
        }
        // Чередуем игры: мод из первой, из второй… — чтобы карусель не была про одну игру.
        var result = new List<(GameState, Sources.ModInfo)>();
        for (var i = 0; result.Count < 10 && lists.Any(l => l.Count > i); i++)
            foreach (var l in lists) if (l.Count > i && result.Count < 10) result.Add(l[i]);
        _featured = result;
        foreach (var g in mine) await Views.Aside.PopularAsync(g);
        Build();
    }

    Control TopMods(List<GameState> mine)
    {
        var chips = new WrapPanel();
        Button Chip(string id, string text, Games.GameDef? def)
        {
            Control content = def is null ? new TextBlock { Text = text } : Ui.Row(6, new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), ClipToBounds = true, Child = Ui.GameImage(def, 40, art: Images.Art.Cover) }, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            var b = new Button { Classes = { "chip" }, Content = content, Margin = new Thickness(0, 0, 8, 8) };
            if (_topGame == id) b.Classes.Add("active");
            b.Click += (_, _) => { _topGame = id; Build(); };
            return b;
        }
        chips.Children.Add(Chip("all", I18n.T("top.all"), null));
        foreach (var g in mine.Where(g => g.Def.HasCatalog)) chips.Children.Add(Chip(g.Def.Id, g.Def.ShortName, g.Def));

        var pool = mine.Where(g => _topGame == "all" || g.Def.Id == _topGame)
            .SelectMany(g => (Views.Aside.Popular(g) ?? []).Select(m => (Game: g, Mod: m)))
            .OrderByDescending(x => x.Mod.Downloads).Take(8).ToList();
        var list = Ui.Col(10);
        foreach (var (g, m) in pool)
        {
            var gg = g; var mm = m;
            list.Children.Add(ModRow.Build(g.Def, m, Actions.IsInstalled(g, m.Id), Actions.IsBusy(g, m.Id), false,
                () => _ = Actions.Install(gg, mm), () => MainWindow.Current?.Navigate(() => new ModPage(gg.Def.Id, mm))));
        }
        if (pool.Count == 0) list.Children.Add(Ui.Text(I18n.T("common.loading"), "muted"));
        var head = new DockPanel();
        var title = Ui.Row(10, Ui.Icon(Icons.Trophy, 20, Ui.Hex("#F2C25C")), Ui.Text(I18n.T("top.title"), "h2"));
        title.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(title);
        return Ui.Col(12, head, chips, list);
    }

    /// <summary>Полка модов: небольшие карточки в ряд с прокруткой.</summary>
    static Control Shelf(string title, List<(string GameId, Sources.ModInfo Mod)> mods, Action? more)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        foreach (var (gameId, mod) in mods)
        {
            var card = new Button
            {
                Classes = { "card-btn" },
                Width = 168,
                Padding = new Thickness(10),
                Content = Ui.Col(8,
                    Ui.Thumb(mod.Icon, mod.Name, 148, 10, 300),
                    Ui.Text(mod.Name, "h3"),
                    Ui.Text(mod.Author == "" ? AppState.Game(gameId).Def.ShortName : mod.Author, "small muted")),
            };
            var id = gameId;
            var m = mod;
            card.Click += (_, _) => MainWindow.Current?.Navigate(() => new ModPage(id, m));
            row.Children.Add(card);
        }
        return Ui.Col(12, Header(title, more is null ? null : I18n.T("home.all"), more),
            new ScrollViewer { Content = row, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 0, 10) });
    }
}
