using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Games;

namespace ModLaunch.Views;

/// <summary>ModLaunch Hub внутри Creator Hub: обзор модов сообщества и мои публикации.</summary>
public sealed partial class CreatorPage
{
    static List<HubMod>? _hub;
    static string? _hubError;
    static bool _hubLoading;
    string _hubSort = "trending";
    string? _hubGame;
    string? _hubTag;
    string _hubKind = "all";

    async Task LoadHub(bool force = false)
    {
        if (_hubLoading) return;
        _hubLoading = true;
        _hubError = null;
        Build();
        try { _hub = await Hub.All(force); }
        catch { _hub = []; _hubError = Hub.LastError; }
        _hubLoading = false;
        Build();
    }

    /// <summary>Для скриншотов: готовый список без сети.</summary>
    public static void DemoHub(List<HubMod> list) { _hub = list; _hubError = null; }

    Control HubView()
    {
        if (_hub is null && !_hubLoading && !Program.Screenshot) _ = LoadHub();
        var col = new StackPanel { Spacing = 16 };
        var all = _hub ?? [];

        // Сортировка, фильтры и действия — одной строкой.
        var sorts = Ui.Row(6);
        foreach (var (id, key) in new[] { ("trending", "hub.sort.trending"), ("new", "hub.sort.new"), ("updated", "hub.sort.updated"), ("downloads", "hub.sort.downloads"), ("likes", "hub.sort.likes") })
        {
            var chip = Ui.Button(I18n.T(key), () => { _hubSort = id; Build(); }, "chip");
            if (_hubSort == id) chip.Classes.Add("active");
            sorts.Children.Add(chip);
        }
        var game = new ComboBox { Width = 200 };
        var games = new List<string?> { null };
        game.Items.Add(I18n.T("hub.allGames"));
        foreach (var g in AppState.Games) { games.Add(g.Def.Id); game.Items.Add(g.Def.Name); }
        game.SelectedIndex = Math.Max(0, games.IndexOf(_hubGame));
        game.SelectionChanged += (_, _) => { var v = games[Math.Max(0, game.SelectedIndex)]; if (v != _hubGame) { _hubGame = v; Build(); } };
        var kind = new ComboBox { Width = 150 };
        var kinds = new[] { "all", "package", "script" };
        foreach (var k in kinds) kind.Items.Add(I18n.T("hub.kind." + k));
        kind.SelectedIndex = Array.IndexOf(kinds, _hubKind);
        kind.SelectionChanged += (_, _) => { var v = kinds[Math.Max(0, kind.SelectedIndex)]; if (v != _hubKind) { _hubKind = v; Build(); } };
        var right = Ui.Row(8, game, kind,
            Ui.Button("", () => _ = LoadHub(force: true), "icon ghost", Icons.Refresh, I18n.T("cr.refresh")),
            Ui.Button(I18n.T("hub.upload"), PublishArchive, "primary", Icons.Upload));
        var bar = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(right);
        sorts.VerticalAlignment = VerticalAlignment.Center;
        bar.Children.Add(sorts);
        col.Children.Add(bar);

        // Теги.
        var tags = Ui.Row(6);
        foreach (var tag in Hub.Tags)
        {
            var t = tag;
            var chip = Ui.Button("#" + I18n.T("hub.tag." + tag), () => { _hubTag = _hubTag == t ? null : t; Build(); }, "chip");
            chip.Padding = new Thickness(10, 4);
            chip.FontSize = 12;
            if (_hubTag == tag) chip.Classes.Add("active");
            tags.Children.Add(chip);
        }
        tags.Margin = new Thickness(0, 0, 0, 12);
        col.Children.Add(new ScrollViewer { Content = tags, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });

        if (_hubLoading && _hub is null)
        {
            var skeleton = new UniformGrid { Columns = 3 };
            for (var i = 0; i < 6; i++) skeleton.Children.Add(Skeleton());
            col.Children.Add(skeleton);
            return col;
        }
        if (_hubError is not null)
        {
            col.Children.Add(Ui.Card(Ui.Col(8,
                Ui.Row(10, Ui.Icon(Icons.Alert, 20, Ui.Res("Warn")), Ui.Text(I18n.T("hub.error"), "h3")),
                Ui.Text(_hubError, "muted", wrap: true),
                Ui.Button(I18n.T("hub.retry"), () => _ = LoadHub(force: true), "", Icons.Refresh)), 24));
            return col;
        }
        if (all.Count == 0)
        {
            col.Children.Add(Welcome());
            return col;
        }

        // Итоги Hub.
        var authors = all.Select(m => m.Uid).Distinct().Count();
        col.Children.Add(Ui.Row(22,
            Stat(Icons.Package, all.Count.ToString("N0"), I18n.T("hub.stat.mods")),
            Stat(Icons.Users, authors.ToString("N0"), I18n.T("hub.stat.authors")),
            Stat(Icons.Download, I18n.Compact(all.Sum(m => m.Downloads)), I18n.T("hub.stat.downloads")),
            Stat(Icons.Heart, I18n.Compact(all.Sum(m => m.Likes)), I18n.T("hub.stat.likes"))));

        if (Hub.Updates.Count > 0)
        {
            var row = Ui.Row(8, Ui.Icon(Icons.Sparkles, 16, Ui.Res("Brand2")), Ui.Text(I18n.T("hub.followUpdates", ("n", Hub.Updates.Count)), "h3"));
            col.Children.Add(Ui.Card(Ui.Col(10, row, Grid3(Hub.Updates)), 18));
        }

        var list = Hub.Sort(all.Where(m =>
            (_hubGame is null || m.Game == _hubGame) &&
            (_hubTag is null || m.Tags.Contains(_hubTag)) &&
            (_hubKind == "all" || m.Kind == _hubKind) &&
            (_filter == "" || m.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase) || m.Summary.Contains(_filter, StringComparison.OrdinalIgnoreCase)
                || m.Author.Contains(_filter, StringComparison.OrdinalIgnoreCase) || m.Tags.Any(t => t.Contains(_filter, StringComparison.OrdinalIgnoreCase)))), _hubSort).ToList();
        if (list.Count == 0) col.Children.Add(Ui.Card(Ui.Col(6, Ui.Text(I18n.T("hub.nothing"), "h3"), Ui.Text(I18n.T("hub.nothing.text"), "small muted", wrap: true)), 22));
        else col.Children.Add(Grid3(list));
        return col;
    }

    static Control Stat(string icon, string value, string label) => Ui.Row(10,
        new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(10), Background = Ui.Res("BrandSoft"), Child = Ui.Icon(icon, 18, Ui.Res("Brand2")) },
        Ui.Col(0, Ui.Text(value, "h3"), Ui.Text(label, "small muted")));

    static Control Skeleton()
    {
        var b = new Border { Classes = { "card", "skeleton" }, Height = 230, Margin = new Thickness(0, 0, 14, 14) };
        return b;
    }

    /// <summary>Пустой Hub: модов пока 0 — зовём стать первым автором.</summary>
    Control Welcome()
    {
        var art = new Border
        {
            Width = 120, Height = 120, CornerRadius = new CornerRadius(60), Background = Ui.Res("BrandSoft"),
            Child = Ui.Icon(Icons.Sparkles, 54, Ui.Res("Brand2")), Classes = { "float" },
            RenderTransform = new TranslateTransform(),
        };
        var text = Ui.Col(10,
            new TextBlock { Text = I18n.T("hub.empty"), FontSize = 26, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap },
            Ui.Text(I18n.T("hub.empty.text"), "muted", wrap: true),
            Ui.Row(10,
                Ui.Button(I18n.T("hub.upload"), PublishArchive, "primary", Icons.Upload),
                Ui.Button(I18n.T("hub.empty.workshop"), () => { _tab = "mine"; Build(); }, "", Icons.Edit),
                Ui.Button(I18n.T("cr.tab.examples"), () => { _tab = "examples"; Build(); }, "ghost", Icons.Wand)),
            Ui.Text(I18n.T("hub.empty.how"), "small muted", wrap: true));
        text.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 32 };
        grid.Children.Add(art);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return new Border { Classes = { "card", "hero" }, Padding = new Thickness(36), Child = grid };
    }

    static Control Grid3(IEnumerable<HubMod> mods)
    {
        var grid = new UniformGrid { Columns = 3 };
        var i = 0;
        foreach (var m in mods) grid.Children.Add(Card(m, i++));
        return grid;
    }

    /// <summary>Карточка мода: обложка, название, автор, счётчики.</summary>
    public static Control Card(HubMod m, int index = 0)
    {
        var game = GameCatalog.ById(m.Game);
        Control cover = m.Images.Count > 0
            ? Ui.Thumb(m.Images[0], m.Name, 400, 0, 600)
            : new Border
            {
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse(game?.Accent ?? "#7C5CFF"), 0), new GradientStop(Color.Parse("#141620"), 1) },
                },
                Child = Ui.Icon(m.IsPackage ? Icons.Package : Icons.Code, 34, Brushes.White),
            };
        if (cover is Border cb && m.Images.Count > 0) { cb.Width = double.NaN; cb.Height = double.NaN; }
        var badge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 15, 17, 22)), CornerRadius = new CornerRadius(999), Padding = new Thickness(9, 3),
            Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = (game?.ShortName ?? m.Game) + " · " + I18n.T("hub.kind." + m.Kind), FontSize = 11, Foreground = Brushes.White, FontWeight = FontWeight.SemiBold },
        };
        var top = new Border { Height = 118, ClipToBounds = true, CornerRadius = new CornerRadius(16, 16, 0, 0), Child = new Panel { Children = { cover, badge } } };

        var stats = Ui.Row(14,
            Ui.Row(5, Ui.Icon(Icons.Download, 12, Ui.Res("Muted")), Ui.Text(I18n.Compact(m.Downloads), "small muted")),
            Ui.Row(5, Ui.Icon(Icons.Heart, 12, Hub.Liked(m.Id) ? Ui.Res("Bad") : Ui.Res("Muted")), Ui.Text(I18n.Compact(m.Likes), "small muted")),
            Ui.Row(5, Ui.Icon(Icons.Chat, 12, Ui.Res("Muted")), Ui.Text(I18n.Compact(m.Comments), "small muted")),
            Ui.Text(Ui.Ago(m.Updated), "small muted"));
        var body = Ui.Col(5,
            Ui.Text(m.Name, "h3"),
            Ui.Text(I18n.T("mod.by", ("author", m.Author)) + $" · {m.Version}", "small brand"),
            new TextBlock { Text = m.Summary, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Ui.Res("Muted"), FontSize = 13, Height = 36 },
            stats);
        body.Margin = new Thickness(14, 12, 14, 14);
        var card = new Button
        {
            Classes = { "tile", "rise" }, Margin = new Thickness(0, 0, 14, 14), Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel { Children = { top, body } },
        };
        Animate.Stagger(card, index);
        card.Click += (_, _) => OpenMod(m);
        return card;
    }

    /// <summary>Открыть мод Hub: страница мода игры (если игра есть) или карточка с архивом.</summary>
    public static void OpenMod(HubMod m)
    {
        var w = MainWindow.Current!;
        if (AppState.Games.Any(g => g.Def.Id == m.Game)) { w.Navigate(() => new ModPage(m.Game, Hub.ToModInfo(m))); return; }
        w.Dialog(m.Name, Ui.Col(8, Ui.Text(I18n.T("hub.noGame", ("game", m.Game)), "muted", wrap: true), Ui.Text(m.Summary, "small", wrap: true)),
            Ui.Button(I18n.T("common.close"), w.CloseDialog),
            Ui.Button(I18n.T("add.title"), () => { w.CloseDialog(); w.Navigate(() => new AddGamePage()); }, "primary", Icons.Plus));
    }

    void PublishArchive() => HubPublish.Show(new HubDraft { Game = _hubGame ?? AppState.Games.FirstOrDefault(g => g.Status == Detect.Found)?.Def.Id ?? "" },
        fromProject: false, pack: null, done: () => { _tab = "published"; _ = LoadHub(force: true); });

    // ---------------------------------------------------------------- мои публикации

    Control PublishedView()
    {
        var col = new StackPanel { Spacing = 16 };
        if (!Social.Account.SignedIn)
        {
            col.Children.Add(Ui.Card(Ui.Col(10, Ui.Text(I18n.T("hub.mine.signin"), "h3"), Ui.Text(I18n.T("cr.publish.signin"), "muted", wrap: true),
                Ui.Button(I18n.T("acc.title"), () => MainWindow.Current?.Navigate(() => new SettingsPage("accounts")), "primary", Icons.User)), 24));
            return col;
        }
        if (_hub is null && !_hubLoading && !Program.Screenshot) _ = LoadHub();
        var mine = (_hub ?? []).Where(m => m.Uid == Social.Account.Uid).OrderByDescending(m => m.Updated).ToList();
        var head = new DockPanel();
        var upload = Ui.Button(I18n.T("hub.upload"), PublishArchive, "primary", Icons.Upload);
        DockPanel.SetDock(upload, Dock.Right);
        head.Children.Add(upload);
        head.Children.Add(Ui.Row(22,
            Stat(Icons.Package, mine.Count.ToString(), I18n.T("hub.stat.mods")),
            Stat(Icons.Download, I18n.Compact(mine.Sum(m => m.Downloads)), I18n.T("hub.stat.downloads")),
            Stat(Icons.Heart, I18n.Compact(mine.Sum(m => m.Likes)), I18n.T("hub.stat.likes")),
            Stat(Icons.Chat, I18n.Compact(mine.Sum(m => m.Comments)), I18n.T("hub.stat.comments"))));
        col.Children.Add(head);
        if (_hubError is not null) col.Children.Add(Ui.Card(Ui.Text(_hubError, "muted", wrap: true), 20));
        if (mine.Count == 0 && _hubError is null)
            col.Children.Add(Ui.Card(Ui.Col(6, Ui.Text(I18n.T("hub.mine.empty"), "h3"), Ui.Text(I18n.T("hub.mine.empty.text"), "small muted", wrap: true)), 22));
        foreach (var m in mine)
        {
            var mm = m;
            var actions = Ui.Row(6,
                Ui.Button("", () => OpenMod(mm), "icon ghost", Icons.Eye, I18n.T("hub.open")),
                Ui.Button(I18n.T("hub.newVersion"), () => NewVersion(mm), "", Icons.ArrowUp),
                Ui.Button("", () => DeleteMod(mm), "icon ghost", Icons.Trash, I18n.T("hub.delete")));
            actions.VerticalAlignment = VerticalAlignment.Center;
            var row = new DockPanel();
            DockPanel.SetDock(actions, Dock.Right);
            row.Children.Add(actions);
            var game = GameCatalog.ById(m.Game);
            row.Children.Add(Ui.Row(14, Ui.Thumb(m.Images.FirstOrDefault() ?? game?.ArtUrl, m.Name, 48, 10), Ui.Col(3,
                Ui.Text($"{m.Name} · {m.Version}", "h3"),
                Ui.Text($"{game?.Name ?? m.Game} · {I18n.T("hub.kind." + m.Kind)} · {I18n.T("cr.edited", ("when", Ui.Ago(m.Updated)))}", "small muted"),
                Ui.Row(14,
                    Ui.Text("↓ " + m.Downloads.ToString("N0"), "small"), Ui.Text("♥ " + m.Likes.ToString("N0"), "small"),
                    Ui.Text("💬 " + m.Comments.ToString("N0"), "small")))));
            col.Children.Add(Ui.Card(row, 14));
        }
        return col;
    }

    void NewVersion(HubMod m)
    {
        var parts = m.Version.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
        var next = parts.Length == 3 ? $"{parts[0]}.{parts[1]}.{parts[2] + 1}" : "1.0.1";
        var project = Projects.List().FirstOrDefault(p => p.Name == m.Name);
        HubPublish.Show(new HubDraft
        {
            Name = m.Name, Summary = m.Summary, Description = m.Description, Game = m.Game, Version = next, Code = m.Code,
            Tags = [.. m.Tags], Images = [.. m.Images],
        }, fromProject: m.Kind == "script" && project is not null, pack: null, done: () => _ = LoadHub(force: true), lockName: true);
    }

    void DeleteMod(HubMod m)
    {
        var w = MainWindow.Current!;
        w.Dialog(I18n.T("hub.delete"), Ui.Text(I18n.T("hub.delete.text", ("name", m.Name)), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("hub.delete"), async () =>
            {
                w.CloseDialog();
                try { await Hub.Delete(m); w.Toast(I18n.T("hub.deleted")); _ = LoadHub(force: true); }
                catch (Exception e) { w.Toast(Social.Firebase.Explain("creator", e), bad: true); }
            }, "primary", Icons.Trash));
    }
}
