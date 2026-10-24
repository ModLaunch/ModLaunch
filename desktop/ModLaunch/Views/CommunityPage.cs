using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Games;
using ModLaunch.Minecraft;
using ModLaunch.Social;

namespace ModLaunch.Views;

/// <summary>
/// 11.0: «Сообщество» — один список друзей на все игры (кто в сети, во что играет, их серверы,
/// добавить по коду), доска серверов (свои, друзей и открытые всем) и свой сервер Minecraft на этом ПК.
/// Вкладки: friends · servers · host.
/// </summary>
public sealed partial class CommunityPage : Page
{
    static readonly FontFamily Mono = new("Cascadia Mono, Consolas, JetBrains Mono, DejaVu Sans Mono, monospace");

    string _tab;
    /// <summary>20.0: вкладка страницы — она горит в ряду вкладок раздела «Сообщество».</summary>
    public string Tab => _tab;
    string _shownTab = "";
    string? _code, _friendsError, _boardError;
    bool _loading;
    /// <summary>Набранный код друга и где был курсор — вкладка перестраивается, когда приходят данные.</summary>
    string _addCode = "", _friendsKey = "";
    bool _addFocused;
    readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMinutes(2) };

    public CommunityPage(string tab = "friends")
    {
        _tab = tab is "servers" or "host" ? tab : "friends";
        _refresh.Tick += (_, _) => { if (SignedIn && !Program.Demo) _ = LoadBoard(); };
        AttachedToVisualTree += (_, _) =>
        {
            Friends.Changed += OnFriends;
            Servers.Changed += OnServers;
            MarketData.Changed += OnWorkshop;
            _ = MarketData.Load();
            McHost.Changed += OnHost;
            _refresh.Start();
            if (SignedIn && !Program.Demo) _ = Load();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            Friends.Changed -= OnFriends;
            Servers.Changed -= OnServers;
            MarketData.Changed -= OnWorkshop;
            McHost.Changed -= OnHost;
            _refresh.Stop();
            DetachConsole();
        };
    }

    public override string Title => I18n.T("v11.cm.title");
    /// <summary>Другая вкладка сообщества — тот же экран: без въезда всей страницы.</summary>
    public override bool SameScreenAs(Page? previous) => previous is CommunityPage;

    /// <summary>Вошёл ли человек (в снимках — по примеру друзей).</summary>
    static bool SignedIn => Friends.View().SignedIn;

    void OnFriends() => Dispatcher.UIThread.Post(() => { if (_tab == "friends") Build(); });
    void OnWorkshop() => Dispatcher.UIThread.Post(() => { if (_tab == "friends") Build(); });

    /// <summary>Полка «Мастерская сообщества»: самые любимые моды ModLaunch Hub.</summary>
    static Control? Workshop()
    {
        var mods = MarketData.Mods.OrderByDescending(h => h.Likes).ThenByDescending(h => h.Downloads).Take(10).ToList();
        if (mods.Count == 0) return null;
        return Ui.Col(14,
            StoreKit.Header(I18n.T("v20.cm.hub"), () => MainWindow.Current?.Navigate(() => new MarketPage("workshop"))),
            StoreKit.Shelf(mods.Select(h => MarketTiles.WorkshopCard(h, 252))));
    }
    // Друзья: с доски на вкладке только их серверы и число серверов в сети — остальное (отметки «в сети» и т. п.) её не трогает.
    void OnServers() => Dispatcher.UIThread.Post(() => { if (_tab == "servers" || (_tab == "friends" && FriendsBoardKey() != _friendsKey)) Build(); });

    static string FriendsBoardKey()
    {
        var board = Servers.Cached;
        return $"{board.Count(Servers.IsOnline)}|{string.Join(",", board.Where(s => s.Owner != Servers.Me).Select(s => $"{s.Id}:{s.Owner}:{s.Name}:{s.Game}:{s.Players}:{s.Max}:{Servers.IsOnline(s)}"))}";
    }

    async Task Load()
    {
        _loading = true;
        try
        {
            _code = await Friends.MyCode();
            await Friends.Refresh(force: true);
            _friendsError = null;
        }
        catch (Exception e) { _friendsError = Friends.Explain(e); }
        await LoadBoard();
        _loading = false;
        Build();
    }

    async Task LoadBoard()
    {
        try { await Servers.List(force: true); _boardError = null; }
        catch (Exception e) when (e is ServiceError or HttpRequestException) { _boardError = Servers.Explain(e); Build(); }
    }

    public override void Build()
    {
        var col = StoreKit.Fit(new StackPanel { Spacing = 18, Margin = new Thickness(30, 22, 30, 34) }, 1400);
        col.Children.Add(Head());
        // 20.0: вкладки «Друзья · Серверы · Свой сервер» — в ряду вкладок раздела «Сообщество» под шапкой окна.
        if (_tab != "host") DetachConsole();
        col.Children.Add(_tab switch { "servers" => Board(), "host" => Host(), _ => FriendsTab() });
        // 20.0: под друзьями — мастерская сообщества (моды ModLaunch Hub), её видно и без входа.
        if (_tab == "friends" && Workshop() is { } workshop) col.Children.Add(workshop);

        // Тот же экран перестроился (пришли новые данные) — прокрутка остаётся на месте.
        var offset = _shownTab == _tab && Content is ScrollViewer old ? old.Offset : default;
        var scroll = new ScrollViewer { Content = col, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Content = scroll;
        if (offset != default) Dispatcher.UIThread.Post(() => scroll.Offset = offset, DispatcherPriority.Loaded);
        _shownTab = _tab;
    }

    Control Head()
    {
        var words = Ui.Col(6,
            new TextBlock { Text = I18n.T("v11.cm.title"), FontFamily = Look.Display, FontSize = 30, FontWeight = FontWeight.Bold, LetterSpacing = -0.8 },
            Ui.Text(I18n.T("v11.cm.lead"), "muted", wrap: true));
        var head = new DockPanel();
        if (SignedIn)
        {
            var view = Friends.View();
            var board = Servers.Cached;
            var stats = Ui.Row(28,
                Gx.Stat(view.Friends.Count(f => f.State != "offline").ToString(), I18n.T("v11.cm.stat.online"), size: 22),
                Gx.Stat(view.Friends.Count(f => f.State == "playing").ToString(), I18n.T("v11.cm.stat.playing"), size: 22),
                Gx.Stat(board.Count(Servers.IsOnline).ToString(), I18n.T("v11.cm.stat.servers"), size: 22));
            stats.VerticalAlignment = VerticalAlignment.Bottom;
            DockPanel.SetDock(stats, Dock.Right);
            head.Children.Add(stats);
        }
        head.Children.Add(words);
        return head;
    }

    /// <summary>Спокойное приглашение войти: друзья и доска серверов — с аккаунтом ModLaunch.</summary>
    static Control SignInCard()
    {
        var icon = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(14), Background = Ui.Res("BrandSoft"), Child = Ui.Icon(Icons.Users, 24, Ui.Res("Brand2")) };
        var words = Ui.Col(6, Ui.Text(I18n.T("v11.cm.signin.title"), "h3"), Ui.Text(I18n.T("v11.cm.signin.text"), "muted", wrap: true),
            Ui.Row(10, Ui.Button(I18n.T("v11.cm.signin.go"), () => MainWindow.Current?.Navigate(() => new AccountPage()), "primary", Icons.User)));
        words.Margin = new Thickness(0, 0, 0, 2);
        var row = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        icon.Margin = new Thickness(0, 0, 16, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        row.Children.Add(icon);
        row.Children.Add(words);
        return Ui.Card(row, 22);
    }

    static Control Notice(string text, string icon = Icons.Info, IBrush? color = null) => new Border
    {
        Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10),
        Child = Ui.Row(10, Ui.Icon(icon, 15, color ?? Ui.Res("Muted")), Ui.Text(text, "small muted", wrap: true)),
    };

    /// <summary>Поле формы: подпись сверху, под полем — пояснение.</summary>
    static Control Field(string label, Control input, string? hint = null)
    {
        var col = Ui.Col(6, Ui.Text(label, "small muted"), input);
        if (hint is not null) col.Children.Add(Ui.Text(hint, "small faint", wrap: true));
        return col;
    }

    static async void Copy(string text, string toast)
    {
        if (TopLevel.GetTopLevel(MainWindow.Current)?.Clipboard is not { } clip) return;
        await clip.SetTextAsync(text);
        MainWindow.Current?.Toast(toast);
    }

    /// <summary>«5 мин назад», «3 ч назад», дальше — как везде в программе.</summary>
    static string Ago(DateTime? when)
    {
        if (when is not DateTime w) return "";
        var span = DateTime.UtcNow + Firebase.Skew - w;
        if (span.TotalMinutes < 1) return I18n.T("v11.cm.ago.now");
        if (span.TotalHours < 1) return I18n.T("v11.cm.ago.min", ("n", (int)span.TotalMinutes));
        if (span.TotalDays < 1) return I18n.T("v11.cm.ago.hours", ("n", (int)span.TotalHours));
        return Ui.Ago(w);
    }

    static Control GamePic(string gameId, double size)
    {
        var def = GameCatalog.ById(gameId);
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 4.5), ClipToBounds = true, Background = Ui.Res("Surface2"),
            Child = def is null ? Ui.Icon(Icons.Server, size * 0.4, Ui.Res("Muted")) : Ui.GameImage(def, (int)(size * 3), art: Images.Art.Cover),
        };
    }

    // ---------------------------------------------------------------- «Друзья»

    Control FriendsTab()
    {
        var view = Friends.View();
        if (!view.Configured) return Ui.Card(Ui.Text(I18n.T("v11.cm.f.off"), "muted", wrap: true), 22);
        if (!view.SignedIn) return SignInCard();
        var col = Ui.Col(16);
        if (_friendsError is not null) col.Children.Add(Notice(_friendsError, Icons.Alert, Ui.Res("Warn")));

        // Свой код и добавление по коду друга — рядом.
        var code = _code ?? view.Code;
        var codeText = new SelectableTextBlock { Text = code ?? "…", FontSize = 24, FontWeight = FontWeight.Bold, FontFamily = Mono, Foreground = Ui.Res("Brand2"), VerticalAlignment = VerticalAlignment.Center };
        var copy = Ui.Button(I18n.T("v11.cm.f.copy"), () => { if (code is not null) Copy(code, I18n.T("v11.cm.f.copied")); }, "", Icons.Copy);
        copy.IsEnabled = code is not null;
        var modes = new[] { "all", "online", "hidden" };
        var mode = new ComboBox { MinWidth = 240 };
        foreach (var m in modes) mode.Items.Add(I18n.T("v11.cm.f.mode." + m));
        mode.SelectedIndex = Array.IndexOf(modes, view.Mode);
        mode.SelectionChanged += (_, _) => { if (mode.SelectedIndex >= 0) Friends.Mode = modes[mode.SelectedIndex]; };
        var mine = Ui.Card(Ui.Col(12,
            Ui.Text(I18n.T("v11.cm.f.myCode"), "h3"),
            Ui.Row(14, codeText, copy),
            Field(I18n.T("v11.cm.f.mode"), mode)), 20);

        var box = new TextBox { Text = _addCode, Watermark = "XXXX-XXXX", FontFamily = Mono, MaxLength = 12, MinWidth = 200 };
        box.TextChanged += (_, _) => _addCode = box.Text ?? "";
        box.GotFocus += (_, _) => _addFocused = true;
        box.LostFocus += (_, _) => _addFocused = false;
        if (_addFocused)
            Dispatcher.UIThread.Post(() => { box.Focus(); box.CaretIndex = box.Text?.Length ?? 0; }, DispatcherPriority.Loaded);
        async void Add()
        {
            if ((box.Text ?? "").Trim() == "") return;
            try
            {
                var (status, name) = await Friends.Add(box.Text ?? "");
                MainWindow.Current?.Toast(I18n.T("v11.cm.f.added." + status, ("name", name)));
                box.Text = "";
            }
            catch (Exception e) { MainWindow.Current?.Toast(Friends.Explain(e), bad: true); }
            Build();
        }
        box.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Add(); };
        var add = Ui.Card(Ui.Col(12,
            Ui.Text(I18n.T("v11.cm.f.add"), "h3"),
            Ui.Row(10, box, Ui.Button(I18n.T("v11.cm.f.add.go"), Add, "primary", Icons.Plus)),
            Ui.Text(I18n.T("v11.cm.f.add.hint"), "small muted", wrap: true)), 20);
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 16 };
        top.Children.Add(mine);
        Grid.SetColumn(add, 1);
        top.Children.Add(add);
        col.Children.Add(top);

        if (view.Incoming.Count > 0 || view.Outgoing.Count > 0)
        {
            var list = Ui.Col(6, Ui.Text(I18n.T("v11.cm.f.requests"), "h3"));
            foreach (var r in view.Incoming)
            {
                var uid = r.Uid;
                list.Children.Add(PersonRow(r.Name, I18n.T("v11.cm.f.wantsYou") + (r.At is null ? "" : " · " + Ago(r.At)), Ui.Res("Warn"), null,
                    Ui.Button(I18n.T("v11.cm.f.accept"), () => Do(() => Friends.Accept(uid)), "primary", Icons.Check),
                    Ui.Button(I18n.T("v11.cm.f.decline"), () => Do(() => Friends.Remove(uid)), "ghost")));
            }
            foreach (var r in view.Outgoing)
            {
                var uid = r.Uid;
                list.Children.Add(PersonRow(r.Name, I18n.T("v11.cm.f.waiting"), Ui.Res("Faint"), null,
                    Ui.Button(I18n.T("v11.cm.f.cancel"), () => Do(() => Friends.Remove(uid)), "ghost")));
            }
            col.Children.Add(Ui.Card(list, 18));
        }

        var online = view.Friends.Count(f => f.State != "offline");
        var friendsCard = Ui.Col(6, Ui.Row(10, Ui.Text(I18n.T("v11.cm.f.list"), "h3"),
            Ui.Text(I18n.T("v11.cm.f.onlineOf", ("n", online), ("total", view.Friends.Count)), "small muted")));
        ((StackPanel)friendsCard.Children[0]).Children[1].VerticalAlignment = VerticalAlignment.Center;
        if (view.Friends.Count == 0) friendsCard.Children.Add(Ui.Text(_loading ? I18n.T("common.loading") : I18n.T("v11.cm.f.empty"), "muted", wrap: true));
        var board = Servers.Cached;
        _friendsKey = FriendsBoardKey();
        void Group(string key, IEnumerable<Friend> friends)
        {
            var list = friends.ToList();
            if (list.Count == 0) return;
            friendsCard.Children.Add(new TextBlock
            {
                Text = $"{I18n.T(key)} — {list.Count}", FontSize = 11.5, FontWeight = FontWeight.SemiBold, Foreground = Ui.Res("Faint"), Margin = new Thickness(2, 10, 0, 2),
            });
            foreach (var f in list) friendsCard.Children.Add(FriendRow(f, board.Where(s => s.Owner == f.Uid).ToList()));
        }
        Group("v11.cm.f.g.playing", view.Friends.Where(f => f.State == "playing"));
        Group("v11.cm.f.g.online", view.Friends.Where(f => f.State == "online"));
        Group("v11.cm.f.g.offline", view.Friends.Where(f => f.State is not ("playing" or "online")));
        col.Children.Add(Ui.Card(friendsCard, 18));
        var refresh = Ui.Button(I18n.T("v11.cm.refresh"), () => { if (!Program.Demo) _ = Load(); }, "ghost", Icons.Refresh);
        refresh.IsEnabled = !_loading;
        col.Children.Add(refresh);
        return col;
    }

    /// <summary>Строка человека: аватар с точкой состояния, имя, строка под ним, справа — кнопки.</summary>
    static Control PersonRow(string name, string line, IBrush dotColor, Control? below, params Control[] actions)
    {
        var dot = new Border
        {
            Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = dotColor, BorderBrush = Ui.Res("Surface"), BorderThickness = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
        };
        var face = new Panel { Width = 42, Height = 42, VerticalAlignment = VerticalAlignment.Top, Children = { Ui.Thumb(null, name, 42, 21, person: true), dot } };
        var info = Ui.Col(3, Ui.Text(name, "h3"), Ui.Text(line, "small muted"));
        if (below is not null) info.Children.Add(below);
        info.VerticalAlignment = VerticalAlignment.Center;
        var right = Ui.Row(6, actions);
        right.VerticalAlignment = VerticalAlignment.Center;
        var row = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        row.Children.Add(right);
        row.Children.Add(Ui.Row(12, face, info));
        return new Border { Classes = { "friend-row" }, Padding = new Thickness(10, 8), CornerRadius = new CornerRadius(12), Child = row };
    }

    Control FriendRow(Friend f, List<GameServer> servers)
    {
        var line = f.State switch
        {
            "playing" => I18n.T("v11.cm.f.playing", ("game", f.GameName)) + (f.Since is DateTime s ? " · " + PlayTime.Format((long)(DateTime.UtcNow - s).TotalMilliseconds) : ""),
            "online" => I18n.T("v11.cm.f.online"),
            _ => f.Seen is DateTime seen ? I18n.T("v11.cm.f.seen", ("ago", Ago(seen))) : I18n.T("v11.cm.f.offline"),
        };
        var color = f.State switch { "playing" => Ui.Res("Good"), "online" => Ui.Res("Brand2"), _ => Ui.Res("Faint") };
        Control? below = null;
        if (servers.Count > 0)
        {
            var pills = new WrapPanel();
            foreach (var s in servers.Take(4))
            {
                var server = s;
                var on = Servers.IsOnline(s);
                var pill = new Button
                {
                    Classes = { "chip" }, Margin = new Thickness(0, 4, 6, 0), Padding = new Thickness(8, 3),
                    Content = Ui.Row(6, Ui.Dot(on ? Ui.Res("Good") : Ui.Res("Faint"), 7), new TextBlock { Text = s.Name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = I18n.T("v11.cm.s.players", ("n", s.Players), ("max", s.Max)), FontSize = 12, Foreground = Ui.Res("Muted"), VerticalAlignment = VerticalAlignment.Center }),
                };
                ToolTip.SetTip(pill, I18n.T("v11.cm.f.serverTip", ("game", GameCatalog.ById(s.Game)?.Name ?? s.Game)));
                pill.Click += (_, _) => Join(server);
                pills.Children.Add(pill);
            }
            below = pills;
        }
        var actions = new List<Control>();
        if (f.State == "playing" && GameCatalog.ById(f.Game) is { } game)
        {
            var pic = GamePic(game.Id, 36);
            ToolTip.SetTip(pic, game.Name);
            actions.Add(pic);
            actions.Add(Ui.Button(I18n.T("v11.cm.f.toGame"), () => MainWindow.Current?.Navigate(() => new GamePage(game.Id)), "ghost", Icons.Play));
        }
        var uid = f.Uid;
        var name = f.Name;
        actions.Add(Ui.Button("", () => ConfirmRemove(uid, name), "icon ghost", Icons.Trash, I18n.T("v11.cm.f.remove")));
        return PersonRow(f.Name, line, color, below, actions.ToArray());
    }

    async void Do(Func<Task> action)
    {
        try { await action(); } catch (Exception e) { MainWindow.Current?.Toast(Friends.Explain(e), bad: true); }
        Build();
    }

    void ConfirmRemove(string uid, string name)
    {
        var w = MainWindow.Current!;
        w.Dialog(I18n.T("v11.cm.f.remove.title", ("name", name)), Ui.Text(I18n.T("v11.cm.f.remove.text"), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("v11.cm.f.remove"), () => { w.CloseDialog(); Do(() => Friends.Remove(uid)); }, "primary", Icons.Trash));
    }
}
