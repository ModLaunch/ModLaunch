using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Minecraft;
using ModLaunch.Mods;

namespace ModLaunch.Views;

/// <summary>
/// Страница Minecraft (8.5): шапка с выбором сборки и «Играть», вкладки «Моды» (содержимое сборки),
/// «Каталог» (Modrinth под версию и загрузчик), «Сборки», «Настройки» и «Ещё» (миры, скриншоты, лог).
/// </summary>
public sealed partial class MinecraftPage : Page
{
    string _tab;
    static GameState G => Mc.State!;

    public MinecraftPage(string tab = "", string query = "")
    {
        _tab = tab switch
        {
            "" or "installed" => Mc.Active is null ? "builds" : "mods",
            "catalog" or "mods" or "builds" or "settings" or "worlds" or "shots" or "log" => tab,
            "saves" => "worlds",
            "config" or "tools" => "settings",
            "profiles" => "builds",
            _ => "mods",
        };
        _query = query;
        if (_tab == "catalog") _ = LoadCatalog(reset: true);
        if (_tab == "mods" && Mc.Active is { } a && !Program.Screenshot) _ = Refresh(a);
    }

    public override string Title => "Minecraft";
    public override string? GameId => Mc.Id;
    public override string SearchHint => I18n.T("mine.search");
    public override Control? Aside() => AsideView();
    public override bool SameScreenAs(Page? previous)
    {
        TabSwitch = previous is MinecraftPage;
        return TabSwitch;
    }
    bool TabSwitch;

    public override IEnumerable<(string Text, Action? Open)> Crumbs =>
    [
        ("Minecraft", () => MainWindow.Current?.Navigate(() => new MinecraftPage())),
        (TabName(_tab), null),
    ];

    static string TabName(string tab) => tab switch
    {
        "catalog" => I18n.T("mine.tab.catalog"),
        "builds" => I18n.T("mine.tab.builds"),
        "settings" => I18n.T("mine.tab.settings"),
        "worlds" => I18n.T("mine.tab.worlds"),
        "shots" => I18n.T("shots.tab"),
        "log" => I18n.T("games.log"),
        _ => I18n.T("mine.tab.mods"),
    };

    public override void Search(string text)
    {
        _query = text.Trim();
        _tab = "catalog";
        _ = LoadCatalog(reset: true);
        Build();
        MainWindow.Current?.RenderCrumbs();
    }

    static MainWindow W => MainWindow.Current!;

    void Go(string tab)
    {
        if (_tab == tab) return;
        MainWindow.Current?.Navigate(() => new MinecraftPage(tab, tab == "catalog" ? _query : ""));
    }

    ScrollViewer? _scroll;
    string? _scrollTab;

    public override void Build()
    {
        var content = new StackPanel { Spacing = 24, Margin = new Thickness(40, 26, 40, 40), MaxWidth = 1640 };
        content.Children.Add(Header());
        if (G.Status == Detect.Found)
        {
            content.Children.Add(Tabs());
            var body = _tab switch
            {
                "catalog" => CatalogView(),
                "builds" => BuildsView(),
                "settings" => SettingsView(),
                "worlds" => WorldsView(),
                "shots" => ShotsView(),
                "log" => LogView(),
                _ => ContentView(),
            };
            content.Children.Add(body);
            if (!Shown) GamePage.Stagger(body, TabSwitch ? 0 : 140);
        }
        else content.Children.Add(NotFoundView());

        _scroll ??= new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        if (_scrollTab != _tab) { _scroll.Offset = default; _scrollTab = _tab; }
        _scroll.Content = content;
        Content = _scroll;
    }

    // ---------------------------------------------------------------- шапка

    Control Header()
    {
        var active = Mc.Active;
        var launcher = Program.Demo ? new Mc.LauncherInfo("store", null) : Mc.FindLauncher();
        var (dot, line) = G.Status switch
        {
            Detect.Found when active is null => (Ui.Res("Warn"), I18n.T("mine.status.noBuild")),
            Detect.Found when launcher is null => (Ui.Res("Warn"), I18n.T("mine.status.noLauncher")),
            Detect.Found => (Ui.Res("Good"), I18n.T("mine.status.ready", ("build", active!.Name), ("label", active.Label))),
            Detect.Searching => (Ui.Res("Muted"), I18n.T("games.searching")),
            _ => (Ui.Res("Faint"), I18n.T("games.notDetected")),
        };

        var title = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Minecraft", FontSize = 32, FontWeight = FontWeight.ExtraBold, Foreground = Brushes.White, LetterSpacing = -0.5 },
                new Border
                {
                    Background = Ui.Hex("#333FB950"), BorderBrush = Ui.Hex("#663FB950"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(8, 3), VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = "Java Edition", FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Ui.Hex("#9BE7A8") },
                },
            },
        };
        var info = Ui.Col(10, title, Ui.Row(8, Ui.Dot(dot), Ui.Text(line, "small", color: Ui.Hex("#D5DAE5"))));
        if (G.Path is not null)
        {
            var path = Ui.Row(16, Ui.Row(8, Ui.Icon(Icons.Folder, 13, Ui.Hex("#AAB2C2")), Ui.Text(Ui.ShortPath(active?.Dir ?? G.Path), "small", color: Ui.Hex("#AAB2C2"))));
            var played = Features.PlayTime.Get(Mc.Id);
            if (played.TotalMs > 0) path.Children.Add(Ui.Row(8, Ui.Icon(Icons.Clock, 13, Ui.Hex("#AAB2C2")), Ui.Text(I18n.T("time.total", ("time", Features.PlayTime.Format(played.TotalMs))), "small", color: Ui.Hex("#AAB2C2"))));
            info.Children.Add(path);
        }
        info.VerticalAlignment = VerticalAlignment.Bottom;

        var buttons = Ui.Row(10);
        buttons.VerticalAlignment = VerticalAlignment.Bottom;
        if (G.Status == Detect.Found)
        {
            buttons.Children.Add(BuildSwitcher());
            if (Features.Launcher.IsRunning(Mc.Id))
            {
                buttons.Children.Add(PlayControls.RunningPill(Mc.Id));
                var stop = Ui.Button(I18n.T("v4.stop"), () => { Features.Launcher.Stop(Mc.Id); W.Toast(I18n.T("v4.stopped")); }, "", Icons.Stop);
                stop.Padding = new Thickness(20, 13);
                buttons.Children.Add(stop);
            }
            else buttons.Children.Add(PlayButton());
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(26, 22) };
        grid.Children.Add(info);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        var hero = Ui.GameImage(G.Def, 1400, art: Images.Art.Hero);
        if (!Shown && !TabSwitch)
        {
            Animate.From(hero, "scale(1.12)", 1400, 0, new Avalonia.Animation.Easings.QuadraticEaseOut(), 1);
            Animate.From(info, "translateX(-28px)", 520, 120);
            Animate.From(buttons, "translateX(28px)", 520, 180);
        }
        return new Border
        {
            CornerRadius = new CornerRadius(20), ClipToBounds = true, BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1), Height = 184,
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

    /// <summary>Выбор сборки в шапке: значок загрузчика, название, стрелка; в меню — все сборки и «Новая сборка».</summary>
    Control BuildSwitcher()
    {
        var a = Mc.Active;
        var words = Ui.Col(0,
            new TextBlock { Text = a?.Name ?? I18n.T("mine.build.none"), FontWeight = FontWeight.SemiBold, MaxWidth = 210, TextTrimming = TextTrimming.CharacterEllipsis },
            Ui.Text(a?.Label ?? I18n.T("mine.build.create"), "tiny muted"));
        words.VerticalAlignment = VerticalAlignment.Center;
        var button = new Button { Classes = { "glass" }, Padding = new Thickness(10, 7, 12, 7), Content = Ui.Row(10, LoaderBadge(a?.Loader ?? "vanilla", 32), words, Ui.Icon(Icons.ChevronDown, 14)) };
        button.Click += (_, _) =>
        {
            var items = Mc.Instances().Select(i => (object?)Ctx.Item((i.Id == a?.Id ? "✓  " : "") + i.Name + "  ·  " + i.Label, LoaderIcon(i.Loader), () => { Mc.Active = i; Mc.Notify(); })).ToList();
            items.Add("-");
            items.Add(Ctx.Item(I18n.T("mine.build.new"), Icons.Plus, CreateDialog));
            items.Add(Ctx.Item(I18n.T("mine.tab.builds"), Icons.Layers, () => Go("builds")));
            var menu = Ctx.Menu(items.ToArray());
            menu.Placement = PlacementMode.BottomEdgeAlignedRight;
            menu.ShowAt(button);
        };
        ToolTip.SetTip(button, I18n.T("mine.build.switch"));
        return button;
    }

    Control PlayButton()
    {
        var play = Ui.Button(I18n.T("games.play"), () => Play(), "primary", Icons.Play);
        play.FontSize = 17;
        play.Padding = new Thickness(30, 13);
        var accent = Color.Parse(G.Def.Accent);
        return new Border
        {
            CornerRadius = new CornerRadius(12),
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 10, Blur = 30, Spread = -6, Color = Color.FromArgb(0xB3, accent.R, accent.G, accent.B) }),
            Child = play,
        };
    }

    /// <summary>Значок загрузчика: цветная плашка с буквой (F, Q, NF, Fo) или кубик у чистой игры.</summary>
    public static Control LoaderBadge(string loader, double size)
    {
        var (color, text) = loader switch
        {
            "fabric" => ("#DBB98A", "F"),
            "quilt" => ("#9E6CE8", "Q"),
            "forge" => ("#E07C4E", "Fo"),
            "neoforge" => ("#E8A04B", "NF"),
            _ => ("#3FB950", ""),
        };
        var c = Color.Parse(color);
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.28),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(c, 0), new GradientStop(Color.FromRgb((byte)(c.R * 0.55), (byte)(c.G * 0.55), (byte)(c.B * 0.55)), 1) },
            },
            Child = text == ""
                ? Ui.Icon(Icons.Cube, size * 0.5, Brushes.White)
                : new TextBlock { Text = text, FontSize = size * (text.Length > 1 ? 0.34 : 0.45), FontWeight = FontWeight.ExtraBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    static string LoaderIcon(string loader) => loader == "vanilla" ? Icons.Cube : Icons.Layers;

    // ---------------------------------------------------------------- вкладки

    static readonly Dictionary<string, (double X, double W)> Mark = [];

    Control Tabs()
    {
        var a = Mc.Active;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        Button? active = null;
        Button Tab(string id, string text, string icon, string? badge, string? badgeClass = null)
        {
            var content = Ui.Row(8, Ui.Icon(icon, 16), new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            if (badge is not null)
            {
                var b = new Border { Classes = { "tab-badge" }, Child = Ui.Text(badge, "tiny") };
                if (badgeClass is not null) b.Classes.Add(badgeClass);
                content.Children.Add(b);
            }
            var button = new Button { Classes = { "gtab" }, Content = content };
            if (_tab == id) { button.Classes.Add("active"); active = button; }
            button.Click += (_, _) => Go(id);
            return button;
        }
        var count = a is null ? 0 : Mc.Kinds.Sum(k => McContent.Count(a, k));
        var updates = a is null ? 0 : McContent.UpdateCount(a);
        var mods = Tab("mods", I18n.T("mine.tab.mods"), Icons.Package, count > 0 ? count.ToString() : null);
        if (updates > 0 && mods.Content is StackPanel sp) sp.Children.Add(new Border { Classes = { "tab-badge", "good" }, Child = Ui.Text("↑" + updates, "tiny") });
        row.Children.Add(mods);
        row.Children.Add(Tab("catalog", I18n.T("mine.tab.catalog"), Icons.Bag, null));
        var builds = Mc.Instances().Count;
        row.Children.Add(Tab("builds", I18n.T("mine.tab.builds"), Icons.Layers, builds > 0 ? builds.ToString() : null));
        row.Children.Add(Tab("settings", I18n.T("mine.tab.settings"), Icons.Sliders, null));

        var extra = new List<(string Id, string Text, string Icon)>
        {
            ("worlds", I18n.T("mine.tab.worlds"), Icons.Globe),
            ("shots", I18n.T("shots.tab"), Icons.Image),
            ("log", I18n.T("games.log"), Icons.Alert),
        };
        var current = extra.FirstOrDefault(e => e.Id == _tab);
        var more = new Button
        {
            Classes = { "gtab" },
            Content = Ui.Row(8, Ui.Icon(current.Id is null ? Icons.More : current.Icon, 16),
                new TextBlock { Text = current.Id is null ? I18n.T("tab.more") : current.Text, VerticalAlignment = VerticalAlignment.Center }, Ui.Icon(Icons.ChevronDown, 13)),
        };
        if (current.Id is not null) { more.Classes.Add("active"); active = more; }
        more.Click += (_, _) =>
        {
            var menu = Ctx.Menu(extra.Select(e => (object?)Ctx.Item(e.Text, e.Icon, () => Go(e.Id))).ToArray());
            menu.Placement = PlacementMode.BottomEdgeAlignedLeft;
            menu.ShowAt(more);
        };
        row.Children.Add(more);

        // Индикатор под выбранной вкладкой, как на страницах игр: едет от прежнего места.
        var ink = new Border { Classes = { "tab-ink" }, Height = 2.5, CornerRadius = new CornerRadius(2), Width = 0, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
        var bar = new Panel { Children = { row, ink } };
        var placed = false;
        void Place(bool animate)
        {
            if (active is null || active.Bounds.Width <= 0) return;
            var p = active.TranslatePoint(new Point(0, 0), row) ?? new Point();
            var target = (X: p.X + 10, W: Math.Max(0, active.Bounds.Width - 20));
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (animate && Animate.On && Mark.TryGetValue("mc", out var was) && Math.Abs(was.X - target.X) > 1)
            {
                ink.Transitions = null;
                ink.Width = was.W;
                ink.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse($"translateX({was.X.ToString(inv)}px)");
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    ink.Transitions =
                    [
                        new Avalonia.Animation.DoubleTransition { Property = Layoutable.WidthProperty, Duration = TimeSpan.FromMilliseconds(320), Easing = new Avalonia.Animation.Easings.CubicEaseOut() },
                        new Avalonia.Animation.TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(360), Easing = new Avalonia.Animation.Easings.BackEaseOut() },
                    ];
                    ink.Width = target.W;
                    ink.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse($"translateX({target.X.ToString(inv)}px)");
                }, Avalonia.Threading.DispatcherPriority.Background);
            }
            else
            {
                ink.Transitions = null;
                ink.Width = target.W;
                ink.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse($"translateX({target.X.ToString(inv)}px)");
            }
            Mark["mc"] = target;
        }
        row.LayoutUpdated += (_, _) => { if (placed || active is null || active.Bounds.Width <= 0) return; placed = true; Place(true); };
        row.SizeChanged += (_, _) => { if (placed) Place(false); };
        return new Border
        {
            Classes = { "gtabs" },
            Child = new ScrollViewer { Content = bar, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled },
        };
    }

    // ---------------------------------------------------------------- игра не найдена

    Control NotFoundView()
    {
        var col = Ui.Col(14,
            Ui.Text(I18n.T("mine.notFound.title"), "h2"),
            Ui.Text(I18n.T("mine.notFound.text"), "muted", wrap: true),
            Ui.Row(10,
                Ui.Button(I18n.T("mine.notFound.download"), () => Ui.OpenUrl("https://www.minecraft.net/download"), "primary", Icons.Download),
                Ui.Button(I18n.T("games.detectAgain"), () => _ = AppState.DetectOne(G), "", Icons.Refresh),
                Ui.Button(I18n.T("games.setPath"), () => Actions.PickGameFolder(G), "ghost", Icons.Folder)));
        return Ui.Card(col, 26);
    }

    // ---------------------------------------------------------------- «Играть»

    /// <summary>Играть выбранной (или указанной) сборкой. Без сборок — предложить создать.</summary>
    public static void Play(McInstance? instance = null)
    {
        var w = MainWindow.Current;
        if (w is null) return;
        var i = instance ?? Mc.Active;
        if (i is null)
        {
            w.Navigate(() => new MinecraftPage("builds"));
            CreateDialog();
            return;
        }
        if (Features.Launcher.IsRunning(Mc.Id)) { w.Toast(I18n.T("launch.already")); return; }
        if (!Mc.VersionReady(i))
        {
            // Загрузчик ещё не стоит — ставим задачей (видно в загрузках), потом открываем лаунчер.
            Jobs.Run($"{i.LoaderTitle} {i.GameVersion}", "Minecraft", async (_, progress, ct) =>
            {
                var r = await McLaunch.Play(i, progress, ct);
                Avalonia.Threading.Dispatcher.UIThread.Post(() => AfterPlay(i, r));
            });
            w.Toast(I18n.T("mine.play.preparing", ("loader", i.LoaderTitle)));
            return;
        }
        _ = PlayNow(i);
    }

    static async Task PlayNow(McInstance i)
    {
        try { AfterPlay(i, await McLaunch.Play(i)); }
        catch (Exception e) { Guard.Log(e); MainWindow.Current?.Toast(Jobs.Explain(e), bad: true); }
    }

    static void AfterPlay(McInstance i, McLaunch.Result r)
    {
        var w = MainWindow.Current;
        if (w is null) return;
        if (r == McLaunch.Result.NoLauncher)
        {
            w.Dialog(I18n.T("mine.noLauncher.title"), Ui.Text(I18n.T("mine.noLauncher.text"), "muted", wrap: true),
                Ui.Button(I18n.T("common.close"), w.CloseDialog),
                Ui.Button(I18n.T("mine.notFound.download"), () => { w.CloseDialog(); Ui.OpenUrl("https://www.minecraft.net/download"); }, "primary", Icons.Download));
            return;
        }
        w.Toast(I18n.T(r == McLaunch.Result.AlreadyOpen ? "mine.play.reopen" : "mine.play.opened", ("build", "ModLaunch · " + i.Name)));
        Mc.Notify();
    }

    static readonly Dictionary<string, DateTime> Refreshed = [];

    /// <summary>
    /// Узнать файлы, поставленные вручную, и проверить обновления — в фоне (хеши сотен модов считаются не мгновенно)
    /// и не чаще раза в 15 минут на сборку: вкладки переключаются без лишних запросов.
    /// </summary>
    async Task Refresh(McInstance a, bool force = false)
    {
        lock (Refreshed)
        {
            if (!force && Refreshed.TryGetValue(a.Id, out var at) && DateTime.UtcNow - at < TimeSpan.FromMinutes(15)) return;
            Refreshed[a.Id] = DateTime.UtcNow;
        }
        try
        {
            await Task.Run(async () =>
            {
                await McContent.Identify(a);
                await McContent.CheckUpdates(a);
            });
        }
        catch { }
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (MainWindow.Current?.CurrentPage == this) Build(); });
    }

    // ---------------------------------------------------------------- правая панель

    Control? AsideView()
    {
        var a = Mc.Active;
        if (a is null) return null;
        var col = Ui.Col(18);
        col.Children.Add(Ui.Col(10,
            Ui.Row(10, LoaderBadge(a.Loader, 40), Ui.Col(2, Ui.Text(a.Name, "h3"), Ui.Text(a.Label, "small muted"))),
            Ui.Text(a.Linked ? I18n.T("mine.aside.linked", ("source", McImport.SourceTitle(a.Source ?? ""))) : I18n.T("mine.aside.own"), "small muted", wrap: true)));
        var stats = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 10, RowSpacing = 10 };
        var cells = new[]
        {
            (McContent.Count(a, "mod").ToString(), I18n.T("mine.kind.mod.many")),
            (McContent.Count(a, "resourcepack").ToString(), I18n.T("mine.kind.resourcepack.many")),
            (McContent.Count(a, "shader").ToString(), I18n.T("mine.kind.shader.many")),
            (McContent.UpdateCount(a).ToString(), I18n.T("aside.updatesShort")),
        };
        for (var n = 0; n < cells.Length; n++)
        {
            var cell = new Border { Classes = { "card" }, Padding = new Thickness(12, 10), Child = Ui.Col(2, Ui.Text(cells[n].Item1, "h2"), Ui.Text(cells[n].Item2, "tiny muted")) };
            Grid.SetColumn(cell, n % 2);
            Grid.SetRow(cell, n / 2);
            stats.Children.Add(cell);
        }
        col.Children.Add(stats);
        col.Children.Add(Ui.Col(6,
            Ui.Text(I18n.T("mine.aside.memory", ("gb", (a.MemoryMb / 1024.0).ToString("0.#", I18n.Culture))), "small"),
            Ui.Text(a.LastPlayed is null ? I18n.T("mine.aside.never") : I18n.T("mine.aside.played", ("ago", Ui.Ago(a.LastPlayed))), "small muted")));
        col.Children.Add(Ui.Col(8,
            Ui.Button(I18n.T("games.openFolder"), () => Actions.OpenFolder(a.Dir), "", Icons.Folder),
            Ui.Button(I18n.T("mine.tab.catalog"), () => Go("catalog"), "", Icons.Bag)));
        return col;
    }
}
