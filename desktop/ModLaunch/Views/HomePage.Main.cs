using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Features;
using ModLaunch.Minecraft;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>
/// 9.5: «Главная» — сводка важного, а не витрина игр. Сверху — приветствие и одна строка о том,
/// что происходит. Дальше «Важное» (что не скачалось, проблемы игр, обновления модов и сборок,
/// новая версия ModLaunch, заявки в друзья) рядом с «Продолжить»; «Для вас» — моды по тем, что
/// у вас уже стоят, и почему; друзья в сети, загрузки и «Что нового»; совет дня и вход в «Ленту».
/// Игры обложками — в «Библиотеке», бесконечные подборки — в «Ленте».
/// </summary>
public sealed partial class HomePage
{
    const double MainGap = 12;
    static int _tip = -1;
    static bool _attentionAll, _checking;

    /// <summary>Яркие «молодёжные» цвета ярлыков: розовый, бирюзовый, жёлтый.</summary>
    static readonly IBrush Pink = new SolidColorBrush(Color.Parse("#FF3D81"));
    static readonly IBrush Cyan = new SolidColorBrush(Color.Parse("#19D3F5"));
    static readonly IBrush Yellow = new SolidColorBrush(Color.Parse("#FFD43B"));

    void BuildMain()
    {
        var mine = FeedGames();
        if (_featured is null && !_featuredLoading && mine.Count > 0) { _featuredLoading = true; Dispatcher.UIThread.Post(() => _ = LoadFeatured(mine)); }
        if (!_feedExtrasLoading && (_feedHub is null || _feedMarket is null)) { _feedExtrasLoading = true; _ = LoadFeedExtras(); }
        CheckHealthLater(mine);

        var notices = Notices(mine);
        var col = StoreKit.Column(spacing: MainGap, top: 18);
        var n = 0;
        if (Starter(mine) is { } starter) col.Children.Add(Intro(starter, n++));
        col.Children.Add(Intro(Header(mine, notices), n++));
        col.Children.Add(Intro(Columns(MainGap, (7, AttentionCard(notices)), (5, ContinueCard(mine))), n++));
        col.Children.Add(Intro(ForYou(mine), n++));
        col.Children.Add(Intro(Columns(MainGap, (4, FriendsCard()), (4, new DownloadsTile()), (4, NewsCard())), n++));
        col.Children.Add(Intro(Columns(MainGap, (5, TipCard()), (7, FeedTeaser(BestMods(mine)))), n++));

        if (Ads.Enabled)
        {
            _ad ??= AdSlot.Banner();
            if (_ad.Parent is Panel was) was.Children.Remove(_ad);
            col.Children.Add(_ad);
        }
        Content = new ScrollViewer { Content = col, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    /// <summary>Ряд карточек одной высоты: доли ширины (из 12) и сами карточки.</summary>
    static Grid Columns(double gap, params (int Span, Control Card)[] cells)
    {
        var grid = new Grid { ColumnSpacing = gap };
        for (var i = 0; i < cells.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(cells[i].Span, GridUnitType.Star));
            Grid.SetColumn(cells[i].Card, i);
            grid.Children.Add(cells[i].Card);
        }
        return grid;
    }

    /// <summary>Подпись карточки: «▍ВАЖНОЕ» слева, ссылка или счётчик справа.</summary>
    static DockPanel CardHead(string title, string? sub = null, Control? right = null)
    {
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        if (right is not null)
        {
            right.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(right, Dock.Right);
            head.Children.Add(right);
        }
        var words = Ui.Row(10, Gx.Eyebrow(title, Ui.Res("Text"), 12));
        if (sub is not null) words.Children.Add(new TextBlock { Text = sub, FontSize = 12, Foreground = Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        head.Children.Add(words);
        return head;
    }

    static Button LinkButton(string text, Action go)
    {
        var b = Ui.Button(text + "  ›", go, "ghost");
        b.Padding = new Thickness(8, 2);
        b.FontSize = 12.5;
        b.Foreground = Ui.Res("Muted");
        return b;
    }

    static Border Card(Control head, Control body, double minHeight = 0)
    {
        // Подпись сверху, остальное занимает всю высоту: пустое состояние стоит посередине карточки.
        DockPanel.SetDock(head, Dock.Top);
        return new Border
        {
            Classes = { "card", "home-card" }, Padding = new Thickness(16, 14), MinHeight = minHeight, ClipToBounds = true,
            Child = new DockPanel { Children = { head, body } },
        };
    }

    static IBrush Soft(IBrush tint, byte alpha = 40) =>
        tint is ISolidColorBrush b ? new SolidColorBrush(Color.FromArgb(alpha, b.Color.R, b.Color.G, b.Color.B)) : Ui.Res("BrandSoft");

    /// <summary>Пустое состояние карточки: значок в круге, заголовок, пояснение и кнопка.</summary>
    static Control Empty(string icon, IBrush tint, string title, string text, Button? action = null)
    {
        var badge = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(23), Background = Soft(tint), Child = Ui.Icon(icon, 21, tint), HorizontalAlignment = HorizontalAlignment.Left };
        var col = Ui.Col(6, badge, Ui.Text(title, "h3"), Ui.Text(text, "small muted", wrap: true));
        if (action is not null) { action.Margin = new Thickness(0, 4, 0, 0); action.HorizontalAlignment = HorizontalAlignment.Left; col.Children.Add(action); }
        col.VerticalAlignment = VerticalAlignment.Center;
        return col;
    }

    // ---------------------------------------------------------------- приветствие и строка «что происходит»

    static string Greeting()
    {
        var h = DateTime.Now.Hour;
        return I18n.T(h < 5 ? "v93.home.night" : h < 12 ? "v93.home.morning" : h < 18 ? "v93.home.day" : "v93.home.evening");
    }

    Control Header(List<GameState> mine, List<Notice> notices)
    {
        var accent = Gx.Accent;
        var back = new Panel
        {
            Children =
            {
                new Border { Background = Ui.Res("Surface") },
                new Border { Background = new RadialGradientBrush { Center = new RelativePoint(0.04, 0.0, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.04, 0.0, RelativeUnit.Relative), RadiusX = new RelativeScalar(0.55, RelativeUnit.Relative), RadiusY = new RelativeScalar(1.6, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Gx.Alpha(accent, 120), 0), new GradientStop(Gx.Alpha(accent, 0), 1) } } },
                new Border { Background = new RadialGradientBrush { Center = new RelativePoint(1, 1, RelativeUnit.Relative), GradientOrigin = new RelativePoint(1, 1, RelativeUnit.Relative), RadiusX = new RelativeScalar(0.4, RelativeUnit.Relative), RadiusY = new RelativeScalar(1.4, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.FromArgb(70, 255, 61, 129), 0), new GradientStop(Color.FromArgb(0, 255, 61, 129), 1) } } },
            },
        };
        var logo = new Border
        {
            Width = 54, Height = 54, CornerRadius = new CornerRadius(16), Background = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), BorderThickness = new Thickness(1),
            Child = new Image { Source = Images.Asset("icon.png", 128), Width = 34, Height = 34 },
        };
        var p = Social.Account.Get();
        var name = p.SignedIn ? p.Name ?? I18n.T("v93.feed.player") : I18n.T("v93.feed.player");
        var date = DateTime.Now.ToString("dddd, d MMMM", I18n.Culture);
        var greet = new TextBlock { Text = Greeting() + ", " + name, FontFamily = Gx.Display, FontSize = 26, FontWeight = FontWeight.Bold, LetterSpacing = -0.6, TextTrimming = TextTrimming.CharacterEllipsis };

        // Одна строка о главном: цветная точка — пункт; нажатие ведёт туда, где с ним разбираться.
        var line = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        void Part(IBrush dot, string text, Action? go = null)
        {
            var row = Ui.Row(7, new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = dot, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Ui.Res("Text"), VerticalAlignment = VerticalAlignment.Center });
            var chip = new Border { Classes = { "home-part" }, Padding = new Thickness(10, 4), CornerRadius = new CornerRadius(999), Margin = new Thickness(0, 0, 6, 4), Background = Soft(dot, 26), Child = row };
            if (go is not null) { chip.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand); StoreKit.OnClick(chip, go); }
            line.Children.Add(chip);
        }
        var updates = mine.Sum(g => Features.ModUpdates.Found.TryGetValue(g.Def.Id, out var l) ? l.Count : 0) + McUpdatesTotal();
        var problems = notices.Count(x => x.Kind is "failed" or "health");
        var friends = Social.Friends.View().Friends.Count(f => f.State != "offline");
        if (problems > 0) Part(Ui.Res("Bad"), I18n.T("h95.sub.problems", ("n", problems)));
        if (updates > 0) Part(Pink, I18n.T("h95.sub.updates", ("n", updates)));
        if (Jobs.ActiveCount > 0) Part(Ui.Res("Brand2"), I18n.T("h95.sub.downloads", ("n", Jobs.ActiveCount)), () => MainWindow.Current?.OpenDownloads());
        if (friends > 0) Part(Ui.Res("Good"), I18n.T("h95.sub.friends", ("n", friends)), () => MainWindow.Current?.Navigate(() => new FriendsPage()));
        if (Setup.Updater.Available) Part(Yellow, I18n.T("h95.sub.app"), () => MainWindow.Current?.ShowUpdate());
        if (line.Children.Count == 0) Part(Ui.Res("Good"), I18n.T("h95.sub.calm"));

        var words = Ui.Col(3, Gx.Eyebrow(date, Ui.Res("Muted"), 10.5), greet, line);
        words.VerticalAlignment = VerticalAlignment.Center;

        // Справа — три цифры и две кнопки.
        var hours = mine.Sum(g => Features.PlayTime.Get(g.Def.Id).TotalMs) / 3_600_000.0;
        Control Stat(string value, string label) => Gx.Stat(value, label, null, 22);
        Control Divider() => new Border { Width = 1, Height = 34, Background = Ui.Res("Line"), VerticalAlignment = VerticalAlignment.Center };
        var stats = Ui.Row(18, Stat(mine.Count.ToString(), I18n.T("h95.stat.games")), Divider(), Stat(mine.Sum(g => g.ModCount).ToString(), I18n.T("h95.stat.mods")), Divider(), Stat(((int)hours).ToString(), I18n.T("h95.stat.hours")));
        stats.HorizontalAlignment = HorizontalAlignment.Right;
        var feed = Ui.Button(I18n.T("v93.home.toFeed"), () => MainWindow.Current?.Navigate(() => new HomePage(feed: true)), "primary", Icons.Flame);
        var add = Ui.Button(I18n.T("add.title"), () => MainWindow.Current?.Navigate(() => new AddGamePage()), "", Icons.Plus);
        var buttons = Ui.Row(8, add, feed);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        var right = Ui.Col(12, stats, buttons);
        right.VerticalAlignment = VerticalAlignment.Center;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 16, Margin = new Thickness(20, 16, 20, 16) };
        grid.Children.Add(logo);
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        return new Border { Classes = { "card", "home-card", "home-head" }, Padding = new Thickness(0), ClipToBounds = true, Child = new Panel { Children = { back, grid } } };
    }

    // ---------------------------------------------------------------- «Важное»

    /// <summary>Одна строка «Важного»: что случилось, кнопка «сделать» и куда ведёт нажатие на строку.</summary>
    sealed record Notice(string Kind, string Icon, IBrush Tint, string Title, string Text, string? Action = null, Action? Run = null, Action? Open = null);

    static int McUpdatesTotal() => Mc.State is { Status: Detect.Found } ? Mc.Instances().Sum(McContent.UpdateCount) : 0;

    /// <summary>Что требует внимания — по важности: ошибки, проблемы игр, новая версия, обновления, заявки.</summary>
    static List<Notice> Notices(List<GameState> mine)
    {
        var list = new List<Notice>();
        static void Go(Func<Page> page) => MainWindow.Current?.Navigate(page);

        var failed = Jobs.All.Where(j => j.Status == JobStatus.Failed && !j.Retried).ToList();
        if (failed.Count > 0)
            list.Add(new("failed", Icons.Alert, Ui.Res("Bad"), I18n.T("h95.a.failed", ("n", failed.Count)),
                I18n.T("h95.a.failed.text", ("list", string.Join(", ", failed.Take(3).Select(j => j.Title)) + (failed.Count > 3 ? " …" : ""))),
                I18n.T("h95.a.failed.go"), () => { foreach (var j in failed) try { Jobs.Retry(j); } catch { } RebuildCurrent(); }, () => MainWindow.Current?.OpenDownloads()));

        foreach (var g in mine)
        {
            List<HealthIssue>? issues;
            lock (_health) issues = _health.TryGetValue(g.Def.Id, out var h) ? h.Issues : null;
            if (issues is not { Count: > 0 }) continue;
            var issue = issues[0];
            var id = g.Def.Id;
            var gs = g;
            if (issue.Kind == "loader")
            {
                if (g.LoaderInstalled || Actions.IsBusy(g, "loader")) continue;
                list.Add(new("health", Icons.Wrench, Ui.Res("Warn"), I18n.T("h95.a.loader", ("game", g.Def.ShortName), ("loader", g.Def.LoaderName)), I18n.T("h95.a.loader.text"),
                    I18n.T("h95.a.loader.go"), () => Actions.InstallLoader(gs), () => Go(() => new GamePage(id))));
            }
            else
                list.Add(new("health", Icons.Shield, Ui.Res("Bad"), I18n.T("h95.a.health", ("game", g.Def.ShortName), ("issue", issue.Title)), issue.Text.Split('\n')[0],
                    I18n.T("h95.a.health.go"), () => Go(() => new GamePage(id, "health")), () => Go(() => new GamePage(id, "health"))));
        }

        if (Setup.Updater.Available && Setup.Updater.Latest is { } latest)
            list.Add(new("app", Icons.Sparkles, Yellow, I18n.T("h95.a.app", ("version", latest.Version)), I18n.T("h95.a.app.text"),
                I18n.T("h95.a.app.go"), () => MainWindow.Current?.ShowUpdate(), () => MainWindow.Current?.ShowUpdate()));

        var updates = mine.SelectMany(g => (Features.ModUpdates.Found.TryGetValue(g.Def.Id, out var l) ? l : []).Select(u => (g, u))).ToList();
        if (updates.Count > 0)
        {
            var games = string.Join(", ", updates.GroupBy(x => x.g).Select(x => $"{x.Key.Def.ShortName} ({x.Count()})"));
            var first = updates[0].g.Def.Id;
            list.Add(new("mods", Icons.ArrowUp, Pink, I18n.T("h95.a.mods", ("n", updates.Count)), I18n.T("h95.a.mods.text", ("games", games)),
                I18n.T("h95.a.mods.go"), () => UpdateReview.Show(updates), () => Go(() => new GamePage(first, "installed"))));
        }

        if (Mc.State is { Status: Detect.Found })
        {
            var builds = Mc.Instances();
            var withUpdates = builds.Select(i => (Build: i, N: McContent.UpdateCount(i))).Where(x => x.N > 0).ToList();
            if (withUpdates.Count > 0)
            {
                var target = withUpdates[0].Build;
                void Open() { Mc.Active = target; Go(() => new MinecraftPage("mods")); }
                list.Add(new("mc", Icons.Cube, Ui.Res("Good"), I18n.T("h95.a.mc", ("n", withUpdates.Sum(x => x.N))),
                    I18n.T("h95.a.mc.text", ("builds", string.Join(", ", withUpdates.Select(x => $"{x.Build.Name} ({x.N})")))), I18n.T("h95.a.mc.go"), Open, Open));
            }
            foreach (var b in builds)
            {
                if (McModrinth.PackUpdate(b) is not { } v) continue;
                var build = b;
                void Open() { Mc.Active = build; Go(() => new MinecraftPage("builds")); }
                list.Add(new("pack", Icons.Layers, Ui.Res("Good"), I18n.T("h95.a.pack", ("build", b.Name)), I18n.T("h95.a.pack.text", ("version", v.Number)), I18n.T("h95.a.mc.go"), Open, Open));
            }
        }

        foreach (var r in Social.Friends.View().Incoming.Take(2))
        {
            var uid = r.Uid;
            list.Add(new("request", Icons.Users, Ui.Res("Brand2"), I18n.T("h95.a.request", ("name", r.Name)), I18n.T("h95.a.request.text"),
                I18n.T("h95.a.request.go"), () => _ = AcceptFriend(uid), () => Go(() => new FriendsPage())));
        }
        return list;
    }

    static async Task AcceptFriend(string uid)
    {
        try { await Social.Friends.Accept(uid); }
        catch (Exception e) { MainWindow.Current?.Toast(Social.Friends.Explain(e), bad: true); }
        RebuildCurrent();
    }

    static Control AttentionCard(List<Notice> notices)
    {
        if (notices.Count == 0)
        {
            var again = Ui.Button(I18n.T("h95.ok.check"), () => _ = CheckNow(), "", Icons.Refresh);
            again.IsEnabled = !_checking;
            return Card(CardHead(I18n.T("h95.attention")), Empty(Icons.Check, Ui.Res("Good"), I18n.T("h95.ok"), I18n.T("h95.ok.text"), again), 250);
        }
        else
        {
            var count = new Border
            {
                Background = Ui.Res("Bad"), CornerRadius = new CornerRadius(999), Padding = new Thickness(8, 1), MinWidth = 22,
                Child = new TextBlock { Text = notices.Count.ToString(), FontSize = 11.5, FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center },
            };
            var rows = Ui.Col(4);
            var shown = _attentionAll ? notices : notices.Take(4).ToList();
            for (var i = 0; i < shown.Count; i++) rows.Children.Add(NoticeRow(shown[i], primary: i == 0));
            if (notices.Count > shown.Count || _attentionAll && notices.Count > 4)
            {
                var more = Ui.Button(_attentionAll ? I18n.T("v93.feed.top.less") : I18n.T("h95.a.more", ("n", notices.Count - shown.Count)), () => { _attentionAll = !_attentionAll; RebuildCurrent(); }, "ghost", _attentionAll ? Icons.ChevronDown : Icons.More);
                more.FontSize = 12.5;
                more.Foreground = Ui.Res("Muted");
                more.Padding = new Thickness(8, 3);
                rows.Children.Add(more);
            }
            return Card(CardHead(I18n.T("h95.attention"), I18n.T("h95.attention.sub"), count), rows, 250);
        }
    }

    static Control NoticeRow(Notice n, bool primary)
    {
        var badge = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(12), Background = Soft(n.Tint), Child = Ui.Icon(n.Icon, 18, n.Tint), VerticalAlignment = VerticalAlignment.Center };
        var words = Ui.Col(1,
            new TextBlock { Text = n.Title, FontWeight = FontWeight.SemiBold, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = n.Text, FontSize = 12, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis });
        words.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
        grid.Children.Add(badge);
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);
        if (n.Action is not null && n.Run is not null)
        {
            var run = n.Run;
            var b = Ui.Button(n.Action, run, primary ? "primary" : "");
            b.Padding = new Thickness(14, 7);
            b.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(b, 2);
            grid.Children.Add(b);
        }
        var row = new Border { Classes = { "store-row" }, Padding = new Thickness(8, 7), CornerRadius = new CornerRadius(12), Child = grid };
        if (n.Open is not null) StoreKit.OnClick(row, n.Open);
        return row;
    }

    /// <summary>«Проверить ещё раз»: обновления модов, проблемы игр и новая версия ModLaunch.</summary>
    static async Task CheckNow()
    {
        if (_checking) return;
        _checking = true;
        RebuildCurrent();
        try
        {
            lock (_health) _health.Clear();
            foreach (var g in FeedGames().Where(g => g.ModCount > 0 && !g.Def.IsMinecraft))
                try { await Features.ModUpdates.Check(g); } catch { }
            try { await Setup.Updater.Check(); } catch { }
        }
        finally
        {
            _checking = false;
            AppState.Notify();
        }
    }

    // ---------------------------------------------------------------- проверка игр в фоне

    static readonly Dictionary<string, (DateTime At, List<HealthIssue> Issues)> _health = [];
    static bool _healthRunning;

    /// <summary>
    /// Проблемы игр для «Важного» — та же проверка, что перед запуском, но в фоне и не чаще раза в
    /// три минуты на игру: главная открывается сразу, а строка о проблеме появляется, когда готово.
    /// </summary>
    static void CheckHealthLater(List<GameState> mine)
    {
        if (_healthRunning) return;
        List<GameState> due;
        lock (_health)
            due = mine.Where(g => !g.Def.IsMinecraft && g.ModCount > 0 && (!_health.TryGetValue(g.Def.Id, out var h) || DateTime.UtcNow - h.At > TimeSpan.FromMinutes(3))).ToList();
        if (due.Count == 0) return;
        _healthRunning = true;
        _ = Task.Run(() =>
        {
            var changed = false;
            foreach (var g in due)
            {
                List<HealthIssue> issues;
                try { issues = Health.Check(g, quick: true).Where(i => i.Level == HealthLevel.Critical).ToList(); }
                catch { issues = []; }
                lock (_health)
                {
                    var before = _health.TryGetValue(g.Def.Id, out var h) ? string.Join("|", h.Issues.Select(i => i.Kind)) : null;
                    if (before != string.Join("|", issues.Select(i => i.Kind)) && (before is not null || issues.Count > 0)) changed = true;
                    _health[g.Def.Id] = (DateTime.UtcNow, issues);
                }
            }
            Dispatcher.UIThread.Post(() =>
            {
                _healthRunning = false;
                if (changed && MainWindow.Current?.CurrentPage is HomePage { IsFeed: false } home) home.Build();
            });
        });
    }

    // ---------------------------------------------------------------- «Продолжить»

    /// <summary>«Продолжить» — игра, в которую играли последней.</summary>
    static Control ContinueCard(List<GameState> mine)
    {
        var g = mine.FirstOrDefault();
        if (g is null)
        {
            var add = Ui.Button(I18n.T("add.title"), () => MainWindow.Current?.Navigate(() => new AddGamePage()), "primary", Icons.Plus);
            return Card(CardHead(I18n.T("v4.continue")), Empty(Icons.Gamepad, Cyan, I18n.T("v4.continue"), I18n.T("h95.continue.empty"), add), 250);
        }
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
        var meta = Ui.Row(6, StoreKit.Pill(state, dot));
        if (played.TotalMs >= 3_600_000) meta.Children.Add(StoreKit.Pill(I18n.T("v93.home.hours", ("n", played.TotalMs / 3_600_000))));
        var words = Ui.Col(10, eyebrow, title, meta, action);
        words.VerticalAlignment = VerticalAlignment.Bottom;
        words.HorizontalAlignment = HorizontalAlignment.Left;
        words.Margin = new Thickness(20, 14, 20, 18);
        var layers = new Panel { Children = { Ui.GameImage(g.Def, 1024, art: Images.Art.Hero), new Border { Background = Gx.ShadeLeft(0.85, 235) }, new Border { Background = Gx.ShadeUp(0.4, 200) }, words } };
        var t = Tile(layers, () => MainWindow.Current?.Navigate(() => new GamePage(id)), "hero");
        t.MinHeight = 250;
        t.ContextFlyout = GameCard.Menu(g);
        return t;
    }

    // ---------------------------------------------------------------- «Для вас»

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

    /// <summary>Из чего выбирать советы для игры: подборка ModLaunch и популярные (уже загружены для ленты).</summary>
    static IEnumerable<ModInfo> Candidates(GameState g)
    {
        foreach (var (fg, m) in _featured ?? []) if (fg.Def.Id == g.Def.Id) yield return m;
        foreach (var m in Views.Aside.Popular(g) ?? []) yield return m;
    }

    static Control ForYou(List<GameState> mine)
    {
        var recs = Recommend.All(mine.Where(g => g.Def.HasCatalog), Candidates, 8);
        var personal = recs.Count > 0;
        var items = personal
            ? recs.Select(s => (s.Game, s.Mod, s.Reason)).ToList()
            : BestMods(mine).Take(8).Select(x => (x.Game, x.Mod, Reason: "")).ToList();
        var head = CardHead(personal ? I18n.T("h95.forYou") : I18n.T("h95.forYou.popular"), personal ? I18n.T("h95.forYou.sub") : items.Count > 0 ? I18n.T("h95.forYou.popular.sub") : null,
            LinkButton(I18n.T("h95.forYou.more"), () => MainWindow.Current?.Navigate(() => new HomePage(feed: true))));
        Control body;
        if (items.Count == 0)
        {
            var loading = mine.Count > 0 && (_featured is null || mine.Any(g => g.Def.HasCatalog && Views.Aside.Popular(g) is null));
            if (loading)
            {
                var row = new UniformGrid { Columns = 4, Height = 214 };
                for (var i = 0; i < 4; i++) { var ph = StoreKit.Placeholder(double.NaN, double.NaN); ph.Margin = new Thickness(i == 0 ? 0 : MainGap / 2, 0, i == 3 ? 0 : MainGap / 2, 0); row.Children.Add(ph); }
                body = Ui.Col(8, row, Ui.Text(I18n.T("h95.forYou.loading"), "small muted"));
            }
            else body = Empty(Icons.Sparkles, Yellow, I18n.T("h95.forYou"), I18n.T("h95.forYou.empty"),
                Ui.Button(I18n.T("add.title"), () => MainWindow.Current?.Navigate(() => new AddGamePage()), "primary", Icons.Plus));
        }
        else
        {
            // Сколько карточек в ряд — по ширине: на большом мониторе больше, но всегда одним рядом.
            var cards = items.Select(x => RecCard(x.Game, x.Mod, x.Reason)).ToList();
            var row = new UniformGrid { Columns = Math.Min(4, cards.Count) };
            foreach (var c in cards) row.Children.Add(c);
            void Fit(double width)
            {
                var cols = Math.Clamp((int)Math.Round(width / 300), 2, 6);
                cols = Math.Min(cols, cards.Count);
                row.Columns = cols;
                for (var i = 0; i < cards.Count; i++)
                {
                    cards[i].IsVisible = i < cols;
                    cards[i].Margin = new Thickness(i % cols == 0 ? 0 : MainGap / 2, 0, i % cols == cols - 1 ? 0 : MainGap / 2, 0);
                }
            }
            Fit(1200);
            row.SizeChanged += (_, e) => { if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 1) Fit(e.NewSize.Width); };
            body = row;
        }
        return new Border { Classes = { "card", "home-card" }, Padding = new Thickness(16, 14, 16, 16), Child = Ui.Col(0, head, body) };
    }

    /// <summary>Совет: обложка мода, название, почему он здесь («Работает с вашим X») и установка.</summary>
    static Control RecCard(GameState g, ModInfo m, string reason)
    {
        var cover = new Panel { Height = 118, ClipToBounds = true, Children = { ModCover.Create(g, m, 640, iconShare: 0.58) } };
        var game = StoreKit.Pill(g.Def.ShortName);
        game.HorizontalAlignment = HorizontalAlignment.Left;
        game.VerticalAlignment = VerticalAlignment.Top;
        game.Margin = new Thickness(8);
        cover.Children.Add(game);

        var name = new TextBlock { Text = m.Name, FontWeight = FontWeight.SemiBold, FontSize = 14.5, TextTrimming = TextTrimming.CharacterEllipsis };
        Control why = reason != ""
            ? new TextBlock { Text = "✦ " + reason, FontSize = 12, Foreground = Ui.Res("Brand2"), TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis }
            : new TextBlock { Text = (m.Downloads > 0 ? "↓ " + I18n.Compact(m.Downloads) : "") + (m.Author != "" ? (m.Downloads > 0 ? " · " : "") + m.Author : ""), FontSize = 12, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis };
        var action = InstallButton(g, m, false);
        action.VerticalAlignment = VerticalAlignment.Top;
        var foot = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, Margin = new Thickness(12, 10, 10, 12) };
        var words = Ui.Col(3, name, why);
        foot.Children.Add(words);
        Grid.SetColumn(action, 1);
        foot.Children.Add(action);
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        grid.Children.Add(cover);
        Grid.SetRow(foot, 1);
        grid.Children.Add(foot);
        var id = g.Def.Id;
        var t = Tile(grid, () => MainWindow.Current?.Navigate(() => new ModPage(id, m)));
        t.MinHeight = 196;
        if (reason != "") ToolTip.SetTip(t, reason);
        Ctx.Attach(t, () => ModRow.Menu(g.Def, m, Actions.IsInstalled(g, m.Id), Actions.IsBusy(g, m.Id), () => _ = Actions.Install(g, m), () => MainWindow.Current?.Navigate(() => new ModPage(id, m))));
        return t;
    }

    // ---------------------------------------------------------------- друзья, загрузки, новое

    static Control FriendsCard()
    {
        var v = Social.Friends.View();
        void Open() => MainWindow.Current?.Navigate(() => new FriendsPage());
        Control body;
        if (!v.SignedIn)
            body = Empty(Icons.Users, Ui.Res("Brand2"), I18n.T("h95.friends.signin"), I18n.T("h95.friends.signinText"),
                Ui.Button(I18n.T("aside.login"), () => MainWindow.Current?.Navigate(() => new SettingsPage("accounts")), "primary", Icons.User));
        else if (v.Friends.Count == 0)
            body = Empty(Icons.Users, Ui.Res("Brand2"), I18n.T("h95.friends.empty"), I18n.T("h95.friends.emptyText"), Ui.Button(I18n.T("friends.add"), Open, "primary", Icons.Plus));
        else
        {
            var online = v.Friends.Where(f => f.State != "offline").OrderBy(f => f.State == "playing" ? 0 : 1).ToList();
            if (online.Count == 0) body = Empty(Icons.Users, Ui.Res("Faint"), I18n.T("h95.friends.none"), I18n.T("h95.friends.noneText"));
            else
            {
                var rows = Ui.Col(2);
                foreach (var f in online.Take(4))
                    rows.Children.Add(FriendsDock.Row(f.Name, f.State == "playing" && f.GameName != "" ? f.GameName : I18n.T("aside.online"), f.State == "playing" ? Ui.Res("Good") : Ui.Res("Brand2"), null));
                body = rows;
            }
            var offline = v.Friends.Count - online.Count;
            if (offline > 0 && body is StackPanel sp) sp.Children.Add(new TextBlock { Text = I18n.T("h95.friends.offline", ("n", offline)), FontSize = 12, Foreground = Ui.Res("Faint"), Margin = new Thickness(8, 6, 0, 0) });
        }
        var head = CardHead(I18n.T("h95.friends"), null, LinkButton(I18n.T("home.all"), Open));
        return Card(head, body, 236);
    }

    /// <summary>«Что нового»: свежие пункты этой версии и новый лот Creator Hub.</summary>
    static Control NewsCard()
    {
        var rows = Ui.Col(4);
        Control Row(string icon, IBrush tint, string title, string sub, Action? open)
        {
            var badge = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(10), Background = Soft(tint), Child = Ui.Icon(icon, 15, tint), VerticalAlignment = VerticalAlignment.Center };
            var words = Ui.Col(0,
                new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = sub, FontSize = 11.5, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis });
            words.VerticalAlignment = VerticalAlignment.Center;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
            grid.Children.Add(badge);
            Grid.SetColumn(words, 1);
            grid.Children.Add(words);
            var row = new Border { Classes = { "store-row" }, Padding = new Thickness(6, 5), CornerRadius = new CornerRadius(10), Child = grid };
            if (open is not null) StoreKit.OnClick(row, open);
            return row;
        }
        IBrush[] tints = [Ui.Res("Brand2"), Cyan, Pink];
        var k = 0;
        foreach (var (icon, key, open) in WhatsNew.Latest(3))
        {
            var o = open;
            rows.Children.Add(Row(icon, tints[k++ % tints.Length], I18n.T(key), I18n.T(key + ".text"), o is null ? WhatsNew.Show : () => MainWindow.Current?.Navigate(o)));
        }
        var listing = (MarketData.Listings ?? []).Where(l => l.Active && !l.Mine).OrderByDescending(l => l.Created ?? DateTime.MinValue).FirstOrDefault();
        if (listing is not null)
        {
            rows.Children.Add(Row(Icons.Bag, Yellow, I18n.T("h95.new.hub") + ": " + listing.Title, listing.Author + " · " + (listing.Free ? I18n.T("mk.free") : Market.Money(listing.Price)),
                () => MarketTiles.Open(listing)));
        }
        var head = CardHead(I18n.T("h95.new"), I18n.T("h95.new.version", ("version", Http.Version)), LinkButton(I18n.T("h95.new.all"), WhatsNew.Show));
        return Card(head, rows, 236);
    }

    // ---------------------------------------------------------------- совет дня и лента

    static readonly string[] Tips = ["v93.tip.1", "v93.tip.2", "v93.tip.3", "v93.tip.4", "v93.tip.5", "v93.tip.6", "v93.tip.7", "v93.tip.8"];

    /// <summary>«Совет дня» — каждый день новый, «Ещё» листает дальше.</summary>
    static Control TipCard()
    {
        if (_tip < 0) _tip = DateTime.Today.DayOfYear % Tips.Length;
        var text = new TextBlock { Text = I18n.T(Tips[_tip]), TextWrapping = TextWrapping.Wrap, FontSize = 13, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Ui.Res("Text") };
        var more = Ui.Button(I18n.T("v93.home.tipMore"), () => { _tip = (_tip + 1) % Tips.Length; text.Text = I18n.T(Tips[_tip]); Animate.From(text, "translateX(10px)", 260, 0, new CubicEaseOut()); }, "ghost", Icons.Shuffle);
        more.Padding = new Thickness(8, 3);
        more.FontSize = 12;
        var head = new DockPanel();
        DockPanel.SetDock(more, Dock.Right);
        head.Children.Add(more);
        head.Children.Add(Ui.Row(8, Ui.Icon(Icons.Sparkles, 15, Yellow), Gx.Eyebrow(I18n.T("v93.home.tip"), Ui.Res("Text"), 11)));
        var body = Ui.Col(6, head, text);
        body.VerticalAlignment = VerticalAlignment.Center;
        return new Border { Classes = { "card", "home-card" }, Padding = new Thickness(16, 10), Child = body, MinHeight = 108 };
    }

    /// <summary>Вход в «Ленту»: яркая полоса со стопкой обложек модов.</summary>
    static Control FeedTeaser(List<(GameState Game, ModInfo Mod, string Tag)> pool)
    {
        var stack = new Canvas { Width = 150, Height = 54, VerticalAlignment = VerticalAlignment.Center };
        var thumbs = pool.Skip(6).Take(5).ToList();
        if (thumbs.Count < 3) thumbs = pool.Take(5).ToList();
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
            Ui.Row(8, Ui.Icon(Icons.Flame, 18, Brushes.White), new TextBlock { Text = I18n.T("h95.feed"), FontFamily = Gx.Display, FontSize = 20, FontWeight = FontWeight.Bold, Foreground = Brushes.White }),
            new TextBlock { Text = I18n.T("h95.feed.text"), FontSize = 12.5, Foreground = Ui.Hex("#EFEAFF"), TextTrimming = TextTrimming.CharacterEllipsis });
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
        var tile = Tile(new Panel { Children = { background, Gx.Scanlines(), grid } }, () => MainWindow.Current?.Navigate(() => new HomePage(feed: true)), "hero");
        tile.MinHeight = 108;
        return tile;
    }
}

/// <summary>
/// Карточка «Загрузки» на главной: что качается сейчас (с полосой), а если ничего — что
/// поставили недавно. Перерисовывает только себя, не чаще 3 раз в секунду, — главная целиком
/// от каждой загрузки не перестраивается (и нажатия по ней не теряются).
/// </summary>
sealed class DownloadsTile : Border
{
    readonly StackPanel _list = new() { Spacing = 4 };
    readonly TextBlock _sub = new() { FontSize = 12, Foreground = Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Center };
    bool _pending;

    public DownloadsTile()
    {
        Classes.Add("card");
        Classes.Add("home-card");
        Padding = new Thickness(16, 14);
        MinHeight = 236;
        ClipToBounds = true;
        var open = Ui.Button(I18n.T("h95.dl.open") + "  ›", () => MainWindow.Current?.OpenDownloads(), "ghost");
        open.Padding = new Thickness(8, 2);
        open.FontSize = 12.5;
        open.Foreground = Ui.Res("Muted");
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(open, Dock.Right);
        head.Children.Add(open);
        head.Children.Add(Ui.Row(10, Gx.Eyebrow(I18n.T("h95.dl"), Ui.Res("Text"), 12), _sub));
        Child = Ui.Col(0, head, _list);
        Render();
        AttachedToVisualTree += (_, _) => Jobs.Changed += OnChanged;
        DetachedFromVisualTree += (_, _) => Jobs.Changed -= OnChanged;
    }

    void OnChanged(Job _)
    {
        if (_pending) return;
        _pending = true;
        DispatcherTimer.RunOnce(() => { _pending = false; Render(); }, TimeSpan.FromMilliseconds(320));
    }

    void Render()
    {
        _list.Children.Clear();
        var active = Jobs.All.Where(j => j.Active).ToList();
        _sub.Text = active.Count > 0 ? I18n.T("h95.dl.active", ("n", active.Count)) : "";
        if (active.Count > 0)
        {
            foreach (var j in active.Take(3))
            {
                var ratio = j.Ratio;
                var bar = new ProgressBar { Minimum = 0, Maximum = 1, Value = Math.Max(0, ratio), IsIndeterminate = ratio < 0 && j.Status == JobStatus.Running, Height = 4, MinHeight = 4, CornerRadius = new CornerRadius(2) };
                var status = j.Status == JobStatus.Queued ? I18n.T("h95.dl.queued") : ratio >= 0 ? $"{j.Step} · {ratio * 100:0}%" : j.Step;
                _list.Children.Add(new Border
                {
                    Padding = new Thickness(6, 5),
                    Child = Ui.Col(4,
                        new TextBlock { Text = j.Title, FontWeight = FontWeight.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis },
                        new TextBlock { Text = j.GameName + " · " + status, FontSize = 11.5, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis },
                        bar),
                });
            }
        }
        // Под тем, что качается, — недавно поставленное (пока есть место).
        var recent = History.All().Take(Math.Max(0, 3 - Math.Min(3, active.Count))).ToList();
        if (active.Count > 0 && recent.Count == 0) return;
        if (recent.Count == 0)
        {
            _list.Children.Add(new TextBlock { Text = I18n.T("h95.dl.idle"), FontSize = 13, Foreground = Ui.Res("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 4) });
            return;
        }
        _list.Children.Add(new TextBlock { Text = I18n.T("h95.dl.recent"), FontSize = 12, Foreground = Ui.Res("Faint"), Margin = new Thickness(6, active.Count > 0 ? 8 : 0, 0, 2) });
        foreach (var h in recent)
        {
            var mark = new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(13), VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(h.Ok ? Color.FromArgb(40, 61, 214, 140) : Color.FromArgb(40, 240, 80, 80)),
                Child = Ui.Icon(h.Ok ? Icons.Check : Icons.Alert, 13, h.Ok ? Ui.Res("Good") : Ui.Res("Bad")),
            };
            var words = Ui.Col(0,
                new TextBlock { Text = h.Title, FontWeight = FontWeight.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = h.Game + " · " + Ui.Ago(h.At.ToLocalTime()).Trim(), FontSize = 11.5, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis });
            words.VerticalAlignment = VerticalAlignment.Center;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
            grid.Children.Add(mark);
            Grid.SetColumn(words, 1);
            grid.Children.Add(words);
            _list.Children.Add(new Border { Padding = new Thickness(6, 4), Child = grid });
        }
    }
}
