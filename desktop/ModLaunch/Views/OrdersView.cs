using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Games;
using ModLaunch.Social;

namespace ModLaunch.Views;

/// <summary>
/// Живые заказы: человек описывает мод и бюджет, креаторы делают ставки
/// (цена и срок) — как аукцион на понижение, всё обновляется на глазах.
/// Выбранная цена замораживается и уходит исполнителю после сдачи.
/// </summary>
public sealed partial class CreatorPage
{
    static List<Order>? _orders;
    static string? _ordersError;
    string _orderFilter = "open";
    string? _orderId;
    List<Bid>? _bids;
    DispatcherTimer? _live;
    int _liveTick;
    readonly List<(TextBlock Text, Func<string> Value)> _ticks = [];
    StackPanel? _ordersHost;

    void StopLive()
    {
        _live?.Stop();
        _live = null;
        _ticks.Clear();
    }

    /// <summary>Раз в секунду — обратный отсчёт, раз в 6 секунд — свежие заказы и ставки.</summary>
    void StartLive()
    {
        if (_live is not null || Program.Screenshot) return;
        _live = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _live.Tick += async (_, _) =>
        {
            foreach (var (text, value) in _ticks) text.Text = value();
            if (++_liveTick % 6 != 0 || _tab != "orders") return;
            try
            {
                _orders = await Market.Orders(force: true);
                if (_orderId is not null) _bids = await Market.Bids(_orderId);
                _ordersError = null;
                // Человек вводит ставку — не перерисовываем под руками, обновим, когда отпустит поле.
                if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox) return;
                RenderOrders();
            }
            catch { }
        };
        _live.Start();
    }

    async Task LoadOrders(bool force = false)
    {
        try
        {
            _orders = await Market.Orders(force);
            if (_orderId is not null) _bids = await Market.Bids(_orderId);
            _ordersError = null;
        }
        catch (Exception e) { _orders ??= []; _ordersError = Explain(e); }
        RenderOrders();
    }

    static string Countdown(TimeSpan left) =>
        left <= TimeSpan.Zero ? I18n.T("ord.closed")
        : left.TotalDays >= 1 ? I18n.T("ord.left.days", ("d", (int)left.TotalDays), ("h", left.Hours))
        : $"{(int)left.TotalHours}:{left.Minutes:00}:{left.Seconds:00}";

    TextBlock Live(Func<string> value, string classes = "small")
    {
        var t = Ui.Text(value(), classes);
        _ticks.Add((t, value));
        return t;
    }

    static (string Text, string Color) StatusLook(Order o) => o.Status switch
    {
        "open" when !o.Open => (I18n.T("ord.st.waiting"), "#F5B841"),
        "open" => (I18n.T("ord.st.open"), "#22C55E"),
        "assigned" => (I18n.T("ord.st.assigned"), "#3478F6"),
        "delivered" => (I18n.T("ord.st.delivered"), "#8B5CF6"),
        "done" => (I18n.T("ord.st.done"), "#64748B"),
        "refunded" => (I18n.T("ord.st.refunded"), "#EF4444"),
        _ => (I18n.T("ord.st.cancelled"), "#64748B"),
    };

    Control OrdersView()
    {
        _ticks.Clear();
        _ordersHost = new StackPanel { Spacing = 16 };
        if (_orders is null && !Program.Screenshot) _ = LoadOrders();
        StartLive();
        RenderOrders();
        return _ordersHost;
    }

    void RenderOrders()
    {
        if (_ordersHost is null) return;
        _ticks.Clear();
        _ordersHost.Children.Clear();
        var order = _orderId is null ? null : _orders?.FirstOrDefault(o => o.Id == _orderId);
        if (order is not null) { _ordersHost.Children.Add(OrderDetail(order)); return; }

        var hero = new Border
        {
            Classes = { "card", "hero" }, Padding = new Thickness(24, 20),
            Child = new DockPanel
            {
                Children =
                {
                    DockRight(Ui.Button(I18n.T("ord.new"), () => { if (!NeedAccount()) NewOrder(); }, "primary", Icons.Plus)),
                    Ui.Col(4,
                        Ui.Row(8, new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = Ui.Res("Bad"), Classes = { "pulse" }, VerticalAlignment = VerticalAlignment.Center }, Ui.Text(I18n.T("ord.eyebrow"), "eyebrow")),
                        Ui.Text(I18n.T("ord.title"), "h2"), Ui.Text(I18n.T("ord.text"), "muted", wrap: true)),
                },
            },
        };
        _ordersHost.Children.Add(hero);

        var chips = Ui.Row(6);
        foreach (var f in new[] { "open", "mine", "work", "done" })
        {
            var ff = f;
            var chip = Ui.Button(I18n.T("ord.f." + f), () => { _orderFilter = ff; RenderOrders(); }, "chip");
            if (_orderFilter == f) chip.Classes.Add("active");
            chips.Children.Add(chip);
        }
        _ordersHost.Children.Add(chips);

        if (_ordersError is not null && (_orders?.Count ?? 0) == 0)
        {
            _ordersHost.Children.Add(Ui.Card(Ui.Col(8, Ui.Row(10, Ui.Icon(Icons.Alert, 20, Ui.Res("Warn")), Ui.Text(I18n.T("mk.error"), "h3")),
                Ui.Text(_ordersError, "muted", wrap: true)), 24));
            return;
        }
        if (_orders is null)
        {
            var sk = new TileGrid { MinItemWidth = 300 };
            for (var i = 0; i < 6; i++) sk.Children.Add(new Border { Classes = { "card", "skeleton" }, Height = 170 });
            _ordersHost.Children.Add(sk);
            return;
        }
        var me = Market.Me;
        var list = _orders.Where(o => _orderFilter switch
        {
            "mine" => o.Uid == me,
            "work" => o.Winner == me || (_bidOn.Contains(o.Id) && o.Status == "open"),
            "done" => o.Status is "done" or "refunded" or "cancelled",
            _ => o.Status == "open",
        }).Where(o => _filter == "" || o.Title.Contains(_filter, StringComparison.OrdinalIgnoreCase) || o.Text.Contains(_filter, StringComparison.OrdinalIgnoreCase));
        list = _orderFilter == "open" ? list.OrderBy(o => o.Open ? 0 : 1).ThenBy(o => o.Closes) : list.OrderByDescending(o => o.Updated);
        var shown = list.ToList();
        if (shown.Count == 0)
        {
            _ordersHost.Children.Add(Ui.Card(Ui.Col(8, Ui.Text(I18n.T("ord.empty." + _orderFilter), "h3"), Ui.Text(I18n.T("ord.empty.text"), "small muted", wrap: true)), 24));
            return;
        }
        var grid = new TileGrid { MinItemWidth = 300 };
        foreach (var o in shown) grid.Children.Add(OrderCard(o));
        _ordersHost.Children.Add(grid);
    }

    /// <summary>Заказы, где я уже сделал ставку (знаем по своим ставкам за эту сессию и по демо).</summary>
    static readonly HashSet<string> _bidOn = [];

    Control OrderCard(Order o)
    {
        var game = GameCatalog.ById(o.Game);
        var (st, color) = StatusLook(o);
        var head = new DockPanel();
        var budget = Ui.Text(o.Status == "open" ? I18n.T("ord.upto", ("price", Credits(o.Budget))) : Credits(o.Price), "strong", color: Ui.Res("Brand2"));
        DockPanel.SetDock(budget, Dock.Right);
        head.Children.Add(budget);
        var label = Tiles.Label(st, color);
        label.HorizontalAlignment = HorizontalAlignment.Left;
        head.Children.Add(label);
        var foot = new DockPanel();
        Control right = o.Status == "open"
            ? Ui.Row(5, Ui.Icon(Icons.Clock, 12, Ui.Res("Muted")), Live(() => Countdown(o.Left), "small"))
            : Ui.Text(o.WinnerName is { Length: > 0 } wn ? I18n.T("ord.by", ("name", wn)) : "", "small muted");
        DockPanel.SetDock(right, Dock.Right);
        foot.Children.Add(right);
        foot.Children.Add(Ui.Row(12,
            Ui.Row(5, Ui.Icon(Icons.Users, 12, Ui.Res("Muted")), Ui.Text(I18n.T("ord.bids", ("n", o.Bids)), "small muted")),
            Ui.Text(game?.ShortName ?? o.Game, "small muted")));
        var body = Ui.Col(8, head,
            new TextBlock { Text = o.Title, FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = o.Text, FontSize = 12.5, Foreground = Ui.Res("Muted"), TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, Height = 34 },
            Ui.Text(I18n.T("ord.from", ("name", o.Author), ("days", o.Days)), "small muted"),
            foot);
        var b = new Button { Classes = { "tile" }, Padding = new Thickness(16, 14), HorizontalAlignment = HorizontalAlignment.Stretch, Content = body };
        b.Click += (_, _) => { _orderId = o.Id; _bids = null; RenderOrders(); _ = LoadOrders(force: true); };
        return b;
    }

    // ---------------------------------------------------------------- заказ целиком

    Control OrderDetail(Order o)
    {
        var bids = _bids ?? [];
        var best = bids.FirstOrDefault();
        var (st, color) = StatusLook(o);
        var game = GameCatalog.ById(o.Game);

        var back = Ui.Button(I18n.T("ord.back"), () => { _orderId = null; _bids = null; RenderOrders(); }, "ghost", Icons.Back);

        // Ход заказа: открыт → исполнитель → сдан → принят.
        var steps = new[] { "open", "assigned", "delivered", "done" };
        var at = Array.IndexOf(steps, o.Status);
        var timeline = Ui.Row(0);
        for (var i = 0; i < steps.Length; i++)
        {
            var done = at >= i;
            timeline.Children.Add(Ui.Row(6,
                new Border
                {
                    Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = done ? Ui.Res("Brand") : Ui.Res("Surface3"),
                    Child = done ? Ui.Icon(Icons.Check, 12, Brushes.White) : new TextBlock { Text = (i + 1).ToString(), FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                },
                Ui.Text(I18n.T("ord.step." + steps[i]), done ? "small strong" : "small muted")));
            if (i < steps.Length - 1) timeline.Children.Add(new Border { Width = 28, Height = 2, Background = at > i ? Ui.Res("Brand") : Ui.Res("Line"), Margin = new Thickness(8, 0), VerticalAlignment = VerticalAlignment.Center });
        }

        var info = Ui.Col(12,
            Ui.Row(10, Tiles.Label(st, color), Ui.Text(game?.Name ?? o.Game, "small muted"), Ui.Text(I18n.T("ord.from", ("name", o.Author), ("days", o.Days)), "small muted")),
            new TextBlock { Text = o.Title, FontSize = 26, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap },
            Ui.Text(o.Text == "" ? I18n.T("ord.noText") : o.Text, "", wrap: true),
            timeline);
        if (o.Status is "assigned" or "delivered" or "done")
        {
            var who = Ui.Col(4,
                Ui.Text(I18n.T("ord.winner", ("name", o.WinnerName), ("price", Credits(o.Price))), "strong"),
                o.Due is { } due && o.Status == "assigned" ? Live(() => I18n.T("ord.due", ("left", Countdown(due - (DateTime.UtcNow + Firebase.Skew)))), "small muted") : new Control());
            info.Children.Add(new Border { Classes = { "inset" }, Padding = new Thickness(14, 10), Child = who });
        }
        if (o.Status is "delivered" or "done" && o.Delivery != "")
            info.Children.Add(new Border
            {
                Classes = { "inset" }, Padding = new Thickness(14, 10),
                Child = Ui.Col(6, Ui.Text(I18n.T("ord.delivery"), "eyebrow"), Ui.Text(DeliveryTitle(o.Delivery), "strong", wrap: true),
                    o.Note == "" ? new Control() : Ui.Text(o.Note, "small muted", wrap: true),
                    Ui.Button(I18n.T("ord.open"), () => OpenDelivery(o.Delivery), "", Icons.External)),
            });
        if (o.Rating > 0) info.Children.Add(Ui.Text(new string('★', o.Rating) + new string('☆', 5 - o.Rating), "h3", color: Ui.Hex("#F5B841")));

        // Что можно сделать сейчас — зависит от того, кто смотрит.
        var actions = new WrapPanel();
        void Act(Control c) { c.Margin = new Thickness(0, 0, 8, 8); actions.Children.Add(c); }
        if (o.Mine)
        {
            if (o.Status == "open") Act(Ui.Button(I18n.T("ord.cancel"), () => Run(() => Market.CancelOrder(o), I18n.T("ord.cancelled")), "ghost", Icons.Close));
            if (o.Status is "assigned" or "delivered") Act(Ui.Button(I18n.T("ord.accept"), () => RateOrder(o), "primary", Icons.Check));
            if (o.Overdue) Act(Ui.Button(I18n.T("ord.refund"), () => Run(() => Market.Refund(o), I18n.T("ord.refunded")), "", Icons.Refresh));
        }
        else if (o.Working)
        {
            if (o.Status == "assigned")
            {
                Act(Ui.Button(I18n.T("ord.deliver"), () => Deliver(o), "primary", Icons.Upload));
                Act(Ui.Button(I18n.T("ord.decline"), () => Confirm(I18n.T("ord.decline"), I18n.T("ord.decline.text"), () => Run(() => Market.Refund(o), I18n.T("ord.refunded"))), "ghost", Icons.Close));
            }
            if (o.Claimable) Act(Ui.Button(I18n.T("ord.claim"), () => Run(() => Market.Complete(o, 0), I18n.T("ord.paid", ("price", Credits(o.Price)))), "primary", Icons.Bag));
            else if (o.Status == "delivered") Act(Ui.Text(I18n.T("ord.waitAccept"), "small muted", wrap: true));
        }
        if (actions.Children.Count > 0) info.Children.Add(actions);

        // Ставки: лучшая (дешевле) сверху, обновляются сами.
        var right = new StackPanel { Spacing = 10 };
        var head = new DockPanel();
        var timer = o.Status == "open" ? Ui.Row(6, Ui.Icon(Icons.Clock, 14, o.Open ? Ui.Res("Good") : Ui.Res("Warn")), Live(() => Countdown(o.Left), "strong")) : new Control();
        DockPanel.SetDock(timer, Dock.Right);
        head.Children.Add(timer);
        head.Children.Add(Ui.Col(0, Ui.Text(I18n.T("ord.bidsTitle"), "h3"), Ui.Text(I18n.T("ord.budget", ("price", Credits(o.Budget))), "small muted")));
        right.Children.Add(head);
        if (best is not null && o.Status == "open")
            right.Children.Add(new Border
            {
                Background = Ui.Res("BrandSoft"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10),
                Child = Ui.Row(10, Ui.Icon(Icons.Trophy, 18, Ui.Res("Brand2")), Ui.Col(0, Ui.Text(I18n.T("ord.best"), "eyebrow"), Ui.Text($"{Credits(best.Price)} · {I18n.T("ord.days", ("n", best.Days))} · {best.Author}", "strong"))),
            });
        if (_bids is null) right.Children.Add(Ui.Text(I18n.T("mk.loading"), "small muted"));
        else if (bids.Count == 0) right.Children.Add(Ui.Text(I18n.T(o.Status == "open" ? "ord.noBids" : "ord.noBidsClosed"), "small muted", wrap: true));
        foreach (var b in bids)
        {
            var bb = b;
            var row = new DockPanel();
            var price = Ui.Col(0, Ui.Text(Credits(b.Price), "h3", color: b == best ? Ui.Res("Brand2") : null), Ui.Text(I18n.T("ord.days", ("n", b.Days)), "small muted"));
            price.HorizontalAlignment = HorizontalAlignment.Right;
            DockPanel.SetDock(price, Dock.Right);
            row.Children.Add(price);
            var stars = _orders is null ? (0, 0) : Market.Reputation(_orders, b.Uid);
            var who = Ui.Col(3,
                Ui.Row(8, Ui.Text(b.Author, "strong"), stars.Item2 > 0 ? Ui.Text($"★ {stars.Item1:0.0} · {I18n.T("ord.doneCount", ("n", stars.Item2))}", "small", color: Ui.Hex("#F5B841")) : Ui.Text(I18n.T("ord.newbie"), "small muted")),
                b.Text == "" ? new Control() : Ui.Text(b.Text, "small muted", wrap: true),
                Ui.Text(Ui.Ago(b.At), "small muted"));
            row.Children.Add(who);
            var card = new StackPanel { Spacing = 8, Children = { row } };
            if (o.Mine && o.Status == "open")
                card.Children.Add(Ui.Button(I18n.T("ord.pick", ("price", Credits(b.Price))), () => Confirm(I18n.T("ord.pick.title", ("name", bb.Author)),
                    I18n.T("ord.pick.text", ("price", Credits(bb.Price)), ("days", bb.Days)), () => Run(() => Market.Pick(o, bb), I18n.T("ord.picked", ("name", bb.Author))), "primary"), "primary", Icons.Check));
            right.Children.Add(new Border { Classes = { "inset" }, Padding = new Thickness(14, 10), Child = card, BorderBrush = b.Mine ? Ui.Res("Brand") : null });
        }
        if (!o.Mine && o.Open) right.Children.Add(BidForm(o, bids.FirstOrDefault(b => b.Mine)));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,380"), ColumnSpacing = 18 };
        var main = Ui.Card(info, 24);
        main.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(main);
        var side = Ui.Card(right, 18);
        side.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(side, 1);
        grid.Children.Add(side);
        return Ui.Col(14, back, grid);
    }

    // Черновик ставки переживает обновления списка (раз в 6 секунд).
    (string Order, decimal? Price, decimal? Days, string? Text) _bidDraft;

    Control BidForm(Order o, Bid? mine)
    {
        if (_bidDraft.Order != o.Id) _bidDraft = (o.Id, null, null, null);
        var price = new NumericUpDown { Minimum = 1, Maximum = o.Budget, Value = _bidDraft.Price ?? mine?.Price ?? Math.Max(1, (_bids?.FirstOrDefault()?.Price ?? o.Budget) - 5), Increment = 5, FormatString = "N0", Width = 140 };
        var days = new NumericUpDown { Minimum = 1, Maximum = 60, Value = _bidDraft.Days ?? mine?.Days ?? o.Days, Increment = 1, FormatString = "N0", Width = 110 };
        var text = new TextBox { Watermark = I18n.T("ord.bid.note"), Text = _bidDraft.Text ?? mine?.Text ?? "", MaxLength = 500, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60 };
        price.ValueChanged += (_, _) => _bidDraft.Price = price.Value;
        days.ValueChanged += (_, _) => _bidDraft.Days = days.Value;
        text.TextChanged += (_, _) => _bidDraft.Text = text.Text;
        var send = Ui.Button(I18n.T(mine is null ? "ord.bid" : "ord.bid.update"), () =>
        {
            if (NeedAccount()) return;
            _bidOn.Add(o.Id);
            Run(async () => { await Market.PlaceBid(o, (long)(price.Value ?? 1), (int)(days.Value ?? 1), text.Text ?? ""); _bids = await Market.Bids(o.Id); _bidDraft = default; }, I18n.T("ord.bid.done"));
        }, "primary", Icons.ArrowUp);
        var col = Ui.Col(10,
            Ui.Text(I18n.T(mine is null ? "ord.bid.title" : "ord.bid.yours"), "h3"),
            Ui.Text(I18n.T("ord.bid.hint"), "small muted", wrap: true),
            Ui.Row(10, Ui.Col(4, Ui.Text(I18n.T("ord.bid.price"), "small muted"), Ui.Row(6, Ui.Text(Market.Coin, "h3"), price)), Ui.Col(4, Ui.Text(I18n.T("ord.bid.days"), "small muted"), days)),
            text,
            Ui.Row(8, send, mine is null ? new Control() : Ui.Button(I18n.T("ord.bid.withdraw"), () => Run(async () => { await Market.WithdrawBid(o); _bids = await Market.Bids(o.Id); }), "ghost", Icons.Trash)));
        return new Border { Classes = { "inset" }, Padding = new Thickness(14), Child = col };
    }

    static string DeliveryTitle(string delivery)
    {
        var hub = _hub?.FirstOrDefault(m => m.Id == delivery);
        if (hub is not null) return I18n.T("ord.delivery.mod", ("name", hub.Name));
        var asset = _assets?.FirstOrDefault(a => a.Id == delivery);
        if (asset is not null) return I18n.T("ord.delivery.asset", ("name", asset.Name));
        return delivery;
    }

    void OpenDelivery(string delivery)
    {
        if (delivery.StartsWith("https://")) { Ui.OpenUrl(delivery); return; }
        if (_hub?.FirstOrDefault(m => m.Id == delivery) is { } mod) { OpenMod(mod); return; }
        if (_assets?.FirstOrDefault(a => a.Id == delivery) is { } asset) { OpenAsset(asset); return; }
        _ = Find();
        async Task Find()
        {
            try
            {
                if (await Market.GetAsset(delivery) is { } a) { OpenAsset(a); return; }
                var mods = await Hub.All();
                if (mods.FirstOrDefault(m => m.Id == delivery) is { } m) OpenMod(m);
            }
            catch (Exception e) { W.Toast(Explain(e), bad: true); }
        }
    }

    static void Confirm(string title, string text, Action yes, string classes = "primary") =>
        W.Dialog(title, Ui.Text(text, "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), W.CloseDialog),
            Ui.Button(I18n.T("common.continue"), () => { W.CloseDialog(); yes(); }, classes));

    void NewOrder()
    {
        var title = new TextBox { Watermark = I18n.T("ord.f.title"), MaxLength = 80 };
        var text = new TextBox { Watermark = I18n.T("ord.f.text"), MaxLength = 3000, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 110, VerticalContentAlignment = VerticalAlignment.Top };
        var game = new ComboBox { Width = 240 };
        var games = AppState.Games.Select(g => g.Def).Concat(GameCatalog.Builtin).DistinctBy(g => g.Id).ToList();
        foreach (var g in games) game.Items.Add(g.Name);
        game.SelectedIndex = 0;
        var budget = new NumericUpDown { Minimum = 10, Maximum = 1_000_000, Value = 50, Increment = 10, FormatString = "N0", Width = 150 };
        var window = new ComboBox { Width = 160 };
        var windows = new[] { 1, 6, 24, 72, 167 };
        foreach (var h in windows) window.Items.Add(I18n.T("ord.f.window." + h));
        window.SelectedIndex = 2;
        var days = new NumericUpDown { Minimum = 1, Maximum = 60, Value = 7, Increment = 1, FormatString = "N0", Width = 110 };
        var status = Ui.Text(_wallet is null ? "" : I18n.T("ord.f.wallet", ("price", Credits(_wallet.Balance))), "small muted", wrap: true);
        Control Field(string label, Control input) => Ui.Col(5, Ui.Text(label, "small muted"), input);
        Button? go = null;
        go = Ui.Button(I18n.T("ord.post"), async () =>
        {
            go!.IsEnabled = false;
            try
            {
                var id = await Market.PostOrder(title.Text ?? "", text.Text ?? "", games[Math.Max(0, game.SelectedIndex)].Id, (long)(budget.Value ?? 10),
                    TimeSpan.FromHours(windows[Math.Max(0, window.SelectedIndex)]), (int)(days.Value ?? 7));
                W.CloseDialog();
                W.Toast(I18n.T("ord.posted"));
                _orderId = id;
                _orderFilter = "mine";
                await LoadOrders(force: true);
            }
            catch (Exception e) { status.Text = Explain(e); go.IsEnabled = true; }
        }, "primary", Icons.Upload);
        W.Dialog(I18n.T("ord.new"), Ui.Col(12,
                Ui.Text(I18n.T("ord.f.hint"), "small muted", wrap: true),
                Field(I18n.T("ord.f.titleLabel"), title),
                Field(I18n.T("ord.f.textLabel"), text),
                Ui.Row(12, Field(I18n.T("cr.r.game"), game), Field(I18n.T("ord.f.budget"), Ui.Row(6, Ui.Text(Market.Coin, "h3"), budget))),
                Ui.Row(12, Field(I18n.T("ord.f.window"), window), Field(I18n.T("ord.f.days"), days)),
                status),
            Ui.Button(I18n.T("common.cancel"), W.CloseDialog), go);
    }

    /// <summary>Сдать работу: свой мод из хаба, свой ассет или ссылка.</summary>
    void Deliver(Order o)
    {
        var pick = new ComboBox { MinWidth = 300 };
        var values = new List<string>();
        var link = new TextBox { Watermark = "https://…", MaxLength = 200 };
        var note = new TextBox { Watermark = I18n.T("ord.deliver.note"), MaxLength = 1000, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70 };
        pick.Items.Add(I18n.T("ord.deliver.link"));
        values.Add("");
        foreach (var m in (_hub ?? []).Where(m => m.Mine)) { pick.Items.Add(I18n.T("ord.delivery.mod", ("name", m.Name))); values.Add(m.Id); }
        foreach (var a in (_assets ?? []).Where(a => a.Mine)) { pick.Items.Add(I18n.T("ord.delivery.asset", ("name", a.Name))); values.Add(a.Id); }
        pick.SelectedIndex = values.Count > 1 ? 1 : 0;
        link.IsVisible = pick.SelectedIndex == 0;
        pick.SelectionChanged += (_, _) => link.IsVisible = pick.SelectedIndex <= 0;
        W.Dialog(I18n.T("ord.deliver"), Ui.Col(12, Ui.Text(I18n.T("ord.deliver.text"), "muted", wrap: true), pick, link, note),
            Ui.Button(I18n.T("common.cancel"), W.CloseDialog),
            Ui.Button(I18n.T("ord.deliver"), () =>
            {
                var what = pick.SelectedIndex > 0 ? values[pick.SelectedIndex] : (link.Text ?? "").Trim();
                if (what == "" || (pick.SelectedIndex <= 0 && !what.StartsWith("https://"))) { W.Toast(I18n.T("ord.deliver.need"), bad: true); return; }
                W.CloseDialog();
                Run(() => Market.Deliver(o, what, note.Text ?? ""), I18n.T("ord.delivered"));
            }, "primary", Icons.Upload));
    }

    void RateOrder(Order o)
    {
        var rating = 5;
        var stars = Ui.Row(4);
        void Paint()
        {
            for (var i = 0; i < stars.Children.Count; i++)
                if (stars.Children[i] is Button b) b.Content = Ui.Text(i < rating ? "★" : "☆", "h2", color: Ui.Hex("#F5B841"));
        }
        for (var i = 1; i <= 5; i++)
        {
            var n = i;
            stars.Children.Add(Ui.Button("", () => { rating = n; Paint(); }, "ghost"));
        }
        Paint();
        W.Dialog(I18n.T("ord.accept"), Ui.Col(12, Ui.Text(I18n.T("ord.accept.text", ("name", o.WinnerName), ("price", Credits(o.Price))), "muted", wrap: true), stars),
            Ui.Button(I18n.T("common.cancel"), W.CloseDialog),
            Ui.Button(I18n.T("ord.accept"), () => { W.CloseDialog(); Run(async () => { await Market.Complete(o, rating); await LoadOrders(force: true); }, I18n.T("ord.done")); }, "primary", Icons.Check));
    }
}
