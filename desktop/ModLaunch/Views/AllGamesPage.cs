using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Games;
using ModLaunch.Social;

namespace ModLaunch.Views;

/// <summary>
/// 27.0: единая библиотека — все игры со всех лаунчеров ПК в одном списке. Слева — поиск, фильтр по магазину,
/// недавние сверху; справа — выбранная игра: «Играть» (через её лаунчер или через ModLaunch с модами),
/// последний запуск, часы, размер, папка и есть ли моды. Раскладка — как у библиотеки Steam (SteamLibraryPage).
/// </summary>
public sealed class AllGamesPage : Page
{
    public override string Title => I18n.T("v27.lib.title");
    public override string SearchHint => I18n.T("v27.lib.search");

    string _query = "";
    string _store = Settings.Data.Str("allGamesStore") ?? "all";
    // 30.3: фильтр по статусу прохождения ("" — все).
    string _status = Settings.Data.Str("allGamesStatus") is { } st && Shelves.Statuses.Contains(st) ? st : "";
    string _selected = Settings.Data.Str("allGamesSel") ?? "";
    ScrollViewer _list = new();
    ContentControl _detail = new();
    ScrollViewer? _right;

    /// <summary>Снимки экрана: показать низ страницы игры (отзывы).</summary>
    internal void ScrollDetailToEnd() => _right?.ScrollToEnd();

    /// <summary>Снимки экрана: прокрутить страницу игры до нужного места.</summary>
    internal void ScrollDetailTo(double y) { if (_right is not null) _right.Offset = new Vector(0, y); }

    public AllGamesPage(string? key = null)
    {
        if (key is not null) _selected = key;
        void Changed() => Dispatcher.UIThread.Post(() => { if (MainWindow.Current?.CurrentPage == this) Build(); });
        AttachedToVisualTree += (_, _) => { AllGames.Changed += Changed; SteamAccount.Changed += Refresh; Shelves.Changed += Changed; };
        DetachedFromVisualTree += (_, _) => { AllGames.Changed -= Changed; SteamAccount.Changed -= Refresh; Shelves.Changed -= Changed; };
    }

    static void Refresh() => Dispatcher.UIThread.Post(() => _ = AllGames.Refresh());

    public override void Search(string text) { _query = text.Trim(); RenderList(); }

    IReadOnlyList<LibGame> Games => AllGames.Current ?? [];

    IEnumerable<LibGame> Visible()
    {
        IEnumerable<LibGame> all = Games;
        // 30.2: полки — избранное, свои коллекции, скрытые (скрытые видны только в своём фильтре).
        var shelves = Shelves.Current;
        if (_store == "hidden") all = all.Where(g => shelves.IsHidden(g.Key));
        else
        {
            all = all.Where(g => !shelves.IsHidden(g.Key));
            if (_store == "installed") all = all.Where(g => g.Installed);
            else if (_store == "fav") all = all.Where(g => shelves.IsFavorite(g.Key));
            else if (_store.StartsWith("c:", StringComparison.Ordinal)) { var name = _store[2..]; all = all.Where(g => shelves.Contains(name, g.Key)); }
            else if (_store != "all") all = all.Where(g => g.Store == _store);
        }
        if (_status != "") all = all.Where(g => shelves.StatusOf(g.Key) == _status);
        if (_query != "") all = all.Where(g => g.Name.Contains(_query, StringComparison.CurrentCultureIgnoreCase));
        return all;
    }

    public override void Build()
    {
        if (AllGames.Stale) _ = AllGames.Refresh();
        var games = Games;
        if (AllGames.Current is null)
        {
            Content = Center(Ui.Col(12, Ui.Text(I18n.T("v27.lib.scanning"), "h2"), Ui.Text(I18n.T("v27.lib.scanning.text"), "muted", wrap: true)));
            return;
        }
        if (games.Count == 0)
        {
            Content = Center(Ui.Col(10,
                Ui.Text(I18n.T("v27.lib.none"), "h2"),
                Ui.Text(I18n.T("v27.lib.none.text"), "muted", wrap: true),
                Ui.Row(8, Ui.Button(I18n.T("add.title"), () => MainWindow.Current?.Navigate(() => new AddGamePage()), "primary", Icons.Plus))));
            return;
        }
        var shelves = Shelves.Current;
        if (_store is "fav" && shelves.FavoriteCount == 0) _store = "all";
        else if (_store is "hidden" && shelves.HiddenCount == 0) _store = "all";
        else if (_store.StartsWith("c:", StringComparison.Ordinal) && !shelves.Collections.Contains(_store[2..])) _store = "all";
        else if (_store is not ("all" or "installed" or "fav" or "hidden") && !_store.StartsWith("c:", StringComparison.Ordinal) && !games.Any(g => g.Store == _store)) _store = "all";
        if (games.All(g => g.Key != _selected)) _selected = (games.Where(g => g.Installed).OrderByDescending(g => AllGames.Played(g).Last).FirstOrDefault() ?? games[0]).Key;
        _list = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _detail = new ContentControl();

        var search = new TextBox { Watermark = I18n.T("v27.lib.filter"), Text = _query, Classes = { "search" } };
        search.TextChanged += (_, _) => { _query = search.Text?.Trim() ?? ""; RenderList(); };
        var chips = new WrapPanel();
        Button Chip(string id, string text, int count)
        {
            var b = Ui.Button(text + " " + count.ToString("N0", I18n.Culture), () =>
            {
                _store = id;
                Settings.Data["allGamesStore"] = id;
                Settings.Save();
                Build();
            }, id == _store ? "chip active" : "chip");
            b.Margin = new Thickness(0, 0, 4, 4);
            chips.Children.Add(b);
            return b;
        }
        var shown = games.Where(g => !shelves.IsHidden(g.Key)).ToList();
        Chip("all", I18n.T("v27.lib.f.all"), shown.Count);
        Chip("installed", I18n.T("v27.lib.f.installed"), shown.Count(g => g.Installed));
        if (shelves.FavoriteCount > 0) Chip("fav", "★ " + I18n.T("v30.shelf.fav"), shown.Count(g => shelves.IsFavorite(g.Key)));
        foreach (var name in shelves.Collections)
        {
            var collection = name;
            Ctx.Attach(Chip("c:" + name, name, shown.Count(g => shelves.Contains(collection, g.Key))), () => Ctx.Menu(
                Ctx.Item(I18n.T("v30.shelf.rename"), Icons.Edit, () => AskName(collection, null)),
                Ctx.Item(I18n.T("v30.shelf.delete"), Icons.Trash, () => DeleteCollection(collection))));
        }
        foreach (var store in Stores.Order)
            if (shown.Count(g => g.Store == store) is var n and > 0) Chip(store, Stores.Title(store), n);
        if (shelves.HiddenCount > 0) Chip("hidden", I18n.T("v30.shelf.hidden"), games.Count(g => shelves.IsHidden(g.Key)));
        var newCollection = Ui.Button("+ " + I18n.T("v30.shelf.collection"), () => AskName(null, null), "chip");
        newCollection.Margin = new Thickness(0, 0, 4, 4);
        ToolTip.SetTip(newCollection, I18n.T("v30.shelf.new.hint"));
        chips.Children.Add(newCollection);

        // 30.2: порядок списка — по имени, недавние, наиграно, размер.
        var sort = new ComboBox
        {
            ItemsSource = Shelves.Sorts.Select(s => I18n.T("v30.shelf.sort." + s)).ToList(),
            SelectedIndex = Array.IndexOf(Shelves.Sorts, shelves.Sort), MinWidth = 150, HorizontalAlignment = HorizontalAlignment.Left,
        };
        ToolTip.SetTip(sort, I18n.T("v30.shelf.sort"));
        sort.SelectionChanged += (_, _) => { if (sort.SelectedIndex >= 0 && Shelves.Sorts[sort.SelectedIndex] != shelves.Sort) shelves.Sort = Shelves.Sorts[sort.SelectedIndex]; };
        // 30.3: статус прохождения — фильтром рядом с порядком.
        var statuses = new[] { "" }.Concat(Shelves.Statuses).ToArray();
        var statusBox = new ComboBox
        {
            ItemsSource = statuses.Select(s => s == "" ? I18n.T("v30.status.any") : I18n.T("v30.status." + s) + " " + shelves.CountStatus(s).ToString(I18n.Culture)).ToList(),
            SelectedIndex = Array.IndexOf(statuses, _status), MinWidth = 110,
        };
        ToolTip.SetTip(statusBox, I18n.T("v30.status.title"));
        statusBox.SelectionChanged += (_, _) =>
        {
            if (statusBox.SelectedIndex < 0 || statuses[statusBox.SelectedIndex] == _status) return;
            _status = statuses[statusBox.SelectedIndex];
            Settings.Data["allGamesStatus"] = _status;
            Settings.Save();
            Build();
        };
        // 30.3: «Во что поиграть?» — случайная игра из установленных (пройденные и брошенные — нет).
        var dice = Ui.Button("🎲 " + I18n.T("v30.pick.short"), PickRandom, "chip");
        dice.Margin = new Thickness(0, 0, 4, 4);
        ToolTip.SetTip(dice, I18n.T("v30.pick.title"));
        chips.Children.Add(dice);
        // Два списка делят ширину поровну, кубик — справа: в колонке 300 px всё помещается.
        var sortRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,1.12*"), ColumnSpacing = 6 };
        sort.MinWidth = 0;
        statusBox.MinWidth = 0;
        sort.HorizontalAlignment = statusBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        sortRow.Children.Add(sort);
        Grid.SetColumn(statusBox, 1);
        sortRow.Children.Add(statusBox);


        var hours = games.Sum(g => AllGames.Played(g).Minutes) / 60;
        var refresh = Ui.Button("", () => { _ = AllGames.Refresh(); }, "icon ghost", Icons.Refresh, I18n.T("v27.lib.refresh"));
        // 28.0: «Желаемое» — рядом с библиотекой, как в Steam.
        var wish = Ui.Button("", () => MainWindow.Current?.Navigate(() => new WishlistPage()), "icon ghost", Icons.Heart, I18n.T("v28.wish.title") + " · Ctrl+Shift+W");
        var titleRow = new DockPanel();
        DockPanel.SetDock(refresh, Dock.Right);
        titleRow.Children.Add(refresh);
        DockPanel.SetDock(wish, Dock.Right);
        titleRow.Children.Add(wish);
        // 29.0: «Для вас» — похожие игры.
        var forYou = Ui.Button("", () => MainWindow.Current?.Navigate(() => new RecommendPage()), "icon ghost", Icons.Sparkles, I18n.T("v29.rec.title"));
        DockPanel.SetDock(forYou, Dock.Right);
        titleRow.Children.Add(forYou);
        // 30.2: «Место на дисках» — для игр из всех лаунчеров.
        var disk = Ui.Button("", () => MainWindow.Current?.Navigate(() => new StoragePage()), "icon ghost", Icons.Folder, I18n.T("v30.disk.title"));
        DockPanel.SetDock(disk, Dock.Right);
        titleRow.Children.Add(disk);
        titleRow.Children.Add(Ui.Row(10, Ui.Icon(Icons.Grid, 16, Ui.Res("Muted")), Ui.Text(I18n.T("v27.lib.title"), "h3")));
        var stores = games.Select(g => g.Store).Distinct().Count();
        var head = Ui.Col(10,
            titleRow,
            Ui.Text(I18n.T("v27.lib.total", ("n", games.Count.ToString("N0", I18n.Culture)), ("stores", stores), ("h", hours.ToString("N0", I18n.Culture))), "tiny muted"),
            search, chips, sortRow);
        head.Margin = new Thickness(16, 18, 16, 10);

        var left = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        left.Children.Add(head);
        var add = Ui.Button(I18n.T("v27.lib.addOwn"), () => MainWindow.Current?.Navigate(() => new AddGamePage()), "ghost", Icons.Plus);
        add.HorizontalAlignment = HorizontalAlignment.Stretch;
        add.Margin = new Thickness(10, 6, 10, 12);
        DockPanel.SetDock(add, Dock.Bottom);
        left.Children.Add(add);
        left.Children.Add(_list);

        var side = new Border { Width = 300, BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(0, 0, 1, 0), Background = Ui.Res("Layer"), Child = left };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(side);
        var right = _right = new ScrollViewer { Content = _detail, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        Content = grid;
        RenderList();
        RenderDetail();
    }

    static Control Center(Control inner)
    {
        var card = Ui.Card(inner, 28);
        card.Margin = new Thickness(34, 28);
        card.MaxWidth = 720;
        card.HorizontalAlignment = HorizontalAlignment.Left;
        card.VerticalAlignment = VerticalAlignment.Top;
        return card;
    }

    void RenderList()
    {
        var col = new StackPanel { Spacing = 1, Margin = new Thickness(8, 0, 8, 8) };
        var visible = Visible().ToList();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-14).ToUnixTimeSeconds();
        var shelves = Shelves.Current;
        var top = _query == "" && _store is "all" or "installed";
        // 30.2: избранное — первой полкой, как в Steam.
        var favorites = top ? visible.Where(g => shelves.IsFavorite(g.Key)).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList() : [];
        if (favorites.Count > 0)
        {
            col.Children.Add(Group("★ " + I18n.T("v30.shelf.fav")));
            foreach (var g in favorites) col.Children.Add(Row(g));
        }
        var recent = top && shelves.Sort != "recent" ? visible.Select(g => (Game: g, Last: AllGames.Played(g).Last)).Where(x => x.Last >= cutoff).OrderByDescending(x => x.Last).Take(8).Select(x => x.Game).ToList() : [];
        if (recent.Count > 0)
        {
            col.Children.Add(Group(I18n.T("v27.lib.recent")));
            foreach (var g in recent) col.Children.Add(Row(g));
        }
        col.Children.Add(Group(I18n.T("v27.lib.allGames", ("n", visible.Count.ToString("N0", I18n.Culture)))));
        foreach (var g in Shelves.Order(visible, shelves.Sort, AllGames.Played)) col.Children.Add(Row(g));
        if (visible.Count == 0)
            col.Children.Add(new Border
            {
                Padding = new Thickness(10, 8),
                Child = Ui.Text(_store.StartsWith("c:", StringComparison.Ordinal) && _query == "" ? I18n.T("v30.shelf.empty") : I18n.T("v27.lib.nothing"), "small muted", wrap: true),
            });
        _list.Content = col;
    }

    static Control Group(string text) => new TextBlock
    {
        Text = text.ToUpper(I18n.Culture), FontSize = 10, FontWeight = FontWeight.SemiBold, LetterSpacing = 1, Foreground = Ui.Res("Faint"), Margin = new Thickness(10, 12, 10, 6),
    };

    static string NameOf(LibGame g) => g.Name == "" ? "Steam · " + g.Key[(g.Key.IndexOf(':') + 1)..] : g.Name;

    /// <summary>Картинка игры: Steam (свой номер или найденный по имени), логотип пакета Xbox или буква на цветном фоне.</summary>
    static Control Art(LibGame g, int decode, bool hero = false)
    {
        if (g.SteamAppId > 0)
            return hero ? HomePage.WebArt(SteamLibrary.Hero(g.SteamAppId), GameDef.SteamArt(g.SteamAppId), g.Name, decode) : HomePage.WebArt(GameDef.SteamArt(g.SteamAppId), null, g.Name, decode);
        var art = HomePage.WebArt("", null, g.Name, decode);
        if (g.Source?.Logo is { } logo && art is Panel panel)
        {
            var image = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(hero ? 60 : 3) };
            panel.Children.Add(image);
            _ = Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(logo)) return;
                    using var s = File.OpenRead(logo);
                    var bmp = Bitmap.DecodeToWidth(s, Math.Min(decode, 480));
                    Dispatcher.UIThread.Post(() => image.Source = bmp);
                }
                catch { }
            });
        }
        return art;
    }

    Control Row(LibGame g)
    {
        var art = new Border { Width = 46, Height = 22, CornerRadius = new CornerRadius(3), ClipToBounds = true, Child = Art(g, 120), Opacity = g.Installed ? 1 : 0.55 };
        var name = new TextBlock
        {
            Text = NameOf(g), FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            Foreground = g.Installed ? Ui.Res("Text") : Ui.Res("Muted"),
        };
        var store = new TextBlock { Text = Stores.Title(g.Store), FontSize = 10.5, Foreground = Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(art, Dock.Left);
        art.Margin = new Thickness(0, 0, 10, 0);
        row.Children.Add(art);
        if (AllGames.IsRunning(g))
        {
            var dot = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Background = Ui.Res("Good"), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(dot, Dock.Right);
            row.Children.Add(dot);
        }
        if (_store is not ("steam" or "epic" or "gog" or "xbox" or "ea" or "ubisoft" or "battlenet" or "other"))
        {
            DockPanel.SetDock(store, Dock.Right);
            row.Children.Add(store);
        }
        if (Shelves.Current.IsFavorite(g.Key))
        {
            var star = new TextBlock { Text = "★", FontSize = 11, Foreground = Ui.Res("Warn"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(star, Dock.Right);
            row.Children.Add(star);
        }
        row.Children.Add(name);
        var b = new Button { Classes = { "ghost", "steam-row" }, Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8, 5) };
        if (g.Key == _selected) { b.Classes.Add("active"); b.Background = Ui.Res("Surface2"); }
        b.Click += (_, _) => { _selected = g.Key; Settings.Data["allGamesSel"] = g.Key; Settings.Save(); RenderList(); RenderDetail(); };
        b.DoubleTapped += (_, _) => { if (g.Installed) Play(g); };
        ToolTip.SetTip(b, NameOf(g) + " · " + Stores.Title(g.Store));
        Ctx.Attach(b, () => ShelfMenu(g));
        return b;
    }

    // ---------------------------------------------------------------- 30.2: полки

    readonly Random _dice = new();

    /// <summary>Случайная игра: выделить её и сказать, почему бы не она.</summary>
    void PickRandom()
    {
        if (Shelves.Current.Pick(Games, g => AllGames.Played(g).Last, _dice, DateTimeOffset.UtcNow, _selected) is not { } g)
        {
            MainWindow.Current?.Toast(I18n.T("v30.pick.none"), bad: true);
            return;
        }
        _selected = g.Key;
        if (!Visible().Any(x => x.Key == g.Key)) { _store = "all"; _status = ""; _query = ""; Build(); }
        else { RenderList(); RenderDetail(); }
        _right?.ScrollToHome();
        MainWindow.Current?.Toast(I18n.T("v30.pick.toast", ("name", NameOf(g))));
    }

    /// <summary>Меню игры: избранное, коллекции, скрыть. И правым щелчком в списке, и кнопкой на странице игры.</summary>
    MenuFlyout ShelfMenu(LibGame g)
    {
        var shelves = Shelves.Current;
        var collections = shelves.Collections.Select(c => (object?)Ctx.Item((shelves.Contains(c, g.Key) ? "✓ " : "") + c, Icons.Layers, () =>
        {
            var on = shelves.Toggle(c, g.Key);
            MainWindow.Current?.Toast(I18n.T(on ? "v30.shelf.added" : "v30.shelf.removedFrom", ("name", NameOf(g)), ("collection", c)));
        })).ToList();
        if (collections.Count > 0) collections.Add("-");
        collections.Add(Ctx.Item(I18n.T("v30.shelf.new"), Icons.Plus, () => AskName(null, g)));
        var hidden = shelves.IsHidden(g.Key);
        var current = shelves.StatusOf(g.Key);
        var statusItems = Shelves.Statuses.Select(s => (object?)Ctx.Item((current == s ? "✓ " : "") + I18n.T("v30.status." + s), StatusIcon(s), () => shelves.SetStatus(g.Key, current == s ? "" : s))).ToList();
        if (current != "") { statusItems.Add("-"); statusItems.Add(Ctx.Item(I18n.T("v30.status.clear"), Icons.Close, () => shelves.SetStatus(g.Key, ""))); }
        return Ctx.Menu(
            hidden ? null : Ctx.Item(I18n.T(shelves.IsFavorite(g.Key) ? "v30.shelf.favRemove" : "v30.shelf.favAdd"), Icons.Star, () => shelves.ToggleFavorite(g.Key)),
            Ctx.Sub(I18n.T("v30.shelf.toCollection"), Icons.Layers, collections),
            Ctx.Sub(I18n.T("v30.status.title"), Icons.Flag, statusItems),
            g.Installed ? Ctx.Item(I18n.T("v30.link.desktop"), Icons.External, () => DesktopLink(g)) : null,
            "-",
            Ctx.Item(I18n.T(hidden ? "v30.shelf.unhide" : "v30.shelf.hide"), hidden ? Icons.Eye : Icons.EyeOff, () =>
            {
                if (shelves.ToggleHidden(g.Key)) MainWindow.Current?.Toast(I18n.T("v30.shelf.hidden.toast", ("name", NameOf(g))));
            }));
    }

    internal static string StatusIcon(string status) => status switch
    {
        "playing" => Icons.Play,
        "backlog" => Icons.Clock,
        "done" => Icons.Check,
        _ => Icons.Close,
    };

    /// <summary>Ярлык игры на рабочем столе: запускает её через ModLaunch (modlaunch://play/…) из любого лаунчера.</summary>
    static void DesktopLink(LibGame g)
    {
        try
        {
            var path = GameLinks.CreateDesktop(g);
            MainWindow.Current?.Toast(I18n.T("v30.link.done", ("name", Path.GetFileNameWithoutExtension(path))));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { MainWindow.Current?.Toast(I18n.T("v30.link.fail", ("reason", e.Message)), bad: true); }
    }

    /// <summary>Новая коллекция (и сразу положить туда игру) или новое имя для существующей.</summary>
    void AskName(string? rename, LibGame? add)
    {
        if (MainWindow.Current is not { } w) return;
        var box = new TextBox { Text = rename ?? "", MaxLength = Shelves.MaxName, Watermark = I18n.T("v30.shelf.new.name"), Width = 360 };
        void Done()
        {
            try
            {
                var shelves = Shelves.Current;
                if (rename is not null)
                {
                    shelves.Rename(rename, box.Text ?? "");
                    var renamed = Shelves.CleanName(box.Text);
                    if (_store == "c:" + rename && renamed != "") { _store = "c:" + renamed; Settings.Data["allGamesStore"] = _store; Settings.Save(); }
                    w.CloseDialog();
                    Build();
                    return;
                }
                var name = shelves.Create(box.Text ?? "");
                if (add is not null && !shelves.Contains(name, add.Key)) shelves.Toggle(name, add.Key);
                w.CloseDialog();
                w.Toast(add is null ? I18n.T("v30.shelf.created", ("collection", name)) : I18n.T("v30.shelf.added", ("name", NameOf(add)), ("collection", name)));
            }
            catch (ServiceError e) { w.Toast(I18n.T("v30.shelf.err." + e.Code), bad: true); }
        }
        box.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Done(); };
        w.Dialog(I18n.T(rename is null ? "v30.shelf.new.title" : "v30.shelf.rename"), Ui.Col(10, Ui.Text(I18n.T("v30.shelf.new.text"), "muted", wrap: true), box),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T(rename is null ? "v30.shelf.create" : "v30.cur.save"), Done, "primary", Icons.Check));
        Dispatcher.UIThread.Post(() => box.Focus());
    }

    void DeleteCollection(string name)
    {
        if (MainWindow.Current is not { } w) return;
        w.Dialog(I18n.T("v30.shelf.delete"), Ui.Text(I18n.T("v30.shelf.delete.text", ("collection", name)), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("v30.shelf.delete"), () => { w.CloseDialog(); Shelves.Current.Delete(name); }, "primary", Icons.Trash));
    }

    static GameState? Managed(LibGame g) => g.ManagedId is { } id ? AppState.Games.FirstOrDefault(x => x.Def.Id == id && x.Path is not null) : null;

    internal static void Play(LibGame g)
    {
        if (Managed(g) is { } managed) { Actions.Play(managed); return; }
        if (!g.Installed) { SteamLibrary.Install(g.SteamAppId); return; }
        if (AllGames.Play(g) is { } error) MainWindow.Current?.Toast(error, bad: true);
        else MainWindow.Current?.Toast(I18n.T("v27.lib.starting", ("name", NameOf(g)), ("store", Stores.Title(g.Store))));
    }

    void RenderDetail()
    {
        if (Games.FirstOrDefault(x => x.Key == _selected) is not { } g) { _detail.Content = null; return; }
        var managed = Managed(g);
        var dir = managed is null ? (g.SteamAppId > 0 ? GameDirectory.BySteam(g.SteamAppId) : null) ?? GameDirectory.ByName(g.Name) : null;
        var (last, minutes) = AllGames.Played(g);

        var hero = new Panel { Height = 320, ClipToBounds = true };
        hero.Children.Add(Art(g, 1600, hero: true));
        var bg = (Ui.Res("Layer") as ISolidColorBrush)?.Color ?? Colors.Black;
        hero.Children.Add(new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(0, bg.R, bg.G, bg.B), 0.35), new GradientStop(bg, 1) },
            },
        });
        var badge = new Border
        {
            CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3), Background = Ui.Res("Surface2"), HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = Stores.Title(g.Store), FontSize = 11.5, FontWeight = FontWeight.SemiBold, Foreground = Ui.Res("Text") },
        };
        var title = new TextBlock { Text = NameOf(g), FontSize = 34, FontWeight = FontWeight.SemiBold, Foreground = Ui.Res("Text"), TextWrapping = TextWrapping.Wrap, MaxWidth = 900 };
        var badges = Ui.Row(6, badge);
        if (Shelves.Current.StatusOf(g.Key) is { Length: > 0 } status)
            badges.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3), Background = Ui.Res("BrandSoft"),
                Child = Ui.Row(5, Ui.Icon(StatusIcon(status), 11, Ui.Res("Brand2")), new TextBlock { Text = I18n.T("v30.status." + status), FontSize = 11.5, FontWeight = FontWeight.SemiBold, Foreground = Ui.Res("Brand2") }),
            });
        hero.Children.Add(new Border { Padding = new Thickness(34, 0, 34, 22), VerticalAlignment = VerticalAlignment.Bottom, Child = Ui.Col(10, badges, title) });

        Button main;
        if (AllGames.IsRunning(g))
        {
            main = Ui.Button(I18n.T("v27.lib.running"), () => { }, "primary big", Icons.Play);
            main.IsEnabled = false;
        }
        else if (!g.Installed) main = Ui.Button(I18n.T("v12w.steam.install"), () => Play(g), "primary big", Icons.Download);
        else main = Ui.Button(managed is not null ? I18n.T("v27.lib.playMods") : I18n.T("games.play"), () => Play(g), "primary big", Icons.Play);
        main.MinWidth = 160;
        var bar = new WrapPanel { Orientation = Orientation.Horizontal };
        bar.Children.Add(main);
        bar.Children.Add(Fact(I18n.T("v14.steam.last"), last > 0 ? When(last) : I18n.T("v14.steam.never")));
        var played = Fact(I18n.T("v14.steam.played"), Hours(minutes));
        if (g.Store != "steam") ToolTip.SetTip(played, I18n.T("v27.lib.ownTime"));
        bar.Children.Add(played);
        if (g.Size > 0) bar.Children.Add(Fact(I18n.T("v14.steam.size"), SizeText(g.Size)));

        var tools = Ui.Row(4);
        if (AllGames.LauncherUri(g) is { } uri)
            tools.Children.Add(Ui.Button("", () => OpenUri(uri, g), "icon ghost", Icons.External, I18n.T("v27.lib.inLauncher", ("store", Stores.Title(g.Store)))));
        if (g.SteamAppId > 0) tools.Children.Add(Ui.Button("", () => SteamLibrary.StorePage(g.SteamAppId), "icon ghost", Icons.Globe, I18n.T("v12w.steam.page")));
        var folder = g.Source?.AltDir ?? g.Dir;
        if (folder is not null && Directory.Exists(folder)) tools.Children.Add(Ui.Button("", () => Ui.OpenUrl(folder), "icon ghost", Icons.Folder, I18n.T("v14.steam.folder")));
        // 30.2: избранное и полки — как в Steam, но для игр из любого лаунчера.
        var fav = Shelves.Current.IsFavorite(g.Key);
        var star = Ui.Button(fav ? "★" : "☆", () => Shelves.Current.ToggleFavorite(g.Key), "icon ghost", null, I18n.T(fav ? "v30.shelf.favRemove" : "v30.shelf.favAdd"));
        if (fav) star.Foreground = Ui.Res("Warn");
        star.FontSize = 18;
        tools.Children.Add(star);
        Button? more = null;
        more = Ui.Button("", () => ShelfMenu(g).ShowAt(more!), "icon ghost", Icons.Layers, I18n.T("v30.shelf.toCollection"));
        tools.Children.Add(more);
        tools.VerticalAlignment = VerticalAlignment.Center;
        tools.Margin = new Thickness(12, 0, 0, 0);
        bar.Children.Add(tools);
        var barCard = new Border { Classes = { "card" }, Padding = new Thickness(18, 14), Child = bar };

        Control mods;
        if (managed is not null)
            mods = Line(Icons.Layers, I18n.T("v14.steam.inModLaunch"), I18n.T("v27.lib.inModLaunch.text"),
                Ui.Button(I18n.T("v12w.steam.open"), () => MainWindow.Current?.Navigate(() => new GamePage(managed.Def.Id)), "", Icons.Forward));
        else if (dir is not null && g.Installed)
        {
            var where = string.Join(" · ", new[] { dir.Community is not null ? "Thunderstore" : null, dir.NexusDomain is not null ? "Nexus Mods" : null }.OfType<string>());
            mods = Line(Icons.Package, I18n.T("v14.steam.hasMods"), where == "" ? I18n.T("v14.steam.hasMods.text") : where,
                Ui.Button(I18n.T("v12w.steam.mods"), () => MainWindow.Current?.Navigate(() => new AddGamePage(g.Name)), "", Icons.Plus));
        }
        else if (dir is not null)
            mods = Line(Icons.Package, I18n.T("v14.steam.hasMods"), I18n.T("v27.lib.modsAfterInstall"), null);
        else mods = Line(Icons.Package, I18n.T("v14.steam.noMods"), I18n.T("v27.lib.noMods.text"), null);

        // 28.0: отзывы игроков — оценить может только тот, кто наиграл час и больше.
        var body = Ui.Col(16, barCard);
        // 30.0: достижения Steam — из файлов Steam на этом ПК.
        if (SteamAchievementsCard.For(g) is { } achievements) body.Children.Add(achievements);
        body.Children.Add(Ui.Card(mods, 18));
        body.Children.Add(NotesCard(g));
        // 30.4: кто из друзей сейчас в этой игре — написать или позвать.
        if (FriendsInGame(g) is { } friendsCard) body.Children.Add(friendsCard);
        // 30.7: последние снимки экрана — свои (Ctrl+F12) и Steam.
        if (ShotsCard(g) is { } shots) body.Children.Add(shots);
        body.Children.Add(new GameReviewsCard(g));
        // 30.0: что пишут об игре кураторы, на которых подписан.
        body.Children.Add(new CuratorPicksCard(g));
        if (folder is not null) body.Children.Add(Ui.Text(folder, "tiny muted", wrap: true));
        body.Margin = new Thickness(34, -6, 34, 34);
        body.MaxWidth = 1100;
        body.HorizontalAlignment = HorizontalAlignment.Left;
        _detail.Content = new StackPanel { Children = { hero, body } };
    }

    static void OpenUri(string uri, LibGame g)
    {
        if (Program.Screenshot) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch { MainWindow.Current?.Toast(I18n.T("v27.lib.noLauncher", ("store", Stores.Title(g.Store))), bad: true); }
    }

    /// <summary>Последние снимки игры полосой: щелчок — открыть снимок, «Все снимки» — папка. Нет снимков — карточки нет.</summary>
    static Control? ShotsCard(LibGame g)
    {
        var folders = ScreenshotGallery.Folders(g);
        var files = ScreenshotGallery.Find(folders, 7);
        if (files.Count == 0) return null;
        var strip = new UniformGrid { Columns = 3 };
        foreach (var file in files.Take(6))
        {
            var path = file;
            var image = new Image { Stretch = Stretch.UniformToFill };
            var tile = new Button
            {
                Classes = { "ghost" }, Padding = new Thickness(0), Margin = new Thickness(0, 0, 8, 8), Height = 120, HorizontalAlignment = HorizontalAlignment.Stretch,
                Content = new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Background = Ui.Res("Surface3"), Child = image },
            };
            ToolTip.SetTip(tile, Path.GetFileName(path) + " · " + Ui.Ago(File.GetLastWriteTimeUtc(path)));
            tile.Click += (_, _) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or FileNotFoundException) { MainWindow.Current?.Toast(e.Message, bad: true); }
            };
            strip.Children.Add(tile);
            _ = Task.Run(() =>
            {
                try
                {
                    using var s = File.OpenRead(path);
                    var bmp = Bitmap.DecodeToWidth(s, 360);
                    Dispatcher.UIThread.Post(() => image.Source = bmp);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { }
            });
        }
        var folder = folders.FirstOrDefault(f => f.StartsWith(GameShots.Root, StringComparison.OrdinalIgnoreCase)) ?? folders[0];
        var all = Ui.Button(I18n.T("v30.shots.all"), () => Actions.OpenFolder(folder), "ghost", Icons.Folder);
        all.VerticalAlignment = VerticalAlignment.Center;
        var head = new DockPanel();
        DockPanel.SetDock(all, Dock.Right);
        head.Children.Add(all);
        head.Children.Add(Ui.Row(8, Ui.Icon(Icons.Image, 16, Ui.Res("Brand2")), Ui.Text(I18n.T("v30.shots.title"), "h3")));
        return Ui.Card(Ui.Col(10, head, strip, Ui.Text(I18n.T("v30.shots.hint", ("key", Hotkey.Display(Hotkey.ShotKeyOf(Settings.Data.Str("shotKey"))))), "tiny muted", wrap: true)), 18);
    }

    /// <summary>Друзья, которые прямо сейчас в этой игре (по ключу игры, номеру игры ModLaunch или по имени).</summary>
    internal static List<Social.Friend> PlayingNow(LibGame g, IEnumerable<Social.Friend> friends)
    {
        var key = Social.GameReviews.Key(g);
        return friends.Where(f => f.State == "playing" && (f.Game == key || (g.ManagedId is { } id && f.Game == id)
            || string.Equals(f.GameName, g.Name, StringComparison.CurrentCultureIgnoreCase))).ToList();
    }

    [SelfTest]
    static string FriendsPlayingThisGame()
    {
        var g = new LibGame("steam:1245620", "steam", "ELDEN RING", null, 1245620, true, 0);
        var managed = new LibGame("steam:264710", "steam", "Subnautica", null, 264710, true, 0, ManagedId: "subnautica");
        Social.Friend F(string name, string state, string game, string gameName) => new("u-" + name, name, state, game, gameName, null, null);
        var list = new[] { F("a", "playing", "steam:1245620", "Elden Ring"), F("b", "online", "steam:1245620", ""), F("c", "playing", "epic:x", "elden ring"),
            F("d", "playing", "subnautica", "Subnautica"), F("e", "playing", "steam:1", "Other") };
        if (string.Join(",", PlayingNow(g, list).Select(f => f.Name)) != "a,c") throw new Exception("by key and by name, only playing");
        if (string.Join(",", PlayingNow(managed, list).Select(f => f.Name)) != "d") throw new Exception("ModLaunch game id");
        return "friend in game matched by game key, ModLaunch id or name; online-but-idle friends skipped";
    }

    static Control? FriendsInGame(LibGame g)
    {
        if (!Social.Account.SignedIn && Social.Friends.DemoView is null) return null;
        var now = PlayingNow(g, Social.Friends.View().Friends);
        if (now.Count == 0) return null;
        var col = Ui.Col(10, Ui.Row(8, Ui.Icon(Icons.Users, 16, Ui.Res("Brand2")), Ui.Text(I18n.T("v30.friends.inGame", ("n", now.Count)), "h3")));
        foreach (var f in now)
        {
            var friend = f;
            var write = Ui.Button(I18n.T("v30.friends.write"), () => MainWindow.Current?.OpenChat(friend), "ghost", Icons.Chat);
            write.VerticalAlignment = VerticalAlignment.Center;
            var line = new DockPanel();
            DockPanel.SetDock(write, Dock.Right);
            line.Children.Add(write);
            var since = f.Since is DateTime at ? " · " + I18n.T("v30.friends.since", ("time", PlayTime.Format((long)(DateTime.UtcNow - at.ToUniversalTime()).TotalMilliseconds))) : "";
            line.Children.Add(Ui.Row(8, Ui.Dot(Ui.Res("Brand2")), Ui.Text(f.Name, "h3"), Ui.Text(I18n.T("v30.friends.playing") + since, "small muted")));
            col.Children.Add(line);
        }
        return Ui.Card(col, 18);
    }

    /// <summary>30.3: заметки к игре — пароли от серверов не надо, а вот «где остановился» и коды — да. Видны и в оверлее.</summary>
    static Control NotesCard(LibGame g)
    {
        var box = new TextBox
        {
            Text = Shelves.Current.Note(g), Watermark = I18n.T("ovl.notes.placeholder"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            MinHeight = 80, MaxHeight = 260, MaxLength = Shelves.MaxNote, VerticalContentAlignment = VerticalAlignment.Top,
        };
        var saved = Ui.Text("", "small", color: Ui.Res("Good"));
        void Save()
        {
            if ((box.Text ?? "") == Shelves.Current.Note(g)) return;
            Shelves.Current.SetNote(g, box.Text ?? "");
            saved.Text = "✓ " + I18n.T("ovl.notes.saved");
        }
        box.LostFocus += (_, _) => Save();
        box.DetachedFromVisualTree += (_, _) => Save();
        box.TextChanged += (_, _) => saved.Text = "";
        return Ui.Card(Ui.Col(10, Ui.Row(8, Ui.Text(I18n.T("ovl.notes"), "h3"), saved), box), 18);
    }

    static Control Fact(string label, string value) => new Border
    {
        Margin = new Thickness(26, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent,
        Child = Ui.Col(2,
            new TextBlock { Text = label.ToUpper(I18n.Culture), FontSize = 9.5, LetterSpacing = 1, FontWeight = FontWeight.SemiBold, Foreground = Ui.Res("Faint") },
            new TextBlock { Text = value, FontSize = 14, Foreground = Ui.Res("Text") }),
    };

    static Control Line(string icon, string title, string text, Control? action)
    {
        var row = new DockPanel();
        if (action is not null)
        {
            action.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(action, Dock.Right);
            row.Children.Add(action);
        }
        row.Children.Add(Ui.Row(14, Ui.Icon(icon, 20, Ui.Res("Muted")), Ui.Col(3, Ui.Text(title, "h3"), Ui.Text(text, "small muted", wrap: true))));
        return row;
    }

    internal static string Hours(long minutes) => minutes <= 0
        ? I18n.T("v14.steam.zero")
        : minutes < 60 ? I18n.T("v14.steam.min", ("n", minutes)) : I18n.T("v14.steam.h", ("n", (minutes / 60.0).ToString(minutes < 600 ? "0.#" : "N0", I18n.Culture)));

    static string When(long unix)
    {
        var at = DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime;
        var days = (DateTime.Today - at.Date).Days;
        return days <= 0 ? I18n.T("v14.steam.today") : days == 1 ? I18n.T("v14.steam.yesterday") : at.ToString(at.Year == DateTime.Today.Year ? "d MMMM" : "d MMMM yyyy", I18n.Culture);
    }

    internal static string SizeText(long bytes) => bytes >= 1L << 30
        ? I18n.T("v12w.size.gb", ("n", (bytes / (double)(1L << 30)).ToString("0.#", I18n.Culture)))
        : GamePage.Size(bytes);
}
