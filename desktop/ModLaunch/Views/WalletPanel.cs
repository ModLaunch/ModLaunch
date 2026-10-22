using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Social;

namespace ModLaunch.Views;

/// <summary>
/// 9.5: кошелёк Creator Hub. Сверху — баланс и две кнопки (пополнить, вывести) и итоги;
/// при открытии выручка за новые продажи переносится в кошелёк сама. Ниже — заявки «в работе»
/// и выписка: каждое движение денег с основанием и остатком после него, как в банке.
/// Внизу — коротко, как устроен кошелёк и почему деньги не могут «потеряться».
/// </summary>
public sealed class WalletPanel : UserControl
{
    public sealed record Data(Wallet Wallet, List<LedgerEntry> Ledger, List<Payout> Payouts, List<TopUp> TopUps, int Claimed, long ClaimedAmount);

    static Data? _data;
    static string? _error;
    static bool _loading;

    /// <summary>Для снимков экрана: кошелёк без сервера.</summary>
    public static void Demo(Data d) { _data = d; _error = null; }

    public static void Reset() { _data = null; _error = null; }

    public WalletPanel() => Render();

    void Reload() { Reset(); MarketData.Reload(); Render(); }

    void Load()
    {
        if (_loading || Program.Screenshot) return;
        _loading = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var (count, amount) = (0, 0L);
                try { (count, amount) = await Market.ClaimSales(); } catch (ServiceError) { }
                var wallet = await Market.MyWallet();
                var ledger = await Market.MyLedger();
                List<Payout> payouts = [];
                List<TopUp> topups = [];
                try { payouts = await Market.MyPayouts(); } catch (ServiceError) { }
                try { topups = await Market.MyTopUps(); } catch (ServiceError) { }
                _data = new Data(wallet, ledger, payouts, topups, count, amount);
            }
            catch (Exception e) { _error = Market.Explain(e); }
            _loading = false;
            Dispatcher.UIThread.Post(Render);
        });
    }

    void Render()
    {
        var col = Ui.Col(StoreKit.Gap);
        Content = col;
        if (!Social.Account.SignedIn && _data is null)
        {
            col.Children.Add(Ui.Card(Ui.Col(10, Ui.Icon(Icons.Bag, 26, Ui.Res("Brand2")), Ui.Text(I18n.T("v95.w.signin"), "h3"), Ui.Text(I18n.T("v95.w.signin.text"), "muted", wrap: true),
                Ui.Button(I18n.T("aside.login"), () => MainWindow.Current?.Navigate(() => new AccountPage()), "primary", Icons.User)), 24));
            return;
        }
        if (_data is null && _error is null) Load();
        if (_error is not null)
        {
            col.Children.Add(Ui.Card(Ui.Col(10, Ui.Text(_error, "muted", wrap: true), Ui.Button(I18n.T("cr.refresh"), Reload, "", Icons.Refresh)), 22));
            return;
        }
        if (_data is not { } d)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("5*,7*"), ColumnSpacing = StoreKit.Gap, Height = 210 };
            var a = StoreKit.Placeholder(double.NaN, double.NaN);
            var b = StoreKit.Placeholder(double.NaN, double.NaN);
            Grid.SetColumn(b, 1);
            row.Children.Add(a);
            row.Children.Add(b);
            col.Children.Add(row);
            col.Children.Add(StoreKit.Placeholder(double.NaN, 260));
            return;
        }

        // ---- баланс и итоги
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("5*,7*"), ColumnSpacing = StoreKit.Gap };
        top.Children.Add(BalanceCard(d));
        var totals = new UniformGrid { Columns = 2, Rows = 2 };
        totals.Children.Add(Total(Icons.Chart, I18n.T("st.earned"), d.Wallet.Earned, Ui.Res("Good"), new Thickness(0, 0, StoreKit.Gap / 2, StoreKit.Gap / 2)));
        totals.Children.Add(Total(Icons.Bag, I18n.T("acc.spent"), d.Wallet.Spent, null, new Thickness(StoreKit.Gap / 2, 0, 0, StoreKit.Gap / 2)));
        totals.Children.Add(Total(Icons.Upload, I18n.T("acc.paidOut"), d.Wallet.PaidOut, null, new Thickness(0, StoreKit.Gap / 2, StoreKit.Gap / 2, 0)));
        totals.Children.Add(Total(Icons.Plus, I18n.T("v95.w.toppedUp"), d.Wallet.ToppedUp, null, new Thickness(StoreKit.Gap / 2, StoreKit.Gap / 2, 0, 0)));
        Grid.SetColumn(totals, 1);
        top.Children.Add(totals);
        col.Children.Add(top);

        if (d.Claimed > 0)
            col.Children.Add(Banner(Icons.Check, Ui.Res("Good"), I18n.T("v95.w.claimed", ("n", d.Claimed), ("sum", Market.Money(d.ClaimedAmount)))));

        // ---- заявки в работе
        var pending = d.TopUps.Where(t => t.Status == "pending").Select(t => (t.Created, Icons.Plus, I18n.T("acc.h.topup"), "+" + Market.Money(t.Amount), t.Reference))
            .Concat(d.Payouts.Where(p => p.Status == "pending").Select(p => (p.Created, Icons.Upload, I18n.T("acc.h.payout"), "−" + Market.Money(p.Amount), p.Method)))
            .OrderByDescending(x => x.Created).ToList();
        if (pending.Count > 0)
        {
            var list = Ui.Col(4);
            foreach (var (when, icon, what, sum, note) in pending)
                list.Children.Add(Line(icon, Ui.Res("Warn"), what, Ui.Ago(when).Trim() + (note != "" ? " · " + note : ""), sum, I18n.T("acc.status.pending"), Ui.Res("Warn")));
            col.Children.Add(Section(I18n.T("v95.w.pending"), I18n.T("v95.w.pending.text"), list));
        }

        // ---- выписка
        var rows = Ui.Col(2);
        if (d.Ledger.Count == 0) rows.Children.Add(Ui.Text(I18n.T("v95.w.empty"), "muted", wrap: true));
        foreach (var e in d.Ledger)
        {
            var (icon, tint, title, sub) = Describe(e);
            var sign = e.Amount > 0 ? "+" : "−";
            rows.Children.Add(Line(icon, tint, title, (e.Created is { } c ? c.ToLocalTime().ToString("d MMM yyyy, HH:mm", I18n.Culture) : "") + (sub != "" ? " · " + sub : ""),
                sign + Market.Money(Math.Abs(e.Amount)), I18n.T("v95.w.after", ("sum", Market.Money(e.Balance))), e.Amount > 0 ? Ui.Res("Good") : Ui.Res("Text")));
        }
        Control? check = null;
        if (d.Ledger.Count > 0)
        {
            // Каждая запись = прошлый остаток + сумма, последняя сходится с балансом.
            var ok = Ledger.Consistent(d.Ledger, new WalletState(d.Wallet.Balance, d.Wallet.Earned, d.Wallet.Spent, d.Wallet.PaidOut, d.Wallet.ToppedUp, d.Wallet.Seq));
            check = Ui.Row(6, Ui.Icon(ok ? Icons.Shield : Icons.Alert, 14, ok ? Ui.Res("Good") : Ui.Res("Bad")),
                new TextBlock { Text = I18n.T(ok ? "v95.w.check.ok" : "v95.w.check.bad"), FontSize = 12, Foreground = ok ? Ui.Res("Good") : Ui.Res("Bad"), VerticalAlignment = VerticalAlignment.Center });
        }
        var refresh = Ui.Button("", Reload, "icon ghost", Icons.Refresh, I18n.T("cr.refresh"));
        col.Children.Add(Section(I18n.T("v95.w.ledger"), I18n.T("v95.w.ledger.text"), rows, check is null ? refresh : Ui.Row(8, check, refresh)));

        // ---- как устроено
        var how = Ui.Col(8);
        foreach (var key in new[] { "v95.w.how.1", "v95.w.how.2", "v95.w.how.3" })
            how.Children.Add(Ui.Row(10, Ui.Icon(Icons.Check, 14, Ui.Res("Brand2")), Ui.Text(I18n.T(key, ("fee", Market.FeePercent)), "small muted", wrap: true)));
        col.Children.Add(Section(I18n.T("v95.w.how"), null, how));
    }

    static Control Section(string title, string? sub, Control body, Control? right = null)
    {
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        if (right is not null) { right.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(right, Dock.Right); head.Children.Add(right); }
        var words = Ui.Col(2, Gx.Eyebrow(title, Ui.Res("Text"), 12));
        if (sub is not null) words.Children.Add(Ui.Text(sub, "small muted", wrap: true));
        head.Children.Add(words);
        return new Border { Classes = { "card" }, Padding = new Thickness(18, 16), Child = Ui.Col(0, head, body) };
    }

    static Control Banner(string icon, IBrush tint, string text) => new Border
    {
        Classes = { "card" }, Padding = new Thickness(16, 12), BorderBrush = tint, BorderThickness = new Thickness(1),
        Child = Ui.Row(10, Ui.Icon(icon, 18, tint), new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center }),
    };

    Control BalanceCard(Data d)
    {
        var accent = Gx.Accent;
        var back = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Look.Mix(accent, Colors.Black, 0.1), 0), new GradientStop(Look.Mix(accent, Color.Parse("#E83E8C"), 0.55), 1) },
            },
        };
        var amount = new TextBlock { Text = Market.Money(d.Wallet.Balance), FontFamily = Gx.Display, FontSize = 38, FontWeight = FontWeight.Bold, Foreground = Brushes.White, LetterSpacing = -1 };
        var topUp = Ui.Button(I18n.T("v95.w.topup"), () => TopUpDialog(Reload), "hero-ghost", Icons.Plus);
        var payout = Ui.Button(I18n.T("v95.w.payout"), () => PayoutDialog(d.Wallet.Balance, Reload), "hero-ghost", Icons.Upload);
        payout.IsEnabled = d.Wallet.Balance >= Market.MinPayout;
        if (!payout.IsEnabled) ToolTip.SetTip(payout, I18n.T("v95.w.payout.min", ("min", Market.Money(Market.MinPayout))));
        var body = Ui.Col(8,
            Gx.Eyebrow(I18n.T("st.balance"), Ui.Hex("#F1EEFF"), 11),
            amount,
            new TextBlock { Text = I18n.T("v95.w.balance.text"), FontSize = 12.5, Foreground = Ui.Hex("#EDE9FF"), TextWrapping = TextWrapping.Wrap },
            Ui.Row(8, topUp, payout));
        body.Margin = new Thickness(22, 18);
        body.VerticalAlignment = VerticalAlignment.Center;
        return new Border { Classes = { "card" }, Padding = new Thickness(0), ClipToBounds = true, MinHeight = 200, Child = new Panel { Children = { back, Gx.Scanlines(), body } } };
    }

    static Control Total(string icon, string label, long value, IBrush? color, Thickness margin)
    {
        var head = Ui.Row(8, Ui.Icon(icon, 15, Ui.Res("Brand2")), new TextBlock { Text = label.ToUpper(I18n.Culture), FontSize = 10.5, FontWeight = FontWeight.Bold, LetterSpacing = 1.1, Foreground = Ui.Res("Muted"), VerticalAlignment = VerticalAlignment.Center });
        var num = new TextBlock { Text = Market.Money(value), FontFamily = Gx.Display, FontSize = 22, FontWeight = FontWeight.Bold, Foreground = color ?? Ui.Res("Text") };
        return new Border { Classes = { "card" }, Padding = new Thickness(16, 14), Margin = margin, Child = Ui.Col(8, head, num) };
    }

    /// <summary>Строка выписки: значок, что и когда, сумма и остаток.</summary>
    static Control Line(string icon, IBrush tint, string title, string sub, string sum, string after, IBrush sumColor)
    {
        var badge = new Border
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(11), VerticalAlignment = VerticalAlignment.Center,
            Background = tint is ISolidColorBrush s ? new SolidColorBrush(Color.FromArgb(40, s.Color.R, s.Color.G, s.Color.B)) : Ui.Res("BrandSoft"),
            Child = Ui.Icon(icon, 16, tint),
        };
        var words = Ui.Col(1,
            new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = sub, FontSize = 11.5, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis });
        words.VerticalAlignment = VerticalAlignment.Center;
        var money = Ui.Col(1,
            new TextBlock { Text = sum, FontWeight = FontWeight.Bold, FontSize = 14, Foreground = sumColor, HorizontalAlignment = HorizontalAlignment.Right },
            new TextBlock { Text = after, FontSize = 11.5, Foreground = Ui.Res("Faint"), HorizontalAlignment = HorizontalAlignment.Right });
        money.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
        grid.Children.Add(badge);
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);
        Grid.SetColumn(money, 2);
        grid.Children.Add(money);
        return new Border { Classes = { "store-row" }, Padding = new Thickness(8, 7), CornerRadius = new CornerRadius(10), Child = grid };
    }

    /// <summary>Что за запись: значок, цвет, название и подпись.</summary>
    static (string Icon, IBrush Tint, string Title, string Sub) Describe(LedgerEntry e)
    {
        string ListingTitle() => MarketData.All.FirstOrDefault(l => l.Id == e.Ref)?.Title ?? (e.Note != "" ? e.Note : e.Ref);
        return e.Kind switch
        {
            "purchase" => (Icons.Bag, Ui.Res("Brand2"), I18n.T("v95.w.k.purchase", ("title", e.Note != "" ? e.Note : ListingTitle())), ""),
            "sale" => (Icons.Chart, Ui.Res("Good"), I18n.T("v95.w.k.sale", ("title", ListingTitle())), I18n.T("v95.w.k.sale.sub", ("fee", Market.FeePercent))),
            "payout" => (Icons.Upload, Ui.Res("Warn"), I18n.T("v95.w.k.payout"), ""),
            "payout-return" => (Icons.Undo, Ui.Res("Good"), I18n.T("v95.w.k.return"), e.Note),
            "topup" => (Icons.Plus, Ui.Res("Good"), I18n.T("v95.w.k.topup"), ""),
            "fee" => (Icons.Percent, Ui.Res("Good"), I18n.T("v14.w.k.fee", ("title", e.Note != "" ? e.Note : ListingTitle())), I18n.T("v14.w.k.fee.sub")),
            "grant" => (Icons.Sparkles, Ui.Res("Brand2"), I18n.T("v95.w.k.grant"), e.Note),
            _ => (Icons.Info, Ui.Res("Muted"), e.Kind, e.Note),
        };
    }

    // ---------------------------------------------------------------- пополнить и вывести

    static void TopUpDialog(Action done)
    {
        var w = MainWindow.Current;
        if (w is null) return;
        var uid = Social.Account.Uid ?? "";
        var amount = new TextBox { Watermark = "500", Width = 160 };
        var presets = Ui.Row(6);
        foreach (var rub in new[] { 100, 300, 500, 1000 })
        {
            var v = rub;
            var chip = Ui.Button($"{v} {Market.Currency}", () => amount.Text = v.ToString(I18n.Culture), "chip");
            chip.Padding = new Thickness(10, 4);
            presets.Children.Add(chip);
        }
        var reference = new TextBox { Watermark = I18n.T("acc.topup.ref"), MaxLength = 300 };
        var steps = Ui.Col(12,
            Ui.Text(I18n.T("acc.topup.text"), "muted", wrap: true),
            Ui.Row(8, Ui.Text(I18n.T("acc.topup.id"), "small muted"), new SelectableTextBlock { Text = uid, FontSize = 12, VerticalAlignment = VerticalAlignment.Center },
                Ui.Button("", async () => { if (TopLevel.GetTopLevel(w)?.Clipboard is { } c) { await c.SetTextAsync(uid); w.Toast(I18n.T("acc.copied")); } }, "icon ghost", Icons.Copy, I18n.T("acc.copy"))));
        if (Market.TopUpUrl != "") steps.Children.Add(Ui.Button(I18n.T("acc.topup.pay"), () => Ui.OpenUrl(Market.TopUpUrl.Replace("{uid}", uid)), "", Icons.External));
        steps.Children.Add(Ui.Col(6, Ui.Text(I18n.T("v95.w.amount"), "small muted"), Ui.Row(8, amount, Ui.Text(Market.Currency, "muted")), presets));
        steps.Children.Add(Ui.Col(6, Ui.Text(I18n.T("acc.topup.ref"), "small muted"), reference));
        var send = Ui.Button(I18n.T("acc.topup.send"), async () =>
        {
            var a = Market.ParseMoney(amount.Text);
            if (a is null or 0) { w.Toast(I18n.T("mk.f.price.bad"), bad: true); return; }
            if (string.IsNullOrWhiteSpace(reference.Text)) { w.Toast(I18n.T("v95.w.ref.need"), bad: true); return; }
            try { await Market.RequestTopUp(a.Value, reference.Text ?? ""); w.CloseDialog(); w.Toast(I18n.T("acc.topup.sent")); done(); }
            catch (Exception e) { w.Toast(Market.Explain(e), bad: true); }
        }, "primary", Icons.Check);
        w.Dialog(I18n.T("acc.wallet.topup"), steps, 520, Ui.Button(I18n.T("common.cancel"), w.CloseDialog), send);
    }

    static void PayoutDialog(long balance, Action done)
    {
        var w = MainWindow.Current;
        if (w is null) return;
        var amount = new TextBox { Watermark = (Market.MinPayout / 100).ToString(I18n.Culture), Width = 160 };
        var all = Ui.Button(I18n.T("acc.payout.all"), () => amount.Text = (balance / 100m).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), "chip");
        all.Padding = new Thickness(10, 4);
        var method = new TextBox { Watermark = I18n.T("acc.payout.method"), MaxLength = 300 };
        var body = Ui.Col(12,
            Ui.Text(I18n.T("acc.payout.text", ("min", Market.Money(Market.MinPayout))), "muted", wrap: true),
            Ui.Text(I18n.T("v95.w.available", ("sum", Market.Money(balance))), "h3"),
            Ui.Col(6, Ui.Text(I18n.T("v95.w.amount"), "small muted"), Ui.Row(8, amount, Ui.Text(Market.Currency, "muted"), all)),
            Ui.Col(6, Ui.Text(I18n.T("acc.payout.method"), "small muted"), method));
        var send = Ui.Button(I18n.T("acc.payout.send"), async () =>
        {
            var a = Market.ParseMoney(amount.Text);
            if (a is null or 0) { w.Toast(I18n.T("mk.f.price.bad"), bad: true); return; }
            try { await Market.RequestPayout(a.Value, method.Text ?? ""); w.CloseDialog(); w.Toast(I18n.T("acc.payout.sent")); done(); }
            catch (Exception e) { w.Toast(Market.Explain(e), bad: true); }
        }, "primary", Icons.Upload);
        w.Dialog(I18n.T("acc.payout"), body, 520, Ui.Button(I18n.T("common.cancel"), w.CloseDialog), send);
    }
}
