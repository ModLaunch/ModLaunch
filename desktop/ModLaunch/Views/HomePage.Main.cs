using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>
/// 9.3: «Главная» — короткая, яркая, на один-полтора экрана. Плитки «бенто» плотной сеткой:
/// приветствие с логотипом и цифрами, «Продолжить» последнюю игру, свои игры обложками, профиль
/// игрока с уровнем, «Мод дня», «Лучшие моды» из ваших игр, обновления, быстрые действия, совет
/// дня и вход в «Ленту». Всё длинное и бесконечное — в «Ленте».
/// </summary>
public sealed partial class HomePage
{
    const double MainGap = 10;
    static int _tip = -1;

    /// <summary>Яркие «молодёжные» цвета ярлыков: розовый, бирюзовый, жёлтый.</summary>
    static readonly IBrush Pink = new SolidColorBrush(Color.Parse("#FF3D81"));
    static readonly IBrush Cyan = new SolidColorBrush(Color.Parse("#19D3F5"));
    static readonly IBrush Yellow = new SolidColorBrush(Color.Parse("#FFD43B"));

    void BuildMain()
    {
        var mine = FeedGames();
        if (_featured is null && !_featuredLoading && mine.Count > 0) { _featuredLoading = true; Dispatcher.UIThread.Post(() => _ = LoadFeatured(mine)); }

        var col = StoreKit.Column(spacing: MainGap, top: 18);
        if (Starter(mine) is { } starter) col.Children.Add(Intro(starter, 0));

        var grid = new Grid { ColumnSpacing = MainGap, RowSpacing = MainGap };
        for (var i = 0; i < 12; i++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var i = 0; i < 4; i++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var n = 0;
        void Put(Control c, int column, int row, int span, double height, bool scale = true)
        {
            Grid.SetColumn(c, column);
            Grid.SetRow(c, row);
            Grid.SetColumnSpan(c, span);
            // Картинки растут вместе с шириной; карточки-списки тянутся на высоту ряда.
            if (double.IsNaN(height)) { }
            else if (scale) StoreKit.Scaled(c, height, 1300.0 * span / 12, maxScale: 1.25);
            else c.Height = height;
            grid.Children.Add(Intro(c, n++));
        }

        var pool = BestMods(mine);
        Put(Hero(mine), 0, 0, 7, 214);
        Put(ContinueCard(mine), 7, 0, 5, 214);
        Put(MyGames(mine), 0, 1, 8, 262, scale: false);
        Put(mine.Count > 0 ? StatsTile() : PromoTile("add", false), 8, 1, 4, 262, scale: false);
        Put(pool.Count > 0 ? ModOfDay(pool) : PromoTile("creator", true), 0, 2, 5, 300);
        Put(BestModsCard(pool), 5, 2, 4, double.NaN);
        Put(SideStack(mine), 9, 2, 3, double.NaN);
        Put(TipCard(), 0, 3, 5, double.NaN);
        Put(FeedTeaser(pool), 5, 3, 7, 118);
        col.Children.Add(grid);

        if (Ads.Enabled)
        {
            _ad ??= AdSlot.Banner();
            if (_ad.Parent is Panel was) was.Children.Remove(_ad);
            col.Children.Add(_ad);
        }
        Content = new ScrollViewer { Content = col, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    /// <summary>Лучшие моды из ваших игр (подборка ModLaunch и популярные), без уже установленных.</summary>
    static List<(GameState Game, ModInfo Mod, string Tag)> BestMods(List<GameState> mine)
    {
        var list = new List<(GameState, ModInfo, string)>();
        var seen = new HashSet<string>();
        foreach (var (g, m) in _featured ?? [])
            if (!Actions.IsInstalled(g, m.Id) && seen.Add(g.Def.Id + "/" + m.Id)) list.Add((g, m, "pick"));
        foreach (var g in mine)
            foreach (var m in Views.Aside.Popular(g) ?? [])
                if (!Actions.IsInstalled(g, m.Id) && seen.Add(g.Def.Id + "/" + m.Id)) list.Add((g, m, "popular"));
        return list.OrderByDescending(x => x.Item3 == "pick" ? 1 : 0).ThenByDescending(x => x.Item2.Downloads).ToList();
    }

    static string Greeting()
    {
        var h = DateTime.Now.Hour;
        return I18n.T(h < 5 ? "v93.home.night" : h < 12 ? "v93.home.morning" : h < 18 ? "v93.home.day" : "v93.home.evening");
    }

    /// <summary>Приветствие: логотип, «ModLaunch», имя игрока, цифры и две главные кнопки.</summary>
    Control Hero(List<GameState> mine)
    {
        var accent = Gx.Accent;
        var blobs = new Panel
        {
            Children =
            {
                new Border { Background = new SolidColorBrush(Color.Parse("#0C0B14")) },
                new Border { Background = new RadialGradientBrush { Center = new RelativePoint(0.12, 0.1, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.12, 0.1, RelativeUnit.Relative), RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative), RadiusY = new RelativeScalar(1.2, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Gx.Alpha(accent, 210), 0), new GradientStop(Gx.Alpha(accent, 0), 1) } } },
                new Border { Background = new RadialGradientBrush { Center = new RelativePoint(0.95, 1, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.95, 1, RelativeUnit.Relative), RadiusX = new RelativeScalar(0.6, RelativeUnit.Relative), RadiusY = new RelativeScalar(1.1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.FromArgb(170, 255, 61, 129), 0), new GradientStop(Color.FromArgb(0, 255, 61, 129), 1) } } },
                Gx.Scanlines(),
            },
        };
        var logo = new Border
        {
            Width = 62, Height = 62, CornerRadius = new CornerRadius(18), Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), BorderThickness = new Thickness(1), BoxShadow = Gx.Glow(200, 30, 2),
            Child = new Image { Source = Images.Asset("icon.png", 128), Width = 40, Height = 40 },
        };
        var p = Social.Account.Get();
        var name = p.SignedIn ? p.Name ?? I18n.T("v93.feed.player") : I18n.T("v93.feed.player");
        var title = Ui.Col(0,
            new TextBlock { Text = Greeting().ToUpper(I18n.Culture) + ", " + name.ToUpper(I18n.Culture), FontSize = 11.5, FontWeight = FontWeight.Bold, LetterSpacing = 1.4, Foreground = Ui.Hex("#E9E6FF"), TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = "ModLaunch", FontFamily = Gx.Display, FontSize = 38, FontWeight = FontWeight.Bold, LetterSpacing = -1, Foreground = Brushes.White });
        title.VerticalAlignment = VerticalAlignment.Center;

        Control Chip(string icon, string text, IBrush? tint = null) => new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)), CornerRadius = new CornerRadius(999), Padding = new Thickness(10, 5), Margin = new Thickness(0, 0, 6, 6),
            BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), BorderThickness = new Thickness(1),
            Child = Ui.Row(6, Ui.Icon(icon, 13, tint ?? Brushes.White), new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center }),
        };
        var mods = mine.Sum(g => g.ModCount);
        var updates = Features.ModUpdates.Found.Sum(kv => kv.Value.Count);
        var chips = new WrapPanel();
        chips.Children.Add(Chip(Icons.Gamepad, I18n.T("v93.home.games", ("n", mine.Count)), Cyan));
        chips.Children.Add(Chip(Icons.Package, I18n.T("v93.home.mods", ("n", mods)), Yellow));
        if (updates > 0) chips.Children.Add(Chip(Icons.ArrowUp, I18n.T("v93.home.updates", ("n", updates)), Pink));
        var hours = mine.Sum(g => Features.PlayTime.Get(g.Def.Id).TotalMs) / 3_600_000.0;
        if (hours >= 1) chips.Children.Add(Chip(Icons.Clock, I18n.T("v93.home.hours", ("n", (int)hours))));

        var feed = Ui.Button(I18n.T("v93.home.toFeed"), () => MainWindow.Current?.Navigate(() => new HomePage(feed: true)), "primary", Icons.Flame);
        feed.Padding = new Thickness(18, 9);
        var add = Ui.Button(I18n.T("add.title"), () => MainWindow.Current?.Navigate(() => new AddGamePage()), "hero-ghost", Icons.Plus);
        add.Padding = new Thickness(16, 9);
        var body = Ui.Col(12, Ui.Row(14, logo, title), chips, Ui.Row(8, feed, add));
        body.Margin = new Thickness(22, 18, 18, 16);
        body.VerticalAlignment = VerticalAlignment.Center;
        return new Border { Classes = { "store-tile", "home-hero" }, Cursor = null, Child = new Panel { Children = { blobs, body } } };
    }

    /// <summary>«Продолжить» — игра, в которую играли последней.</summary>
    static Control ContinueCard(List<GameState> mine)
    {
        var g = mine.FirstOrDefault();
        if (g is null) return PromoTile("add", true);
        var id = g.Def.Id;
        var played = Features.PlayTime.Get(id);
        var running = Features.Launcher.IsRunning(id);
        var (dot, state) = StoreKit.GameState(g);
        var logo = Images.GameAsset(g.Def, Images.Art.Logo, 512);
        Control title = logo is null
            ? Gx.Title(g.Def.Name, 22, Brushes.White, 2)
            : new Image { Source = logo, MaxHeight = 64, MaxWidth = 260, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
        var when = running ? I18n.T("time.running") : played.LastPlayed is not null ? Ui.Ago(played.LastPlayed) : I18n.T("v93.home.notPlayed");
        var eyebrow = Gx.Eyebrow(I18n.T("v4.continue") + " · " + when, Ui.Hex("#E6E7EE"), 10.5);
        Control action;
        if (running) action = PlayControls.RunningPill(id, big: false);
        else if (g.LoaderInstalled || g.Def.Loader is Games.LoaderKind.None or Games.LoaderKind.Minecraft)
        {
            var b = Ui.Button(I18n.T("games.play"), () => Actions.Play(g), "primary", Icons.Play);
            b.Padding = new Thickness(20, 9);
            action = b;
        }
        else
        {
            var b = Ui.Button(I18n.T("games.installLoader", ("loader", g.Def.LoaderName)), () => Actions.InstallLoader(g), "primary", Icons.Download);
            b.Padding = new Thickness(16, 9);
            action = b;
        }
        var words = Ui.Col(10, eyebrow, title, StoreKit.Pill(state, dot), action);
        words.VerticalAlignment = VerticalAlignment.Center;
        words.HorizontalAlignment = HorizontalAlignment.Left;
        words.Margin = new Thickness(20, 14);
        var layers = new Panel { Children = { Ui.GameImage(g.Def, 1024, art: Images.Art.Hero), new Border { Background = Gx.ShadeLeft(0.85, 235) }, Gx.Haze(true), words } };
        var t = Tile(layers, () => MainWindow.Current?.Navigate(() => new GamePage(id)), "hero");
        t.ContextFlyout = GameCard.Menu(g);
        return t;
    }

    /// <summary>«Мои игры» — обложки установленных игр в ряд и «Добавить».</summary>
    static Control MyGames(List<GameState> mine)
    {
        var head = new DockPanel();
        var all = Ui.Button(I18n.T("lib.title") + "  ›", () => MainWindow.Current?.Navigate(() => new LibraryPage()), "ghost");
        all.Padding = new Thickness(8, 2);
        all.Foreground = Ui.Res("Muted");
        DockPanel.SetDock(all, Dock.Right);
        head.Children.Add(all);
        head.Children.Add(Ui.Row(10, Gx.Eyebrow(I18n.T("v93.home.myGames"), Ui.Res("Text"), 12), new TextBlock { Text = mine.Count.ToString(), FontFamily = Gx.Display, FontWeight = FontWeight.Bold, FontSize = 12, Foreground = Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Center }));
        var posters = mine.Select(g => StoreKit.Poster(g, 128)).Append(StoreKit.AddPoster(128)).ToList();
        var body = Ui.Col(10, head, StoreKit.Shelf(posters, 8));
        body.Margin = new Thickness(14, 12, 14, 0);
        return new Border { Classes = { "card", "home-card" }, Padding = new Thickness(0), Child = body, ClipToBounds = true };
    }

    /// <summary>«Мод дня» — каждый день другой: обложка мода во всю плитку, ярлык, установка.</summary>
    static Control ModOfDay(List<(GameState Game, ModInfo Mod, string Tag)> pool)
    {
        var top = pool.Take(Math.Min(pool.Count, 12)).ToList();
        var (g, m, _) = top[DateTime.Today.DayOfYear % top.Count];
        var sticker = Gx.Tag(I18n.T("v93.home.modOfDay"), Pink, Brushes.White, 12);
        sticker.Margin = new Thickness(14);
        var words = Ui.Col(8,
            Gx.Title(m.Name, 22, Brushes.White, 2),
            new TextBlock { Text = $"{g.Def.ShortName}" + (m.Downloads > 0 ? " · ↓ " + I18n.Compact(m.Downloads) : "") + (m.Author != "" ? " · " + (m.Author.Length > 24 ? m.Author[..24] + "…" : m.Author) : ""), FontSize = 12.5, Foreground = Ui.Hex("#D9DBE3"), TextTrimming = TextTrimming.CharacterEllipsis },
            Ui.Row(8, InstallButton(g, m, true)));
        words.VerticalAlignment = VerticalAlignment.Bottom;
        words.Margin = new Thickness(18, 0, 18, 16);
        var id = g.Def.Id;
        var t = Tile(new Panel { Children = { ModCover.Create(g, m, 1024, big: true, iconV: VerticalAlignment.Top, iconShare: 0.5), new Border { Background = Gx.ShadeUp(0.3, 240) }, sticker, words } },
            () => MainWindow.Current?.Navigate(() => new ModPage(id, m)), "hero");
        Ctx.Attach(t, () => ModRow.Menu(g.Def, m, Actions.IsInstalled(g, m.Id), Actions.IsBusy(g, m.Id), () => _ = Actions.Install(g, m), () => MainWindow.Current?.Navigate(() => new ModPage(id, m))));
        return t;
    }

    /// <summary>«Лучшие моды» — пять самых скачиваемых из ваших игр, местами как в таблице рекордов.</summary>
    static Control BestModsCard(List<(GameState Game, ModInfo Mod, string Tag)> pool)
    {
        // Пять строк делят высоту карточки поровну: на большом экране список не «висит» сверху.
        var rows = new Grid { RowSpacing = 2 };
        var list = pool.Skip(1).Take(5).ToList();
        if (list.Count == 0) rows.Children.Add(Ui.Text(I18n.T("common.loading"), "small muted"));
        foreach (var _ in list) rows.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        IBrush[] rankColors = [Yellow, Ui.Hex("#C9D1DC"), Ui.Hex("#D9894E"), Ui.Res("Faint"), Ui.Res("Faint")];
        for (var i = 0; i < list.Count; i++)
        {
            var (g, m, _) = list[i];
            var rank = new TextBlock { Text = (i + 1).ToString(), FontFamily = Gx.Display, FontSize = 18, FontWeight = FontWeight.Bold, Width = 24, Foreground = rankColors[i], VerticalAlignment = VerticalAlignment.Center };
            var words = Ui.Col(1,
                new TextBlock { Text = m.Name, FontWeight = FontWeight.SemiBold, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = $"{g.Def.ShortName} · ↓ {I18n.Compact(m.Downloads)}", FontSize = 11.5, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis });
            words.VerticalAlignment = VerticalAlignment.Center;
            var action = InstallButton(g, m, false);
            if (action is Button b) { b.Width = b.Height = 30; b.CornerRadius = new CornerRadius(15); }
            action.VerticalAlignment = VerticalAlignment.Center;
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), ColumnSpacing = 10, VerticalAlignment = VerticalAlignment.Center };
            line.Children.Add(rank);
            var thumb = Ui.Thumb(m.Icon, m.Name, 34, 8, 120);
            Grid.SetColumn(thumb, 1);
            line.Children.Add(thumb);
            Grid.SetColumn(words, 2);
            line.Children.Add(words);
            Grid.SetColumn(action, 3);
            line.Children.Add(action);
            var row = new Border { Classes = { "store-row" }, Padding = new Thickness(6, 3), Child = line };
            var id = g.Def.Id;
            var mm = m;
            StoreKit.OnClick(row, () => MainWindow.Current?.Navigate(() => new ModPage(id, mm)));
            row.VerticalAlignment = VerticalAlignment.Stretch;
            Grid.SetRow(row, i);
            rows.Children.Add(row);
        }
        var head = new DockPanel();
        var sticker = Gx.Tag("TOP", Yellow, new SolidColorBrush(Color.Parse("#1A1400")), 11);
        sticker.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(sticker, Dock.Right);
        head.Children.Add(sticker);
        head.Children.Add(Gx.Eyebrow(I18n.T("v93.home.best"), Ui.Res("Text"), 12));
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 6, Margin = new Thickness(12, 10, 12, 8) };
        body.Children.Add(head);
        Grid.SetRow(rows, 1);
        body.Children.Add(rows);
        return new Border { Classes = { "card", "home-card" }, Padding = new Thickness(0), Child = body, ClipToBounds = true };
    }

    /// <summary>Правая колонка: обновления модов и быстрые действия.</summary>
    static Control SideStack(List<GameState> mine)
    {
        var updates = mine.SelectMany(g => (Features.ModUpdates.Found.TryGetValue(g.Def.Id, out var l) ? l : []).Select(u => (g, u))).ToList();
        Control top;
        if (updates.Count > 0)
        {
            var go = Ui.Button(I18n.T("upd.all"), () => UpdateReview.Show(updates), "primary", Icons.ArrowUp);
            go.HorizontalAlignment = HorizontalAlignment.Stretch;
            go.HorizontalContentAlignment = HorizontalAlignment.Center;
            top = Ui.Col(8,
                Ui.Row(10, new TextBlock { Text = updates.Count.ToString(), FontFamily = Gx.Display, FontSize = 30, FontWeight = FontWeight.Bold, Foreground = Pink }, Ui.Col(0, Gx.Eyebrow(I18n.T("v93.home.updatesTitle"), Ui.Res("Text"), 11), Ui.Text(I18n.T("v93.home.updatesText"), "small muted"))),
                go);
        }
        else
            top = Ui.Row(12,
                new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(21), Background = new SolidColorBrush(Color.FromArgb(40, 61, 214, 140)), Child = Ui.Icon(Icons.Check, 20, Ui.Res("Good")) },
                Ui.Col(1, Gx.Eyebrow(I18n.T("v93.home.fresh"), Ui.Res("Text"), 11), Ui.Text(I18n.T("v93.home.freshText"), "small muted")));
        var updatesCard = new Border { Classes = { "card", "home-card" }, Padding = new Thickness(14, 12), Child = top };

        (string Icon, string Key, Action Run, IBrush Tint)[] actions =
        [
            (Icons.Plus, "add.title", () => MainWindow.Current?.Navigate(() => new AddGamePage()), Cyan),
            (Icons.Package, "mc.title", () => MainWindow.Current?.Navigate(() => new ModsCenterPage()), Yellow),
            (Icons.Creator, "Creator Hub", () => MainWindow.Current?.Navigate(() => new MarketPage()), Pink),
            (Icons.Tv, "Big Picture", BigPictureWindow.Open, Cyan),
            (Icons.Palette, "v92.style.section", () => StylePicker.Show(), Yellow),
            (Icons.Users, "v92.nav.friends", () => MainWindow.Current?.Navigate(() => new FriendsPage()), Pink),
        ];
        var quick = new UniformGrid { Columns = 3 };
        foreach (var (icon, key, run, tint) in actions)
        {
            var label = key.Contains('.') ? I18n.T(key) : key;
            var b = new Button
            {
                Classes = { "quick" }, Margin = new Thickness(0, 0, 6, 6), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(4),
                Content = Ui.Col(4, Ui.Icon(icon, 20, tint), new TextBlock { Text = label, FontSize = 10.5, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 90 }),
            };
            if (b.Content is StackPanel sp) foreach (var c in sp.Children) c.HorizontalAlignment = HorizontalAlignment.Center;
            ToolTip.SetTip(b, label);
            b.Click += (_, _) => run();
            quick.Children.Add(b);
        }
        // Кнопки делят всю высоту карточки — без пустого места снизу на большом экране.
        var quickBody = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 8 };
        quickBody.Children.Add(new Border { Margin = new Thickness(4, 0, 0, 0), Child = Gx.Eyebrow(I18n.T("v93.home.quick"), Ui.Res("Text"), 11) });
        Grid.SetRow(quick, 1);
        quickBody.Children.Add(quick);
        var quickCard = new Border { Classes = { "card", "home-card" }, Padding = new Thickness(10, 10, 4, 4), Child = quickBody };
        var stack = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = MainGap };
        stack.Children.Add(updatesCard);
        Grid.SetRow(quickCard, 1);
        stack.Children.Add(quickCard);
        return stack;
    }

    static readonly string[] Tips = ["v93.tip.1", "v93.tip.2", "v93.tip.3", "v93.tip.4", "v93.tip.5", "v93.tip.6", "v93.tip.7", "v93.tip.8"];

    /// <summary>«Совет дня» — каждый день новый, «Ещё» листает дальше.</summary>
    static Control TipCard()
    {
        if (_tip < 0) _tip = DateTime.Today.DayOfYear % Tips.Length;
        var text = new TextBlock { Text = I18n.T(Tips[_tip]), TextWrapping = TextWrapping.Wrap, FontSize = 13, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Ui.Res("Text") };
        var more = Ui.Button(I18n.T("v93.home.tipMore"), () => { _tip = (_tip + 1) % Tips.Length; text.Text = I18n.T(Tips[_tip]); Animate.From(text, "translateX(16px)", 300, 0, new CubicEaseOut()); }, "ghost", Icons.Shuffle);
        more.Padding = new Thickness(8, 3);
        more.FontSize = 12;
        var head = new DockPanel();
        DockPanel.SetDock(more, Dock.Right);
        head.Children.Add(more);
        head.Children.Add(Ui.Row(8, Ui.Icon(Icons.Sparkles, 15, Yellow), Gx.Eyebrow(I18n.T("v93.home.tip"), Ui.Res("Text"), 11)));
        var body = Ui.Col(6, head, text);
        body.VerticalAlignment = VerticalAlignment.Center;
        return new Border { Classes = { "card", "home-card" }, Padding = new Thickness(16, 10), Child = body };
    }

    /// <summary>Вход в «Ленту»: яркая полоса со стопкой обложек модов.</summary>
    static Control FeedTeaser(List<(GameState Game, ModInfo Mod, string Tag)> pool)
    {
        var stack = new Canvas { Width = 150, Height = 54, VerticalAlignment = VerticalAlignment.Center };
        var thumbs = pool.Skip(6).Take(5).ToList();
        for (var i = 0; i < thumbs.Count; i++)
        {
            var (g, m, _) = thumbs[i];
            var t = new Border
            {
                Width = 50, Height = 50, CornerRadius = new CornerRadius(14), ClipToBounds = true, BorderBrush = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), BorderThickness = new Thickness(2),
                Child = Ui.Thumb(m.Icon, m.Name, 50, 0, 120), RenderTransform = new RotateTransform((i - 2) * 6),
            };
            Canvas.SetLeft(t, i * 24);
            Canvas.SetTop(t, 2);
            stack.Children.Add(t);
        }
        var words = Ui.Col(2,
            Ui.Row(8, Ui.Icon(Icons.Flame, 18, Brushes.White), new TextBlock { Text = I18n.T("v93.feed.title"), FontFamily = Gx.Display, FontSize = 20, FontWeight = FontWeight.Bold, Foreground = Brushes.White }),
            new TextBlock { Text = I18n.T("v93.home.feedText"), FontSize = 12.5, Foreground = Ui.Hex("#EFEAFF"), TextTrimming = TextTrimming.CharacterEllipsis });
        words.VerticalAlignment = VerticalAlignment.Center;
        var go = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Background = Brushes.White, VerticalAlignment = VerticalAlignment.Center,
            Child = Ui.Icon(Icons.ChevronRight, 18, new SolidColorBrush(Gx.Accent)),
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 18, Margin = new Thickness(20, 0, 16, 0) };
        grid.Children.Add(words);
        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);
        Grid.SetColumn(go, 2);
        grid.Children.Add(go);
        var accent = Gx.Accent;
        var background = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Look.Mix(accent, Colors.Black, 0.15), 0), new GradientStop(Color.Parse("#E83E8C"), 1) },
            },
        };
        return Tile(new Panel { Children = { background, Gx.Scanlines(), grid } }, () => MainWindow.Current?.Navigate(() => new HomePage(feed: true)), "hero");
    }
}
