using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>
/// Страница игры 9.2 «Store» — как страница приложения в Microsoft Store. Всё стоит по одной
/// сетке страницы (StoreKit.Column): слева обложка, рядом название, состояние, цифры и «Играть»;
/// ниже — вкладки с полоской акцента, и под ними содержимое той же ширины.
/// Новая вкладка «Обзор» собирает главное: что сделать сейчас, установленные моды,
/// рекомендации и сведения об игре.
/// </summary>
public sealed partial class GamePage
{
    List<ModInfo>? _popular;
    bool _popularLoading;

    void BuildStore()
    {
        var page = new StackPanel();
        page.Children.Add(StoreHeader());
        var content = StoreKit.Column(spacing: 22, top: 0);
        if (_g.Status == Detect.Found)
        {
            content.Children.Add(StorePivot());
            if (PurgeBanner() is { } banner) content.Children.Add(banner);
            var body = _tab switch
            {
                "installed" => InstalledView(),
                "catalog" => CatalogView(),
                "profiles" => ProfilesView(),
                "saves" => SavesView(),
                "log" => LogView(),
                "tools" => ToolsView(),
                "health" => HealthView(),
                "config" => ConfigView(),
                "shots" => ShotsView(),
                _ => OverviewView(),
            };
            content.Children.Add(body);
            if (!Shown) Stagger(body, TabSwitch ? 0 : 160);
        }
        else
        {
            content.Margin = new Thickness(content.Margin.Left, 6, content.Margin.Right, content.Margin.Bottom);
            content.Children.Add(NotFoundStore());
        }
        page.Children.Add(content);

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

    // ---------------------------------------------------------------- шапка «страницы приложения»

    Control StoreHeader()
    {
        var found = _g.Status == Detect.Found;
        var running = Features.Launcher.IsRunning(_g.Def.Id);
        var played = Features.PlayTime.Get(_g.Def.Id);
        // На «Обзоре» шапка большая; на остальных вкладках — компактная, чтобы список был сразу виден.
        var compact = found && _tab != "overview";

        // Обложка 2:3 с тенью — как значок приложения в Store, только крупнее.
        var cover = new Border
        {
            Width = compact ? 88 : 168, Height = compact ? 132 : 252, CornerRadius = new CornerRadius(compact ? 8 : 10), ClipToBounds = true,
            BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1),
            Child = Ui.GameImage(_g.Def, 400, art: Images.Art.Cover),
        };
        var coverHost = new Border { CornerRadius = new CornerRadius(10), BoxShadow = BoxShadows.Parse("0 18 40 -10 #A0000000"), Child = cover, VerticalAlignment = VerticalAlignment.Bottom };

        var name = new TextBlock { Text = _g.Def.Name, FontSize = compact ? 26 : 34, FontWeight = FontWeight.Bold, LetterSpacing = -0.8, TextWrapping = TextWrapping.Wrap, MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };

        // Строка состояния: загрузчик и папка игры (по ней — открыть).
        var (dot, stateText) = _g.Status switch
        {
            Detect.Found when _g.Def.Loader == LoaderKind.None => (Ui.Res("Good"), I18n.T("add.noLoader", ("folder", _g.Def.ModsFolder))),
            Detect.Found when _g.LoaderInstalled => (Ui.Res("Good"), I18n.T("games.loaderReady", ("loader", _g.Def.LoaderName))),
            Detect.Found => (Ui.Res("Warn"), I18n.T("games.loaderMissing", ("loader", _g.Def.LoaderName))),
            Detect.Searching => (Ui.Res("Muted"), _g.SearchingWhere is null ? I18n.T("games.searching") : I18n.T("games.searchingWhere", ("where", _g.SearchingWhere))),
            _ => (Ui.Res("Faint"), I18n.T("games.notDetected")),
        };
        var status = Ui.Row(8, Ui.Dot(dot), Ui.Text(stateText, color: Ui.Res("Text")));
        if (_g.Path is not null)
        {
            var path = new Button
            {
                Classes = { "ghost" }, Padding = new Thickness(6, 2), Margin = new Thickness(6, 0, 0, 0),
                Content = Ui.Row(6, Ui.Icon(Icons.Folder, 13, Ui.Res("Muted")), new TextBlock { Text = Ui.ShortPath(_g.Path), FontSize = 13, Foreground = Ui.Res("Muted"), VerticalAlignment = VerticalAlignment.Center }),
            };
            ToolTip.SetTip(path, _g.Path);
            path.Click += (_, _) => Actions.OpenFolder(_g.Path);
            status.Children.Add(path);
        }

        // Цифры, как «4,8★ · 899 оценок» в Store: моды, обновления, каталог, время.
        var facts = new WrapPanel();
        void Fact(string value, string label)
        {
            if (facts.Children.Count > 0) facts.Children.Add(new Border { Width = 1, Height = 30, Background = Ui.Res("Line"), Margin = new Thickness(18, 0), VerticalAlignment = VerticalAlignment.Center });
            facts.Children.Add(Ui.Col(1,
                new TextBlock { Text = value, FontSize = 17, FontWeight = FontWeight.Bold },
                new TextBlock { Text = label, FontSize = 12, Foreground = Ui.Res("Muted") }));
        }
        if (found)
        {
            Fact(_g.ModCount.ToString(), I18n.T("v92.game.mods"));
            var updates = Features.ModUpdates.Found.TryGetValue(_g.Def.Id, out var up) ? up.Count : 0;
            if (updates > 0) Fact(updates.ToString(), I18n.T("v92.game.updates"));
            if (_total > 0) Fact(I18n.Compact(_total), I18n.T("v92.game.catalog"));
            if (played.TotalMs > 0) Fact(Features.PlayTime.Format(played.TotalMs), I18n.T("v92.game.played"));
            if (played.LastPlayed is not null) Fact(Ui.Ago(played.LastPlayed), I18n.T("v92.game.last"));
        }

        // Кнопки: «Играть» (или «Поставить загрузчик»), папка и «⋯».
        var buttons = Ui.Row(10);
        if (found)
        {
            if (running)
            {
                buttons.Children.Add(PlayControls.RunningPill(_g.Def.Id, onArt: false));
                var stop = Ui.Button(I18n.T("v4.stop"), () => { Features.Launcher.Stop(_g.Def.Id); MainWindow.Current?.Toast(I18n.T("v4.stopped")); }, "", Icons.Stop);
                stop.Padding = new Thickness(20, 13);
                buttons.Children.Add(stop);
            }
            else if (_g.LoaderInstalled || _g.Def.Loader == LoaderKind.None) buttons.Children.Add(PlayControls.PlayButton(_g));
            else
            {
                var busy = Jobs.All.Any(j => j.Active && j.Title == _g.Def.LoaderName && j.GameName == _g.Def.Name);
                var install = Ui.Button(busy ? I18n.T("aside.installing") : I18n.T("games.installLoader", ("loader", _g.Def.LoaderName)), () => Actions.InstallLoader(_g), "primary", Icons.Download);
                install.IsEnabled = !busy;
                install.FontSize = 15;
                install.Padding = new Thickness(26, 13);
                buttons.Children.Add(install);
            }
            var folder = Ui.Button(I18n.T("games.openFolder"), () => Actions.OpenFolder(_g.Path), "", Icons.Folder);
            folder.Padding = new Thickness(18, 13);
            buttons.Children.Add(folder);
        }
        else
        {
            var searching = _g.Status == Detect.Searching;
            var pick = Ui.Button(I18n.T("games.setPath"), () => Actions.PickGameFolder(_g), "primary", Icons.Folder);
            pick.Padding = new Thickness(24, 13);
            var again = Ui.Button(searching ? I18n.T("games.searching") : I18n.T("games.detectAgain"), () => _ = AppState.DetectOne(_g), "", Icons.Refresh);
            again.Padding = new Thickness(18, 13);
            again.IsEnabled = !searching;
            buttons.Children.Add(pick);
            buttons.Children.Add(again);
        }
        var more = Ui.Button("", () => { }, "icon", Icons.More, I18n.T("top.more"));
        more.Width = more.Height = 46;
        more.Flyout = GameCard.Menu(_g);
        buttons.Children.Add(more);

        var info = Ui.Col(compact ? 10 : 14, name, status);
        if (facts.Children.Count > 0 && !compact) info.Children.Add(facts);
        info.Children.Add(buttons);
        info.VerticalAlignment = VerticalAlignment.Bottom;
        info.Margin = new Thickness(0, 0, 0, 4);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 30, MaxWidth = StoreKit.MaxWidth,
            Margin = new Thickness(StoreKit.Gutter, compact ? 24 : 40, StoreKit.Gutter, compact ? 20 : 26),
        };
        if (compact)
        {
            // В компактной шапке кнопки — справа, на одной линии с названием.
            info.Children.Remove(buttons);
            buttons.VerticalAlignment = VerticalAlignment.Center;
            grid.ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto");
            Grid.SetColumn(buttons, 2);
            grid.Children.Add(buttons);
            info.VerticalAlignment = VerticalAlignment.Center;
            coverHost.VerticalAlignment = VerticalAlignment.Center;
        }
        grid.Children.Add(coverHost);
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);

        // Арт игры — справа сверху и растворяется в цвет листа, как на странице приложения в Store.
        var hero = Ui.GameImage(_g.Def, 1600, art: Images.Art.Hero);
        var layer = Ui.Res("Layer") is SolidColorBrush lb ? lb.Color : Color.Parse("#1E1E22");
        Color L(byte a) => Color.FromArgb(a, layer.R, layer.G, layer.B);
        var art = new FillLayer
        {
            IsHitTestVisible = false,
            Children =
            {
                hero,
                new Border { Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(L(255), 0), new GradientStop(L(235), 0.28), new GradientStop(L(110), 0.62), new GradientStop(L(40), 1) } } },
                new Border { Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(L(60), 0), new GradientStop(L(120), 0.55), new GradientStop(L(255), 1) } } },
            },
        };
        if (!Shown && !TabSwitch)
        {
            // Вход на страницу игры: арт мягко «отъезжает», обложка и надписи выезжают.
            Animate.From(hero, "scale(1.1)", 1400, 0, new Avalonia.Animation.Easings.QuadraticEaseOut(), 1);
            Animate.From(coverHost, "translateY(24px) scale(0.94)", 560, 60, new Avalonia.Animation.Easings.BackEaseOut());
            Animate.From(info, "translateX(-22px)", 520, 140);
        }
        return new Panel { ClipToBounds = true, Children = { art, grid } };
    }

    // ---------------------------------------------------------------- вкладки

    /// <summary>
    /// Вкладки-«пивот» как в Windows 11: подпись, число, полоска акцента под выбранной.
    /// Главные — всегда на виду, редкие — в «Ещё».
    /// </summary>
    Control StorePivot()
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
        Button Tab(string id, string text, string? badge, string? badgeClass = null)
        {
            var content = Ui.Row(7, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            if (badge is not null)
            {
                var chip = new Border { Classes = { "tab-badge" }, Child = Ui.Text(badge, "tiny") };
                if (badgeClass is not null) chip.Classes.Add(badgeClass);
                content.Children.Add(chip);
            }
            var b = new Button { Classes = { "pivot" }, Content = content };
            if (_tab == id) { b.Classes.Add("active"); active = b; }
            b.Click += (_, _) => Go(id);
            return b;
        }
        row.Children.Add(Tab("overview", I18n.T("v92.tab.overview"), null));
        var mods = Tab("installed", I18n.T("tab.mods"), _g.ModCount > 0 ? _g.ModCount.ToString() : null);
        if (updates > 0 && mods.Content is StackPanel modsRow) modsRow.Children.Add(new Border { Classes = { "tab-badge", "good" }, Child = Ui.Text("↑" + updates, "tiny") });
        row.Children.Add(mods);
        if (_g.Def.HasCatalog) row.Children.Add(Tab("catalog", I18n.T("games.market"), _total > 0 ? I18n.Compact(_total) : null));
        row.Children.Add(Tab("config", I18n.T("tab.config"), configs > 0 ? configs.ToString() : null));
        row.Children.Add(Tab("profiles", I18n.T("games.profiles"), profiles > 0 ? profiles.ToString() : null));

        var extra = new List<(string Id, string Text, string Icon, string? Badge)>
        {
            ("saves", I18n.T("games.saves"), Icons.Shield, saves > 0 ? saves.ToString() : null),
            ("tools", I18n.T("v4.tools"), Icons.Wrench, tools > 0 ? tools.ToString() : null),
        };
        if (shots > 0) extra.Add(("shots", I18n.T("shots.tab"), Icons.Image, shots.ToString()));
        extra.Add(("log", I18n.T("games.log"), Icons.Alert, null));
        extra.Add(("health", I18n.T("health.tab"), Icons.Activity, health > 0 ? health.ToString() : null));
        var current = extra.FirstOrDefault(e => e.Id == _tab);
        var moreContent = Ui.Row(7, new TextBlock { Text = current.Id is null ? I18n.T("tab.more") : current.Text, VerticalAlignment = VerticalAlignment.Center }, Ui.Icon(Icons.ChevronDown, 13));
        if (health > 0) moreContent.Children.Add(new Border { Classes = { "tab-badge", "bad" }, Child = Ui.Text(health.ToString(), "tiny") });
        var more = new Button { Classes = { "pivot" }, Content = moreContent };
        if (current.Id is not null) { more.Classes.Add("active"); active = more; }
        more.Click += (_, _) =>
        {
            var menu = Ctx.Menu(extra.Select(e => (object?)Ctx.Item(e.Badge is null ? e.Text : $"{e.Text}  ·  {e.Badge}", e.Icon, () => Go(e.Id))).ToArray());
            menu.Placement = PlacementMode.BottomEdgeAlignedLeft;
            menu.ShowAt(more);
        };
        row.Children.Add(more);

        // Полоска под выбранной вкладкой — переезжает от прежней.
        var indicator = new Border
        {
            Classes = { "tab-ink" }, Height = 3, CornerRadius = new CornerRadius(2), Width = 0,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false,
        };
        var bar = new Panel { Children = { row, indicator } };
        var id = _g.Def.Id;
        void Place(bool animate)
        {
            if (active is null || active.Bounds.Width <= 0) return;
            var p = active.TranslatePoint(new Point(0, 0), row) ?? new Point();
            var target = (X: p.X + 14, W: Math.Max(0, active.Bounds.Width - 28));
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (animate && Animate.On && TabMark.TryGetValue(id, out var was) && Math.Abs(was.X - target.X) > 1)
            {
                indicator.Transitions = null;
                indicator.Width = was.W;
                indicator.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse($"translateX({was.X.ToString(inv)}px)");
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    indicator.Transitions =
                    [
                        new Avalonia.Animation.DoubleTransition { Property = Layoutable.WidthProperty, Duration = TimeSpan.FromMilliseconds(320), Easing = new Avalonia.Animation.Easings.CubicEaseOut() },
                        new Avalonia.Animation.TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(360), Easing = new Avalonia.Animation.Easings.BackEaseOut() },
                    ];
                    indicator.Width = target.W;
                    indicator.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse($"translateX({target.X.ToString(inv)}px)");
                }, Avalonia.Threading.DispatcherPriority.Background);
            }
            else
            {
                indicator.Transitions = null;
                indicator.Width = target.W;
                indicator.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse($"translateX({target.X.ToString(inv)}px)");
            }
            TabMark[id] = target;
        }
        var placed = false;
        row.LayoutUpdated += (_, _) =>
        {
            if (placed || active is null || active.Bounds.Width <= 0) return;
            placed = true;
            Place(animate: true);
        };
        row.SizeChanged += (_, _) => { if (placed) Place(animate: false); };

        return new Border
        {
            BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 0, 0, 4),
            Child = new ScrollViewer { Content = bar, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled },
        };

        void Go(string tab) { if (_tab != tab) MainWindow.Current?.Navigate(() => new GamePage(_g.Def.Id, tab)); }
    }

    // ---------------------------------------------------------------- «Обзор»

    Control OverviewView()
    {
        var registry = _g.Registry!;
        var left = new StackPanel { Spacing = 28 };

        // 1. Что сделать сейчас: загрузчик, обновления, недостающие зависимости.
        var todo = new StackPanel { Spacing = 10 };
        if (!_g.LoaderInstalled && _g.Def.Loader is not (LoaderKind.None or LoaderKind.Minecraft))
        {
            var busy = Jobs.All.Any(j => j.Active && j.Title == _g.Def.LoaderName && j.GameName == _g.Def.Name);
            var install = Ui.Button(busy ? I18n.T("aside.installing") : I18n.T("games.installLoader", ("loader", _g.Def.LoaderName)), () => Actions.InstallLoader(_g), "primary", Icons.Download);
            install.IsEnabled = !busy;
            todo.Children.Add(Callout(Icons.Zap, Ui.Res("Warn"), I18n.T("v92.ov.loader", ("loader", _g.Def.LoaderName)), I18n.T("v92.ov.loader.text", ("loader", _g.Def.LoaderName)), install));
        }
        if (Features.ModUpdates.Found.TryGetValue(_g.Def.Id, out var updates) && updates.Count > 0) todo.Children.Add(UpdatesCard(updates));
        var missing = _missing ?? Features.Deps.Find(registry);
        if (missing.Count > 0)
        {
            Control? fix = missing.Any(m => m.ResolveId is not null) ? Ui.Button(I18n.T("deps.install"), () => _ = Actions.InstallMissing(_g), "primary", Icons.Download) : null;
            todo.Children.Add(Callout(Icons.Alert, Ui.Res("Warn"), I18n.T("v92.ov.deps"),
                I18n.T("inst.problems", ("list", string.Join(", ", missing.Select(m => m.Name).Distinct().Take(6)))), fix));
        }
        if (todo.Children.Count > 0) left.Children.Add(todo);

        // 2. Установленные моды — первые шесть, остальное на вкладке «Моды».
        var list = registry.List();
        var installed = new StackPanel { Spacing = 8 };
        if (list.Count == 0)
        {
            var open = Ui.Button(I18n.T("v92.ov.empty.open"), () => MainWindow.Current?.Navigate(() => new GamePage(_g.Def.Id, "catalog")), "primary", Icons.Bag);
            open.IsVisible = _g.Def.HasCatalog;
            var file = Ui.Button(I18n.T("inst.fromFile"), () => Actions.InstallFromFile(_g), "", Icons.FilePlus);
            installed.Children.Add(Ui.Card(Ui.Col(10, Ui.Text(I18n.T("v92.ov.empty"), "h3"), Ui.Text(I18n.T("v92.ov.empty.text"), "muted", wrap: true), Ui.Row(10, open, file)), 22));
        }
        else
        {
            var byRecord = updates?.ToDictionary(u => u.RecordId) ?? [];
            var top = list.OrderByDescending(m => DateTime.TryParse(m.Str("installedAt"), out var at) ? at : DateTime.MinValue).Take(6);
            foreach (var mod in top) installed.Children.Add(InstalledRow(registry, mod, byRecord.GetValueOrDefault(mod.Str("id")!)));
        }
        left.Children.Add(Ui.Col(12,
            StoreKit.Header(I18n.T("v92.ov.installed"), list.Count > 0 ? () => MainWindow.Current?.Navigate(() => new GamePage(_g.Def.Id, "installed")) : null, count: list.Count > 0 ? list.Count.ToString() : null),
            installed));

        // 3. Рекомендуемые моды: «Выбор ModLaunch» или популярные в каталоге.
        if (_g.Def.HasExternalCatalog)
        {
            if (_popular is null && !_popularLoading) _ = LoadOverviewMods();
            var picks = (_popular ?? []).Where(m => !Actions.IsInstalled(_g, m.Id)).Take(8).ToList();
            Control body = _popular is null
                ? StoreKit.ModGrid(Enumerable.Range(0, 4).Select(_ => StoreKit.Placeholder(double.NaN, 72)), 2)
                : picks.Count > 0 ? StoreKit.ModGrid(picks.Select(m => StoreKit.ModItem(_g, m, showGame: false)), 2)
                : Ui.Text(I18n.T("v92.ov.allSet"), "muted");
            left.Children.Add(Ui.Col(10, StoreKit.Header(I18n.T("v92.ov.recommended"), () => MainWindow.Current?.Navigate(() => new GamePage(_g.Def.Id, "catalog"))), body));
        }

        // Справа: сведения об игре и быстрые действия.
        var right = new StackPanel { Spacing = 16 };
        right.Children.Add(AboutCard());
        right.Children.Add(QuickActions());

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,340"), ColumnSpacing = 28 };
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    async Task LoadOverviewMods()
    {
        _popularLoading = true;
        try
        {
            // Сначала «Выбор ModLaunch» (подобран вручную), потом — самые популярные в каталоге.
            var picks = _g.Def.Picks.Length > 0 ? (Program.Demo ? Demo.Many(_g.Def, _g.Def.Picks) : await Catalog.Many(_g.Def, _g.Def.Picks)) : [];
            var popular = await Views.Aside.PopularAsync(_g);
            _popular = picks.Concat(popular.Where(p => picks.All(x => x.Id != p.Id))).ToList();
        }
        catch { _popular = []; }
        if (_tab == "overview") Build();
    }

    /// <summary>Карточка «что сделать»: значок, заголовок, пояснение и кнопка справа.</summary>
    static Control Callout(string icon, IBrush color, string title, string text, Control? action)
    {
        var words = Ui.Col(3, Ui.Text(title, "h3"), Ui.Text(text, "muted small", wrap: true));
        words.VerticalAlignment = VerticalAlignment.Center;
        var badge = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(10), Background = Ui.Res("Surface2"), Child = Ui.Icon(icon, 18, color), VerticalAlignment = VerticalAlignment.Center };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 14 };
        grid.Children.Add(badge);
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);
        if (action is not null)
        {
            action.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(action, 2);
            grid.Children.Add(action);
        }
        return new Border { Classes = { "card" }, Padding = new Thickness(14), BorderBrush = color, Child = grid };
    }

    /// <summary>«Об игре»: где лежит, загрузчик, моды, каталоги, время в игре.</summary>
    Control AboutCard()
    {
        var list = _g.Registry?.List() ?? [];
        var enabled = list.Count(m => m.Bool("enabled", true) && !m.Bool("missing"));
        var played = Features.PlayTime.Get(_g.Def.Id);
        var rows = Ui.Col(10);
        void Row(string key, string value, IBrush? color = null, Action? open = null)
        {
            var v = new TextBlock { Text = value, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = color ?? Ui.Res("Text"), TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Right, MaxWidth = 190 };
            if (open is not null) { v.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand); v.PointerPressed += (_, _) => open(); v.Foreground = Ui.Res("Brand2"); }
            var row = new DockPanel();
            DockPanel.SetDock(v, Dock.Right);
            row.Children.Add(v);
            row.Children.Add(new TextBlock { Text = key, FontSize = 13, Foreground = Ui.Res("Muted") });
            rows.Children.Add(row);
        }
        if (_g.Path is not null) Row(I18n.T("v92.about.folder"), Ui.ShortPath(_g.Path), null, () => Actions.OpenFolder(_g.Path));
        Row(I18n.T("aside.loader"), _g.Def.Loader == LoaderKind.None ? "—" : _g.Def.LoaderName, _g.LoaderInstalled || _g.Def.Loader == LoaderKind.None ? Ui.Res("Good") : Ui.Res("Warn"));
        Row(I18n.T("v92.game.mods"), list.Count == 0 ? "0" : $"{enabled} / {list.Count}");
        if (_g.Def.Sources.Length > 0) Row(I18n.T("v92.about.sources"), string.Join(", ", _g.Def.Sources.Select(Catalog.Title)));
        Row(I18n.T("aside.playtime"), played.TotalMs > 0 ? Features.PlayTime.Format(played.TotalMs) : "—");
        Row(I18n.T("aside.lastPlayed"), played.LastPlayed is null ? "—" : Ui.Ago(played.LastPlayed));
        if (_g.Def.Engine is { } engine) Row(I18n.T("v92.about.engine"), engine);
        return new Border { Classes = { "card" }, Padding = new Thickness(18), Child = Ui.Col(14, Ui.Text(I18n.T("v92.about.title"), "h3"), rows) };
    }

    /// <summary>Быстрые действия: редкие вкладки и обслуживание игры — одним списком.</summary>
    Control QuickActions()
    {
        var col = Ui.Col(2);
        void Item(string icon, string text, Action run)
        {
            var b = new Button
            {
                Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 9),
                Content = Ui.Row(12, Ui.Icon(icon, 16, Ui.Res("Muted")), new TextBlock { Text = text, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center }),
            };
            b.Click += (_, _) => run();
            col.Children.Add(b);
        }
        void Tab(string icon, string text, string tab) => Item(icon, text, () => MainWindow.Current?.Navigate(() => new GamePage(_g.Def.Id, tab)));
        Tab(Icons.Activity, I18n.T("health.tab"), "health");
        Tab(Icons.Shield, I18n.T("games.saves"), "saves");
        Tab(Icons.Wrench, I18n.T("v4.tools"), "tools");
        if (ShotFiles().Count > 0) Tab(Icons.Image, I18n.T("shots.tab"), "shots");
        Tab(Icons.Alert, I18n.T("games.log"), "log");
        Item(Icons.FilePlus, I18n.T("inst.fromFile"), () => Actions.InstallFromFile(_g));
        Item(Icons.Refresh, I18n.T("aside.rescan"), () => _ = AppState.DetectOne(_g, deep: true));
        return new Border { Classes = { "card" }, Padding = new Thickness(10, 14), Child = Ui.Col(8, new Border { Padding = new Thickness(8, 0), Child = Ui.Text(I18n.T("v92.quick.title"), "h3") }, col) };
    }

    /// <summary>Игра не найдена: что сделать — по шагам, с картинкой.</summary>
    Control NotFoundStore()
    {
        var searching = _g.Status == Detect.Searching;
        var steps = Ui.Col(12);
        var n = 1;
        foreach (var key in new[] { "v92.nf.step1", "v92.nf.step2", "v92.nf.step3" })
        {
            var num = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = Ui.Res("BrandSoft"), Child = new TextBlock { Text = (n++).ToString(), FontWeight = FontWeight.Bold, Foreground = Ui.Res("Brand2"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            var text = Ui.Text(I18n.T(key, ("game", _g.Def.Name), ("set", I18n.T("games.setPath")), ("deep", I18n.T("games.deep"))), wrap: true);
            text.VerticalAlignment = VerticalAlignment.Center;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
            row.Children.Add(num);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            steps.Children.Add(row);
        }
        var deep = Ui.Button(I18n.T("games.deep"), () => _ = AppState.DetectOne(_g, deep: true), "", Icons.Search);
        deep.IsEnabled = !searching;
        var left = Ui.Col(16, Ui.Text(I18n.T("v92.nf.title"), "h2"), Ui.Text(I18n.T("games.notDetected.text", ("game", _g.Def.Name)), "muted", wrap: true), steps, Ui.Row(10, deep));
        var picture = new Border { Width = 360, Height = 186, CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = new Image { Source = Images.Asset("not-found.jpg", 720), Stretch = Stretch.UniformToFill }, VerticalAlignment = VerticalAlignment.Top };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 28 };
        grid.Children.Add(left);
        Grid.SetColumn(picture, 1);
        grid.Children.Add(picture);
        return Ui.Card(grid, 26);
    }
}
