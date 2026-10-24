using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Mods;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>
/// 20.0: страница игры по эскизам. Сверху — баннер со скруглёнными углами: арт игры, обложка, метка
/// загрузчика, название, одна строка сведений (моды · каталог · папка) и кнопки «Играть», «Открыть папку», «⋯».
/// «Обзор»: слева — установленные моды одним листом (переключатель и меню «⋯» у каждого; библиотеки —
/// одной свёрнутой строкой) и «Рекомендуем поставить» карточками; справа — «Об игре», «Инструменты»
/// сеткой кнопок и карточка бэкапа (раньше бэкап жил в строке состояния внизу окна).
/// </summary>
public sealed partial class GamePage
{
    // ---------------------------------------------------------------- баннер

    Control StoreHeader20()
    {
        var found = _g.Status == Detect.Found;
        var running = Features.Launcher.IsRunning(_g.Def.Id);
        var compact = found && _tab != "overview";
        var white = Brushes.White;
        var soft = Ui.Hex("#DCD8E6");

        var cover = new Border
        {
            Width = compact ? 64 : 120, Height = compact ? 96 : 180, CornerRadius = new CornerRadius(compact ? 8 : 12), ClipToBounds = true,
            BorderBrush = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)), BorderThickness = new Thickness(1),
            Child = Ui.GameImage(_g.Def, 400, art: Images.Art.Cover),
        };
        var coverHost = new Border { CornerRadius = new CornerRadius(12), BoxShadow = BoxShadows.Parse("0 16 36 -10 #A0000000"), Child = cover, VerticalAlignment = VerticalAlignment.Bottom };

        // Метка состояния: загрузчик стоит (зелёная), нужен (жёлтая), игру ищут или не нашли.
        var (dot, stateText) = _g.Status switch
        {
            Detect.Found when _g.Def.Loader == LoaderKind.None => (Ui.Res("Good"), I18n.T("add.noLoader", ("folder", _g.Def.ModsFolder))),
            Detect.Found when _g.LoaderInstalled => (Ui.Res("Good"), I18n.T("games.loaderReady", ("loader", _g.Def.LoaderName))),
            Detect.Found => (Ui.Res("Warn"), I18n.T("games.loaderMissing", ("loader", _g.Def.LoaderName))),
            Detect.Searching => (Ui.Res("Muted"), _g.SearchingWhere is null ? I18n.T("games.searching") : I18n.T("games.searchingWhere", ("where", _g.SearchingWhere))),
            _ => (Ui.Res("Faint"), I18n.T("games.notDetected")),
        };
        var dotColor = dot is ISolidColorBrush ds ? ds.Color : Colors.Gray;
        var chip = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(42, dotColor.R, dotColor.G, dotColor.B)), CornerRadius = new CornerRadius(999), Padding = new Thickness(11, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = Ui.Row(7, Ui.Dot(dot, 7), new TextBlock { Text = stateText, FontSize = 12.5, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Look.Mix(dotColor, Colors.White, 0.45)), VerticalAlignment = VerticalAlignment.Center }),
        };
        var name = new TextBlock
        {
            Text = _g.Def.Name, FontFamily = Look.Display, FontSize = compact ? 26 : 42, FontWeight = FontWeight.Bold, LetterSpacing = compact ? -0.6 : -1.2,
            Foreground = white, TextWrapping = TextWrapping.Wrap, MaxLines = compact ? 1 : 2, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left, LineHeight = compact ? 32 : 48,
        };

        // Одна строка сведений: моды · обновления · каталог · время · папка (по нажатию — открыть).
        var meta = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        void Meta(string text)
        {
            if (meta.Children.Count > 0) meta.Children.Add(new TextBlock { Text = "·", Margin = new Thickness(8, 0), Foreground = soft, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            meta.Children.Add(new TextBlock { Text = text, Foreground = soft, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
        }
        var played = Features.PlayTime.Get(_g.Def.Id);
        if (found)
        {
            Meta(StoreKit.Mods(_g.ModCount));
            var updates = Features.ModUpdates.Found.TryGetValue(_g.Def.Id, out var up) ? up.Count : 0;
            if (updates > 0) Meta(I18n.T("v20.game.updates", ("n", updates)));
            if (_total > 0) Meta(I18n.T("v20.game.inCatalog", ("n", I18n.Compact(_total))));
            if (played.TotalMs > 0) Meta(Features.PlayTime.Format(played.TotalMs));
        }
        if (_g.Path is not null && !compact)
        {
            if (meta.Children.Count > 0) meta.Children.Add(new TextBlock { Text = "·", Margin = new Thickness(8, 0), Foreground = soft, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            var path = new Button
            {
                Classes = { "chip20" }, Background = Brushes.Transparent, Padding = new Thickness(6, 2), CornerRadius = new CornerRadius(6),
                Content = Ui.Row(6, Ui.Icon(Icons.Folder, 13, soft), new TextBlock { Text = Ui.ShortPath(_g.Path), FontSize = 14, Foreground = soft, VerticalAlignment = VerticalAlignment.Center }),
            };
            ToolTip.SetTip(path, _g.Path);
            path.Click += (_, _) => Actions.OpenFolder(_g.Path);
            meta.Children.Add(path);
        }

        // Кнопки: «Играть» (или «Поставить загрузчик»), папка и «⋯».
        var buttons = new WrapPanel();
        void Add(Control c) { c.Margin = new Thickness(0, 0, 10, 0); buttons.Children.Add(c); }
        if (found)
        {
            if (running)
            {
                Add(PlayControls.RunningPill(_g.Def.Id));
                var stop = Ui.Button(I18n.T("v4.stop"), () => { Features.Launcher.Stop(_g.Def.Id); MainWindow.Current?.Toast(I18n.T("v4.stopped")); }, "hero-ghost", Icons.Stop);
                stop.Padding = new Thickness(20, 13);
                Add(stop);
            }
            else if (_g.LoaderInstalled || _g.Def.Loader == LoaderKind.None) Add(PlayControls.PlayButton(_g));
            else
            {
                var busy = Jobs.All.Any(j => j.Active && j.Title == _g.Def.LoaderName && j.GameName == _g.Def.Name);
                var install = Ui.Button(busy ? I18n.T("aside.installing") : I18n.T("games.installLoader", ("loader", _g.Def.LoaderName)), () => Actions.InstallLoader(_g), "primary", Icons.Download);
                install.IsEnabled = !busy;
                install.FontSize = 15;
                install.Padding = new Thickness(26, 13);
                Add(install);
            }
            var folder = Ui.Button(I18n.T("games.openFolder"), () => Actions.OpenFolder(_g.Path), "hero-ghost", Icons.Folder);
            folder.Padding = new Thickness(18, 13);
            Add(folder);
        }
        else
        {
            var searching = _g.Status == Detect.Searching;
            var pick = Ui.Button(I18n.T("games.setPath"), () => Actions.PickGameFolder(_g), "primary", Icons.Folder);
            pick.Padding = new Thickness(24, 13);
            var again = Ui.Button(searching ? I18n.T("games.searching") : I18n.T("games.detectAgain"), () => _ = AppState.DetectOne(_g), "hero-ghost", Icons.Refresh);
            again.Padding = new Thickness(18, 13);
            again.IsEnabled = !searching;
            Add(pick);
            Add(again);
        }
        var more = Ui.Button("", () => { }, "icon hero-ghost", Icons.More, I18n.T("top.more"));
        more.Width = more.Height = 48;
        more.CornerRadius = new CornerRadius(12);
        more.Flyout = GameCard.Menu(_g);
        Add(more);

        Control info;
        if (compact)
        {
            var words = Ui.Col(6, name, meta.Children.Count > 0 ? meta : chip);
            words.VerticalAlignment = VerticalAlignment.Center;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 20 };
            coverHost.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(coverHost);
            Grid.SetColumn(words, 1);
            row.Children.Add(words);
            buttons.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(buttons, 2);
            row.Children.Add(buttons);
            info = row;
        }
        else
        {
            var words = Ui.Col(10, chip, name, meta);
            words.Children.Add(new Border { Height = 4 });
            words.Children.Add(buttons);
            words.VerticalAlignment = VerticalAlignment.Bottom;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 26 };
            row.Children.Add(coverHost);
            Grid.SetColumn(words, 1);
            row.Children.Add(words);
            info = row;
        }
        info.Margin = compact ? new Thickness(22, 18) : new Thickness(28, 26, 28, 26);
        info.VerticalAlignment = VerticalAlignment.Bottom;

        var hero = Ui.GameImage(_g.Def, 1600, art: Images.Art.Hero);
        var stage = new Panel
        {
            Height = compact ? 132 : 360, ClipToBounds = true,
            Children =
            {
                hero,
                new Border { Background = Gx.ShadeUp(0.0, (byte)(compact ? 225 : 238)), IsHitTestVisible = false },
                new Border { Background = Gx.ShadeLeft(0.85, (byte)(compact ? 210 : 160)), IsHitTestVisible = false },
                info,
            },
        };
        var banner = new Border
        {
            CornerRadius = new CornerRadius(Look.Radius == "sharp" ? 6 : 20), ClipToBounds = true, Child = stage,
            Margin = new Thickness(StoreKit.Gutter, 24, StoreKit.Gutter, 18), MaxWidth = StoreKit.MaxWidth,
        };
        if (!Shown && !TabSwitch)
        {
            Animate.From(hero, "scale(1.08)", 1400, 0, new Avalonia.Animation.Easings.QuadraticEaseOut(), 1);
            Animate.From(coverHost, "translateY(14px)", 460, 60, new Avalonia.Animation.Easings.QuinticEaseOut());
            Animate.From(info, "translateX(-18px)", 520, 120);
        }
        return banner;
    }

    // ---------------------------------------------------------------- «Обзор»

    Control OverviewView20()
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

        // 2. Установленные моды — одним листом: сначала свои моды, библиотеки — одной строкой.
        left.Children.Add(InstalledCard20(registry, updates));

        // 3. Рекомендуем поставить: «Выбор ModLaunch», затем популярные — карточками.
        if (_g.Def.HasExternalCatalog)
        {
            if (_popular is null && !_popularLoading) _ = LoadOverviewMods();
            var picks = (_popular ?? []).Where(m => !Actions.IsInstalled(_g, m.Id)).Take(3).ToList();
            Control body = _popular is null
                ? Kit20.Grid(Enumerable.Range(0, 3).Select(_ => (Control)new Border { Classes = { "skeleton" }, Height = 196, CornerRadius = new CornerRadius(14) }).ToList(), 200, 3, 16)
                : picks.Count > 0 ? Kit20.Grid(picks.Select(m => ModTileSmall(m)).ToList(), 200, 3, 16)
                : Ui.Text(I18n.T("v92.ov.allSet"), "muted");
            left.Children.Add(Ui.Col(14,
                Kit20.SectionHead(I18n.T("v20.game.rec"), null, I18n.T("v20.home.rec.all"), () => MainWindow.Current?.Navigate(() => new GamePage(_g.Def.Id, "catalog"))),
                body));
        }

        // Справа: «Об игре», «Инструменты» и бэкап.
        var right = new StackPanel { Spacing = 16 };
        right.Children.Add(AboutCard20());
        right.Children.Add(ToolsCard20());
        right.Children.Add(BackupCard20());

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,340"), ColumnSpacing = 24 };
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    /// <summary>Небольшая карточка рекомендации: обложка, название, автор и «Установить» во всю ширину.</summary>
    Control ModTileSmall(ModInfo m)
    {
        var cover = new Panel { Height = 96, ClipToBounds = true, Children = { ModCover.Create(_g, m, 480, iconShare: 0.62) } };
        var body = Ui.Col(4,
            new TextBlock { Text = m.Name, FontSize = 14.5, FontWeight = FontWeight.Bold, TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = m.Author != "" ? m.Author : Kit20.Facts(m), FontSize = 12.5, Foreground = Ui.Res("Faint"), TextTrimming = TextTrimming.CharacterEllipsis });
        var action = Kit20.InstallButton(_g, m, wide: true);
        action.Margin = new Thickness(0, 8, 0, 0);
        body.Children.Add(action);
        body.Margin = new Thickness(14, 12, 14, 14);
        var tile = new Border { Classes = { "tile20" }, Background = Ui.Res("Surface"), CornerRadius = new CornerRadius(14), ClipToBounds = true, Child = Ui.Col(0, cover, body) };
        var id = _g.Def.Id;
        StoreKit.OnClick(tile, () => MainWindow.Current?.Navigate(() => new ModPage(id, m)));
        return tile;
    }

    /// <summary>
    /// «Установленные моды» листом: шапка (фильтр по названию открывает вкладку «Моды», «Проверить обновления»),
    /// свои моды строками, библиотеки (поставленные как зависимости) — одной свёрнутой строкой, ссылка «Показать все».
    /// </summary>
    Control InstalledCard20(ModRegistry registry, List<Features.ModUpdate>? updates)
    {
        var list = registry.List();
        var head = new DockPanel { Margin = new Thickness(20, 16, 14, 14) };
        var check = Ui.Button(I18n.T("upd.check"), () => _ = CheckUpdates20(), "", Icons.Refresh);
        check.Padding = new Thickness(14, 8);
        var tools = Ui.Row(8, check);
        tools.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(tools, Dock.Right);
        head.Children.Add(tools);
        head.Children.Add(Ui.Row(8,
            new TextBlock { Text = I18n.T("v92.ov.installed"), FontSize = 17, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center },
            new TextBlock { Text = "· " + list.Count, FontSize = 17, Foreground = Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Center }));
        var col = new StackPanel { Children = { head } };
        if (list.Count == 0)
        {
            var open = Ui.Button(I18n.T("v92.ov.empty.open"), () => MainWindow.Current?.Navigate(() => new GamePage(_g.Def.Id, "catalog")), "primary", Icons.Compass);
            open.IsVisible = _g.Def.HasCatalog;
            var file = Ui.Button(I18n.T("inst.fromFile"), () => Actions.InstallFromFile(_g), "", Icons.FilePlus);
            col.Children.Add(new Border
            {
                BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(20, 16, 20, 20),
                Child = Ui.Col(10, Ui.Text(I18n.T("v92.ov.empty"), "h3"), Ui.Text(I18n.T("v92.ov.empty.text"), "muted", wrap: true), Ui.Row(10, open, file)),
            });
            return Kit20.Card(col, new Thickness(0));
        }
        var byRecord = updates?.ToDictionary(u => u.RecordId) ?? [];
        var own = list.Where(m => m.Str("requestedBy") is null).ToList();
        var libs = list.Where(m => m.Str("requestedBy") is not null).ToList();
        var top = own.OrderByDescending(m => DateTime.TryParse(m.Str("installedAt"), out var at) ? at : DateTime.MinValue).Take(6).ToList();
        foreach (var mod in top) col.Children.Add(InstalledRow(registry, mod, byRecord.GetValueOrDefault(mod.Str("id")!), flat: true));
        if (libs.Count > 0)
        {
            var names = string.Join(" · ", libs.Take(5).Select(m => (m.Str("name") ?? m.Str("id") ?? "?") + (m.Str("version") is { Length: > 0 } v ? " " + v : "")));
            var icon = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(11), Background = Ui.Res("Surface2"), Child = Ui.Icon(Icons.Package, 20, Ui.Res("Muted")) };
            var title = Ui.Row(8,
                new TextBlock { Text = I18n.T("v20.game.libs", ("n", libs.Count)), FontSize = 15, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
                ModRow.Tag(I18n.T("v20.game.libs.auto"), Ui.Res("Surface2"), Ui.Res("Muted")));
            var words = Ui.Col(3, title, new TextBlock { Text = names, FontSize = 13, Foreground = Ui.Res("Faint"), TextTrimming = TextTrimming.CharacterEllipsis });
            words.VerticalAlignment = VerticalAlignment.Center;
            var show = Kit20.Link(I18n.T("v20.game.libs.show"), () => MainWindow.Current?.Navigate(() => new GamePage(_g.Def.Id, "installed")), 13.5);
            show.VerticalAlignment = VerticalAlignment.Center;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 14 };
            grid.Children.Add(icon);
            Grid.SetColumn(words, 1);
            grid.Children.Add(words);
            Grid.SetColumn(show, 2);
            grid.Children.Add(show);
            col.Children.Add(new Border { BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(20, 12, 14, 12), Child = grid });
        }
        var all = Kit20.Link(I18n.T("v20.game.showAll", ("n", list.Count)), () => MainWindow.Current?.Navigate(() => new GamePage(_g.Def.Id, "installed")), 13.5);
        all.HorizontalAlignment = HorizontalAlignment.Left;
        col.Children.Add(new Border { BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(14, 8), Child = all });
        return Kit20.Card(col, new Thickness(0));
    }

    async Task CheckUpdates20()
    {
        MainWindow.Current?.Toast(I18n.T("upd.checking"));
        try { await Features.ModUpdates.Check(_g); } catch { }
        Build();
    }

    /// <summary>«Об игре»: полный путь к папке (с копированием), загрузчик, моды, каталоги, время в игре.</summary>
    Control AboutCard20()
    {
        var list = _g.Registry?.List() ?? [];
        var enabled = list.Count(m => m.Bool("enabled", true) && !m.Bool("missing"));
        var played = Features.PlayTime.Get(_g.Def.Id);
        var col = Ui.Col(14, new TextBlock { Text = I18n.T("v92.about.title"), FontSize = 15, FontWeight = FontWeight.Bold });
        if (_g.Path is not null)
        {
            var path = new TextBlock { Text = _g.Path, FontFamily = new FontFamily("avares://AvaloniaEdit/Resources/Fonts#Cascadia Code,Consolas,monospace"), FontSize = 12.5, LineHeight = 18, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            var copy = Ui.Button("", async () => { if (MainWindow.Current?.Clipboard is { } cb) await cb.SetTextAsync(_g.Path); MainWindow.Current?.Toast(I18n.T("v20.copied")); }, "icon", Icons.Copy, I18n.T("v20.copyPath"));
            copy.Width = copy.Height = 30;
            copy.CornerRadius = new CornerRadius(8);
            copy.VerticalAlignment = VerticalAlignment.Top;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            row.Children.Add(path);
            Grid.SetColumn(copy, 1);
            row.Children.Add(copy);
            col.Children.Add(Ui.Col(6, new TextBlock { Text = I18n.T("v92.about.folder"), FontSize = 12.5, Foreground = Ui.Res("Faint") }, row));
            col.Children.Add(new Border { Height = 1, Background = Ui.Res("Line") });
        }
        var rows = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 16, RowSpacing = 11 };
        void Row(string key, string value, IBrush? color = null, string? icon = null)
        {
            var r = rows.RowDefinitions.Count;
            rows.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var k = new TextBlock { Text = key, FontSize = 13.5, Foreground = Ui.Res("Faint") };
            Grid.SetRow(k, r);
            rows.Children.Add(k);
            var text = new TextBlock { Text = value, FontSize = 13.5, Foreground = color ?? Ui.Res("Text"), FontWeight = color is null ? FontWeight.Normal : FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            Control v = icon is null ? text : Ui.Row(6, Ui.Icon(icon, 14, color), text);
            Grid.SetRow(v, r);
            Grid.SetColumn(v, 1);
            rows.Children.Add(v);
        }
        if (_g.Def.Loader != LoaderKind.None)
            Row(I18n.T("aside.loader"), _g.Def.LoaderName, _g.LoaderInstalled ? Kit20.Readable("Good") : Kit20.Readable("Warn"), _g.LoaderInstalled ? Icons.Check : Icons.Alert);
        Row(I18n.T("v92.game.mods"), list.Count == 0 ? "0" : I18n.T("v20.game.enabledOf", ("on", enabled), ("all", list.Count)));
        if (_g.Def.Sources.Length > 0) Row(I18n.T("v92.about.sources"), string.Join(", ", _g.Def.Sources.Select(Catalog.Title)));
        Row(I18n.T("aside.playtime"), played.TotalMs > 0 ? Features.PlayTime.Format(played.TotalMs) : I18n.T("v20.game.notPlayed"));
        if (played.LastPlayed is not null) Row(I18n.T("aside.lastPlayed"), Ui.Ago(played.LastPlayed));
        if (_g.Def.Engine is { } engine) Row(I18n.T("v92.about.engine"), engine);
        col.Children.Add(rows);
        return Kit20.Card(col, new Thickness(20, 18));
    }

    /// <summary>«Инструменты»: редкие вкладки и обслуживание игры — сеткой кнопок со значком над подписью.</summary>
    Control ToolsCard20()
    {
        var items = new List<Control>();
        void Item(string icon, string text, Action run)
        {
            var b = new Button
            {
                Classes = { "tool20" }, Height = 68, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(12), Padding = new Thickness(6, 0),
                Content = Ui.Col(6, Ui.Icon(icon, 18, Ui.Res("Muted")), new TextBlock { Text = text, FontSize = 12.5, FontWeight = FontWeight.Medium, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center }),
            };
            b.Click += (_, _) => run();
            ToolTip.SetTip(b, text);
            items.Add(b);
        }
        void Tab(string icon, string text, string tab) => Item(icon, text, () => MainWindow.Current?.Navigate(() => new GamePage(_g.Def.Id, tab)));
        var health = Features.Health.Count(_g);
        Tab(Icons.Activity, health > 0 ? I18n.T("v20.tools.health.n", ("n", health)) : I18n.T("v20.tools.health"), "health");
        Tab(Icons.Save, I18n.T("games.saves"), "saves");
        Tab(Icons.FilePlus, I18n.T("games.log"), "log");
        Item(Icons.Upload, I18n.T("v20.tools.fromFile"), () => Actions.InstallFromFile(_g));
        Item(Icons.Search, I18n.T("v20.tools.rescan"), () => _ = AppState.DetectOne(_g, deep: true));
        Tab(Icons.Wrench, I18n.T("v4.tools"), "tools");
        if (ShotFiles().Count > 0) Tab(Icons.Image, I18n.T("shots.tab"), "shots");
        var grid = new Grid { ColumnSpacing = 8, RowSpacing = 8, ColumnDefinitions = new ColumnDefinitions("*,*") };
        for (var i = 0; i < items.Count; i++)
        {
            if (i % 2 == 0) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetRow(items[i], i / 2);
            Grid.SetColumn(items[i], i % 2);
            grid.Children.Add(items[i]);
        }
        return Kit20.Card(Ui.Col(12, new TextBlock { Text = I18n.T("v4.tools"), FontSize = 15, FontWeight = FontWeight.Bold }, grid), new Thickness(20, 18));
    }

    /// <summary>Бэкап в один клик — здесь, у игры, а не в строке внизу окна.</summary>
    static Control BackupCard20()
    {
        var last = Features.OneClickBackup.Last;
        var running = Features.OneClickBackup.Running;
        var (title, color) = running ? (I18n.T("v11.sb.backup.running"), Ui.Res("Brand2"))
            : last is null ? (I18n.T("v11.sb.backup.never"), Ui.Res("Warn"))
            : last.Ok ? (I18n.T("v11.sb.backup.ago", ("ago", Ui.Ago(last.At))), Ui.Res("Good"))
            : (I18n.T("v11.sb.backup.fail"), Ui.Res("Warn"));
        var c = color is ISolidColorBrush s ? s.Color : Colors.Gray;
        var icon = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromArgb(36, c.R, c.G, c.B)), Child = Ui.Icon(Icons.Shield, 18, color) };
        var head = Ui.Row(10, icon, new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 240 });
        var text = new TextBlock { Text = I18n.T("v20.backup.text"), FontSize = 13.5, LineHeight = 19, Foreground = Ui.Res("Muted"), TextWrapping = TextWrapping.Wrap };
        var now = Ui.Button(I18n.T("v11.sb.backup.now"), BackupPanel.Quick, "", Icons.Shield);
        now.IsEnabled = !running;
        now.HorizontalAlignment = HorizontalAlignment.Left;
        var settings = Kit20.Link(I18n.T("v20.backup.settings"), () => MainWindow.Current?.Navigate(() => new SettingsPage("backups")), 13.5);
        return Kit20.Card(Ui.Col(10, head, text, Ui.Row(8, now, settings)), new Thickness(20, 18));
    }
}
