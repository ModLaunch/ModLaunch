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

    public SettingsPage(string tab = "look") => _tab = tab;

    public override string Title => I18n.T("nav.settings");

    /// <summary>Разделы настроек по группам: id, название, иконка, цвет значка.</summary>
    static readonly (string Group, (string Id, string Key, string Icon, string Color)[] Items)[] Groups =
    [
        ("set.g.personal", [
            ("look", "settings.tab.look", Icons.Palette, "#A78BFA"),
            ("interface", "look.tab.interface", Icons.Layers, "#60A5FA"),
        ]),
        ("set.g.games", [
            ("games", "settings.tab.games", Icons.Gamepad, "#34D399"),
            ("launch", "settings.tab.launch", Icons.Play, "#22C55E"),
            ("backups", "settings.tab.backups", Icons.Shield, "#2DD4BF"),
            ("graphics", "settings.tab.graphics", Icons.Sparkles, "#F472B6"),
        ]),
        ("set.g.system", [
            ("system", "sys.tab", Icons.Settings, "#94A3B8"),
            ("downloads", "settings.tab.downloads", Icons.Download, "#38BDF8"),
            ("updates", "settings.tab.updates", Icons.ArrowUp, "#FBBF24"),
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
                var shown = items.Where(i => q == "" || I18n.T(i.Key).Contains(q, StringComparison.OrdinalIgnoreCase) || I18n.T("set.d." + i.Id).Contains(q, StringComparison.OrdinalIgnoreCase) || SearchHit(i.Id, q)).ToList();
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
                    var words = Ui.Col(1, Ui.Text(I18n.T(key), "", size: 13.5), Ui.Text(I18n.T("set.d." + id), "small muted"));
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
            "updates" => UpdatesTab(),
            "accounts" => AccountsTab(),
            "about" => AboutTab(),
            "interface" => InterfaceTab(),
            "system" => SystemTab(),
            "services" => ServicesTab(),
            _ => LookTab(),
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
        var titleWords = Ui.Col(4, title, Ui.Text(I18n.T("set.d." + current.Id + ".long"), "muted", wrap: true));
        titleWords.VerticalAlignment = VerticalAlignment.Center;
        var pageHead = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var crumbs = Ui.Text(I18n.T(Groups.First(g => g.Items.Any(i => i.Id == current.Id)).Group), "small muted");
        crumbs.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(crumbs, Dock.Right);
        pageHead.Children.Add(crumbs);
        pageHead.Children.Add(Ui.Row(16, Badge(current.Icon, current.Color, 56), titleWords));

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

    Control LookTab()
    {
        // Первым — «как в системе», дальше все найденные языки (свои переводы из папки languages тоже).
        var languages = I18n.Available();
        var lang = new ComboBox { Width = 300 };
        lang.Items.Add(I18n.T("lang.auto", ("name", languages.FirstOrDefault(l => l.Code == I18n.Detect())?.Native ?? "English")));
        foreach (var l in languages) lang.Items.Add(l.Coverage < 100 ? $"{l.Native} · {l.Coverage}%" : l.Native);
        var saved = Settings.Language;
        lang.SelectedIndex = saved == "auto" ? 0 : Math.Max(0, languages.FindIndex(l => l.Code.Equals(saved, StringComparison.OrdinalIgnoreCase)) + 1);
        lang.SelectionChanged += (_, _) =>
        {
            if (lang.SelectedIndex < 0) return;
            var value = lang.SelectedIndex == 0 ? "auto" : languages[lang.SelectedIndex - 1].Code;
            if (value == Settings.Language) return;
            Settings.Language = value;
            I18n.Set(value);
        };
        var langHint = Ui.Text(I18n.T("lang.hint"), "small muted", wrap: true);
        var langFolder = Ui.Button(I18n.T("lang.folder"), () => Actions.OpenFolder(Directory.CreateDirectory(Path.Combine(Paths.DataDir, "languages")).FullName), "ghost", Icons.Folder);
        var col = new StackPanel { Spacing = 16 };
        // 9.2: три готовых оформления Store — первым делом, крупными карточками.
        col.Children.Add(Section(I18n.T("v92.style.section"), I18n.T("v92.style.section.hint"), StylePicker.Row(Build)));
        col.Children.Add(Section(I18n.T("settings.language"), null, lang, langHint, langFolder));

        // Тема: карточки-образцы.
        var themes = new WrapPanel();
        foreach (var theme in Look.Themes)
        {
            var (bg, surface, text) = Look.Sample(theme);
            var sample = new Border
            {
                Width = 150, Height = 84, CornerRadius = new CornerRadius(12), Background = Ui.Hex(bg), Padding = new Thickness(10), Margin = new Thickness(0, 0, 12, 12),
                BorderThickness = new Thickness(2), BorderBrush = Look.Theme == theme ? Ui.Res("Brand") : Ui.Res("Line"),
                Child = Ui.Col(6,
                    new Border { Height = 10, Width = 70, CornerRadius = new CornerRadius(5), Background = Ui.Hex(Look.Accent), HorizontalAlignment = HorizontalAlignment.Left },
                    new Border { Height = 22, CornerRadius = new CornerRadius(6), Background = Ui.Hex(surface) },
                    new TextBlock { Text = I18n.T("look.theme." + theme), Foreground = Ui.Hex(text), FontSize = 12, FontWeight = FontWeight.SemiBold }),
            };
            var t = theme;
            var b = new Button { Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Content = sample };
            b.Click += (_, _) => { Look.SetTheme(t); Build(); };
            themes.Children.Add(b);
        }
        col.Children.Add(Section(I18n.T("look.theme"), I18n.T("look.theme.hint"), themes));

        // Дизайн: «Студия» (строгий) или «Аврора» (живой фон и стекло).
        // Карточки дизайнов переносятся на новую строку, если колонке не хватает ширины.
        var designs = new WrapPanel();
        foreach (var dz in Look.Designs)
        {
            var d = dz;
            var card = new Button { Classes = { "card-btn" }, Width = 230, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(16), HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = Ui.Col(4, Ui.Text(I18n.T("look.design." + dz), "h3"), Ui.Text(I18n.T("look.design." + dz + ".text"), "small muted", wrap: true)) };
            if (Look.Design == dz) { card.BorderBrush = Ui.Res("Text"); card.BorderThickness = new Thickness(2); }
            card.Click += (_, _) => { Look.SetDesign(d); Build(); };
            designs.Children.Add(card);
        }
        col.Children.Insert(2, Section(I18n.T("look.design"), I18n.T("look.design.hint"), designs));

        // Шрифт, скругление и свечение фона.
        var font = new ComboBox { Width = 260, ItemsSource = Look.Fonts, SelectedItem = Look.Font };
        font.SelectionChanged += (_, _) => { if (font.SelectedItem is string f && f != Look.Font) Look.SetFont(f); };
        var radii = Ui.Row(6);
        foreach (var r in Look.Radii)
        {
            var rr = r;
            var chip = Ui.Button(I18n.T("look.radius." + r), () => { Look.SetRadius(rr); Build(); }, Look.Radius == r ? "chip active" : "chip");
            radii.Children.Add(chip);
        }
        var backdrops = new WrapPanel();
        foreach (var bd in Look.Backdrops)
        {
            var v = bd;
            var chip = Ui.Button(I18n.T("look.bg." + bd), () => { Look.SetBackdrop(v); Build(); }, Look.Backdrop == bd ? "chip active" : "chip");
            chip.Margin = new Thickness(0, 0, 6, 6);
            backdrops.Children.Add(chip);
        }
        var glow = new Slider { Minimum = 0, Maximum = 1, Value = Look.Glow, Width = 260, TickFrequency = 0.1, IsSnapToTickEnabled = true };
        glow.ValueChanged += (_, e) => Look.SetGlow(e.NewValue);
        col.Children.Add(Section(I18n.T("look.style"), I18n.T("look.style.hint"),
            Ui.Col(6, Ui.Text(I18n.T("look.font"), "small muted"), font),
            Ui.Col(6, Ui.Text(I18n.T("look.radius"), "small muted"), radii),
            Ui.Col(6, Ui.Text(I18n.T("look.bg"), "small muted"), backdrops),
            Ui.Col(6, Ui.Text(I18n.T("look.glow"), "small muted"), glow)));

        // Наборы оформления: сохранить, применить, поделиться кодом.
        var presets = Ui.Col(8);
        foreach (var (name, code) in Look.Presets())
        {
            var n = name;
            var c = code;
            var row = new DockPanel();
            var actions = Ui.Row(6,
                Ui.Button(I18n.T("look.preset.apply"), () => { Look.Import(c); Build(); }, "primary", Icons.Check),
                Ui.Button("", () => { Look.DeletePreset(n); Build(); }, "icon ghost", Icons.Trash, I18n.T("ctx.delete")));
            DockPanel.SetDock(actions, Dock.Right);
            row.Children.Add(actions);
            row.Children.Add(Ui.Text(n, "h3"));
            presets.Children.Add(row);
        }
        if (presets.Children.Count == 0) presets.Children.Add(Ui.Text(I18n.T("look.preset.none"), "small muted"));
        var presetName = new TextBox { Watermark = I18n.T("look.preset.name"), Width = 220, MaxLength = 40 };
        var shareCode = new TextBox { Watermark = "ML-LOOK:…", Width = 360 };
        col.Children.Add(Section(I18n.T("look.presets"), I18n.T("look.presets.hint"),
            presets,
            Ui.Row(8, presetName, Ui.Button(I18n.T("look.preset.save"), () =>
            {
                var n = (presetName.Text ?? "").Trim();
                if (n == "") return;
                Look.SavePreset(n);
                Build();
            }, "", Icons.Save)),
            Ui.Row(8, shareCode,
                Ui.Button(I18n.T("look.share.import"), () =>
                {
                    if (Look.Import(shareCode.Text ?? "")) { MainWindow.Current?.Toast(I18n.T("look.share.done")); Build(); }
                    else MainWindow.Current?.Toast(I18n.T("look.share.bad"), bad: true);
                }, "", Icons.Download),
                Ui.Button(I18n.T("look.share.copy"), async () =>
                {
                    if (MainWindow.Current?.Clipboard is { } cb) { await cb.SetTextAsync(Look.Export()); MainWindow.Current.Toast(I18n.T("ctx.copied")); }
                }, "ghost", Icons.List)),
            Ui.Button(I18n.T("look.reset"), () => { Look.Reset(); MainWindow.Current?.Refresh(); Build(); }, "ghost", Icons.Refresh)));

        // Цвет акцента.
        var accents = new WrapPanel();
        foreach (var hex in Look.Accents)
        {
            var h = hex;
            var dot = new Button
            {
                Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Padding = new Thickness(0), Background = Ui.Hex(hex), Margin = new Thickness(0, 0, 10, 10),
                BorderThickness = new Thickness(3), BorderBrush = Look.Accent.Equals(hex, StringComparison.OrdinalIgnoreCase) ? Ui.Res("Text") : Brushes.Transparent,
            };
            dot.Click += (_, _) => { Look.SetAccent(h); Build(); };
            accents.Children.Add(dot);
        }
        var custom = new TextBox { Width = 120, Text = Look.Accents.Contains(Look.Accent) ? "" : Look.Accent, Watermark = "#RRGGBB" };
        custom.LostFocus += (_, _) =>
        {
            var v = (custom.Text ?? "").Trim();
            if (System.Text.RegularExpressions.Regex.IsMatch(v, "^#[0-9a-fA-F]{6}$")) { Look.SetAccent(v.ToUpperInvariant()); Build(); }
        };
        accents.Children.Add(custom);
        col.Children.Add(Section(I18n.T("look.accent"), null, accents));

        // Масштаб интерфейса.
        var scales = new WrapPanel();
        var auto = Ui.Button(I18n.T("look.scale.auto"), () => { Look.SetAutoScale(); Build(); }, Look.AutoScale ? "chip active" : "chip", Icons.Sparkles);
        auto.Margin = new Thickness(0, 0, 6, 6);
        ToolTip.SetTip(auto, I18n.T("look.scale.auto.hint"));
        scales.Children.Add(auto);
        foreach (var k in Look.Scales.Concat([1.4]))
        {
            var kk = k;
            var chip = Ui.Button($"{k * 100:0}%", () => { Look.SetScale(kk); Build(); }, "chip");
            if (!Look.AutoScale && Math.Abs(Look.Scale - k) < 0.001) chip.Classes.Add("active");
            chip.Margin = new Thickness(0, 0, 6, 6);
            scales.Children.Add(chip);
        }
        col.Children.Add(Section(I18n.T("look.scale"), I18n.T("look.scale.hint"), scales,
            Toggle(I18n.T("look.anim"), I18n.T("look.anim.hint"), Look.Animations, v => { Settings.Data["animations"] = v; MainWindow.Current?.Refresh(); }),
            Toggle(I18n.T("look.smooth"), I18n.T("look.smooth.hint"), SmoothScroll.On, v => Settings.Data["smoothScroll"] = v),
            Toggle(I18n.T("look.friendsDock"), I18n.T("look.friendsDock.hint"), Settings.Data.Bool("friendsDock", true), v => { Settings.Data["friendsDock"] = v; MainWindow.Current?.RefreshFriendsDock(); }),
            Toggle(I18n.T("look.compact"), I18n.T("look.compact.hint"), Settings.Data.Bool("compactLists"), v => Settings.Data["compactLists"] = v)));
        return col;
    }

    // ---------------------------------------------------------------- интерфейс: что показывать

    Control InterfaceTab()
    {
        var col = new StackPanel { Spacing = 16 };
        void Chrome() => MainWindow.Current?.Refresh();

        var start = new ComboBox { Width = 240 };
        var starts = new[] { "home", "lastGame", "creator", "add" };
        foreach (var st in starts) start.Items.Add(I18n.T("look.start." + st));
        start.SelectedIndex = Math.Max(0, Array.IndexOf(starts, Settings.Data.Str("startPage") ?? "home"));
        start.SelectionChanged += (_, _) => { Settings.Data["startPage"] = starts[Math.Max(0, start.SelectedIndex)]; Settings.Save(); };
        var startRow = new DockPanel();
        DockPanel.SetDock(start, Dock.Right);
        startRow.Children.Add(start);
        startRow.Children.Add(Ui.Col(3, Ui.Text(I18n.T("look.start"), "h3"), Ui.Text(I18n.T("look.start.hint"), "small muted")));
        col.Children.Add(Section(I18n.T("look.start"), null, startRow));

        col.Children.Add(Section(I18n.T("look.chrome"), I18n.T("look.chrome.hint"),
            Toggle(I18n.T("look.brand"), I18n.T("look.brand.hint"), Settings.Data.Bool("showBrand", true), v => { Settings.Data["showBrand"] = v; Chrome(); }),
            Toggle(I18n.T("look.rail"), I18n.T("look.rail.hint"), !Settings.Data.Bool("railHidden"), v => { Settings.Data["railHidden"] = !v; Chrome(); }),
            Toggle(I18n.T("look.railFriends"), I18n.T("look.railFriends.hint"), Settings.Data.Bool("railFriends", true), v => { Settings.Data["railFriends"] = v; Chrome(); })));

        // «Перемешать плитки» — только у главной «Витрины» (у Store плитки стоят ровной сеткой).
        Control? shuffle = Look.Store ? null : Toggle(I18n.T("v91.set.shuffle"), I18n.T("v91.set.shuffle.hint"), Settings.Data.Bool("homeShuffle", true), v => Settings.Data["homeShuffle"] = v);
        col.Children.Add(Section(I18n.T("look.home"), I18n.T("look.home.hint"), new Control?[] {
            shuffle,
            Toggle(I18n.T("v91.set.splash"), I18n.T("v91.set.splash.hint"), Settings.Data.Bool("splash", true), v => Settings.Data["splash"] = v),
            Toggle(I18n.T("v91.set.fx"), I18n.T("v91.set.fx.hint"), Settings.Data.Bool("installFx", true), v => Settings.Data["installFx"] = v),
            Toggle(I18n.T("v92.set.openfx"), I18n.T("v92.set.openfx.hint"), Settings.Data.Bool("modOpenFx", true), v => Settings.Data["modOpenFx"] = v),
            Toggle(I18n.T("v4.continue"), I18n.T("v4.continue.text"), Settings.Data.Bool("homeContinue", true), v => Settings.Data["homeContinue"] = v),
            Toggle(I18n.T("home.favorites"), I18n.T("home.favorites.text"), Settings.Data.Bool("homeFavorites", true), v => Settings.Data["homeFavorites"] = v),
            Toggle(I18n.T("look.home.popular"), I18n.T("look.home.popular.hint"), Settings.Data.Bool("homePopular", true), v => Settings.Data["homePopular"] = v),
            Toggle(I18n.T("feed.setting"), I18n.T("feed.setting.hint"), Settings.Data.Bool("catalogFeeds", true), v => Settings.Data["catalogFeeds"] = v) }.OfType<Control>().ToArray()));

        // Какие игры показывать и в каком порядке.
        var list = new StackPanel { Spacing = 6 };
        var order = MainWindow.OrderedGames().ToList();
        var hidden = Settings.Data.Arr("hiddenGames").Select(x => x?.ToString()).ToHashSet();
        for (var i = 0; i < order.Count; i++)
        {
            var g = order[i];
            var id = g.Def.Id;
            var index = i;
            var sw = new ToggleSwitch { IsChecked = !hidden.Contains(id), OnContent = "", OffContent = "", MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
            sw.IsCheckedChanged += (_, _) =>
            {
                var set = Settings.Data.Arr("hiddenGames").Select(x => x?.ToString()).OfType<string>().ToHashSet();
                if (sw.IsChecked == true) set.Remove(id); else set.Add(id);
                Settings.Data["hiddenGames"] = new System.Text.Json.Nodes.JsonArray(set.Select(x => (System.Text.Json.Nodes.JsonNode)x).ToArray());
                Settings.Save();
                Chrome();
            };
            var move = Ui.Row(2,
                Ui.Button("", () => { MainWindow.MoveGame(id, -1); Chrome(); Build(); }, "icon ghost", Icons.ArrowUp, I18n.T("look.up")),
                Ui.Button("", () => { MainWindow.MoveGame(id, 1); Chrome(); Build(); }, "icon ghost", Icons.Download, I18n.T("look.down")));
            move.Children[0].IsEnabled = index > 0;
            move.Children[1].IsEnabled = index < order.Count - 1;
            var right = Ui.Row(8, move, sw);
            var row = new DockPanel();
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);
            var name = Ui.Row(10, Ui.Thumb(g.Def.ArtUrl, g.Def.Name, 28, 7), Ui.Text(g.Def.Name + (g.Status == Detect.Found ? "" : "  · " + I18n.T("games.notDetected")), g.Status == Detect.Found ? "" : "muted"));
            name.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(name);
            list.Children.Add(row);
        }
        col.Children.Add(Section(I18n.T("look.games"), I18n.T("look.games.hint"), list,
            Ui.Button(I18n.T("add.title"), () => MainWindow.Current?.Navigate(() => new AddGamePage()), "", Icons.Plus)));

        // Скрытые моды и авторы (как блок-лист на Nexus).
        var hiddenList = new StackPanel { Spacing = 6 };
        var blocked = Features.Blocklist.All();
        if (blocked.Count == 0) hiddenList.Children.Add(Ui.Text(I18n.T("nx.hidden.none"), "muted"));
        foreach (var (key, title, author) in blocked)
        {
            var k = key;
            var a = author;
            var back = Ui.Button(I18n.T("nx.unhide"), () => { Features.Blocklist.Unhide(k, a); Build(); }, "ghost", Icons.Eye);
            var row = new DockPanel();
            DockPanel.SetDock(back, Dock.Right);
            row.Children.Add(back);
            var label = Ui.Row(8, Ui.Icon(author ? Icons.User : Icons.Package, 15, Ui.Res("Muted")), Ui.Text(title));
            label.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(label);
            hiddenList.Children.Add(row);
        }
        col.Children.Add(Section(I18n.T("nx.hidden.title"), I18n.T("nx.hidden.hint"), hiddenList));
        return col;
    }

    // ---------------------------------------------------------------- система

    Control SystemTab()
    {
        var col = new StackPanel { Spacing = 16 };
        col.Children.Add(Section(I18n.T("sys.tab"), null,
            Toggle(I18n.T("offline.title"), I18n.T("offline.hint"), Offline.On, v => { Offline.Set(v); MainWindow.Current?.Toast(I18n.T(v ? "offline.on" : "offline.off")); }),
            Toggle(I18n.T("sys.tray"), I18n.T("sys.tray.hint"), Settings.Data.Bool("closeToTray"), v => { Settings.Data["closeToTray"] = v; Settings.Save(); MainWindow.Current?.UpdateTray(); }),
            Toggle(I18n.T("sys.autostart"), I18n.T("sys.autostart.hint"), Features.Autostart.Enabled, v => Features.Autostart.Set(v)),
            Toggle(I18n.T("sys.minimized"), I18n.T("sys.minimized.hint"), Settings.Data.Bool("startMinimized"), v => Settings.Data["startMinimized"] = v),
            Toggle(I18n.T("sys.confirm"), I18n.T("sys.confirm.hint"), Settings.Data.Bool("confirmRemove", true), v => Settings.Data["confirmRemove"] = v)));

        var toast = new NumericUpDown { Minimum = 2, Maximum = 30, Value = (decimal)Math.Clamp(Settings.Data.Long("toastSeconds") is var t && t > 0 ? t : 4, 2, 30), Width = 140, FormatString = "0" };
        toast.ValueChanged += (_, _) => { Settings.Data["toastSeconds"] = (int)(toast.Value ?? 4); Settings.Save(); };
        var toastRow = new DockPanel();
        DockPanel.SetDock(toast, Dock.Right);
        toastRow.Children.Add(toast);
        toastRow.Children.Add(Ui.Col(3, Ui.Text(I18n.T("sys.toast"), "h3"), Ui.Text(I18n.T("sys.toast.hint"), "small muted")));
        col.Children.Add(Section(I18n.T("sys.notify"), null, toastRow,
            Toggle(I18n.T("sys.toastDone"), I18n.T("sys.toastDone.hint"), Settings.Data.Bool("toastOnDone", true), v => Settings.Data["toastOnDone"] = v)));

        col.Children.Add(Section(I18n.T("sys.transfer"), I18n.T("sys.transfer.hint"),
            Ui.Row(10,
                Ui.Button(I18n.T("sys.export"), async () =>
                {
                    var file = await MainWindow.Current!.PickSaveFile(I18n.T("sys.export"), "modlaunch-settings.json");
                    if (file is null) return;
                    File.WriteAllText(file, Features.SettingsTransfer.Export());
                    MainWindow.Current.Toast(I18n.T("sys.exported"));
                }, "", Icons.Upload),
                Ui.Button(I18n.T("sys.import"), async () =>
                {
                    var file = await MainWindow.Current!.PickFile(I18n.T("sys.import"), json: true);
                    if (file is null) return;
                    try
                    {
                        var n = Features.SettingsTransfer.Import(File.ReadAllText(file));
                        Look.Apply();
                        MainWindow.Current.Toast(I18n.T("sys.imported", ("n", n)));
                        Build();
                    }
                    catch (Exception e) { MainWindow.Current.Toast(Jobs.Explain(e), bad: true); }
                }, "", Icons.Download),
                Ui.Button(I18n.T("keys.title"), () => MainWindow.Current?.Shortcuts(), "ghost", Icons.Key))));
        return col;
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
            ("look", ["settings.language", "look.theme", "look.accent", "look.scale", "look.anim", "look.compact", "look.font", "look.radius", "look.bg", "look.glow", "look.presets"]),
            ("interface", ["look.start", "look.chrome", "look.home", "look.games", "look.brand", "look.rail"]),
            ("system", ["sys.tray", "sys.autostart", "sys.toast", "sys.transfer", "sys.confirm"]),
            ("games", ["settings.games", "games.deep"]),
            ("launch", ["settings.tab.launch", "ov.title", "launch.args", "launch.time"]),
            ("backups", ["settings.tab.backups", "bak.keep"]),
            ("downloads", ["v4.archive", "dl.parallel"]),
            ("graphics", ["settings.tab.graphics"]),
            ("updates", ["settings.tab.updates", "upd.auto", "upd.beforePlay"]),
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
        return Section(I18n.T("settings.games"), I18n.T("settings.games.hint"), list,
            Ui.Button(I18n.T("games.deep"), () => _ = AppState.DetectMany(AppState.Games.Where(x => x.Status != Detect.Found).ToList(), deep: true), "", Icons.Search),
            guide);
    }

    // ---------------------------------------------------------------- запуск и игровое время

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
        col.Children.Add(Section(I18n.T("settings.tab.launch"), null, rows.ToArray()));

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
        var keep = new NumericUpDown { Minimum = 1, Maximum = 200, Value = Backups.Keep, Width = 140, FormatString = "0" };
        keep.ValueChanged += (_, _) => { Settings.Data["backupKeep"] = (int)(keep.Value ?? 10); Settings.Save(); };
        var keepRow = new DockPanel();
        DockPanel.SetDock(keep, Dock.Right);
        keepRow.Children.Add(keep);
        keepRow.Children.Add(Ui.Col(3, Ui.Text(I18n.T("bak.keep"), "h3"), Ui.Text(I18n.T("bak.keep.hint"), "small muted")));
        col.Children.Add(Section(I18n.T("settings.tab.backups"), null,
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
        return Ui.Col(16, Section(I18n.T("dl.title"), null, parallelRow), Section(I18n.T("v4.archive"), I18n.T("v4.archive.hint"),
            Toggle(I18n.T("v4.archive.keep"), I18n.T("v4.archive.hint"), DownloadArchive.Keep, v => Settings.Data["keepArchives"] = v),
            limitRow,
            Ui.Text(I18n.T("v4.archive.size", ("n", count), ("size", GamePage.Size(bytes))), "muted"),
            Ui.Row(10,
                Ui.Button(I18n.T("games.openFolder"), () => Actions.OpenFolder(DownloadArchive.Root), "", Icons.Folder),
                Ui.Button(I18n.T("v4.archive.clear"), () => { DownloadArchive.Clear(); MainWindow.Current?.Toast(I18n.T("v4.archive.cleared")); Build(); }, "ghost", Icons.Trash))), StorageSection());
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

    Control UpdatesTab()
    {
        var col = new StackPanel { Spacing = 16 };
        col.Children.Add(Section(I18n.T("settings.tab.updates"), null,
            Toggle(I18n.T("upd.onStart"), I18n.T("upd.onStart.hint"), ModUpdates.OnStart, v => Settings.Data["checkModUpdates"] = v),
            Toggle(I18n.T("upd.auto"), I18n.T("upd.auto.hint"), ModUpdates.Auto, v => Settings.Data["autoUpdateMods"] = v),
            Toggle(I18n.T("upd.beforePlay"), I18n.T("upd.beforePlay.hint"), ModUpdates.BeforePlay, v => Settings.Data["updateBeforePlay"] = v),
            Ui.Row(10, Ui.Button(I18n.T("upd.checkAll"), CheckAll, "primary", Icons.Refresh)),
            Ui.Text(I18n.T("upd.checkAll.hint2"), "small muted", wrap: true)));

        var list = new StackPanel { Spacing = 8 };
        var total = 0;
        foreach (var g in AppState.Games)
        {
            if (!ModUpdates.Found.TryGetValue(g.Def.Id, out var ups) || ups.Count == 0) continue;
            total += ups.Count;
            var id = g.Def.Id;
            var row = new DockPanel();
            var open = Ui.Button(I18n.T("upd.found." + I18n.Plural(ups.Count, "one", "few", "many"), ("n", ups.Count)), () => MainWindow.Current?.Navigate(() => new GamePage(id, "installed")), "", Icons.ArrowUp);
            DockPanel.SetDock(open, Dock.Right);
            row.Children.Add(open);
            row.Children.Add(Ui.Col(3, Ui.Text(g.Def.Name, "h3"), Ui.Text(string.Join(", ", ups.Select(u => $"{u.Name} {u.Current} → {u.Latest}").Take(4)), "small muted", wrap: true)));
            list.Children.Add(row);
        }
        if (total == 0) list.Children.Add(Ui.Text(ModUpdates.Found.IsEmpty ? I18n.T("upd.never") : I18n.T("upd.none"), "muted"));
        col.Children.Add(Section(I18n.T("upd.list"), null, list));
        return col;
    }

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

        var latest = Setup.Updater.Latest;
        var status = !Setup.Updater.Configured ? I18n.T("upd.app.off")
            : latest is null ? I18n.T("upd.never")
            : Setup.Updater.Available ? I18n.T("upd.app.available", ("version", latest.Version))
            : I18n.T("upd.app.latest", ("version", Http.Version));
        var updates = Ui.Col(10, Ui.Text(status, "h3"), Ui.Text(I18n.T("upd.app.hint"), "small muted", wrap: true),
            Ui.Row(10,
                Ui.Button(I18n.T("upd.check"), async () =>
                {
                    try { await Setup.Updater.Check(); } catch (Exception e) { MainWindow.Current?.Toast(Jobs.Explain(e), bad: true); }
                    Build();
                }, "", Icons.Refresh),
                Setup.Updater.Available ? Ui.Button(I18n.T("upd.app.open"), () => MainWindow.Current?.ShowUpdate(), "primary", Icons.Sparkles) : new Panel()),
            Toggle(I18n.T("upd.app.auto"), I18n.T("upd.app.auto.hint"), Setup.Updater.AutoDownload, v => Settings.Data["autoDownloadUpdates"] = v));

        return Section(I18n.T("settings.tab.about"), null,
            Ui.Row(16, logo, name),
            updates,
            Ui.Row(10,
                Ui.Button("GitHub", () => Ui.OpenUrl("https://github.com/ModLaunch/ModLaunch"), "", Icons.External),
                Ui.Button(I18n.T("games.openFolder"), () => Actions.OpenFolder(Paths.DataDir), "", Icons.Folder)));
    }
}
