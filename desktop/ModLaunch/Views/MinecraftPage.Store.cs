using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ModLaunch.Core;
using ModLaunch.Minecraft;

namespace ModLaunch.Views;

/// <summary>
/// 10.0.1: Minecraft открывается так же, как остальные игры (стиль «Store»): шапка с обложкой,
/// цифрами и «Играть», вкладки-«пивот» с полоской акцента и «Обзор» по умолчанию.
/// Сверх обычной страницы игры: карточка текущей сборки, полка всех сборок (переключение
/// в один клик), моды сборки, последние миры и сведения о сборке справа.
/// </summary>
public sealed partial class MinecraftPage
{
    void BuildStore()
    {
        var page = new StackPanel();
        page.Children.Add(StoreHeader());
        var content = StoreKit.Column(spacing: 22, top: 0);
        if (G.Status == Detect.Found)
        {
            content.Children.Add(StorePivot());
            var body = _tab switch
            {
                "mods" => ContentView(),
                "catalog" => CatalogView(),
                "builds" => BuildsView(),
                "settings" => SettingsView(),
                "worlds" => WorldsView(),
                "servers" => ServersView(),
                "shots" => ShotsView(),
                "log" => LogView(),
                _ => OverviewView(),
            };
            content.Children.Add(body);
            if (!Shown) GamePage.Stagger(body, TabSwitch ? 0 : 160);
        }
        else
        {
            content.Margin = new Thickness(content.Margin.Left, 6, content.Margin.Right, content.Margin.Bottom);
            content.Children.Add(NotFoundView());
        }
        page.Children.Add(content);

        _scroll ??= new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        if (_scrollTab != _tab) { _scroll.Offset = default; _scrollTab = _tab; }
        _scroll.Content = page;
        Content = _scroll;
    }

    // ---------------------------------------------------------------- шапка «страницы приложения»

    Control StoreHeader()
    {
        var found = G.Status == Detect.Found;
        var active = Mc.Active;
        var running = Features.Launcher.IsRunning(Mc.Id);
        var played = Features.PlayTime.Get(Mc.Id);
        var compact = found && _tab != "overview";

        var cover = new Border
        {
            Width = compact ? 88 : 168, Height = compact ? 132 : 252, CornerRadius = new CornerRadius(compact ? 8 : 10), ClipToBounds = true,
            BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1),
            Child = Ui.GameImage(G.Def, 400, art: Images.Art.Cover),
        };
        var coverHost = new Border { CornerRadius = new CornerRadius(10), BoxShadow = BoxShadows.Parse("0 18 40 -10 #A0000000"), Child = cover, VerticalAlignment = VerticalAlignment.Bottom };

        var name = new TextBlock { Text = "Minecraft", FontSize = compact ? 26 : 34, FontWeight = FontWeight.Bold, LetterSpacing = -0.8, VerticalAlignment = VerticalAlignment.Center };
        var edition = new Border
        {
            Background = Ui.Hex("#263FB950"), BorderBrush = Ui.Hex("#553FB950"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 3), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "Java Edition", FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Ui.Res("Good") },
        };
        var title = Ui.Row(12, name, edition);

        var launcher = Program.Demo ? new Mc.LauncherInfo("store", null) : found ? Mc.FindLauncher() : null;
        var (dot, line) = G.Status switch
        {
            Detect.Found when active is null => (Ui.Res("Warn"), I18n.T("mine.status.noBuild")),
            Detect.Found when launcher is null => (Ui.Res("Warn"), I18n.T("mine.status.noLauncher")),
            Detect.Found => (Ui.Res("Good"), I18n.T("mine.status.ready", ("build", active!.Name), ("label", active.Label))),
            Detect.Searching => (Ui.Res("Muted"), I18n.T("games.searching")),
            _ => (Ui.Res("Faint"), I18n.T("games.notDetected")),
        };
        // 15.0: строка статуса — сетка, а не StackPanel: длинный путь обрезается «…», а не заезжает под «Играть».
        var status = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 8 };
        var dotHost = Ui.Dot(dot);
        dotHost.VerticalAlignment = VerticalAlignment.Center;
        status.Children.Add(dotHost);
        var lineText = new TextBlock { Text = line, Foreground = Ui.Res("Text"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(lineText, 1);
        status.Children.Add(lineText);
        if ((active?.Dir ?? G.Path) is { } dir)
        {
            var path = new Button
            {
                Classes = { "ghost" }, Padding = new Thickness(6, 2), Margin = new Thickness(6, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
                // В узкой шапке вкладок — только значок папки (путь в подсказке): строка не обрывается на «……».
                Content = compact ? Ui.Icon(Icons.Folder, 13, Ui.Res("Muted"))
                    : new DockPanel { Children = { Ui.Icon(Icons.Folder, 13, Ui.Res("Muted")), new TextBlock { Text = Ui.ShortPath(dir), FontSize = 13, Foreground = Ui.Res("Muted"), Margin = new Thickness(6, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center } } },
            };
            ToolTip.SetTip(path, dir);
            path.Click += (_, _) => Actions.OpenFolder(dir);
            Grid.SetColumn(path, 2);
            status.Children.Add(path);
        }

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
            if (active is not null)
            {
                Fact(active.GameVersion, I18n.T("v101.mc.version"));
                Fact(McContent.Count(active, "mod").ToString(), I18n.T("v92.game.mods"));
                var updates = McContent.UpdateCount(active);
                if (updates > 0) Fact(updates.ToString(), I18n.T("v92.game.updates"));
            }
            var builds = Mc.Instances().Count;
            if (builds > 0) Fact(builds.ToString(), I18n.T("v101.mc.buildsFact"));
            if (played.TotalMs > 0) Fact(Features.PlayTime.Format(played.TotalMs), I18n.T("v92.game.played"));
            if (played.LastPlayed is not null) Fact(Ui.Ago(played.LastPlayed), I18n.T("v92.game.last"));
        }

        var buttons = Ui.Row(10);
        if (found)
        {
            if (running)
            {
                buttons.Children.Add(PlayControls.RunningPill(Mc.Id, onArt: false));
                var stop = Ui.Button(I18n.T("v4.stop"), () => { Features.Launcher.Stop(Mc.Id); W.Toast(I18n.T("v4.stopped")); }, "", Icons.Stop);
                stop.Padding = new Thickness(20, 13);
                buttons.Children.Add(stop);
            }
            else buttons.Children.Add(PlayButton());
            buttons.Children.Add(BuildSwitcher(""));
        }
        else
        {
            var searching = G.Status == Detect.Searching;
            var get = Ui.Button(I18n.T("get.mcLauncher"), () => { Features.GetGame.InstallMinecraftLauncher(); W.OpenDownloads(); }, "primary", Icons.Download);
            get.Padding = new Thickness(24, 13);
            var again = Ui.Button(searching ? I18n.T("games.searching") : I18n.T("games.detectAgain"), () => _ = AppState.DetectOne(G), "", Icons.Refresh);
            again.Padding = new Thickness(18, 13);
            again.IsEnabled = !searching;
            buttons.Children.Add(get);
            buttons.Children.Add(again);
        }
        var more = Ui.Button("", () => { }, "icon", Icons.More, I18n.T("top.more"));
        more.Width = more.Height = 46;
        more.Flyout = GameCard.Menu(G);
        buttons.Children.Add(more);

        var info = Ui.Col(compact ? 10 : 14, title, status);
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

        var hero = Ui.GameImage(G.Def, 1600, art: Images.Art.Hero);
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
            Animate.From(hero, "scale(1.1)", 1400, 0, new Avalonia.Animation.Easings.QuadraticEaseOut(), 1);
            Animate.From(coverHost, "translateY(14px)", 460, 60, new Avalonia.Animation.Easings.QuinticEaseOut());
            Animate.From(info, "translateX(-22px)", 520, 140);
        }
        return new Panel { ClipToBounds = true, Children = { art, grid } };
    }

    // ---------------------------------------------------------------- вкладки

    Control StorePivot()
    {
        var a = Mc.Active;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        Button? active = null;
        Button Tab(string id, string text, string? badge)
        {
            var content = Ui.Row(7, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            if (badge is not null) content.Children.Add(new Border { Classes = { "tab-badge" }, Child = Ui.Text(badge, "tiny") });
            var b = new Button { Classes = { "pivot" }, Content = content };
            if (_tab == id) { b.Classes.Add("active"); active = b; }
            b.Click += (_, _) => Go(id);
            return b;
        }
        row.Children.Add(Tab("overview", I18n.T("v92.tab.overview"), null));
        var count = a is null ? 0 : Mc.Kinds.Sum(k => McContent.Count(a, k));
        var updates = a is null ? 0 : McContent.UpdateCount(a);
        var mods = Tab("mods", I18n.T("mine.tab.mods"), count > 0 ? count.ToString() : null);
        if (updates > 0 && mods.Content is StackPanel sp) sp.Children.Add(new Border { Classes = { "tab-badge", "good" }, Child = Ui.Text("↑" + updates, "tiny") });
        row.Children.Add(mods);
        row.Children.Add(Tab("catalog", I18n.T("mine.tab.catalog"), null));
        var builds = Mc.Instances().Count;
        row.Children.Add(Tab("builds", I18n.T("mine.tab.builds"), builds > 0 ? builds.ToString() : null));
        row.Children.Add(Tab("worlds", I18n.T("mine.tab.worlds"), null));
        row.Children.Add(Tab("settings", I18n.T("mine.tab.settings"), null));

        var extra = new List<(string Id, string Text, string Icon)>
        {
            ("servers", I18n.T("mc94.servers.title"), Icons.Users),
            ("shots", I18n.T("shots.tab"), Icons.Image),
            ("log", I18n.T("games.log"), Icons.Alert),
        };
        var current = extra.FirstOrDefault(e => e.Id == _tab);
        var more = new Button { Classes = { "pivot" }, Content = Ui.Row(7, new TextBlock { Text = current.Id is null ? I18n.T("tab.more") : current.Text, VerticalAlignment = VerticalAlignment.Center }, Ui.Icon(Icons.ChevronDown, 13)) };
        if (current.Id is not null) { more.Classes.Add("active"); active = more; }
        more.Click += (_, _) =>
        {
            var menu = Ctx.Menu(extra.Select(e => (object?)Ctx.Item(e.Text, e.Icon, () => Go(e.Id))).ToArray());
            menu.Placement = PlacementMode.BottomEdgeAlignedLeft;
            menu.ShowAt(more);
        };
        row.Children.Add(more);

        var ink = new Border { Classes = { "tab-ink" }, Height = 3, CornerRadius = new CornerRadius(2), Width = 0, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
        var bar = new Panel { Children = { row, ink } };
        var placed = false;
        void Place(bool animate)
        {
            if (active is null || active.Bounds.Width <= 0) return;
            var p = active.TranslatePoint(new Point(0, 0), row) ?? new Point();
            var target = (X: p.X + 14, W: Math.Max(0, active.Bounds.Width - 28));
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (animate && Animate.On && Mark.TryGetValue("mc-store", out var was) && Math.Abs(was.X - target.X) > 1)
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
            Mark["mc-store"] = target;
        }
        row.LayoutUpdated += (_, _) => { if (placed || active is null || active.Bounds.Width <= 0) return; placed = true; Place(true); };
        row.SizeChanged += (_, _) => { if (placed) Place(false); };
        return new Border
        {
            BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 0, 0, 4),
            Child = new ScrollViewer { Content = bar, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled },
        };
    }

    // ---------------------------------------------------------------- «Обзор»

    Control OverviewView()
    {
        var a = Mc.Active;
        if (a is null) return NoBuildView();
        var left = new StackPanel { Spacing = 28 };

        // 1. Что сделать сейчас: новая версия модпака и обновления модов.
        var todo = new StackPanel { Spacing = 10 };
        if (PackUpdateBanner(a) is { } pack) todo.Children.Add(pack);
        var updates = McContent.Updates(a);
        if (updates.Count > 0) todo.Children.Add(UpdatesBanner(a, updates));
        if (todo.Children.Count > 0) left.Children.Add(todo);

        // 2. Текущая сборка: крупно, со всем содержимым и «Играть».
        left.Children.Add(CurrentBuildCard(a));

        // 3. Все сборки — полкой: выбрать или запустить в один клик.
        var shelf = Mc.Instances().Select(i => BuildTile(i, i.Id == a.Id)).Append(NewBuildTile()).ToList();
        left.Children.Add(Ui.Col(12,
            StoreKit.Header(I18n.T("v101.mc.builds"), () => Go("builds"), count: Mc.Instances().Count.ToString()),
            StoreKit.Shelf(shelf)));

        // 4. Моды сборки — сначала включённые, первые шесть; остальное на вкладке «Моды».
        var items = McContent.List(a, "mod");
        var byKey = updates.ToDictionary(u => u.Item.Key, StringComparer.OrdinalIgnoreCase);
        var mods = new StackPanel { Spacing = 8 };
        if (items.Count == 0)
            mods.Children.Add(Ui.Card(Ui.Col(10,
                Ui.Text(I18n.T("mine.empty.mod"), "h3"),
                Ui.Text(I18n.T("mine.empty.text"), "muted", wrap: true),
                Ui.Row(10,
                    Ui.Button(I18n.T("mine.add"), () => { _type = "mod"; Go("catalog"); }, "primary", Icons.Bag),
                    Ui.Button(I18n.T("mine.addFile"), () => AddFile(a), "", Icons.FilePlus))), 22));
        else
            foreach (var item in items.OrderBy(i => byKey.ContainsKey(i.Key) ? 0 : 1).ThenBy(i => !i.Enabled).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).Take(6))
                mods.Children.Add(ItemRow(a, item, byKey.GetValueOrDefault(item.Key)));
        left.Children.Add(Ui.Col(12,
            StoreKit.Header(I18n.T("v101.mc.mods"), items.Count > 0 ? () => Go("mods") : null, count: items.Count > 0 ? items.Count.ToString() : null),
            mods));

        // 5. Последние миры этой сборки.
        var worlds = RecentWorlds(a, 4);
        if (worlds.Count > 0)
        {
            var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -12, -12) };
            foreach (var w in worlds) grid.Children.Add(WorldCard(a, w));
            left.Children.Add(Ui.Col(12, StoreKit.Header(I18n.T("v101.mc.worlds"), () => Go("worlds")), grid));
        }

        var right = new StackPanel { Spacing = 16 };
        right.Children.Add(BuildAboutCard(a));
        right.Children.Add(McQuickActions(a));

        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,340"), ColumnSpacing = 28 };
        layout.Children.Add(left);
        Grid.SetColumn(right, 1);
        layout.Children.Add(right);
        return layout;
    }

    static List<DirectoryInfo> RecentWorlds(McInstance a, int take)
    {
        var saves = Path.Combine(a.Dir, "saves");
        try
        {
            return Directory.Exists(saves)
                ? new DirectoryInfo(saves).EnumerateDirectories().Where(d => File.Exists(Path.Combine(d.FullName, "level.dat"))).OrderByDescending(d => d.LastWriteTimeUtc).Take(take).ToList()
                : [];
        }
        catch { return []; }
    }

    /// <summary>Текущая сборка: значок, название, версия и загрузчик, что внутри — и кнопки.</summary>
    Control CurrentBuildCard(McInstance a)
    {
        var name = new TextBlock { Text = a.Name, FontSize = 22, FontWeight = FontWeight.Bold, LetterSpacing = -0.4, TextTrimming = TextTrimming.CharacterEllipsis };
        var chips = new WrapPanel();
        void Chip(string text, string icon)
        {
            chips.Children.Add(new Border
            {
                Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 5), Margin = new Thickness(0, 0, 8, 6),
                Child = Ui.Row(6, Ui.Icon(icon, 13, Ui.Res("Muted")), Ui.Text(text, "small")),
            });
        }
        foreach (var kind in Mc.Kinds)
        {
            var n = McContent.Count(a, kind);
            if (n > 0 || kind == "mod") Chip($"{n} · {I18n.T($"mine.kind.{kind}.many")}", kind switch { "mod" => Icons.Package, "shader" => Icons.Zap, "resourcepack" => Icons.Image, _ => Icons.Layers });
        }
        var words = Ui.Col(6,
            Ui.Text(I18n.T("v101.mc.current"), "tiny muted"),
            name,
            Ui.Text(a.Label + (a.Linked ? " · " + McImport.SourceTitle(a.Source ?? "") : ""), "muted"),
            chips);
        words.VerticalAlignment = VerticalAlignment.Center;

        var running = Features.Launcher.IsRunning(Mc.Id);
        var play = Ui.Button(running ? I18n.T("v4.running") : I18n.T("games.play"), () => Play(a), "primary", Icons.Play);
        play.IsEnabled = !running;
        play.Padding = new Thickness(22, 11);
        var buttons = Ui.Row(8,
            play,
            Ui.Button(I18n.T("mine.add"), () => { _type = "mod"; Go("catalog"); }, "", Icons.Plus),
            Ui.Button("", () => Actions.OpenFolder(a.Dir), "icon ghost", Icons.Folder, I18n.T("games.openFolder")),
            Ui.Button("", () => Go("settings"), "icon ghost", Icons.Sliders, I18n.T("mine.tab.settings")));
        buttons.VerticalAlignment = VerticalAlignment.Center;

        // 15.0: кнопки — под названием, а не третьей колонкой: имя сборки больше не режется до «Выживан…».
        buttons.Margin = new Thickness(0, 6, 0, 0);
        words.Children.Add(buttons);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 20 };
        var icon = InstanceIcon(a, 96, 22);
        icon.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(icon);
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);

        // Мягкий отсвет цветом загрузчика — сборки отличаются с первого взгляда.
        var tint = Color.Parse(a.Loader switch { "fabric" => "#DBB98A", "quilt" => "#9E6CE8", "forge" => "#E07C4E", "neoforge" => "#E8A04B", _ => "#3FB950" });
        return new Border
        {
            Classes = { "card" }, Padding = new Thickness(20), ClipToBounds = true,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(0x30, tint.R, tint.G, tint.B), 0), new GradientStop(Color.FromArgb(0x00, tint.R, tint.G, tint.B), 0.7) },
            },
            Child = grid,
        };
    }

    /// <summary>Сборка на полке: значок, название, версия. Нажатие — выбрать; ▶ — сразу играть.</summary>
    Control BuildTile(McInstance i, bool selected)
    {
        var name = new TextBlock { Text = i.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 150 };
        var tileIcon = InstanceIcon(i, 56, 14);
        tileIcon.HorizontalAlignment = HorizontalAlignment.Left;
        var col = Ui.Col(10,
            tileIcon,
            Ui.Col(2, name, Ui.Text(i.Label, "tiny muted")),
            Ui.Text(selected ? I18n.T("v101.mc.selected") : i.LastPlayed is null ? I18n.T("mine.aside.never") : Ui.Ago(i.LastPlayed), selected ? "tiny" : "tiny muted", color: selected ? Ui.Res("Good") : null));
        var play = Ui.Button("", () => Play(i), "icon", Icons.Play, I18n.T("games.play"));
        play.HorizontalAlignment = HorizontalAlignment.Right;
        play.VerticalAlignment = VerticalAlignment.Top;
        var card = new Border
        {
            Classes = { "card" }, Width = 186, Padding = new Thickness(14),
            BorderBrush = selected ? Ui.Res("Good") : Ui.Res("Line"), BorderThickness = new Thickness(selected ? 1.5 : 1),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Child = new Panel { Children = { col, play } },
        };
        ToolTip.SetTip(card, selected ? i.Name : I18n.T("v101.mc.pick", ("build", i.Name)));
        card.PointerPressed += (_, e) =>
        {
            if (selected || e.Source is Visual v && v.FindAncestorOfType<Button>(true) is not null) return;
            Mc.Active = i;
            Mc.Notify();
        };
        return card;
    }

    Control NewBuildTile()
    {
        var plus = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(14), Background = Ui.Res("Surface2"), Child = Ui.Icon(Icons.Plus, 24, Ui.Res("Muted")), HorizontalAlignment = HorizontalAlignment.Left };
        var card = new Border
        {
            Classes = { "card" }, Width = 186, Padding = new Thickness(14), Background = Brushes.Transparent,
            BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Child = Ui.Col(10, plus, Ui.Col(2, Ui.Text(I18n.T("mine.build.new"), "strong"), Ui.Text(I18n.T("v101.mc.new.text"), "tiny muted", wrap: true))),
        };
        card.PointerPressed += (_, _) => CreateDialog();
        return card;
    }

    /// <summary>«О сборке»: версия, загрузчик, память, Java, лаунчер, папка и время в игре.</summary>
    Control BuildAboutCard(McInstance a)
    {
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
        var launcher = Program.Demo ? new Mc.LauncherInfo("store", null) : Mc.FindLauncher();
        Row(I18n.T("v101.mc.version"), a.GameVersion);
        Row(I18n.T("aside.loader"), a.Loader == "vanilla" ? a.LoaderTitle : $"{a.LoaderTitle} {a.LoaderVersion}".Trim());
        Row(I18n.T("v101.mc.memory"), (a.MemoryMb / 1024.0).ToString("0.#", I18n.Culture) + " " + I18n.T("v101.mc.gb"));
        Row("Java", a.JavaPath == "" ? I18n.T("v101.mc.javaAuto") : Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(a.JavaPath)) ?? a.JavaPath));
        Row(I18n.T("v101.mc.launcher"), launcher is null ? I18n.T("v101.mc.launcher.no") : I18n.T("v101.mc.launcher.ok"), launcher is null ? Ui.Res("Warn") : Ui.Res("Good"));
        Row(I18n.T("v92.about.folder"), Ui.ShortPath(a.Dir), null, () => Actions.OpenFolder(a.Dir));
        Row(I18n.T("aside.playtime"), a.PlayMs > 0 ? Features.PlayTime.Format(a.PlayMs) : "—");
        Row(I18n.T("aside.lastPlayed"), a.LastPlayed is null ? "—" : Ui.Ago(a.LastPlayed));
        return new Border { Classes = { "card" }, Padding = new Thickness(18), Child = Ui.Col(14, Ui.Text(I18n.T("v101.mc.about"), "h3"), rows) };
    }

    Control McQuickActions(McInstance a)
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
        Item(Icons.Bag, I18n.T("mine.tab.catalog") + " · " + I18n.T("mine.kind.modpack.many"), () => { _type = "modpack"; _category = ""; Go("catalog"); });
        Item(Icons.Users, I18n.T("mc94.servers.title"), () => Go("servers"));
        Item(Icons.Image, I18n.T("shots.tab"), () => Go("shots"));
        Item(Icons.Alert, I18n.T("games.log"), () => Go("log"));
        Item(Icons.FilePlus, I18n.T("mine.import.mrpack"), () => ImportMrpack());
        Item(Icons.Folder, I18n.T("games.openFolder"), () => Actions.OpenFolder(a.Dir));
        Item(Icons.Refresh, I18n.T("mr.checkUpdates"), () => _ = CheckNow(a));
        return new Border { Classes = { "card" }, Padding = new Thickness(10, 14), Child = Ui.Col(8, new Border { Padding = new Thickness(8, 0), Child = Ui.Text(I18n.T("v92.quick.title"), "h3") }, col) };
    }
}
