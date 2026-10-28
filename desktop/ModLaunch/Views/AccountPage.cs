using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Social;

namespace ModLaunch.Views;

/// <summary>
/// Аккаунт ModLaunch — отдельная страница: профиль, безопасность, кошелёк
/// (пополнение и вывод), покупки, продажи, свои данные и панель админа.
/// </summary>
public sealed partial class AccountPage : Page
{
    public override string Title => I18n.T("acc.page");

    string _tab;
    string _mode = "signin";
    bool _busy;

    // Загружается по требованию, сбрасывается кнопкой «Обновить».
    List<Purchase>? _purchases;
    List<Payout>? _payouts;
    List<TopUp>? _topups;
    (List<TopUp> TopUps, List<Payout> Payouts, Wallet Platform)? _admin;
    string? _error;

    public AccountPage(string tab = "profile") { _tab = tab == "selling" ? "earnings" : tab; }

    void Reset() { _earn = null; _purchases = null; _payouts = null; _topups = null; _admin = null; _error = null; _tried.Clear(); }

    /// <summary>Что уже пробовали загрузить: после ошибки не повторяем сами (иначе бесконечный цикл запросов), только по кнопке «Обновить».</summary>
    readonly HashSet<string> _tried = [];

    /// <summary>Загрузить данные раздела в фоне и перестроить страницу.</summary>
    async void Load(string key, Func<Task> load)
    {
        if (_busy || !_tried.Add(key)) return;
        _busy = true;
        // Всегда асинхронно: даже мгновенная ошибка не уходит в рекурсию Build → Load → Build.
        await Task.Yield();
        try { await load(); _error = null; }
        catch (Exception e) { _error = Market.Explain(e); }
        _busy = false;
        Build();
    }

    async void Call(Func<Task> action, string? ok = null, bool reload = true)
    {
        _busy = true;
        Build();
        try
        {
            await action();
            if (ok is not null) MainWindow.Current?.Toast(ok);
            if (reload) Reset();
        }
        catch (Exception e) { MainWindow.Current?.Toast(!Account.SignedIn || _tab is "profile" or "security" ? Account.Explain(e) : Market.Explain(e), bad: true); }
        _busy = false;
        Build();
    }

    public override void Build()
    {
        var p = Account.Get();
        var sections = new List<(string Id, string Key, string Icon)>
        {
            ("profile", "acc.s.profile", Icons.User),
            ("security", "acc.s.security", Icons.Lock),
            ("wallet", "acc.s.wallet", Icons.Bag),
            ("purchases", "acc.s.purchases", Icons.Download),
            ("earnings", "v20.acc.earnings", Icons.Chart),
            ("data", "acc.s.data", Icons.Folder),
        };
        // 14.0 → 20.0: комиссия площадки — доход владельца, место ей здесь, а не на рынке.
        if (Account.Owner && p.SignedIn) sections.Add(("commission", "v14.mk.tab.commission", Icons.Percent));
        if (p.Admin) sections.Add(("admin", "acc.s.admin", Icons.Shield));
        // Снимки экрана: «Заработок» на примере, без входа.
        var demo = Program.Screenshot && _tab == "earnings";
        if (!p.SignedIn && !demo) _tab = "profile";

        var tabs = new StackPanel { Spacing = 4, Width = 230 };
        var head = Ui.Row(12, ProfileKit.Avatar(p.SignedIn ? p.Name ?? p.Email ?? "?" : "?", 46),
            Ui.Col(2, Ui.Text(p.SignedIn ? p.Name ?? "" : I18n.T("acc.menu.guest"), "h3"), Ui.Text(p.SignedIn ? p.Email ?? "" : I18n.T("acc.page.guest"), "small muted")));
        head.Margin = new Thickness(8, 6, 8, 10);
        tabs.Children.Add(head);
        foreach (var (id, key, icon) in sections)
        {
            var b = Ui.Button(I18n.T(key), () => { _tab = id; _error = null; Build(); }, "tab", icon);
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.HorizontalContentAlignment = HorizontalAlignment.Left;
            b.IsEnabled = p.SignedIn || id == "profile" || demo;
            if (_tab == id) b.Classes.Add("active");
            tabs.Children.Add(b);
        }

        Control body;
        if (demo) body = Earnings();
        else if (!p.Configured) body = Card(I18n.T("acc.title"), I18n.T("acc.off"));
        else if (!p.SignedIn) body = SignForm();
        else body = _tab switch
        {
            "security" => Security(p),
            "wallet" => new WalletPanel(),
            "purchases" => PurchasesTab(),
            "earnings" => Earnings(),
            "commission" when Account.Owner => new CommissionPanel(),
            "data" => DataTab(p),
            "admin" when p.Admin => Admin(),
            _ => Profile(p),
        };
        if (_error is not null) body = Ui.Col(16, Ui.Card(Ui.Col(10, Ui.Text(_error, "", color: Ui.Res("Bad"), wrap: true), Ui.Button(I18n.T("cr.refresh"), () => { Reset(); Build(); }, "", Icons.Refresh)), 18), body);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 24, Margin = new Thickness(34, 26, 34, 34), MaxWidth = 1500 };
        var side = Ui.Card(tabs, 10);
        side.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(side);
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);
        Content = new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    static Border Card(string title, string? hint, params Control[] rows)
    {
        var col = Ui.Col(14, Ui.Text(title, "h2"));
        if (hint is not null) col.Children.Add(Ui.Text(hint, "muted", wrap: true));
        foreach (var r in rows) col.Children.Add(r);
        return Ui.Card(col, 24);
    }

    static Control Stat(string label, string value, IBrush? color = null)
    {
        var c = Ui.Card(Ui.Col(4, Ui.Text(label, "small muted"), Ui.Text(value, "h2", color: color)), 16);
        c.Margin = new Thickness(0, 0, 12, 12);
        return c;
    }

    static Control Loading() => Ui.Text(I18n.T("common.loading"), "muted");

    // ---------------------------------------------------------------- вход

    Control SignForm()
    {
        var tabs = Ui.Row(6);
        foreach (var (id, key) in new[] { ("signin", "acc.tab.signin"), ("signup", "acc.tab.signup"), ("reset", "acc.forgot") })
        {
            var b = Ui.Button(I18n.T(key), () => { _mode = id; Build(); }, "chip");
            if (_mode == id) b.Classes.Add("active");
            tabs.Children.Add(b);
        }
        var name = new TextBox { Watermark = I18n.T("acc.name"), MaxLength = 32, Width = 360, HorizontalAlignment = HorizontalAlignment.Left };
        var email = new TextBox { Watermark = I18n.T("acc.email"), Width = 360, HorizontalAlignment = HorizontalAlignment.Left };
        var password = new TextBox { Watermark = I18n.T("acc.password"), PasswordChar = '•', MaxLength = 128, Width = 360, HorizontalAlignment = HorizontalAlignment.Left };
        var col = Ui.Col(12, Ui.Text(I18n.T("acc.out.title"), "h2"),
            Ui.Col(4, Ui.Text("✓ " + I18n.T("acc.perk.1"), "small muted"), Ui.Text("✓ " + I18n.T("acc.perk.2"), "small muted"),
                Ui.Text("✓ " + I18n.T("acc.perk.3"), "small muted"), Ui.Text("✓ " + I18n.T("acc.perk.market"), "small muted")),
            tabs);
        if (_mode == "signup") col.Children.Add(name);
        col.Children.Add(email);
        if (_mode != "reset") col.Children.Add(password);
        Button go = _mode switch
        {
            "signup" => Ui.Button(I18n.T("acc.signup.go"), () => Call(async () =>
            {
                var p = await Account.SignUp(name.Text ?? "", email.Text ?? "", password.Text ?? "");
                MainWindow.Current?.Toast(I18n.T("acc.welcomeNew", ("name", p.Name ?? "")));
            }), "primary", Icons.User),
            "reset" => Ui.Button(I18n.T("acc.reset.go"), () => Call(() => Account.ResetPassword(email.Text), I18n.T("acc.reset.sent", ("email", email.Text ?? ""))), "primary"),
            _ => Ui.Button(I18n.T("acc.signin.go"), () => Call(async () =>
            {
                var p = await Account.SignIn(email.Text ?? "", password.Text ?? "");
                MainWindow.Current?.Toast(I18n.T("acc.welcome", ("name", p.Name ?? "")));
            }), "primary", Icons.Key),
        };
        go.IsEnabled = !_busy;
        col.Children.Add(go);
        col.Children.Add(Ui.Text(I18n.T("acc.note"), "small muted", wrap: true));
        return Ui.Card(col, 28);
    }

    // ---------------------------------------------------------------- профиль

    Control Profile(Profile p)
    {
        var col = Ui.Col(16);
        var badge = p.Verified ? Ui.Text("✓ " + I18n.T("acc.verified"), "small", color: Ui.Res("Good")) : Ui.Text(I18n.T("acc.unverified"), "small", color: Ui.Res("Warn"));
        // 9.9: шапка аккаунта — надетый баннер, аватар в рамке, титул и уровень игрока.
        var all = Features.Achievements.All();
        var (level, _, next) = Features.Achievements.Level(all);
        var words = Ui.Col(5,
            new TextBlock { Text = p.Name ?? "", FontFamily = Gx.Display, FontSize = 28, FontWeight = FontWeight.Bold, Foreground = Brushes.White },
            Ui.Row(10, ProfileKit.Title(), new TextBlock { Text = I18n.T("v99.acc.level", ("n", level)), FontSize = 12.5, FontWeight = FontWeight.SemiBold, Foreground = Ui.Hex("#E6E7EE"), VerticalAlignment = VerticalAlignment.Center }),
            new TextBlock { Text = p.Email ?? "", FontSize = 13, Foreground = Ui.Hex("#C9CFDB") },
            Ui.Row(12, badge, new TextBlock { Text = p.Since is DateTime s ? I18n.T("acc.since", ("date", s.ToString("d", I18n.Culture))) : "", FontSize = 12, Foreground = Ui.Hex("#C9CFDB") },
                p.Admin ? Ui.Text("👑 " + I18n.T("acc.admin.title"), "small brand") : new Panel()));
        words.VerticalAlignment = VerticalAlignment.Bottom;
        var toProfile = Ui.Button(I18n.T("v99.home.profile"), () => MainWindow.Current?.Navigate(() => new ProfilePage()), "primary", Icons.Trophy);
        toProfile.VerticalAlignment = VerticalAlignment.Bottom;
        ToolTip.SetTip(toProfile, I18n.T("v98.level.next", ("n", next)));
        var whoGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 20, Margin = new Thickness(24, 0, 24, 22), VerticalAlignment = VerticalAlignment.Bottom };
        whoGrid.Children.Add(ProfileKit.Avatar(p.Name ?? "?", 96, ring: 5));
        Grid.SetColumn(words, 1);
        whoGrid.Children.Add(words);
        Grid.SetColumn(toProfile, 2);
        whoGrid.Children.Add(toProfile);
        col.Children.Add(new Border { Classes = { "card" }, Padding = new Thickness(0), ClipToBounds = true, Height = 230, Child = new Panel { Children = { ProfileKit.Banner(), whoGrid } } });

        // 20.0: цифры кошелька и дохода здесь больше не повторяются — они в «Кошельке» и «Заработке».
        var rename = new TextBox { Text = p.Name, MaxLength = 32, Width = 280 };
        col.Children.Add(Card(I18n.T("acc.rename"), I18n.T("acc.rename.hint"),
            Ui.Row(8, rename, Ui.Button(I18n.T("common.save"), () => Call(() => Account.Rename(rename.Text ?? ""), I18n.T("acc.renamed"), reload: false), "primary"))));

        col.Children.Add(Card(I18n.T("acc.quick"), null, Ui.Row(10,
            Ui.Button(I18n.T("acc.menu.friends"), () => MainWindow.Current?.Navigate(() => new CommunityPage()), "", Icons.Users),
            Ui.Button("Creator Hub", () => MainWindow.Current?.Navigate(() => new CreatorPage()), "", Icons.Creator),
            Ui.Button(I18n.T("mk.tab"), () => MainWindow.Current?.Navigate(() => new MarketPage()), "", Icons.Bag),
            Ui.Button(I18n.T("v20.acc.earnings"), () => { _tab = "earnings"; Build(); }, "", Icons.Chart))));
        return col;
    }

    // ---------------------------------------------------------------- безопасность

    Control Security(Profile p)
    {
        var col = Ui.Col(16);
        var verify = p.Verified
            ? Ui.Text("✓ " + I18n.T("acc.verify.done"), "small", color: Ui.Res("Good"))
            : (Control)Ui.Col(8, Ui.Text(I18n.T("acc.verify.text", ("email", p.Email ?? "")), "small muted", wrap: true),
                Ui.Row(8,
                    Ui.Button(I18n.T("acc.verify.again"), () => Call(Account.SendVerification, I18n.T("acc.verify.sent", ("email", p.Email ?? "")), reload: false)),
                    Ui.Button(I18n.T("acc.verify.check"), () => Call(async () =>
                    {
                        var fresh = await Account.RefreshProfile();
                        MainWindow.Current?.Toast(fresh.Verified ? I18n.T("acc.verify.done") : I18n.T("acc.verify.notYet"), bad: !fresh.Verified);
                    }, reload: false), "primary")));
        col.Children.Add(Card(I18n.T("acc.email.title"), null, Ui.Text(p.Email ?? "", "h3"), verify));
        col.Children.Add(Card(I18n.T("acc.password.change"), I18n.T("acc.password.hint", ("email", p.Email ?? "")),
            Ui.Button(I18n.T("acc.password.send"), () => Call(() => Account.ResetPassword(p.Email), I18n.T("acc.password.sent", ("email", p.Email ?? "")), reload: false), "", Icons.Key)));
        col.Children.Add(Card(I18n.T("acc.session"), I18n.T("acc.session.text"),
            Ui.Row(10,
                Ui.Button(I18n.T("acc.session.refresh"), () => Call(async () => { await Account.RefreshProfile(); }, I18n.T("acc.session.ok"), reload: false), "", Icons.Refresh),
                Ui.Button(I18n.T("acc.signout"), () => { Account.SignOut(); Reset(); MainWindow.Current?.Toast(I18n.T("acc.signedOut")); Build(); }, "", Icons.Power))));

        var password = new TextBox { Watermark = I18n.T("acc.password"), PasswordChar = '•', Width = 280 };
        var del = Ui.Button(I18n.T("acc.delete.go"), () =>
        {
            var pass = password.Text ?? "";
            Call(async () =>
            {
                try { await Friends.Forget(); } catch { }
                await Account.Delete(pass);
            }, I18n.T("acc.deleted"));
        }, "", Icons.Trash);
        del.Foreground = Ui.Res("Bad");
        var danger = Card(I18n.T("acc.danger"), I18n.T("acc.delete.text"), Ui.Text(I18n.T("acc.delete.market"), "small muted", wrap: true), Ui.Row(8, password, del));
        danger.BorderBrush = Ui.Res("Bad");
        danger.BorderThickness = new Thickness(1);
        col.Children.Add(danger);
        return col;
    }

    // ---------------------------------------------------------------- кошелёк

    // ---------------------------------------------------------------- покупки

    Control PurchasesTab()
    {
        if (_purchases is null && !_busy) Load("purchases", async () => { _purchases = await Market.Purchases(); });
        var col = Ui.Col(8);
        if (_purchases is null) { col.Children.Add(Loading()); return Card(I18n.T("acc.s.purchases"), null, col); }
        if (_purchases.Count == 0)
            col.Children.Add(Ui.Col(10, Ui.Text(I18n.T("acc.purchases.empty"), "muted", wrap: true),
                Ui.Button(I18n.T("mk.tab"), () => MainWindow.Current?.Navigate(() => new MarketPage()), "primary", Icons.Bag)));
        foreach (var p in _purchases)
        {
            var item = p;
            var line = new DockPanel();
            var buttons = Ui.Row(6,
                Ui.Button(I18n.T("st.open"), async () => { if (await Market.Get(item.ListingId) is { } l) MarketViews.Open(l); else MainWindow.Current?.Toast(I18n.T("err.market.NOT_FOUND"), bad: true); }, "ghost", Icons.Eye),
                Ui.Button(I18n.T("mk.download"), async () => { if (await Market.Get(item.ListingId) is { } l) MarketViews.Download(l); else MainWindow.Current?.Toast(I18n.T("err.market.NOT_FOUND"), bad: true); }, "primary", Icons.Download));
            DockPanel.SetDock(buttons, Dock.Right);
            line.Children.Add(buttons);
            line.Children.Add(Ui.Row(12,
                new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(10), Background = Ui.Res("Surface3"), Child = Ui.Icon(MarketViews.KindIcon(item.Kind), 18) },
                Ui.Col(2, Ui.Text(item.Title, "h3"),
                    Ui.Text(I18n.T("mod.by", ("author", item.Seller)) + " · " + (item.Price == 0 ? I18n.T("mk.free") : Market.Money(item.Price)) + " · " + Ui.Ago(item.Created), "small muted"))));
            col.Children.Add(Ui.Card(line, 12));
        }
        return Card(I18n.T("acc.s.purchases"), I18n.T("acc.purchases.text"), col);
    }

    // ---------------------------------------------------------------- данные

    Control DataTab(Profile p)
    {
        return Ui.Col(16,
            Card(I18n.T("acc.export"), I18n.T("acc.export.text"),
                Ui.Button(I18n.T("acc.export.go"), () => Call(async () =>
                {
                    var top = TopLevel.GetTopLevel(this);
                    if (top is null) return;
                    var file = await Pickers.Save(top, I18n.T("acc.export"), $"modlaunch-account-{DateTime.Now:yyyyMMdd}.json");
                    if (file is null) return;
                    var data = new
                    {
                        profile = new { p.Uid, p.Email, p.Name, p.Verified, p.Since },
                        wallet = await Market.MyWallet(),
                        purchases = await Market.Purchases(),
                        listings = await Market.Mine(),
                        payouts = await Market.MyPayouts(),
                        topups = await Market.MyTopUps(),
                        exported = DateTime.UtcNow,
                    };
                    await File.WriteAllTextAsync(file, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
                    MainWindow.Current?.Toast(I18n.T("acc.export.done"));
                }, reload: false), "primary", Icons.Download)),
            Card(I18n.T("acc.local"), I18n.T("acc.local.text"),
                Ui.Button(I18n.T("games.openFolder"), () => Actions.OpenFolder(Paths.DataDir), "", Icons.Folder)));
    }

    // ---------------------------------------------------------------- админ

    Control Admin()
    {
        if (_admin is null && !_busy)
            Load("admin", async () => { _admin = (await Market.PendingTopUps(), await Market.PendingPayouts(), await Market.PlatformWallet()); });
        var col = Ui.Col(16);
        if (_admin is not { } a) { col.Children.Add(Loading()); return col; }

        var stats = new UniformGrid { Columns = 3 };
        stats.Children.Add(Stat(I18n.T("adm.platform"), Market.Money(a.Platform.Balance), Ui.Res("Good")));
        stats.Children.Add(Stat(I18n.T("adm.topups"), a.TopUps.Count.ToString(I18n.Culture)));
        stats.Children.Add(Stat(I18n.T("adm.payouts"), a.Payouts.Count.ToString(I18n.Culture)));
        col.Children.Add(stats);

        var tops = Ui.Col(6);
        if (a.TopUps.Count == 0) tops.Children.Add(Ui.Text(I18n.T("adm.none"), "muted"));
        foreach (var t in a.TopUps)
        {
            var item = t;
            var line = new DockPanel();
            var buttons = Ui.Row(6,
                Ui.Button(I18n.T("adm.approve"), () => Call(() => Market.ResolveTopUp(item, true), I18n.T("adm.done")), "primary", Icons.Check),
                Ui.Button(I18n.T("adm.reject"), () => Call(() => Market.ResolveTopUp(item, false), I18n.T("adm.done")), "ghost", Icons.Close));
            DockPanel.SetDock(buttons, Dock.Right);
            line.Children.Add(buttons);
            line.Children.Add(Ui.Col(2, Ui.Text($"{item.Author} · +{Market.Money(item.Amount)}", "h3"), Ui.Text($"{item.Reference} · {item.Uid} · {Ui.Ago(item.Created)}", "small muted", wrap: true)));
            tops.Children.Add(Ui.Card(line, 12));
        }
        col.Children.Add(Card(I18n.T("adm.topups"), I18n.T("adm.topups.text"), tops));

        var outs = Ui.Col(6);
        if (a.Payouts.Count == 0) outs.Children.Add(Ui.Text(I18n.T("adm.none"), "muted"));
        foreach (var p in a.Payouts)
        {
            var item = p;
            var note = new TextBox { Watermark = I18n.T("adm.note"), Width = 200, MaxLength = 300 };
            var line = new DockPanel();
            var buttons = Ui.Row(6, note,
                Ui.Button(I18n.T("adm.paid"), () => Call(() => Market.ResolvePayout(item, true, note.Text ?? ""), I18n.T("adm.done")), "primary", Icons.Check),
                Ui.Button(I18n.T("adm.reject"), () => Call(() => Market.ResolvePayout(item, false, note.Text ?? ""), I18n.T("adm.done")), "ghost", Icons.Close));
            DockPanel.SetDock(buttons, Dock.Right);
            line.Children.Add(buttons);
            line.Children.Add(Ui.Col(2, Ui.Text($"{item.Author} · {Market.Money(item.Amount)}", "h3"), Ui.Text($"{item.Method} · {item.Uid} · {Ui.Ago(item.Created)}", "small muted", wrap: true)));
            outs.Children.Add(Ui.Card(line, 12));
        }
        col.Children.Add(Card(I18n.T("adm.payouts"), I18n.T("adm.payouts.text"), outs));

        var uid = new TextBox { Watermark = "UID", Width = 300 };
        var sum = new TextBox { Watermark = "100", Width = 120 };
        var why = new TextBox { Watermark = I18n.T("v95.adm.grant.note"), Width = 260, MaxLength = 300 };
        col.Children.Add(Card(I18n.T("adm.grant"), I18n.T("adm.grant.text"),
            Ui.Row(8, uid, sum, Ui.Text(Market.Currency, "muted"), why, Ui.Button(I18n.T("adm.grant.go"), () =>
            {
                var v = Market.ParseMoney(sum.Text);
                if (v is null or 0) { MainWindow.Current?.Toast(I18n.T("mk.f.price.bad"), bad: true); return; }
                if (string.IsNullOrWhiteSpace(why.Text)) { MainWindow.Current?.Toast(I18n.T("v95.adm.grant.noteNeeded"), bad: true); return; }
                Call(() => Market.Grant((uid.Text ?? "").Trim(), v.Value, why.Text ?? ""), I18n.T("adm.done"));
            }, "primary", Icons.Plus)),
            Ui.Button(I18n.T("cr.refresh"), () => { Reset(); Build(); }, "ghost", Icons.Refresh)));
        return col;
    }
}
