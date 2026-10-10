using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>Страница игры: шапка с запуском и две вкладки — установленные моды и каталог.</summary>
public sealed partial class GamePage : Page
{
    readonly GameState _g;
    string _tab;
    string _section = "all";
    string? _source;
    readonly Dictionary<string, long> _sourceTotals = [];
    string _query;
    SortBy _sort = SortBy.Popular;
    int _period;
    bool _hideInstalled = Settings.Data.Bool("hideInstalled");
    string _view = Settings.Data.Str("catalogView") == "grid" ? "grid" : "list";
    bool _adult = Settings.Data.Bool("showAdult", false);

    // Каталог грузится отдельно от перерисовки: перерисовка не должна его сбрасывать.
    readonly List<ModInfo> _mods = [];
    long _total;
    bool _hasMore, _loading;
    string? _error;
    int _page = 1;
    int _requestId;
    List<ModInfo>? _picks;

    StackPanel? _listHost;

    public GamePage(string gameId, string tab = "", string query = "")
    {
        // Своя игра могла быть убрана — тогда «назад» ведёт на первую игру.
        _g = AppState.Games.FirstOrDefault(g => g.Def.Id == gameId) ?? AppState.Games[0];
        _query = query;
        // Minecraft показывает своя страница (MainWindow.Show подменяет): здесь ничего не грузим.
        if (_g.Def.IsMinecraft) { _tab = tab; return; }
        // 9.2 Store: страница игры открывается на «Обзоре».
        _tab = tab != "" ? tab : Look.Store ? "overview" : _g.ModCount > 0 || !_g.Def.HasCatalog ? "installed" : "catalog";
        if (_tab == "catalog" && !_g.Def.HasCatalog) _tab = Look.Store ? "overview" : "installed";
        if (_tab == "overview" && !Look.Store) _tab = "installed";
        if (_g.Def.Picks.Length > 0 && _query == "") _section = "picks";
        // На «Обзоре» каталог грузится тоже — ради цифры «модов в каталоге» в шапке.
        if (_tab == "catalog" || _tab == "overview" && _g.Def.HasExternalCatalog && _g.Status == Detect.Found) _ = Load(reset: true);
    }

    /// <summary>Вкладка и запрос — для подмены страницей Minecraft.</summary>
    internal string Tab => _tab;
    internal string Query => _query;

    public override string Title => _tab == "catalog" && _g.Status == Detect.Found ? I18n.T("games.market") : _g.Def.Name;
    public override Control? Aside() => Views.Aside.Game(_g);
    public override IEnumerable<(string Text, Action? Open)> Crumbs =>
    [
        (_g.Def.Name, () => MainWindow.Current?.Navigate(() => new GamePage(_g.Def.Id))),
        (_g.Status != Detect.Found ? I18n.T("games.notDetected") : _tab switch
        {
            "catalog" => I18n.T("games.market"),
            "profiles" => I18n.T("games.profiles"),
            "saves" => I18n.T("games.saves"),
            "tools" => I18n.T("v4.tools"),
            "log" => I18n.T("games.log"),
            "health" => I18n.T("health.tab"),
            "config" => I18n.T("cfg.tab"),
            "shots" => I18n.T("shots.tab"),
            "overview" => I18n.T("v92.tab.overview"),
            _ => I18n.T("games.downloads"),
        }, null),
    ];
    public override string? GameId => _g.Def.Id;
    public override string SearchHint => I18n.T("search.game", ("game", _g.Def.Name));

    public override void Search(string text)
    {
        _query = text;
        _tab = "catalog";
        if (_section == "picks" || _section == "packs") _section = "all";
        _ = Load(reset: true);
        Build();
        MainWindow.Current?.RenderCrumbs();
    }

    public override void Build()
    {
        // 9.2: в дизайне Store — страница как у приложения в Microsoft Store (GamePage.Store.cs).
        if (Look.Store) { BuildStore(); return; }
        var content = new StackPanel { Spacing = 24, Margin = new Thickness(40, 26, 40, 40), MaxWidth = 1640 };
        // 9.0 «Витрина»: шапка игры во всю ширину окна, без рамки; остальное — под ней с полями.
        Control page = content;
        if (Look.Vitrina)
        {
            content.Margin = new Thickness(40, 4, 40, 40);
            page = new StackPanel { Children = { Header(), content } };
        }
        else content.Children.Add(Header());
        if (_g.Status == Detect.Found)
        {
            if (PurgeBanner() is { } banner) content.Children.Add(banner);
            content.Children.Add(Tabs());
            var body = _tab switch
            {
                "catalog" => CatalogView(),
                "profiles" => ProfilesView(),
                "saves" => SavesView(),
                "log" => LogView(),
                "tools" => ToolsView(),
                "health" => HealthView(),
                "config" => ConfigView(),
                "shots" => ShotsView(),
                _ => InstalledView(),
            };
            content.Children.Add(body);
            // Первый показ вкладки: содержимое поднимается волной (при перерисовке — нет).
            if (!Shown) Stagger(body, TabSwitch ? 0 : 140);
        }
        else content.Children.Add(NotFoundView());

        // Прокрутка одна на всю страницу: после перерисовки (включили мод, пришла загрузка)
        // список остаётся на месте, а не прыгает наверх. Сброс — только при смене вкладки.
        _scroll ??= new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        if (_scrollTab != _tab) { _scroll.Offset = default; _scrollTab = _tab; }
        _scroll.Content = page;
        _root ??= new Panel { Children = { _scroll } };
        while (_root.Children.Count > 1) _root.Children.RemoveAt(1);
        if (BulkBar() is { } bar)
        {
            content.Margin = new Thickness(content.Margin.Left, content.Margin.Top, content.Margin.Right, 110);
            _root.Children.Add(bar);
        }
        Content = _root;
    }

    ScrollViewer? _scroll;
    Panel? _root;
    string? _scrollTab;

    // ---------------------------------------------------------------- шапка

    Control Header()
    {
        var status = _g.Status switch
        {
            Detect.Found when _g.Def.Loader == LoaderKind.None => (Ui.Res("Good"), I18n.T("add.noLoader", ("folder", _g.Def.ModsFolder))),
            Detect.Found when _g.LoaderInstalled => (Ui.Res("Good"), I18n.T("games.loaderReady", ("loader", _g.Def.LoaderName))),
            Detect.Found => (Ui.Res("Warn"), I18n.T("games.loaderMissing", ("loader", _g.Def.LoaderName))),
            Detect.Searching => (Ui.Res("Muted"), _g.SearchingWhere is null ? I18n.T("games.searching") : I18n.T("games.searchingWhere", ("where", _g.SearchingWhere))),
            _ => (Ui.Res("Faint"), I18n.T("games.notDetected")),
        };

        // Логотип игры вместо названия, если он есть (как в Steam).
        var vitrina = Look.Vitrina;
        var logo = Images.GameAsset(_g.Def, Images.Art.Logo, vitrina ? 768 : 640);
        Control title = logo is null
            ? NameTitle()
            : new Image { Source = logo, MaxHeight = vitrina ? 92 : 70, MaxWidth = vitrina ? 400 : 260, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
        if (logo is not null) ToolTip.SetTip(title, _g.Def.Name);
        TextBlock NameTitle()
        {
            var t = new TextBlock { Text = _g.Def.Name, FontSize = vitrina ? 34 : 30, FontWeight = FontWeight.Bold, Foreground = Brushes.White };
            if (vitrina) t.FontFamily = Look.Display;
            return t;
        }
        var info = Ui.Col(10,
            title,
            Ui.Row(8, Ui.Dot(status.Item1), Ui.Text(status.Item2, "small", color: Ui.Hex("#D5DAE5"))));
        if (_g.Path is not null)
        {
            var line = Ui.Row(16, Ui.Row(8, Ui.Icon(Icons.Folder, 13, Ui.Hex("#AAB2C2")), Ui.Text(Ui.ShortPath(_g.Path), "small", color: Ui.Hex("#AAB2C2"))));
            var played = Features.PlayTime.Get(_g.Def.Id);
            if (played.Running) line.Children.Add(Ui.Row(8, Ui.Icon(Icons.Clock, 13, Ui.Res("Good")), Ui.Text(I18n.T("time.running"), "small", color: Ui.Res("Good"))));
            else if (played.TotalMs > 0) line.Children.Add(Ui.Row(8, Ui.Icon(Icons.Clock, 13, Ui.Hex("#AAB2C2")), Ui.Text(I18n.T("time.total", ("time", Features.PlayTime.Format(played.TotalMs))), "small", color: Ui.Hex("#AAB2C2"))));
            info.Children.Add(line);
        }
        info.VerticalAlignment = VerticalAlignment.Bottom;

        var buttons = Ui.Row(10);
        buttons.VerticalAlignment = VerticalAlignment.Bottom;
        if (_g.Status == Detect.Found)
        {
            buttons.Children.Add(Ui.Button(I18n.T("games.openFolder"), () => Actions.OpenFolder(_g.Path), vitrina ? "hero-ghost" : "", Icons.Folder));
            if (_g.LoaderInstalled)
            {
                if (Features.Launcher.IsRunning(_g.Def.Id))
                {
                    // Как в Modrinth App: «● Запущено 12:34» и рядом «Остановить».
                    buttons.Children.Add(PlayControls.RunningPill(_g.Def.Id));
                    var stop = Ui.Button(I18n.T("v4.stop"), () => { Features.Launcher.Stop(_g.Def.Id); MainWindow.Current?.Toast(I18n.T("v4.stopped")); }, "", Icons.Stop);
                    stop.Padding = new Thickness(20, 13);
                    buttons.Children.Add(stop);
                }
                else buttons.Children.Add(PlayControls.PlayButton(_g));
            }
            else
            {
                var busy = Jobs.All.Any(j => j.Active && j.Title == _g.Def.LoaderName && j.GameName == _g.Def.Name);
                var install = Ui.Button(busy ? I18n.T("aside.installing") : I18n.T("games.installLoader", ("loader", _g.Def.LoaderName)), () => Actions.InstallLoader(_g), "primary", Icons.Download);
                install.IsEnabled = !busy;
                install.Padding = new Thickness(24, 13);
                buttons.Children.Add(install);
            }
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = vitrina ? new Thickness(40, 24, 40, 26) : new Thickness(26, 22) };
        grid.Children.Add(info);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        var hero = Ui.GameImage(_g.Def, 1400, art: Images.Art.Hero);
        if (!Shown && !TabSwitch)
        {
            // Вход на страницу игры: картинка мягко «отъезжает», надписи и кнопки выезжают.
            Animate.From(hero, "scale(1.12)", 1400, 0, new Avalonia.Animation.Easings.QuadraticEaseOut(), 1);
            Animate.From(info, "translateX(-28px)", 520, 120);
            Animate.From(buttons, "translateX(28px)", 520, 180);
        }
        if (vitrina)
        {
            // Низ арта плавно уходит в цвет окна — вкладки стоят прямо под картинкой.
            var bg = Ui.Res("Bg") is SolidColorBrush bb ? bb.Color : Color.Parse("#0B0C10");
            Color A(byte a) => Color.FromArgb(a, bg.R, bg.G, bg.B);
            return new Border
            {
                ClipToBounds = true,
                Height = 300,
                Child = new Panel
                {
                    Children =
                    {
                        hero,
                        new Border { Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative), GradientStops = { new GradientStop(A(40), 0), new GradientStop(A(30), 0.35), new GradientStop(A(120), 0.62), new GradientStop(A(255), 1) } } },
                        new Border { Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative), GradientStops = { new GradientStop(Color.Parse("#C0080A0E"), 0), new GradientStop(Color.Parse("#00080A0E"), 0.6) } } },
                        grid,
                    },
                },
            };
        }
        return new Border
        {
            CornerRadius = new CornerRadius(20),
            ClipToBounds = true,
            BorderBrush = Ui.Res("Line"),
            BorderThickness = new Thickness(1),
            Height = 184,
            Child = new Panel
            {
                Children =
                {
                    hero,
                    new Border { Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative), GradientStops = { new GradientStop(Color.Parse("#100F1116"), 0), new GradientStop(Color.Parse("#700F1116"), 0.5), new GradientStop(Color.Parse("#F00F1116"), 1) } } },
                    new Border { Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative), GradientStops = { new GradientStop(Color.Parse("#A00F1116"), 0), new GradientStop(Color.Parse("#000F1116"), 0.7) } } },
                    grid,
                },
            },
        };
    }


    /// <summary>Переход между вкладками той же игры: шапка не въезжает заново, меняется только содержимое.</summary>
    bool TabSwitch;
    public override bool SameScreenAs(Page? previous)
    {
        TabSwitch = previous is GamePage gp && gp.GameId == GameId;
        return TabSwitch;
    }

    /// <summary>Где был индикатор вкладок у каждой игры — чтобы он «переехал» к новой, а не появился.</summary>
    static readonly Dictionary<string, (double X, double W)> TabMark = [];

    /// <summary>
    /// Вкладки (8.4): главные — всегда на виду (Моды, Каталог, Настройки, Профили), редкие —
    /// в «Ещё» (Сохранения, Инструменты, Скриншоты, Лог, Проверка). Под выбранной ездит
    /// индикатор, у «Ещё» — точка, если проверка нашла проблемы.
    /// </summary>
    Control Tabs()
    {
        var updates = Features.ModUpdates.Found.TryGetValue(_g.Def.Id, out var found) && found.Count > 0 ? found.Count : 0;
        var profiles = Features.Profiles.List(_g.Def.Id).Count;
        var saves = Features.Backups.List(_g.Def.Id).Count;
        var tools = Features.Tools.For(_g.Def.Id).Count;
        var shots = ShotFiles().Count;
        var health = Features.Health.Count(_g);
        var configs = Features.CfgFile.Files(_g.Registry!).Count;

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        Button? active = null;

        Button Tab(string id, string text, string icon, string? badge, string? badgeClass = null)
        {
            var content = Ui.Row(8, Ui.Icon(icon, 16), new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            if (badge is not null)
                content.Children.Add(new Border { Classes = { "tab-badge" }, Child = Ui.Text(badge, "tiny") });
            if (badgeClass is not null && content.Children[^1] is Border bb) bb.Classes.Add(badgeClass);
            var b = new Button { Classes = { "gtab" }, Content = content };
            if (_tab == id) { b.Classes.Add("active"); active = b; }
            b.Click += (_, _) => Go(id);
            return b;
        }

        row.Children.Add(Tab("installed", I18n.T("tab.mods"), Icons.Package, _g.ModCount > 0 ? _g.ModCount.ToString() : null));
        if (updates > 0 && row.Children[^1] is Button { Content: StackPanel modsRow })
            modsRow.Children.Add(new Border { Classes = { "tab-badge", "good" }, Child = Ui.Text("↑" + updates, "tiny") });
        if (_g.Def.HasCatalog) row.Children.Add(Tab("catalog", I18n.T("games.market"), Icons.Bag, _total > 0 ? I18n.Compact(_total) : null));
        row.Children.Add(Tab("config", I18n.T("tab.config"), Icons.Sliders, configs > 0 ? configs.ToString() : null));
        row.Children.Add(Tab("profiles", I18n.T("games.profiles"), Icons.Layers, profiles > 0 ? profiles.ToString() : null));

        // «Ещё»: редкие вкладки. Если открыта одна из них — кнопка показывает её имя.
        var extra = new List<(string Id, string Text, string Icon, string? Badge)>
        {
            ("saves", I18n.T("games.saves"), Icons.Shield, saves > 0 ? saves.ToString() : null),
            ("tools", I18n.T("v4.tools"), Icons.Wrench, tools > 0 ? tools.ToString() : null),
        };
        if (shots > 0) extra.Add(("shots", I18n.T("shots.tab"), Icons.Image, shots.ToString()));
        extra.Add(("log", I18n.T("games.log"), Icons.Alert, null));
        extra.Add(("health", I18n.T("health.tab"), Icons.Activity, health > 0 ? health.ToString() : null));
        var current = extra.FirstOrDefault(e => e.Id == _tab);
        var moreContent = Ui.Row(8,
            Ui.Icon(current.Id is null ? Icons.More : current.Icon, 16),
            new TextBlock { Text = current.Id is null ? I18n.T("tab.more") : current.Text, VerticalAlignment = VerticalAlignment.Center },
            Ui.Icon(Icons.ChevronDown, 13));
        if (health > 0) moreContent.Children.Add(new Border { Classes = { "tab-badge", "bad" }, Child = Ui.Text(health.ToString(), "tiny") });
        var more = new Button { Classes = { "gtab" }, Content = moreContent };
        if (current.Id is not null) { more.Classes.Add("active"); active = more; }
        more.Click += (_, _) =>
        {
            var menu = Ctx.Menu(extra.Select(e => (object?)Ctx.Item(e.Badge is null ? e.Text : $"{e.Text}  ·  {e.Badge}", e.Icon, () => Go(e.Id))).ToArray());
            menu.Placement = PlacementMode.BottomEdgeAlignedLeft;
            menu.ShowAt(more);
        };
        row.Children.Add(more);

        // Индикатор под выбранной вкладкой: едет от прежнего места.
        var indicator = new Border
        {
            Classes = { "tab-ink" }, Height = 2.5, CornerRadius = new CornerRadius(2), Width = 0,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false,
        };
        var bar = new Panel { Children = { row, indicator } };
        var id = _g.Def.Id;
        void Place(bool animate)
        {
            if (active is null || active.Bounds.Width <= 0) return;
            var p = active.TranslatePoint(new Point(0, 0), row) ?? new Point();
            var target = (X: p.X + 10, W: Math.Max(0, active.Bounds.Width - 20));
            if (animate && Animate.On && TabMark.TryGetValue(id, out var was) && Math.Abs(was.X - target.X) > 1)
            {
                indicator.Transitions = null;
                indicator.Width = was.W;
                indicator.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse($"translateX({was.X.ToString(System.Globalization.CultureInfo.InvariantCulture)}px)");
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    indicator.Transitions =
                    [
                        new Avalonia.Animation.DoubleTransition { Property = Layoutable.WidthProperty, Duration = TimeSpan.FromMilliseconds(320), Easing = new Avalonia.Animation.Easings.CubicEaseOut() },
                        new Avalonia.Animation.TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(360), Easing = new Avalonia.Animation.Easings.BackEaseOut() },
                    ];
                    indicator.Width = target.W;
                    indicator.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse($"translateX({target.X.ToString(System.Globalization.CultureInfo.InvariantCulture)}px)");
                }, Avalonia.Threading.DispatcherPriority.Background);
            }
            else
            {
                indicator.Transitions = null;
                indicator.Width = target.W;
                indicator.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse($"translateX({target.X.ToString(System.Globalization.CultureInfo.InvariantCulture)}px)");
            }
            TabMark[id] = target;
        }
        var placed = false;
        row.LayoutUpdated += (_, _) =>
        {
            if (placed) return;
            if (active is null || active.Bounds.Width <= 0) return;
            placed = true;
            Place(animate: true);
        };
        row.SizeChanged += (_, _) => { if (placed) Place(animate: false); };

        return new Border
        {
            Classes = { "gtabs" },
            Child = new ScrollViewer
            {
                Content = bar,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
        };

        void Go(string tab) { if (_tab != tab) MainWindow.Current?.Navigate(() => new GamePage(_g.Def.Id, tab)); }
    }

    /// <summary>Содержимое вкладки поднимается волной: первые элементы по очереди.</summary>
    internal static void Stagger(Control body, int delay)
    {
        if (!Animate.On) return;
        var items = body is Panel p ? p.Children.ToList() : body is Border { Child: Panel inner } ? inner.Children.ToList() : [];
        if (items.Count == 0) { Animate.From(body, "translateY(14px)", 380, delay); return; }
        for (var i = 0; i < Math.Min(items.Count, 12); i++)
            Animate.From(items[i], "translateY(16px)", 420, delay + i * 45, new Avalonia.Animation.Easings.CubicEaseOut());
    }

    // ---------------------------------------------------------------- игра не найдена

    Control NotFoundView()
    {
        var searching = _g.Status == Detect.Searching;
        var col = Ui.Col(14,
            Ui.Text(I18n.T("games.notDetected"), "h2"),
            Ui.Text(I18n.T("games.notDetected.text", ("game", _g.Def.Name)), "muted", wrap: true));
        var buttons = Ui.Row(10,
            Ui.Button(I18n.T("games.setPath"), () => Actions.PickGameFolder(_g), "primary", Icons.Folder),
            Ui.Button(searching ? I18n.T("games.searching") : I18n.T("games.detectAgain"), () => _ = AppState.DetectOne(_g), "", Icons.Refresh),
            Ui.Button(I18n.T("games.deep"), () => _ = AppState.DetectOne(_g, deep: true), "ghost", Icons.Search));
        foreach (var b in buttons.Children.Skip(1)) b.IsEnabled = !searching;
        col.Children.Add(buttons);
        return Ui.Card(col, 26);
    }

    // ---------------------------------------------------------------- каталог

    Control CatalogView()
    {
        var col = new StackPanel { Spacing = 14 };
        // 9.0 «Витрина»: источники и разделы — колонкой слева, как фильтры в Modrinth.
        var side = Look.Vitrina ? new StackPanel { Spacing = 4 } : null;
        Control SideCaption(string text) => new TextBlock { Text = text.ToUpper(I18n.Culture), FontSize = 11.5, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.9, Foreground = Ui.Res("Faint"), Margin = new Thickness(12, 10, 0, 6) };
        Button SideItem(Button b)
        {
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.HorizontalContentAlignment = HorizontalAlignment.Left;
            b.Margin = new Thickness(0);
            b.Padding = new Thickness(12, 8);
            b.Classes.Add("side");
            return b;
        }

        // Источники: основной каталог и дополнительные (как в Vortex — моды с разных сайтов).
        var source = _source ?? _g.Def.PrimarySource;
        DockPanel? sourceDock = null; // в одну строку с источниками уходят переключатели «скрыть установленные», «только рабочие» и вид списка
        if (_g.Def.Sources.Length > 1 && side is not null)
        {
            if (_sourceTotals.Count < _g.Def.Sources.Length && !Program.Demo) _ = LoadSourceTotals();
            side.Children.Add(SideCaption(I18n.T("v4.source")));
            foreach (var src in _g.Def.Sources)
            {
                var label = Catalog.Title(src) + (_sourceTotals.TryGetValue(src, out var n) ? $" · {I18n.Compact(n)}" : "");
                var b = SideItem(Ui.Button(label, () => { _source = src; _section = "all"; _ = Load(reset: true); Build(); }, "chip"));
                if (src == source) b.Classes.Add("active");
                side.Children.Add(b);
            }
        }
        else if (_g.Def.Sources.Length > 1)
        {
            if (_sourceTotals.Count < _g.Def.Sources.Length && !Program.Demo) _ = LoadSourceTotals();
            var sources = Ui.Row(8, Ui.Text(I18n.T("v4.source"), "small muted"));
            sources.Children[0].VerticalAlignment = VerticalAlignment.Center;
            foreach (var src in _g.Def.Sources)
            {
                var label = Catalog.Title(src) + (_sourceTotals.TryGetValue(src, out var n) ? $" · {n:N0}" : "");
                var b = Ui.Button(label, () => { _source = src; _section = "all"; _ = Load(reset: true); Build(); }, "chip");
                if (src == source) b.Classes.Add("active");
                sources.Children.Add(b);
            }
            sourceDock = new DockPanel { LastChildFill = true };
            sourceDock.Children.Add(sources);
            col.Children.Add(sourceDock);
        }

        var chips = new WrapPanel();
        var primarySource = source == _g.Def.PrimarySource;
        side?.Children.Add(SideCaption(I18n.T("v9.sections")));
        foreach (var s in _g.Def.Sections)
        {
            if (!primarySource && s.Id is not ("all" or "best")) continue;
            if (s.Id == "packs" && _g.Def.Kits.Length == 0 && _g.Def.Catalog != CatalogKind.Nexus) continue;
            var b = Ui.Button(I18n.T("sec." + s.Id), () => SelectSection(s.Id), "chip", SectionIcon(s.Id));
            if (_section == s.Id) b.Classes.Add("active");
            b.Margin = new Thickness(0, 0, 8, 8);
            if (side is not null) side.Children.Add(SideItem(b));
            else chips.Children.Add(b);
        }
        if (side is null) col.Children.Add(chips);

        if (_section is not ("picks" or "packs"))
        {
            var search = new TextBox { Text = _query, Watermark = I18n.T("search.game", ("game", _g.Def.Name)), Height = 42 };
            search.InnerLeftContent = new Border { Padding = new Thickness(12, 0, 0, 0), Child = Ui.Icon(Icons.Search, 16, Ui.Res("Muted")) };
            if (_total > 0) search.InnerRightContent = new Border { Margin = new Thickness(0, 0, 10, 0), Background = Ui.Res("Surface3"), CornerRadius = new CornerRadius(999), Padding = new Thickness(8, 2), VerticalAlignment = VerticalAlignment.Center, Child = Ui.Text(_total.ToString("N0"), "small muted") };
            search.KeyDown += (_, e) => { if (e.Key == Key.Enter) { _query = search.Text ?? ""; _ = Load(reset: true); } };

            var sort = new ComboBox { Width = 200, Height = 42 };
            var sorts = new[] { SortBy.Popular, SortBy.Rating, SortBy.Updated, SortBy.New, SortBy.Name, SortBy.Random };
            foreach (var s in sorts) sort.Items.Add(I18n.T("sort." + s.ToString().ToLowerInvariant()));
            sort.SelectedIndex = Array.IndexOf(sorts, _sort);
            sort.SelectionChanged += (_, _) =>
            {
                if (sort.SelectedIndex < 0 || sorts[sort.SelectedIndex] == _sort) return;
                _sort = sorts[sort.SelectedIndex];
                _ = Load(reset: true);
            };

            // Период, как на Nexus: «обновлены за сутки / неделю / месяц / год».
            var periods = new[] { 0, 1, 7, 30, 365 };
            var period = new ComboBox { Width = 160, Height = 42 };
            foreach (var d in periods) period.Items.Add(I18n.T("period." + d));
            period.SelectedIndex = Array.IndexOf(periods, _period);
            period.SelectionChanged += (_, _) =>
            {
                if (period.SelectedIndex < 0 || periods[period.SelectedIndex] == _period) return;
                _period = periods[period.SelectedIndex];
                _ = Load(reset: true);
            };

            var adult = new ToggleButton { Classes = { "chip" }, Content = "18+", IsChecked = _adult, Height = 42, Padding = new Thickness(14, 0), VerticalContentAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(adult, I18n.T("catalog.adult"));
            adult.IsCheckedChanged += (_, _) =>
            {
                _adult = adult.IsChecked == true;
                Settings.Data["showAdult"] = _adult;
                Settings.Save();
                _ = Load(reset: true);
            };

            var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), ColumnSpacing = 10 };
            bar.Children.Add(search);
            Grid.SetColumn(sort, 1);
            bar.Children.Add(sort);
            Grid.SetColumn(period, 2);
            bar.Children.Add(period);
            Grid.SetColumn(adult, 3);
            bar.Children.Add(adult);
            col.Children.Add(bar);

            // Вторая строка, как в ModLaunch 3 и Modrinth: «скрыть установленные» и вид списком / сеткой.
            var hide = new ToggleSwitch { IsChecked = _hideInstalled, OnContent = "", OffContent = "", MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
            hide.IsCheckedChanged += (_, _) => { _hideInstalled = hide.IsChecked == true; Settings.Data["hideInstalled"] = _hideInstalled; Settings.Save(); RenderList(); };
            Button ViewButton(string id, string icon, string tip)
            {
                var b = Ui.Button("", () => { _view = id; Settings.Data["catalogView"] = id; Settings.Save(); Build(); }, _view == id ? "icon active" : "icon", icon, tip);
                return b;
            }
            var views = new Border { Classes = { "card" }, Margin = new Thickness(16, 0, 0, 0), Padding = new Thickness(3), CornerRadius = new CornerRadius(12), Child = Ui.Row(2, ViewButton("list", Icons.List, I18n.T("cat.list")), ViewButton("grid", Icons.Grid, I18n.T("cat.grid"))) };
            var second = new DockPanel();
            DockPanel.SetDock(views, Dock.Right);
            second.Children.Add(views);
            var hideLabel = Ui.Text(I18n.T("cat.hideInstalled"), "muted");
            hideLabel.VerticalAlignment = VerticalAlignment.Center;
            var hideRow = Ui.Row(10, hide, hideLabel);
            hideRow.VerticalAlignment = VerticalAlignment.Center;
            // «Только рабочие»: прячем моды для старой версии игры (есть там, где известно, когда игра сломала старые моды).
            if (Features.Compat.CanFilter(_g.Def))
            {
                var works = new ToggleSwitch { IsChecked = OnlyWorking, OnContent = "", OffContent = "", MinWidth = 0, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) };
                works.IsCheckedChanged += (_, _) => { Settings.Data["onlyWorkingMods"] = works.IsChecked == true; Settings.Save(); RenderList(); };
                var worksLabel = Ui.Text(I18n.T("compat.only"), "muted");
                worksLabel.VerticalAlignment = VerticalAlignment.Center;
                ToolTip.SetTip(worksLabel, I18n.T("compat.only.hint"));
                hideRow.Children.Add(works);
                hideRow.Children.Add(worksLabel);
            }
            second.Children.Add(hideRow);
            if (sourceDock is not null)
            {
                DockPanel.SetDock(second, Dock.Right);
                sourceDock.Children.Insert(0, second);
            }
            else col.Children.Add(second);
        }

        var host = new StackPanel { Spacing = 10 };
        _listHost = host;
        RenderList();
        // Если за это время страницу уже перестроили, этот список больше не нужен.
        if (ReferenceEquals(_listHost, host)) col.Children.Add(host);
        if (side is null) return col;
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("228,*"), ColumnSpacing = 28 };
        layout.Children.Add(side);
        Grid.SetColumn(col, 1);
        layout.Children.Add(col);
        return layout;
    }

    /// <summary>Открыть раздел каталога (для снимков экрана).</summary>
    public void ShowSection(string id) => SelectSection(id);

    void SelectSection(string id)
    {
        _section = id;
        if (id is not ("picks" or "packs")) _ = Load(reset: true);
        else _ = LoadPicks();
        Build();
    }

    bool IsInstalled(ModInfo mod) => Actions.IsInstalled(_g, mod.Id);

    /// <summary>Значки разделов, как в ModLaunch 3.</summary>
    static string? SectionIcon(string id) => id switch
    {
        "picks" => Icons.Trophy,
        "best" => Icons.Star,
        "packs" or "modpacks" => Icons.Layers,
        "buildings" => Icons.Home,
        "vehicles" => Icons.Car,
        "items" => Icons.Wrench,
        "gameplay" => Icons.Gamepad,
        "content" => Icons.Package,
        "visuals" => Icons.Image,
        "cosmetics" => Icons.Palette,
        "audio" => Icons.Music,
        "ui" => Icons.Sidebar,
        "tools" => Icons.Settings,
        _ => null,
    };

    /// <summary>«Хит» — тройка самых скачиваемых, «Лучшее» — тройка по оценкам, «Новое» — вышло за неделю.</summary>
    string? BadgeFor(ModInfo mod, int index)
    {
        if (_query != "") return null;
        if (_sort == SortBy.Popular && index < 3) return "hit";
        if (_sort == SortBy.Rating && index < 3) return "best";
        if (mod.UpdatedAt is { } d && d > DateTime.UtcNow.AddDays(-7) && _sort is SortBy.New) return "new";
        return null;
    }
    bool IsInstalling(ModInfo mod) => Actions.IsBusy(_g, mod.Id);

    void RenderList()
    {
        if (_listHost is null) return;
        _listHost.Children.Clear();

        if (_section == "picks") { RenderPicks(); return; }
        if (_section == "packs") { RenderKits(); _listHost.Children.Add(CollectionsBlock()); return; }
        if (_section == "modpacks" && _g.Def.Catalog == CatalogKind.Thunderstore) _listHost.Children.Add(Ui.Card(Ui.Text(I18n.T("packs.intro"), "muted", wrap: true), 16));
        if (_section == "visuals") _listHost.Children.Add(ReShadeCard());

        if (_error is not null && _mods.Count == 0)
        {
            _listHost.Children.Add(Ui.Card(Ui.Col(10, Ui.Text(I18n.T("catalog.error"), "h3"), Ui.Text(_error, "muted small", wrap: true),
                Ui.Button(I18n.T("common.retry"), () => _ = Load(reset: true), "primary", Icons.Refresh)), 22));
            return;
        }
        if (_loading && _mods.Count == 0)
        {
            for (var i = 0; i < 5; i++) _listHost.Children.Add(Skeleton());
            return;
        }
        if (!_loading && _mods.Count == 0)
        {
            _listHost.Children.Add(Ui.Card(Ui.Text(_query == "" ? I18n.T("inst.empty") : I18n.T("catalog.nothingFound", ("query", _query)), "muted"), 22));
            return;
        }

        if (FeedShelf() is { } feed) _listHost.Children.Add(feed);
        var picks = _g.Def.Picks.ToHashSet();
        var shown = _hideInstalled ? _mods.Where(m => !IsInstalled(m)).ToList() : _mods;
        if (OnlyWorking && Features.Compat.CanFilter(_g.Def)) shown = shown.Where(m => Features.Compat.Works(_g.Def, m)).ToList();
        var tiles = _view == "grid" ? new WrapPanel() : null;
        if (tiles is not null) _listHost.Children.Add(tiles);
        for (var i = 0; i < shown.Count; i++)
        {
            var mod = shown[i];
            var badge = BadgeFor(mod, i);
            void Install() => _ = Actions.Install(_g, mod);
            void Open() => MainWindow.Current?.Navigate(() => new ModPage(_g.Def.Id, mod));
            if (tiles is not null) tiles.Children.Add(ModRow.Tile(_g.Def, mod, IsInstalled(mod), IsInstalling(mod), Install, Open, badge));
            else _listHost.Children.Add(ModRow.Build(_g.Def, mod, IsInstalled(mod), IsInstalling(mod), picks.Contains(mod.Id), Install, Open, badge));
        }

        if (_hasMore)
        {
            var more = Ui.Button(_loading ? I18n.T("catalog.loading") : I18n.T("catalog.more"), () => _ = Load(reset: false), "", Icons.Refresh);
            more.IsEnabled = !_loading;
            more.HorizontalAlignment = HorizontalAlignment.Center;
            _listHost.Children.Add(more);
        }
    }

    internal static Control Skeleton() => new Border
    {
        Classes = { "card", "shimmer" },
        Height = 118,
        Child = new Border { Width = 88, Height = 88, Margin = new Thickness(14), CornerRadius = new CornerRadius(14), Background = Ui.Res("Surface2"), HorizontalAlignment = HorizontalAlignment.Left },
    };

    /// <summary>Сколько модов в каждом каталоге игры — для подписей у переключателя.</summary>
    async Task LoadSourceTotals()
    {
        foreach (var src in _g.Def.Sources)
        {
            if (_sourceTotals.ContainsKey(src)) continue;
            _sourceTotals[src] = 0;
            try { _sourceTotals[src] = (await Catalog.Browse(_g.Def, new Query(), source: src)).Total; } catch { }
        }
        if (_tab == "catalog") Build();
    }

    async Task Load(bool reset)
    {
        var id = ++_requestId;
        if (reset) { _mods.Clear(); _page = 1; _hasMore = false; }
        else _page++;
        _loading = true;
        _error = null;
        RenderList();
        try
        {
            var page = Program.Demo
                ? Demo.Catalog(_g.Def, new Query(_query, _page, _sort, _section, _period, _adult))
                : await Catalog.Browse(_g.Def, new Query(_query, _page, _sort, _section, _period, _adult), source: _source);
            if (id != _requestId) return;
            _mods.AddRange(page.Mods.Where(m => _mods.All(x => x.Id != m.Id)));
            _total = page.Total;
            _hasMore = page.HasMore;
        }
        catch (Exception e)
        {
            if (id != _requestId) return;
            _error = Jobs.Explain(e);
        }
        _loading = false;
        Build();
    }

    async Task LoadPicks()
    {
        // Сначала дать дорисоваться текущему экрану: иначе Build() изнутри RenderList
        // перестраивал страницу посреди сборки и каталог оставался пустым.
        await Task.Yield();
        if (_picks is not null) { RenderList(); return; }
        try
        {
            var ids = _g.Def.Picks.Concat(_g.Def.Kits.SelectMany(k => k.Mods));
            _picks = Program.Demo ? Demo.Many(_g.Def, ids) : await Catalog.Many(_g.Def, ids);
        }
        catch (Exception e) { _error = Jobs.Explain(e); _picks = []; }
        Build();
    }

    void RenderPicks()
    {
        if (_picks is null)
        {
            _ = LoadPicks();
            for (var i = 0; i < 4; i++) _listHost!.Children.Add(Skeleton());
            return;
        }
        var byId = _picks.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var id in _g.Def.Picks)
            if (byId.TryGetValue(id, out var mod))
                _listHost!.Children.Add(ModRow.Build(_g.Def, mod, IsInstalled(mod), IsInstalling(mod), true, () => _ = Actions.Install(_g, mod), () => MainWindow.Current?.Navigate(() => new ModPage(_g.Def.Id, mod))));
        if (_listHost!.Children.Count == 0)
            _listHost.Children.Add(Ui.Card(Ui.Text(_error ?? I18n.T("catalog.error"), "muted", wrap: true), 22));
    }

    void RenderKits()
    {
        if (_picks is null)
        {
            _ = LoadPicks();
            for (var i = 0; i < 3; i++) _listHost!.Children.Add(Skeleton());
            return;
        }
        var byId = _picks.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var kit in _g.Def.Kits)
        {
            var mods = kit.Mods.Select(id => byId.GetValueOrDefault(id)).OfType<ModInfo>().ToList();
            var missing = mods.Where(m => !IsInstalled(m)).ToList();
            var icons = Ui.Row(-10, mods.Take(7).Select(m => (Control)new Border { BorderBrush = Ui.Res("Surface"), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(12), Child = Ui.Thumb(m.Icon, m.Name, 40, 10, 80) }).ToArray());
            var n = kit.Mods.Length;
            var info = Ui.Col(6,
                Ui.Text(I18n.T($"kit.{kit.Id}.title"), "h3"),
                Ui.Text(I18n.T($"kit.{kit.Id}.text"), "muted", wrap: true),
                Ui.Text(I18n.T("kit.mods." + I18n.Plural(n, "one", "few", "many"), ("n", n)), "small brand"),
                icons);
            var button = missing.Count == 0
                ? Ui.Button(I18n.T("kit.have"), () => { }, "", Icons.Check)
                : Ui.Button(I18n.T("mod.install"), () => InstallKit(kit, missing), "primary", Icons.Download);
            button.IsEnabled = missing.Count > 0;
            button.VerticalAlignment = VerticalAlignment.Center;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16 };
            grid.Children.Add(info);
            Grid.SetColumn(button, 1);
            grid.Children.Add(button);
            _listHost!.Children.Add(Ui.Card(grid, 20));
        }
    }

    void InstallKit(Kit kit, List<ModInfo> missing)
    {
        var w = MainWindow.Current!;
        var title = I18n.T($"kit.{kit.Id}.title");
        w.Dialog(I18n.T("kit.confirm", ("name", title)),
            Ui.Text(I18n.T("kit.confirm.text", ("n", missing.Count)), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("mod.install"), () =>
            {
                w.CloseDialog();
                _ = Actions.InstallQueue(_g, title, missing.Select(m => (m, (Pin?)null)).ToList());
            }, "primary", Icons.Download));
    }
}
