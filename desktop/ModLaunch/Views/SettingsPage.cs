using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>Настройки: те же разделы и те же ключи settings.json, что в 3.x.</summary>
public sealed partial class SettingsPage : Page
{
    string _tab;
    string? _keyStatus;
    ExeInfo? _dxvkPicked;
    string _accMode = "signin";
    bool _accBusy;
    int _versionClicks;
    DateTime _versionAt;
    string? _dxvkApi;

    public SettingsPage(string tab = "general") => _tab = MapTab(tab);

    public override string Title => I18n.T("nav.settings");
    public override bool Workspace => true;

    /// <summary>Разделы настроек по группам: id, название, иконка, цвет значка.</summary>
    /// <summary>10.0: «Состояние сервисов» (сервер, правила, консоль Firebase) — только владельцу.</summary>
    static (string Group, (string Id, string Key, string Icon, string Color)[] Items)[] Groups =>
        Social.Account.Owner ? AllGroups : AllGroups.Select(g => (g.Group, g.Items.Where(i => i.Id != "services").ToArray())).ToArray();

    static readonly (string Group, (string Id, string Key, string Icon, string Color)[] Items)[] AllGroups =
    [
        ("v11.set.g.app", [
            ("general", "v11.set.general", Icons.Settings, "#94A3B8"),
            ("panel", "cp.title", Icons.Grid, "#60A5FA"),
            ("look", "settings.tab.look", Icons.Palette, "#A78BFA"),
        ]),
        ("set.g.games", [
            ("games", "settings.tab.games", Icons.Gamepad, "#34D399"),
            ("launch", "v11.set.launch", Icons.Play, "#22C55E"),
            ("graphics", "settings.tab.graphics", Icons.Sparkles, "#F472B6"),
            ("backups", "settings.tab.backups", Icons.Shield, "#2DD4BF"),
        ]),
        ("set.g.system", [
            ("downloads", "v11.set.downloads", Icons.Download, "#38BDF8"),
            ("services", "svc.title", Icons.Server, "#22D3EE"),
        ]),
        ("set.g.account", [
            ("accounts", "settings.tab.accounts", Icons.Key, "#FB923C"),
            ("about", "settings.tab.about", Icons.Star, "#F87171"),
        ]),
    ];

    string _filter = "";

    static Border Badge(string icon, string color, double size)
    {
        // «Студия»: значки монохромные — цвет не спорит с текстом.
        if (Look.Studio) return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.26), Background = Ui.Res("Surface2"), BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1), Child = Ui.Icon(icon, size * 0.46, Ui.Res("Text")), VerticalAlignment = VerticalAlignment.Center };
        var c = Color.Parse(color);
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.32),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(70, c.R, c.G, c.B), 0), new GradientStop(Color.FromArgb(30, c.R, c.G, c.B), 1) },
            },
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, c.R, c.G, c.B)), BorderThickness = new Thickness(1),
            Child = Ui.Icon(icon, size * 0.48, new SolidColorBrush(c)),
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    public override void Build()
    {
        // ---- боковое меню: поиск, группы, пункты с иконкой и подписью
        var nav = new StackPanel { Spacing = 2 };
        var search = new TextBox { Watermark = I18n.T("set.search"), Text = _filter, Margin = new Thickness(4, 0, 4, 10) };
        search.InnerLeftContent = new Border { Padding = new Thickness(10, 0, 2, 0), Child = Ui.Icon(Icons.Search, 15, Ui.Res("Muted")) };
        var navItems = new StackPanel { Spacing = 2 };
        void FillNav()
        {
            navItems.Children.Clear();
            var q = (search.Text ?? "").Trim();
            foreach (var (group, items) in Groups)
            {
                var shown = items.Where(i => q == "" || I18n.T(i.Key).Contains(q, StringComparison.OrdinalIgnoreCase) || Desc(i.Id).Contains(q, StringComparison.OrdinalIgnoreCase) || SearchHit(i.Id, q)).ToList();
                if (shown.Count == 0) continue;
                var caption = Ui.Text(Look.Studio ? I18n.T(group) : I18n.T(group).ToUpperInvariant(), "small muted");
                caption.FontWeight = FontWeight.Bold;
                caption.FontSize = 11;
                caption.Margin = new Thickness(12, navItems.Children.Count == 0 ? 2 : 14, 0, 6);
                navItems.Children.Add(caption);
                foreach (var (id, key, icon, color) in shown)
                {
                    var tab = id;
                    var active = _tab == id;
                    var words = Ui.Col(1, Ui.Text(I18n.T(key), "", size: 13.5), Ui.Text(Desc(id), "small muted"));
                    ((TextBlock)words.Children[0]).FontWeight = active ? FontWeight.Bold : FontWeight.SemiBold;
                    ((TextBlock)words.Children[1]).TextTrimming = TextTrimming.CharacterEllipsis;
                    ((TextBlock)words.Children[1]).FontSize = 11.5;
                    words.VerticalAlignment = VerticalAlignment.Center;
                    var b = new Button
                    {
                        Classes = { "tab" }, Padding = new Thickness(8, 7), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                        Content = Ui.Row(11, Badge(icon, color, 34), words),
                    };
                    if (active) b.Classes.Add("active");
                    b.Click += (_, _) => { _tab = tab; _filter = search.Text ?? ""; Build(); };
                    navItems.Children.Add(b);
                }
            }
            if (navItems.Children.Count == 0) navItems.Children.Add(Ui.Text(I18n.T("cr.lib.none"), "small muted"));
        }
        search.TextChanged += (_, _) => FillNav();
        search.KeyDown += (_, e) =>
        {
            if (e.Key != Avalonia.Input.Key.Enter) return;
            var first = Groups.SelectMany(g => g.Items).FirstOrDefault(i => I18n.T(i.Key).Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase) || SearchHit(i.Id, search.Text ?? ""));
            if (first.Id is not null) { _tab = first.Id; _filter = search.Text ?? ""; Build(); }
        };
        FillNav();

        var logo = new Image { Source = Images.Asset("icon.png", 64), Width = 38, Height = 38 };
        var who = Ui.Col(1, Ui.Text("ModLaunch", "h3"), Ui.Text(I18n.T("upd.app.version", ("version", Http.Version)), "small muted"));
        who.VerticalAlignment = VerticalAlignment.Center;
        var head = Ui.Row(10, logo, who);
        head.Margin = new Thickness(8, 4, 8, 14);
        nav.Children.Add(head);
        nav.Children.Add(search);
        nav.Children.Add(navItems);
        var side = Ui.Card(new ScrollViewer { Content = nav, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, 10);
        side.Width = 278;
        side.Margin = new Thickness(34, 26, 0, 26);

        // ---- содержимое раздела
        Control body = _tab switch
        {
            "games" => GamesTab(),
            "launch" => LaunchTab(),
            "backups" => BackupsTab(),
            "downloads" => DownloadsTab(),
            "graphics" => GraphicsTab(),
            "accounts" => AccountsTab(),
            "about" => AboutTab(),
            "look" => LookTab(),
            "panel" => Panel(),
            "services" when Social.Account.Owner => ServicesTab(),
            _ => GeneralTab(),
        };
        // На широком окне разделы встают в две колонки «кирпичиками».
        if (body is StackPanel { Children.Count: > 1 } stack)
        {
            var masonry = new Masonry { Gap = stack.Spacing > 0 ? stack.Spacing : 16 };
            var items = stack.Children.ToList();
            stack.Children.Clear();
            foreach (var c in items) masonry.Children.Add(c);
            body = masonry;
        }

        var current = Groups.SelectMany(g => g.Items).FirstOrDefault(i => i.Id == _tab);
        if (current.Id is null) current = Groups[0].Items[0];
        var title = new TextBlock { Text = I18n.T(current.Key), FontSize = 30, FontWeight = FontWeight.Bold };
        // Раздел — строкой над заголовком («Система»), а не одиноким словом в углу.
        var crumbs = new TextBlock
        {
            Text = I18n.T(Groups.First(g => g.Items.Any(i => i.Id == current.Id)).Group).ToUpper(I18n.Culture),
            FontSize = 11, FontWeight = FontWeight.Bold, LetterSpacing = 1.1, Foreground = Ui.Res("Faint"),
        };
        var titleWords = Ui.Col(2, crumbs, title, Ui.Text(Desc(current.Id, ".long"), "muted", wrap: true));
        titleWords.VerticalAlignment = VerticalAlignment.Center;
        var pageHead = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        // Значок слева, слова — на всю оставшуюся ширину (длинное пояснение переносится, а не уходит за край).
        var headBadge = Badge(current.Icon, current.Color, 56);
        headBadge.Margin = new Thickness(0, 0, 16, 0);
        DockPanel.SetDock(headBadge, Dock.Left);
        pageHead.Children.Add(headBadge);
        pageHead.Children.Add(titleWords);

        var right = new StackPanel { Spacing = 18, Margin = new Thickness(24, 26, 34, 34), MaxWidth = 1500 };
        right.Children.Add(pageHead);
        right.Children.Add(body);
        Animate.From(right, "none", 180);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(side);
        var scroll = new ScrollViewer { Content = right, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(scroll, 1);
        grid.Children.Add(scroll);
        Content = grid;
    }

    /// <summary>Попадает ли запрос в раздел по ключам его настроек (как в поиске сверху).</summary>
    static bool SearchHit(string tab, string q) => q.Trim() != "" && SearchMap.Any(m => m.Tab == tab && m.Keys.Any(k => I18n.T(k).Replace((char)160, ' ').Contains(q.Trim(), StringComparison.OrdinalIgnoreCase)));

    static Control Section(string title, string? hint, params Control[] rows)
    {
        var marker = new Border { Width = Look.Studio ? 0 : 4, Height = 20, CornerRadius = new CornerRadius(2), Background = Ui.Res("Brand"), VerticalAlignment = VerticalAlignment.Center, IsVisible = !Look.Studio };
        var col = Ui.Col(14, Ui.Row(10, marker, Ui.Text(title, "h2")));
        if (hint is not null) col.Children.Add(Ui.Text(hint, "muted", wrap: true));
        // Между соседними строками-переключателями — тонкая линия, чтобы строки не слипались.
        Control? prev = null;
        foreach (var row in rows)
        {
            if ((prev is Border { Tag: "toggle" } or DockPanel) && (row is Border { Tag: "toggle" } or DockPanel)) col.Children.Add(new Border { Height = 1, Background = Ui.Res("Line"), Opacity = 0.6 });
            col.Children.Add(row);
            prev = row;
        }
        return Ui.Card(col, 24);
    }

    /// <summary>Строка «название, пояснение — переключатель»; вся строка кликабельна и подсвечивается.</summary>
    Control Toggle(string title, string hint, bool value, Action<bool> set)
    {
        var sw = new ToggleSwitch { IsChecked = value, OnContent = "", OffContent = "", MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        sw.IsCheckedChanged += (_, _) => { set(sw.IsChecked == true); Settings.Save(); };
        var row = new DockPanel();
        DockPanel.SetDock(sw, Dock.Right);
        row.Children.Add(sw);
        row.Children.Add(Ui.Col(3, Ui.Text(title, "h3"), Ui.Text(hint, "small muted", wrap: true)));
        var box = new Border { Tag = "toggle", Child = row, Padding = new Thickness(10, 8), Margin = new Thickness(-10, -4), CornerRadius = new CornerRadius(12), Background = Brushes.Transparent, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
        box.PointerEntered += (_, _) => box.Background = Ui.Res("Surface2");
        box.PointerExited += (_, _) => box.Background = Brushes.Transparent;
        box.PointerReleased += (_, e) =>
        {
            if (e.Source is Visual v && Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<ToggleSwitch>(v, true) is not null) return;
            sw.IsChecked = sw.IsChecked != true;
        };
        return box;
    }

    /// <summary>Поиск по настройкам: открыть вкладку, где встречается слово.</summary>
    public override void Search(string text)
    {
        var q = text.Trim();
        if (q == "") return;
        var hit = SearchMap.FirstOrDefault(m => m.Keys.Any(k => I18n.T(k).Replace((char)160, ' ').Contains(q, StringComparison.OrdinalIgnoreCase) || k.Contains(q, StringComparison.OrdinalIgnoreCase)));
        if (hit.Tab is null) { MainWindow.Current?.Toast(I18n.T("sys.search.none", ("q", q))); return; }
        _tab = hit.Tab;
        _filter = "";
        Build();
    }

    /// <summary>Какие настройки в каком разделе — для поиска.</summary>
    static readonly (string Tab, string[] Keys)[] SearchMap =
        [
            ("general", ["settings.language", "v11.set.startup", "sys.tray", "sys.autostart", "look.start", "v11.set.net", "offline.title", "sys.notify", "sys.toast", "sys.transfer", "sys.confirm", "v11.set.statusBar"]),
            ("look", ["v10.set.configs", "v11.set.theme", "look.theme", "look.accent", "v11.set.size", "look.scale", "look.font", "v11.set.headings", "v10.set.motion", "look.anim", "v11.set.home", "v10.set.hero", "v10.set.widgets", "look.rail", "look.compact", "v11.set.fine", "v10.set.colors", "look.radius", "look.bg", "look.glow"]),
            ("games", ["settings.games", "games.deep", "look.games", "nx.hidden.title", "v94.set.officialArt", "feed.setting"]),
            ("launch", ["v11.set.launch", "ov.title", "launch.args", "launch.time", "v11.inj.mode", "v10.clean.title", "v10.clean.on"]),
            ("backups", ["settings.tab.backups", "bak.keep", "v10.cloud.section", "v10.cloud.on"]),
            ("downloads", ["v4.archive", "dl.parallel", "dl.pieces", "dl.limit", "settings.tab.updates", "upd.auto", "upd.beforePlay", "upd.app.auto"]),
            ("graphics", ["settings.tab.graphics"]),
            ("accounts", ["settings.tab.accounts", "acc.title"]),
        ];

    public override string SearchHint => I18n.T("sys.search");

    Control GamesTab()
    {
        var list = new StackPanel { Spacing = 10 };
        foreach (var g in AppState.Games)
        {
            var status = g.Status switch
            {
                Detect.Found => Ui.ShortPath(g.Path!),
                Detect.Searching => g.SearchingWhere is null ? I18n.T("games.searching") : I18n.T("games.searchingWhere", ("where", g.SearchingWhere)),
                Detect.NotFound => I18n.T("games.notDetected"),
                _ => I18n.T("games.notSearched"),
            };
            var info = Ui.Col(4, Ui.Text(g.Def.Name, "h3"), Ui.Text(status, "small muted"));
            if (g.Path is not null) ToolTip.SetTip(info, g.Path);
            info.VerticalAlignment = VerticalAlignment.Center;
            var buttons = Ui.Row(6,
                Ui.Button("", () => Actions.PickGameFolder(g), "icon", Icons.Folder, I18n.T("games.setPath")),
                Ui.Button("", () => _ = AppState.DetectOne(g), "icon", Icons.Refresh, I18n.T("games.detectAgain")));
            if (g.Path is not null)
                buttons.Children.Add(Ui.Button("", () => { AppState.SetPath(g, null); MainWindow.Current?.Toast(I18n.T("toast.pathForgotten")); _ = AppState.DetectOne(g); }, "icon ghost", Icons.Trash, I18n.T("games.forget")));
            buttons.VerticalAlignment = VerticalAlignment.Center;

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 14 };
            var art = Images.Game(g.Def, 120);
            grid.Children.Add(new Border
            {
                Width = 56, Height = 40, CornerRadius = new CornerRadius(8), ClipToBounds = true,
                Background = Ui.Hex(g.Def.Accent),
                Child = art is null ? null : new Image { Source = art, Stretch = Stretch.UniformToFill },
            });
            Grid.SetColumn(info, 1);
            grid.Children.Add(info);
            Grid.SetColumn(buttons, 2);
            grid.Children.Add(buttons);
            list.Children.Add(new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Child = grid });
        }
        var guide = Ui.Col(6, Ui.Text(I18n.T("guide.title"), "h3"),
            Ui.Text(I18n.T("guide.steam"), "small muted", wrap: true), Ui.Text(I18n.T("guide.gog"), "small muted", wrap: true),
            Ui.Text(I18n.T("guide.repack"), "small muted", wrap: true), Ui.Text(I18n.T("guide.check"), "small muted", wrap: true));
        return Ui.Col(16, Section(I18n.T("settings.games"), I18n.T("settings.games.hint"), list,
            Ui.Button(I18n.T("games.deep"), () => _ = AppState.DetectMany(AppState.Games.Where(x => x.Status != Detect.Found).ToList(), deep: true), "", Icons.Search),
            Toggle(I18n.T("v151.set.autoAdd"), I18n.T("v151.set.autoAdd.hint"), Features.NewGames.AutoAdd, v => Features.NewGames.AutoAdd = v),
            guide), GamesOrderSection(), HiddenSection());
    }

    // ---------------------------------------------------------------- запуск и игровое время

    /// <summary>10.0: профиль в облаке — сборки, награды и стиль с аккаунтом.</summary>
    Control CloudSection()
    {
        var signed = Social.Account.SignedIn;
        var last = Social.CloudProfile.LastSync is { } at && DateTime.TryParse(at, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? Ui.Ago(t) : null;
        var state = Ui.Text(!signed ? I18n.T("v10.cloud.signIn") : last is null ? I18n.T("v10.cloud.never") : I18n.T("v10.cloud.last", ("ago", last)), "small muted", wrap: true);
        var row = Ui.Row(8, Ui.Button(I18n.T("v10.cloud.now"), () => { _ = Task.Run(() => CloudFlow.Check(manual: true)); }, "", Icons.Refresh));
        row.IsEnabled = signed;
        return Section(I18n.T("v10.cloud.section"), I18n.T("v10.cloud.hint"),
            Toggle(I18n.T("v10.cloud.on"), I18n.T("v10.cloud.on.hint"), Social.CloudProfile.On, v => Settings.Data["cloudSync"] = v),
            state, row);
    }

    Control LaunchTab()
    {
        var col = new StackPanel { Spacing = 16 };
        var after = new ComboBox { Width = 260 };
        after.Items.Add(I18n.T("launch.after.stay"));
        after.Items.Add(I18n.T("launch.after.minimize"));
        after.SelectedIndex = Settings.Data.Str("afterLaunch") == "minimize" ? 1 : 0;
        after.SelectionChanged += (_, _) => { Settings.Data["afterLaunch"] = after.SelectedIndex == 1 ? "minimize" : "stay"; Settings.Save(); Build(); };
        var afterRow = new DockPanel();
        DockPanel.SetDock(after, Dock.Right);
        afterRow.Children.Add(after);
        afterRow.Children.Add(Ui.Col(3, Ui.Text(I18n.T("launch.after"), "h3"), Ui.Text(I18n.T("launch.after.hint"), "small muted")));
        var rows = new List<Control> { afterRow };
        if (Settings.Data.Str("afterLaunch") == "minimize")
            rows.Add(Toggle(I18n.T("launch.restore"), I18n.T("launch.restore.hint"), Settings.Data.Bool("restoreAfterGame", true), v => Settings.Data["restoreAfterGame"] = v));
        rows.Add(Toggle(I18n.T("launch.track"), I18n.T("launch.track.hint"), PlayTime.Track, v => Settings.Data["trackPlaytime"] = v));
        rows.Add(Toggle(I18n.T("crash.setting"), I18n.T("crash.setting.hint"), Settings.Data.Bool("crashHelper", true), v => Settings.Data["crashHelper"] = v));
        rows.Add(Toggle(I18n.T("set.checkBeforePlay"), I18n.T("set.checkBeforePlay.hint"), Health.BeforePlay, v => Settings.Data["checkBeforePlay"] = v));
        rows.Add(Toggle(I18n.T("v10.ap.before"), I18n.T("v10.ap.before.hint"), Autopilot.BeforePlay, v => Settings.Data["autopilotBeforePlay"] = v));
        col.Children.Add(Section(I18n.T("settings.tab.launch"), null, rows.ToArray()));
        col.Children.Add(InjectorSection());

        // Оверлей в игре.
        var key = new ComboBox { Width = 200 };
        foreach (var k in Hotkey.Keys) key.Items.Add(Hotkey.Display(k));
        key.SelectedIndex = Array.IndexOf(Hotkey.Keys, Hotkey.KeyOf(Settings.Data.Str("overlayKey")));
        key.SelectionChanged += (_, _) => { if (key.SelectedIndex >= 0) { Settings.Data["overlayKey"] = Hotkey.Keys[key.SelectedIndex]; Settings.Save(); } };
        var keyRow = new DockPanel();
        DockPanel.SetDock(key, Dock.Right);
        keyRow.Children.Add(key);
        keyRow.Children.Add(Ui.Col(3, Ui.Text(I18n.T("ov.key"), "h3"), Ui.Text(I18n.T("ov.key.hint"), "small muted")));
        var preview = new DockPanel();
        var go = Ui.Button(I18n.T("ov.preview.go"), () =>
        {
            OverlayWindow.GameId = AppState.Games.FirstOrDefault(g => g.Status == Detect.Found)?.Def.Id;
            OverlayWindow.StartedAt ??= DateTime.UtcNow;
            OverlayWindow.Preview = true;
            OverlayWindow.Toggle();
        }, "", Icons.Play);
        DockPanel.SetDock(go, Dock.Right);
        preview.Children.Add(go);
        preview.Children.Add(Ui.Col(3, Ui.Text(I18n.T("ov.preview"), "h3"), Ui.Text(I18n.T("ov.preview.hint"), "small muted")));
        col.Children.Add(Section(I18n.T("ov.title"), null,
            Toggle(I18n.T("ov.on"), I18n.T("ov.on.hint"), Settings.Data.Bool("overlay", true), v => Settings.Data["overlay"] = v),
            Toggle(I18n.T("launch.hint.setting"), I18n.T("launch.hint.setting.hint"), Settings.Data.Bool("overlayHint", true), v => Settings.Data["overlayHint"] = v),
            keyRow, preview));

        // Параметры запуска по играм.
        var found = AppState.Games.Where(g => g.Status == Detect.Found).ToList();
        var args = new StackPanel { Spacing = 10 };
        if (found.Count == 0) args.Children.Add(Ui.Text(I18n.T("launch.noGames"), "muted"));
        foreach (var g in found)
        {
            var box = new TextBox { Text = Launcher.Args(g.Def.Id), Watermark = I18n.T("launch.args.placeholder") };
            var id = g.Def.Id;
            box.LostFocus += (_, _) =>
            {
                if ((box.Text ?? "").Trim() == Launcher.Args(id)) return;
                Launcher.SetArgs(id, box.Text ?? "");
                MainWindow.Current?.Toast(I18n.T("launch.args.saved"));
            };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("200,*"), ColumnSpacing = 12 };
            var label = Ui.Text(g.Def.Name, "h3");
            label.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(label);
            Grid.SetColumn(box, 1);
            row.Children.Add(box);
            args.Children.Add(row);
        }
        col.Children.Add(Section(I18n.T("launch.args"), I18n.T("launch.args.hint"), args));

        // Игровое время.
        var times = new StackPanel { Spacing = 8 };
        var any = false;
        foreach (var g in AppState.Games)
        {
            var p = PlayTime.Get(g.Def.Id);
            if (p.TotalMs <= 0 && !p.Running) continue;
            any = true;
            var sessions = I18n.T("time.sessions." + I18n.Plural(p.Sessions, "one", "few", "many"), ("n", p.Sessions));
            var line = $"{PlayTime.Format(p.TotalMs)} · {sessions}" + (p.LastPlayed is { } last ? " · " + I18n.T("time.last", ("when", Ui.Ago(last))) : "");
            var row = new DockPanel();
            var value = Ui.Text(line, "muted");
            DockPanel.SetDock(value, Dock.Right);
            row.Children.Add(value);
            row.Children.Add(Ui.Text(g.Def.Name, "h3"));
            times.Children.Add(row);
        }
        times.Children.Add(Ui.Button(I18n.T("v11.bt.tab"), () => MainWindow.Current?.Navigate(() => new ProfilePage("builds")), "", Icons.Hourglass));
        if (!any) times.Children.Add(Ui.Text(I18n.T("launch.time.none"), "muted", wrap: true));
        else times.Children.Add(Ui.Button(I18n.T("launch.time.reset"), () =>
        {
            var w = MainWindow.Current!;
            w.Dialog(I18n.T("launch.time.reset"), Ui.Text(I18n.T("launch.time.reset.hint"), "muted"),
                Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
                Ui.Button(I18n.T("launch.time.reset.go"), () => { w.CloseDialog(); PlayTime.Reset(null); Build(); }, "primary"));
        }, "ghost", Icons.Trash));
        col.Children.Add(Section(I18n.T("launch.time"), null, times));
        return col;
    }

    // ---------------------------------------------------------------- резервные копии

    Control BackupsTab()
    {
        var col = new StackPanel { Spacing = 16 };
        // 11.0: сначала бэкап в один клик (всё сразу, в папку, облачный сервис или облако ModLaunch), ниже — копии сохранений игр.
        col.Children.Add(BackupPanel.Build(Build));
        col.Children.Add(CloudSection());
        var keep = new NumericUpDown { Minimum = 1, Maximum = 200, Value = Backups.Keep, Width = 140, FormatString = "0" };
        keep.ValueChanged += (_, _) => { Settings.Data["backupKeep"] = (int)(keep.Value ?? 10); Settings.Save(); };
        var keepRow = new DockPanel();
        DockPanel.SetDock(keep, Dock.Right);
        keepRow.Children.Add(keep);
        keepRow.Children.Add(Ui.Col(3, Ui.Text(I18n.T("bak.keep"), "h3"), Ui.Text(I18n.T("bak.keep.hint"), "small muted")));
        col.Children.Add(Section(I18n.T("v11.bk.saves.title"), null,
            Toggle(I18n.T("bak.onLaunch"), I18n.T("bak.onLaunch.hint"), Backups.OnLaunch, v => Settings.Data["backupOnLaunch"] = v),
            keepRow,
            Ui.Row(10, Ui.Text(I18n.T("bak.folder") + ": " + Backups.Root, "small muted"), Ui.Button("", () => { Directory.CreateDirectory(Backups.Root); Actions.OpenFolder(Backups.Root); }, "icon ghost", Icons.Folder))));

        var list = new StackPanel { Spacing = 8 };
        foreach (var g in AppState.Games)
        {
            var items = Backups.List(g.Def.Id);
            var text = items.Count == 0 ? I18n.T("bak.summary.none") : I18n.T("bak.summary",
                ("n", I18n.T("bak.copies." + I18n.Plural(items.Count, "one", "few", "many"), ("n", items.Count))),
                ("size", GamePage.Size(items.Sum(b => b.Size))),
                ("when", Ui.Ago(items[0].At.ToUniversalTime())));
            var row = new DockPanel();
            var value = Ui.Text(text, "muted");
            DockPanel.SetDock(value, Dock.Right);
            row.Children.Add(value);
            row.Children.Add(Ui.Text(g.Def.Name, "h3"));
            list.Children.Add(row);
        }
        col.Children.Add(Section(I18n.T("bak.games"), I18n.T("bak.games.hint"), list));
        return col;
    }

    // ---------------------------------------------------------------- архив загрузок (как в Vortex)

    Control DownloadsTab()
    {
        var (count, bytes) = DownloadArchive.Size();
        var limit = new NumericUpDown { Minimum = 1, Maximum = 500, Value = (decimal)DownloadArchive.LimitGb, Width = 140, FormatString = "0" };
        limit.ValueChanged += (_, _) => { Settings.Data["archiveLimitGb"] = (double)(limit.Value ?? 10); Settings.Save(); };
        var limitRow = new DockPanel();
        DockPanel.SetDock(limit, Dock.Right);
        limitRow.Children.Add(limit);
        limitRow.Children.Add(Ui.Text(I18n.T("v4.archive.limit"), "h3"));
        var parallel = new NumericUpDown { Minimum = 1, Maximum = 6, Value = Jobs.Limit, Width = 140, FormatString = "0", Increment = 1, VerticalAlignment = VerticalAlignment.Center };
        parallel.ValueChanged += (_, _) => { Settings.Data["maxParallel"] = (int)(parallel.Value ?? 3); Settings.Save(); Jobs.LimitChanged(); };
        var parallelRow = new DockPanel();
        DockPanel.SetDock(parallel, Dock.Right);
        parallelRow.Children.Add(parallel);
        parallelRow.Children.Add(Ui.Col(2, Ui.Text(I18n.T("dl.parallel"), "h3"), Ui.Text(I18n.T("dl.parallel.hint"), "small muted", wrap: true)));

        // 9.4: большие файлы — в несколько потоков; общий предел скорости.
        var pieces = new ComboBox { Width = 140, VerticalAlignment = VerticalAlignment.Center };
        int[] pieceValues = [1, 2, 4, 6, 8];
        foreach (var v in pieceValues) pieces.Items.Add(v == 1 ? I18n.T("dl.pieces.off") : v.ToString(I18n.Culture));
        pieces.SelectedIndex = Math.Max(0, Array.IndexOf(pieceValues, Http.Segments));
        pieces.SelectionChanged += (_, _) =>
        {
            if (pieces.SelectedIndex < 0) return;
            Settings.Data["dlSegments"] = pieceValues[pieces.SelectedIndex];
            Settings.Save();
            Http.ApplySettings();
        };
        var piecesRow = new DockPanel();
        DockPanel.SetDock(pieces, Dock.Right);
        piecesRow.Children.Add(pieces);
        piecesRow.Children.Add(Ui.Col(2, Ui.Text(I18n.T("dl.pieces"), "h3"), Ui.Text(I18n.T("dl.pieces.hint"), "small muted", wrap: true)));

        var speed = new ComboBox { Width = 140, VerticalAlignment = VerticalAlignment.Center };
        double[] speedValues = [0, 1, 2, 5, 10, 20, 50];
        foreach (var v in speedValues) speed.Items.Add(v == 0 ? I18n.T("dl.limit.none") : I18n.T("dl.limit.mb", ("n", v.ToString("0", I18n.Culture))));
        var current = Settings.Data.Double("dlLimitMb");
        speed.SelectedIndex = Math.Max(0, Array.FindIndex(speedValues, v => Math.Abs(v - current) < 0.01));
        speed.SelectionChanged += (_, _) =>
        {
            if (speed.SelectedIndex < 0) return;
            Settings.Data["dlLimitMb"] = speedValues[speed.SelectedIndex];
            Settings.Save();
            Http.ApplySettings();
        };
        var speedRow = new DockPanel();
        DockPanel.SetDock(speed, Dock.Right);
        speedRow.Children.Add(speed);
        speedRow.Children.Add(Ui.Col(2, Ui.Text(I18n.T("dl.limit"), "h3"), Ui.Text(I18n.T("dl.limit.hint"), "small muted", wrap: true)));

        return Ui.Col(16, Section(I18n.T("dl.title"), I18n.T("dl.section.hint"), parallelRow, piecesRow, speedRow), Section(I18n.T("v4.archive"), null,
            Toggle(I18n.T("v4.archive.keep"), I18n.T("v4.archive.hint"), DownloadArchive.Keep, v => Settings.Data["keepArchives"] = v),
            limitRow,
            Ui.Text(I18n.T("v4.archive.size", ("n", count), ("size", GamePage.Size(bytes))), "muted"),
            Ui.Row(10,
                Ui.Button(I18n.T("games.openFolder"), () => Actions.OpenFolder(DownloadArchive.Root), "", Icons.Folder),
                Ui.Button(I18n.T("v4.archive.clear"), () => { DownloadArchive.Clear(); MainWindow.Current?.Toast(I18n.T("v4.archive.cleared")); Build(); }, "ghost", Icons.Trash))), StorageSection(), UpdatesSection());
    }

    // ---------------------------------------------------------------- DXVK

    Control GraphicsTab()
    {
        var col = new StackPanel { Spacing = 16 };
        var intro = Section(I18n.T("dxvk.title"), I18n.T("dxvk.lead"),
            Ui.Text("• " + I18n.T("dxvk.note.vulkan"), "small muted", wrap: true),
            Ui.Text("• " + I18n.T("dxvk.note.online"), "small muted", wrap: true),
            Ui.Button(I18n.T("dxvk.add"), PickExe, "primary", Icons.FilePlus));
        col.Children.Add(intro);
        if (_dxvkPicked is not null) col.Children.Add(DxvkPicked(_dxvkPicked));

        var list = new StackPanel { Spacing = 8 };
        var entries = Dxvk.List();
        if (entries.Count == 0) list.Children.Add(Ui.Text(I18n.T("dxvk.empty"), "muted"));
        foreach (var e in entries)
        {
            var status = !e.Exists ? I18n.T("dxvk.missing")
                : e.State.Installed ? I18n.T("dxvk.on", ("version", e.State.Version), ("api", ApiName(e.State.Api)), ("bits", e.State.Arch == "x64" ? "64" : "32"))
                : I18n.T("dxvk.off");
            var info = Ui.Col(3, Ui.Text(e.Name, "h3"), Ui.Text(status, "small", color: e.State.Installed ? Ui.Res("Good") : Ui.Res("Muted")));
            info.VerticalAlignment = VerticalAlignment.Center;
            var exe = e.Exe;
            var buttons = Ui.Row(6);
            if (e.Exists && e.State.Installed)
                buttons.Children.Add(Ui.Button(I18n.T("dxvk.remove"), () =>
                {
                    try { Dxvk.Remove(Path.GetDirectoryName(exe)!); MainWindow.Current?.Toast(I18n.T("dxvk.removed")); } catch (Exception x) { MainWindow.Current?.Toast(Jobs.Explain(x), bad: true); }
                    Build();
                }));
            else if (e.Exists)
                buttons.Children.Add(Ui.Button(I18n.T("dxvk.install"), () => { _dxvkPicked = Dxvk.Inspect(exe); _dxvkApi = _dxvkPicked.Api; Build(); }, "primary"));
            buttons.Children.Add(Ui.Button("", () => Actions.OpenFolder(exe), "icon ghost", Icons.Folder));
            buttons.Children.Add(Ui.Button("", () => { Dxvk.Forget(exe); Build(); }, "icon ghost", Icons.Trash, I18n.T("dxvk.forget")));
            buttons.VerticalAlignment = VerticalAlignment.Center;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            row.Children.Add(info);
            Grid.SetColumn(buttons, 1);
            row.Children.Add(buttons);
            list.Children.Add(new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10), Child = row });
        }
        col.Children.Add(Section(I18n.T("dxvk.games"), null, list));
        return col;
    }

    static string ApiName(string? api) => api switch
    {
        "dx8" => "DirectX 8", "dx9" => "DirectX 9", "dx10" => "DirectX 10", "dx11" => "DirectX 11", "dx12" => "DirectX 12",
        "opengl" => "OpenGL", "vulkan" => "Vulkan", _ => I18n.T("dxvk.api.unknown"),
    };

    async void PickExe()
    {
        var exe = await MainWindow.Current!.PickExe(I18n.T("dialog.pickExe"));
        if (exe is null) return;
        try { _dxvkPicked = Dxvk.Inspect(exe); _dxvkApi = _dxvkPicked.Supported ? _dxvkPicked.Api : null; }
        catch (Exception e) { MainWindow.Current?.Toast(Jobs.Explain(e), bad: true); return; }
        Build();
    }

    Control DxvkPicked(ExeInfo info)
    {
        var state = Dxvk.Status(info.Dir);
        var col = Ui.Col(12,
            Ui.Text(info.Name, "h2"),
            Ui.Text($"{info.Exe} · {(info.Arch == "x64" ? I18n.T("dxvk.bits64") : I18n.T("dxvk.bits32"))} · {ApiName(info.Api)} ({(info.Api is null ? I18n.T("dxvk.notDetected") : I18n.T("dxvk.detected"))})", "small muted", wrap: true));
        if (info.Api is "dx12") col.Children.Add(Ui.Text(I18n.T("dxvk.dx12", ("api", ApiName(info.Api))), "muted", wrap: true));
        else if (info.Api is "vulkan" or "opengl") col.Children.Add(Ui.Text(I18n.T("dxvk.native", ("api", ApiName(info.Api))), "muted", wrap: true));
        if (state.Installed) col.Children.Add(Ui.Text(I18n.T("dxvk.reinstall", ("version", state.Version)), "small brand"));

        col.Children.Add(Ui.Text(info.Supported ? I18n.T("dxvk.choose.change") : I18n.T("dxvk.choose"), "small muted", wrap: true));
        var chips = Ui.Row(8);
        foreach (var api in new[] { "dx8", "dx9", "dx10", "dx11" })
        {
            var a = api;
            var chip = Ui.Button(ApiName(api), () => { _dxvkApi = a; Build(); }, "chip");
            if (_dxvkApi == api) chip.Classes.Add("active");
            chips.Children.Add(chip);
        }
        col.Children.Add(chips);
        if (!info.Supported) col.Children.Add(Ui.Text(I18n.T("dxvk.choose.hint"), "small muted", wrap: true));

        var busy = Jobs.All.Any(j => j.Active && j.Title == "DXVK");
        var go = Ui.Button(busy ? I18n.T("dxvk.working") : I18n.T("dxvk.install"), () =>
        {
            var exe = info.Exe;
            var api = _dxvkApi;
            var name = info.Name;
            Jobs.Run("DXVK", name, async (_, progress, ct) =>
            {
                var (version, _) = await Dxvk.Install(exe, api, progress, ct);
                Dxvk.Remember(exe, name);
                Avalonia.Threading.Dispatcher.UIThread.Post(() => { MainWindow.Current?.Toast(I18n.T("dxvk.done", ("version", version), ("name", name))); _dxvkPicked = null; Build(); });
            });
            Build();
        }, "primary", Icons.Download);
        go.IsEnabled = !busy && _dxvkApi is not null;
        col.Children.Add(Ui.Row(10, Ui.Button(I18n.T("common.cancel"), () => { _dxvkPicked = null; Build(); }), go));
        return Ui.Card(col, 22);
    }

    // ---------------------------------------------------------------- обновления модов

    async void CheckAll()
    {
        MainWindow.Current?.Toast(I18n.T("upd.checking"));
        foreach (var g in AppState.Games.Where(g => g.Status == Detect.Found && g.ModCount > 0))
        {
            try { await ModUpdates.Check(g); } catch { }
        }
        Build();
    }

    // ---------------------------------------------------------------- аккаунты

    Control AccountsTab()
    {
        var box = new TextBox { Text = Settings.NexusApiKey ?? "", PasswordChar = '•', Watermark = "API key", Width = 420 };
        var check = Ui.Button(I18n.T("settings.nexus.check"), async () =>
        {
            var key = box.Text?.Trim() ?? "";
            Settings.Data["nexusApiKey"] = key == "" ? null : key;
            Settings.Data["nexusPremium"] = false;
            Settings.Save();
            if (key == "") { _keyStatus = null; Build(); return; }
            try
            {
                var (name, premium) = await Nexus.ValidateKey(key);
                Settings.Data["nexusPremium"] = premium;
                Settings.Save();
                _keyStatus = I18n.T("settings.nexus.ok", ("name", name)) + (premium ? " · Premium" : "");
            }
            catch (Exception e) { _keyStatus = Jobs.Explain(e); }
            Build();
        }, "primary", Icons.Check);
        var rows = new List<Control>
        {
            Ui.Row(10, box, check),
            Ui.Button("nexusmods.com/users/myaccount?tab=api", () => Ui.OpenUrl("https://www.nexusmods.com/users/myaccount?tab=api"), "ghost", Icons.External),
        };
        if (_keyStatus is not null) rows.Insert(1, Ui.Text(_keyStatus, "small brand"));

        var registered = Nxm.IsRegistered();
        var nxm = new DockPanel();
        if (!registered && OperatingSystem.IsWindows())
        {
            var fix = Ui.Button(I18n.T("common.continue"), () => { try { Nxm.Register(); } catch (Exception e) { MainWindow.Current?.Toast(e.Message, bad: true); } Build(); }, "", Icons.Link);
            DockPanel.SetDock(fix, Dock.Right);
            nxm.Children.Add(fix);
        }
        nxm.Children.Add(Ui.Col(3, Ui.Text(I18n.T("settings.nxm"), "h3"),
            Ui.Text(registered ? I18n.T("settings.nxm.yes") : I18n.T("settings.nxm.no"), "small", color: registered ? Ui.Res("Good") : Ui.Res("Muted"))));
        rows.Add(nxm);

        // 9.8: ключ Claude для «ИИ-сборок» (без ключа работает встроенный подборщик).
        var aiBox = new TextBox { Text = Settings.Data.Str("aiKey") ?? "", PasswordChar = '•', Watermark = "sk-ant-…", Width = 420 };
        var aiSave = Ui.Button(I18n.T("v98.key.save"), () =>
        {
            var key = aiBox.Text?.Trim() ?? "";
            Settings.Data["aiKey"] = key == "" ? null : key;
            Settings.Save();
            MainWindow.Current?.Toast(I18n.T(key == "" ? "v98.key.removed" : "v98.key.saved"));
        }, "", Icons.Sparkles);
        rows.Add(Ui.Col(6, Ui.Text(I18n.T("v98.key.title"), "h3"), Ui.Text(I18n.T("v98.key.text"), "small muted", wrap: true), Ui.Row(10, aiBox, aiSave)));

        // Кнопка Thunderstore «Install with Mod Manager» (ror2mm://) — по желанию: она заберёт её у r2modman.
        var r2 = Nxm.IsRegistered("ror2mm");
        var ror = new DockPanel();
        if (OperatingSystem.IsWindows())
        {
            // Взяли кнопку — можно и отдать обратно (прежней программе, например r2modman).
            var take = r2
                ? Ui.Button(I18n.T("link.ror2mm.give"), () => { try { Nxm.Unregister("ror2mm"); } catch (Exception e) { MainWindow.Current?.Toast(e.Message, bad: true); } Build(); }, "ghost", Icons.Back)
                : Ui.Button(I18n.T("link.ror2mm.take"), () => { try { Nxm.Register("ror2mm"); } catch (Exception e) { MainWindow.Current?.Toast(e.Message, bad: true); } Build(); }, "", Icons.Link);
            DockPanel.SetDock(take, Dock.Right);
            ror.Children.Add(take);
        }
        ror.Children.Add(Ui.Col(3, Ui.Text(I18n.T("link.ror2mm"), "h3"),
            Ui.Text(r2 ? I18n.T("link.ror2mm.yes") : I18n.T("link.ror2mm.no"), "small", color: r2 ? Ui.Res("Good") : Ui.Res("Muted"), wrap: true)));
        rows.Add(ror);
        return Ui.Col(16, AccountSection(), Section(I18n.T("settings.nexus"), I18n.T("settings.nexus.hint"), rows.ToArray()));
    }

    // ---------------------------------------------------------------- аккаунт ModLaunch

    Control AccountSection()
    {
        var p = Social.Account.Get();
        if (!p.Configured) return Section(I18n.T("acc.title"), I18n.T("acc.off"));
        return p.SignedIn ? SignedIn(p) : SignForm();
    }

    async void AccountCall(Func<Task> action, string? ok = null)
    {
        _accBusy = true;
        Build();
        try
        {
            await action();
            if (ok is not null) MainWindow.Current?.Toast(ok);
        }
        catch (Exception e) { MainWindow.Current?.Toast(Social.Account.Explain(e), bad: true); }
        _accBusy = false;
        Build();
    }

    Control SignForm()
    {
        var tabs = Ui.Row(6);
        foreach (var (id, key) in new[] { ("signin", "acc.tab.signin"), ("signup", "acc.tab.signup") })
        {
            var b = Ui.Button(I18n.T(key), () => { _accMode = id; Build(); }, "chip");
            if (_accMode == id) b.Classes.Add("active");
            tabs.Children.Add(b);
        }
        var name = new TextBox { Watermark = I18n.T("acc.name"), MaxLength = 32 };
        var email = new TextBox { Watermark = I18n.T("acc.email") };
        var password = new TextBox { Watermark = I18n.T("acc.password"), PasswordChar = '•', MaxLength = 128 };
        var strength = Ui.Text(I18n.T("acc.pass.0"), "small muted");
        password.TextChanged += (_, _) =>
        {
            var t = password.Text ?? "";
            var score = t.Length < Social.Account.MinPassword ? 0 : (t.Length >= 12 ? 1 : 0) + (t.Any(char.IsDigit) && t.Any(char.IsLetter) ? 1 : 0) + (t.Any(c => !char.IsLetterOrDigit(c)) ? 1 : 0);
            strength.Text = I18n.T($"acc.pass.{Math.Clamp(score, 0, 3)}");
        };

        var col = Ui.Col(12);
        if (_accMode == "reset")
        {
            col.Children.Add(Ui.Text(I18n.T("acc.reset.title"), "h2"));
            col.Children.Add(Ui.Text(I18n.T("acc.reset.lead"), "muted", wrap: true));
            col.Children.Add(email);
            col.Children.Add(Ui.Row(10,
                Ui.Button(_accBusy ? I18n.T("acc.reset.busy") : I18n.T("acc.reset.go"), () => AccountCall(() => Social.Account.ResetPassword(email.Text), I18n.T("acc.reset.sent", ("email", email.Text ?? ""))), "primary"),
                Ui.Button(I18n.T("acc.backToSignin"), () => { _accMode = "signin"; Build(); }, "ghost")));
            return Ui.Card(col, 24);
        }
        col.Children.Add(Ui.Text(I18n.T("acc.out.title"), "h2"));
        col.Children.Add(Ui.Col(4, Ui.Text("✓ " + I18n.T("acc.perk.1"), "small muted"), Ui.Text("✓ " + I18n.T("acc.perk.2"), "small muted"), Ui.Text("✓ " + I18n.T("acc.perk.3"), "small muted")));
        col.Children.Add(tabs);
        if (_accMode == "signup") col.Children.Add(Ui.Col(4, name, Ui.Text(I18n.T("acc.name.hint"), "small muted")));
        col.Children.Add(email);
        col.Children.Add(password);
        if (_accMode == "signup") col.Children.Add(strength);
        var go = _accMode == "signup"
            ? Ui.Button(_accBusy ? I18n.T("acc.signup.busy") : I18n.T("acc.signup.go"), () => AccountCall(async () =>
              {
                  var p = await Social.Account.SignUp(name.Text ?? "", email.Text ?? "", password.Text ?? "");
                  MainWindow.Current?.Toast(I18n.T("acc.welcomeNew", ("name", p.Name ?? "")));
              }), "primary", Icons.User)
            : Ui.Button(_accBusy ? I18n.T("acc.signin.busy") : I18n.T("acc.signin.go"), () => AccountCall(async () =>
              {
                  var p = await Social.Account.SignIn(email.Text ?? "", password.Text ?? "");
                  MainWindow.Current?.Toast(I18n.T("acc.welcome", ("name", p.Name ?? "")));
              }), "primary", Icons.Key);
        go.IsEnabled = !_accBusy;
        var buttons = Ui.Row(10, go);
        if (_accMode == "signin") buttons.Children.Add(Ui.Button(I18n.T("acc.forgot"), () => { _accMode = "reset"; Build(); }, "ghost"));
        col.Children.Add(buttons);
        col.Children.Add(Ui.Text(I18n.T("acc.note"), "small muted", wrap: true));
        return Ui.Card(col, 24);
    }

    Control SignedIn(Social.Profile p)
    {
        var col = Ui.Col(14);
        var who = Ui.Row(12,
            new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(24), Background = Ui.Res("BrandSoft"), Child = new TextBlock { Text = (p.Name ?? "?")[..1].ToUpperInvariant(), FontSize = 22, FontWeight = FontWeight.Bold, Foreground = Ui.Res("Brand2"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } },
            Ui.Col(3, Ui.Text(p.Name ?? "", "h2"), Ui.Text(p.Email ?? "", "small muted"),
                Ui.Text(p.Verified ? "✓ " + I18n.T("acc.verified") : I18n.T("acc.unverified"), "small", color: p.Verified ? Ui.Res("Good") : Ui.Res("Warn"))));
        col.Children.Add(Ui.Text(I18n.T("acc.title"), "h2"));
        col.Children.Add(who);
        if (p.Admin) col.Children.Add(Ui.Col(3, Ui.Text("👑 " + I18n.T("acc.admin.title"), "h3"), Ui.Text(I18n.T("acc.admin.text"), "small muted", wrap: true)));
        else if (p.AdminPending) col.Children.Add(Ui.Text(I18n.T("acc.admin.pending"), "small muted", wrap: true));
        if (!p.Verified)
        {
            col.Children.Add(Ui.Text(I18n.T("acc.verify.text", ("email", p.Email ?? "")), "small muted", wrap: true));
            col.Children.Add(Ui.Row(8,
                Ui.Button(I18n.T("acc.verify.again"), () => AccountCall(Social.Account.SendVerification, I18n.T("acc.verify.sent", ("email", p.Email ?? "")))),
                Ui.Button(I18n.T("acc.verify.check"), () => AccountCall(async () =>
                {
                    var fresh = await Social.Account.RefreshProfile();
                    MainWindow.Current?.Toast(fresh.Verified ? I18n.T("acc.verify.done") : I18n.T("acc.verify.notYet"), bad: !fresh.Verified);
                }), "primary")));
        }
        var rename = new TextBox { Text = p.Name, MaxLength = 32, Width = 260 };
        col.Children.Add(Ui.Col(6, Ui.Text(I18n.T("acc.rename"), "h3"), Ui.Text(I18n.T("acc.rename.hint"), "small muted"),
            Ui.Row(8, rename, Ui.Button(I18n.T("common.save"), () => AccountCall(() => Social.Account.Rename(rename.Text ?? ""), I18n.T("acc.renamed"))))));
        col.Children.Add(Ui.Col(6, Ui.Text(I18n.T("acc.password.change"), "h3"), Ui.Text(I18n.T("acc.password.hint", ("email", p.Email ?? "")), "small muted"),
            Ui.Button(I18n.T("acc.password.send"), () => AccountCall(() => Social.Account.ResetPassword(p.Email), I18n.T("acc.password.sent", ("email", p.Email ?? ""))))));
        col.Children.Add(Ui.Row(10,
            Ui.Button(I18n.T("acc.signout"), () => { Social.Account.SignOut(); MainWindow.Current?.Toast(I18n.T("acc.signedOut")); Build(); }, "", Icons.Close),
            Ui.Button(I18n.T("acc.delete.title"), DeleteAccount, "ghost", Icons.Trash)));
        col.Children.Add(Ui.Text(p.Since is DateTime since ? I18n.T("acc.delete.hint", ("since", since.ToString("d", I18n.Culture))) : I18n.T("acc.delete.hintPlain"), "small muted"));
        return Ui.Card(col, 24);
    }

    void DeleteAccount()
    {
        var w = MainWindow.Current!;
        var password = new TextBox { Watermark = I18n.T("acc.password"), PasswordChar = '•' };
        w.Dialog(I18n.T("acc.delete.title"), Ui.Col(10, Ui.Text(I18n.T("acc.delete.text"), "muted", wrap: true), password),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("acc.delete.go"), () =>
            {
                var pass = password.Text ?? "";
                w.CloseDialog();
                AccountCall(async () =>
                {
                    try { await Social.Friends.Forget(); } catch { }
                    await Social.Account.Delete(pass);
                }, I18n.T("acc.deleted"));
            }, "primary", Icons.Trash));
    }

    Control AboutTab()
    {
        var logo = new Image { Source = Images.Asset("icon.png", 128), Width = 64, Height = 64 };
        var version = new Button { Classes = { "ghost" }, Padding = new Thickness(0), Content = Ui.Text($"{I18n.T("upd.app.version", ("version", Http.Version))} · Avalonia UI · .NET {Environment.Version.ToString(2)}", "muted small") };
        version.Click += (_, _) =>
        {
            // Пять щелчков по версии — включить или спрятать «Статистику» (для владельца), как в 3.x.
            if (DateTime.UtcNow - _versionAt > TimeSpan.FromSeconds(1.5)) _versionClicks = 0;
            _versionAt = DateTime.UtcNow;
            if (++_versionClicks < 5) return;
            _versionClicks = 0;
            var on = !Settings.Data.Bool("ownerStats");
            Settings.Data["ownerStats"] = on;
            Settings.Save();
            MainWindow.Current?.Toast(I18n.T(on ? "stats.enabled" : "stats.disabled"));
            AppState.Notify();
        };
        var name = Ui.Col(4, Ui.Text("ModLaunch", "h2"), version,
            Ui.Button(I18n.T("nav.donate"), () => MainWindow.Current?.Navigate(() => new DonatePage()), "ghost", Icons.Coffee));
        name.VerticalAlignment = VerticalAlignment.Center;

        return Section(I18n.T("settings.tab.about"), null,
            Ui.Row(16, logo, name),
            Ui.Row(10,
                Ui.Button("GitHub", () => Ui.OpenUrl("https://github.com/ModLaunch/ModLaunch"), "", Icons.External),
                Ui.Button(I18n.T("games.openFolder"), () => Actions.OpenFolder(Paths.DataDir), "", Icons.Folder)));
    }
}
