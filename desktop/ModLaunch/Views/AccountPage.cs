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
    (List<TopUp> TopUps, List<Payout> Payouts, Wallet Platform, List<Refund> Refunds)? _admin;
    string? _error;

    public AccountPage(string tab = "profile") { _tab = tab == "selling" ? "earnings" : tab; }

    void Reset() { _earn = null; _purchases = null; _admin = null; _error = null; _tried.Clear(); }

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
            // 25.0: ModLaunch Premium — видно и без входа (купить можно после входа).
            ("premium", "v25.prem.tab", Icons.Sparkles),
            ("security", "acc.s.security", Icons.Lock),
            ("wallet", "acc.s.wallet", Icons.Bag),
            ("purchases", "acc.s.purchases", Icons.Download),
            ("earnings", "v20.acc.earnings", Icons.Chart),
            ("data", "acc.s.data", Icons.Folder),
        };
        // 14.0 → 20.0: комиссия площадки — доход владельца, место ей здесь, а не на рынке.
        if (Account.Owner && p.SignedIn) sections.Add(("commission", "v14.mk.tab.commission", Icons.Percent));
        if (p.Admin) sections.Add(("admin", "acc.s.admin", Icons.Shield));
        // 31.0: очередь жалоб, снятия (DMCA), блокировки и чёрный список файлов.
        if (p.Admin) sections.Add(("moderation", "v31.mod.tab", Icons.Flag));
        // 31.0: сбои программы у людей (отчёты с согласия).
        if (p.Admin) sections.Add(("crashes", "v31.crash.tab", Icons.Bug));
        // Снимки экрана: «Заработок» на примере, без входа.
        var demo = Program.Screenshot && _tab is "earnings" or "premium";
        if (!p.SignedIn && !demo && _tab != "premium") _tab = "profile";

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
            b.IsEnabled = p.SignedIn || id is "profile" or "premium" || demo;
            if (_tab == id) b.Classes.Add("active");
            if (id == "premium") b.Foreground = Ui.Hex("#F0B429");
            tabs.Children.Add(b);
        }

        Control body;
        if (_tab == "premium") body = new PremiumPanel();
        else if (demo) body = Earnings();
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
            "moderation" when p.Admin => new ModerationPanel(),
            "crashes" when p.Admin => new CrashesPanel(),
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
                Profile p;
                try { p = await Account.SignIn(email.Text ?? "", password.Text ?? ""); }
                // 30.0: включена двухфакторная защита — спросить код из приложения.
                catch (ServiceError e) when (e.Code == "MFA_REQUIRED") { if (await MfaUi.AskCode() is not { } second) return; p = second; }
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
        // 30.0: двухфакторная защита (код из приложения-аутентификатора).
        col.Children.Add(MfaUi.Card(p));
        // 31.0: устройства, с которых входили, и «это вы?» для новых.
        col.Children.Add(DevicesUi.Card());
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
                Ui.Button(I18n.T("st.open"), async () => { if (await Market.Get(item.ListingId) is { } l) MarketViews.Open(l); else MainWindow.Current?.Toast(I18n.T("err.market.NOT_FOUND"), bad: true); }, "ghost", Icons.Eye));
            // 30.0: возврат в течение 14 дней; деньги вернули — скачать уже нельзя.
            if (Market.RefundOpen(item)) buttons.Children.Insert(0, Ui.Button(I18n.T("v30.refund.ask"), () => AskRefund(item), "ghost", Icons.Undo));
            if (item.Refund?.Status != "done")
                buttons.Children.Add(Ui.Button(I18n.T("mk.download"), async () => { if (await Market.Get(item.ListingId) is { } l) MarketViews.Download(l); else MainWindow.Current?.Toast(I18n.T("err.market.NOT_FOUND"), bad: true); }, "primary", Icons.Download));
            DockPanel.SetDock(buttons, Dock.Right);
            line.Children.Add(buttons);
            var info = Ui.Col(2, Ui.Text(item.Title, "h3"),
                Ui.Text(I18n.T("mod.by", ("author", item.Seller)) + " · " + (item.Price == 0 ? I18n.T("mk.free") : Market.Money(item.Price)) + " · " + Ui.Ago(item.Created), "small muted"));
            if (RefundLine(item) is { } refund) info.Children.Add(refund);
            line.Children.Add(Ui.Row(12,
                new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(10), Background = Ui.Res("Surface3"), Child = Ui.Icon(MarketViews.KindIcon(item.Kind), 18) },
                info));
            col.Children.Add(Ui.Card(line, 12));
        }
        return Card(I18n.T("acc.s.purchases"), I18n.T("acc.purchases.text"), col);
    }

    /// <summary>Снимки экрана: «Покупки» с заявками на возврат во всех состояниях.</summary>
    internal void DemoPurchases()
    {
        if (!Program.Screenshot) return;
        var now = DateTime.UtcNow;
        Refund R(string lot, string status, string note = "") => new($"r_demo_{lot}", "demo-uid", "Player", lot, "", "", 0, "", status, note, now.AddDays(-1));
        _purchases =
        [
            new("lot-a", "Better Cockpit HUD", "model", "Nova", 24900, now.AddDays(-2)),
            new("lot-b", "Night Sky Pack", "asset", "Orion", 9900, now.AddDays(-5), R("lot-b", "pending")),
            new("lot-c", "Faster Cars Script", "script", "Kite", 14900, now.AddDays(-9), R("lot-c", "rejected", "v1.2")),
            new("lot-d", "Retro Shader", "code", "Pixel", 4900, now.AddDays(-12), R("lot-d", "done")),
            new("lot-e", "Free Map Marker", "script", "Mapper", 0, now.AddDays(-30)),
        ];
        _tried.Add("purchases");
        _error = null;
        Build();
    }

    internal Purchase? FirstPurchase => _purchases?.FirstOrDefault();

    /// <summary>Что с заявкой на возврат: ждёт, деньги вернули или отказали (с пояснением админа).</summary>
    static Control? RefundLine(Purchase p) => p.Refund switch
    {
        { Status: "pending" } => Ui.Text(I18n.T("v30.refund.pending"), "small", color: Ui.Res("Warn"), wrap: true),
        { Status: "done" } => Ui.Text("✓ " + I18n.T("v30.refund.done"), "small", color: Ui.Res("Good"), wrap: true),
        { Status: "rejected", Note: { Length: > 0 } note } => Ui.Text(I18n.T("v30.refund.rejected.note", ("note", note)), "small", color: Ui.Res("Bad"), wrap: true),
        { Status: "rejected" } => Ui.Text(I18n.T("v30.refund.rejected"), "small", color: Ui.Res("Bad"), wrap: true),
        _ => null,
    };

    /// <summary>Заявка на возврат: что не так — решение за админом.</summary>
    internal void AskRefund(Purchase p)
    {
        if (MainWindow.Current is not { } w) return;
        var reason = new TextBox { Watermark = I18n.T("v30.refund.reason"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 96, MaxLength = 500, VerticalContentAlignment = VerticalAlignment.Top };
        var until = Market.RefundUntil(p) is DateTime d ? I18n.T("v30.refund.until", ("date", d.ToLocalTime().ToString("g", I18n.Culture))) : "";
        w.Dialog(I18n.T("v30.refund.title"), Ui.Col(10,
                Ui.Text($"{p.Title} · {Market.Money(p.Price)}", "h3"),
                Ui.Text(I18n.T("v30.refund.text", ("days", Market.RefundDays)), "muted", wrap: true),
                Ui.Text(until, "small muted"),
                reason),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("v30.refund.send"), () =>
            {
                var text = (reason.Text ?? "").Trim();
                if (text.Length < Market.MinRefundReason) { w.Toast(I18n.T("err.market.REFUND_REASON"), bad: true); return; }
                w.CloseDialog();
                _purchases = null;
                _tried.Remove("purchases");
                Call(() => Market.RequestRefund(p, text), I18n.T("v30.refund.sent"));
            }, "primary", Icons.Undo));
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
            Load("admin", async () =>
            {
                // 30.0: заявки на возврат; пока их правил нет в базе — просто пустой список.
                List<Refund> refunds;
                try { refunds = await Market.PendingRefunds(); } catch (ServiceError) { refunds = []; }
                _admin = (await Market.PendingTopUps(), await Market.PendingPayouts(), await Market.PlatformWallet(), refunds);
            });
        var col = Ui.Col(16);
        if (_admin is not { } a) { col.Children.Add(Loading()); return col; }

        var stats = new UniformGrid { Columns = 4 };
        stats.Children.Add(Stat(I18n.T("adm.platform"), Market.Money(a.Platform.Balance), Ui.Res("Good")));
        stats.Children.Add(Stat(I18n.T("adm.topups"), a.TopUps.Count.ToString(I18n.Culture)));
        stats.Children.Add(Stat(I18n.T("adm.payouts"), a.Payouts.Count.ToString(I18n.Culture)));
        stats.Children.Add(Stat(I18n.T("v30.adm.refunds"), a.Refunds.Count.ToString(I18n.Culture)));
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

        var backs = Ui.Col(6);
        if (a.Refunds.Count == 0) backs.Children.Add(Ui.Text(I18n.T("adm.none"), "muted"));
        foreach (var r in a.Refunds)
        {
            var item = r;
            var note = new TextBox { Watermark = I18n.T("adm.note"), Width = 200, MaxLength = 300 };
            var line = new DockPanel();
            var buttons = Ui.Row(6, note,
                Ui.Button(I18n.T("v30.adm.refund.ok"), () => Call(() => Market.ResolveRefund(item, true, note.Text ?? ""), I18n.T("adm.done")), "primary", Icons.Undo),
                Ui.Button(I18n.T("adm.reject"), () => Call(() => Market.ResolveRefund(item, false, note.Text ?? ""), I18n.T("adm.done")), "ghost", Icons.Close));
            DockPanel.SetDock(buttons, Dock.Right);
            line.Children.Add(buttons);
            line.Children.Add(Ui.Col(2, Ui.Text($"{item.Author} · {item.Title} · {Market.Money(item.Price)}", "h3"),
                Ui.Text("«" + item.Reason + "»", "small", wrap: true),
                Ui.Text($"{item.Uid} · {Ui.Ago(item.Created)}", "small muted", wrap: true)));
            backs.Children.Add(Ui.Card(line, 12));
        }
        col.Children.Add(Card(I18n.T("v30.adm.refunds"), I18n.T("v30.adm.refunds.text"), backs));

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
