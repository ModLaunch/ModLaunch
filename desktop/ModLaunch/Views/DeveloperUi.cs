using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Social;

namespace ModLaunch.Views;

/// <summary>
/// 31.0: цены для оплаты картой по регионам (BYN, RUB, USD, EUR) и скидка со сроком — в форме выставления игры
/// и лота. Пустое поле — в этой валюте не продаётся. Итоговую цену платежа всё равно считает сервер.
/// </summary>
public sealed class CardPricesEditor
{
    readonly Dictionary<string, TextBox> _boxes = [];
    readonly TextBox _percent;
    readonly TextBox _days;
    public Control View { get; }

    public CardPricesEditor(Listing? l)
    {
        var grid = new WrapPanel();
        foreach (var c in Store.Currencies)
        {
            var box = new TextBox
            {
                Width = 120, Watermark = c,
                Text = l?.Prices is { } p && p.TryGetValue(c, out var v) ? (v / 100m).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : "",
            };
            _boxes[c] = box;
            var cell = Ui.Row(6, box, Ui.Text(Store.Symbol(c), "muted"));
            cell.Margin = new Thickness(0, 0, 14, 8);
            grid.Children.Add(cell);
        }
        var sale = Store.SaleActive(l ?? Empty);
        _percent = new TextBox { Width = 80, Watermark = "0", Text = sale ? l!.SalePercent.ToString(I18n.Culture) : "" };
        _days = new TextBox { Width = 80, Watermark = "7", Text = sale && l!.SaleUntil is { } u ? Math.Max(1, (int)Math.Ceiling((u - DateTime.UtcNow).TotalDays)).ToString(I18n.Culture) : "" };
        View = Ui.Col(8, grid,
            Ui.Row(8, Ui.Text(I18n.T("v31.seller.sale"), "small"), _percent, Ui.Text("%", "muted"), Ui.Text(I18n.T("v31.seller.sale.days"), "small"), _days),
            Ui.Text(I18n.T("v31.seller.prices.hint"), "small muted", wrap: true));
    }

    static readonly Listing Empty = new("", "", "", "", "", "", "game", "", 0, "personal", "1.0.0", "", [], [], 0, "", 0, "", "hidden", 0, null, null);

    /// <summary>Прочитать: цены (копейки), скидка и её срок; ошибка — текст для человека.</summary>
    public (Dictionary<string, long>? Prices, int Percent, DateTime? Until, string? Error) Read()
    {
        var prices = new Dictionary<string, long>();
        foreach (var (c, box) in _boxes)
        {
            var t = (box.Text ?? "").Trim();
            if (t == "") continue;
            if (!decimal.TryParse(t.Replace(',', '.'), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var v) || v <= 0 || v > 1_000_000)
                return (null, 0, null, I18n.T("v31.seller.prices.bad", ("currency", c)));
            prices[c] = (long)Math.Round(v * 100);
        }
        var pct = 0;
        DateTime? until = null;
        if ((_percent.Text ?? "").Trim() is { Length: > 0 } ps)
        {
            if (!int.TryParse(ps, out pct) || pct is < 0 or > 90) return (null, 0, null, I18n.T("v31.seller.sale.bad"));
            var days = int.TryParse((_days.Text ?? "").Trim(), out var dd) ? dd : 7;
            if (days is < 1 or > 60) return (null, 0, null, I18n.T("v31.seller.sale.bad"));
            if (pct > 0) until = DateTime.UtcNow.AddDays(days);
        }
        return (prices.Count == 0 ? null : prices, pct, until, null);
    }
}

/// <summary>
/// 31.0: кабинет разработчика — заработок с оплат картой по валютам, продажи по дням, заявка на выплату и ключи для
/// раздачи. Деньги принимает ООО (bePaid/WEBPAY) и переводит разработчику по заявке за вычетом комиссии площадки.
/// </summary>
public static class DeveloperUi
{
    public static Control EarningsCard()
    {
        var body = Ui.Col(8, Ui.Text(I18n.T("common.loading"), "muted"));
        var card = Ui.Card(Ui.Col(10, Ui.Text(I18n.T("v31.seller.earnings"), "h3"), Ui.Text(I18n.T("v31.seller.earnings.text"), "small muted", wrap: true), body), 22);
        _ = Task.Run(async () =>
        {
            List<EarningTotal>? totals = null;
            List<EarningEntry> entries = [];
            string? error = null;
            try { totals = await Store.Earnings(); entries = await Store.EarningEntries(); }
            catch (Exception e) { error = Store.Explain(e); }
            Dispatcher.UIThread.Post(() => Fill(body, totals, entries, error));
        });
        return card;
    }

    static void Fill(StackPanel body, List<EarningTotal>? totals, List<EarningEntry> entries, string? error)
    {
        body.Children.Clear();
        if (error is not null) { body.Children.Add(Ui.Text(error, "small muted", wrap: true)); return; }
        if (totals is null || totals.Count == 0) { body.Children.Add(Ui.Text(I18n.T("v31.seller.earnings.none"), "muted", wrap: true)); return; }
        foreach (var t in totals)
        {
            var total = t;
            var line = new DockPanel();
            var payout = Ui.Button(I18n.T("v31.seller.payout"), () => Payout(total), "primary", Icons.Upload);
            payout.IsEnabled = total.Available > 0;
            DockPanel.SetDock(payout, Dock.Right);
            line.Children.Add(payout);
            line.Children.Add(Ui.Col(2,
                new TextBlock { Text = Store.Money(total.Available, total.Currency), FontFamily = Look.Display, FontSize = 22, FontWeight = FontWeight.Bold },
                Ui.Text(I18n.T("v31.seller.earnings.line", ("gross", Store.Money(total.Gross, total.Currency)), ("fee", Store.Money(total.Fee, total.Currency)),
                    ("paid", Store.Money(total.Paid, total.Currency)), ("pending", Store.Money(total.Pending, total.Currency))), "small muted", wrap: true)));
            body.Children.Add(new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10), Child = line });
        }
        // Продажи по дням (последние 30 дней) — по каждой валюте.
        var days = entries.Where(e => !e.Refunded && e.At is { } at && at > DateTime.UtcNow.AddDays(-30))
            .GroupBy(e => (Day: e.At!.Value.ToLocalTime().Date, e.Currency)).OrderByDescending(g => g.Key.Day).Take(30).ToList();
        if (days.Count > 0)
        {
            body.Children.Add(Ui.Text(I18n.T("v31.seller.byDay"), "small"));
            foreach (var g in days)
                body.Children.Add(Ui.Text(I18n.T("v31.seller.dayLine", ("day", g.Key.Day.ToString("d MMM", I18n.Culture)), ("n", g.Count()),
                    ("net", Store.Money(g.Sum(e => e.Net), g.Key.Currency)), ("tips", g.Count(e => e.Type == "tip"))), "small muted"));
        }
    }

    static void Payout(EarningTotal t)
    {
        var w = MainWindow.Current;
        if (w is null) return;
        var sum = new TextBox { Width = 140, Text = (t.Available / 100m).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) };
        var details = new TextBox { Watermark = I18n.T("v31.seller.payout.details"), MaxLength = 300, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60 };
        w.Dialog(I18n.T("v31.seller.payout.title", ("currency", t.Currency)), Ui.Col(10,
                Ui.Text(I18n.T("v31.seller.payout.text", ("available", Store.Money(t.Available, t.Currency))), "muted", wrap: true),
                Ui.Row(8, sum, Ui.Text(Store.Symbol(t.Currency), "muted")), details),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("v31.seller.payout.send"), async () =>
            {
                if (!decimal.TryParse((sum.Text ?? "").Replace(',', '.'), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var v) || v <= 0)
                { w.Toast(I18n.T("mk.f.price.bad"), bad: true); return; }
                try
                {
                    await Store.RequestPayout(t.Currency, (long)Math.Round(v * 100), details.Text ?? "");
                    w.CloseDialog();
                    w.Toast(I18n.T("v31.seller.payout.done"));
                }
                catch (Exception e) { w.Toast(Store.Explain(e), bad: true); }
            }, "primary", Icons.Upload));
    }

    /// <summary>Ключи для раздачи (пресса, блогеры, конкурсы): видны один раз — сохраняются в файл.</summary>
    public static void Keys(Listing l)
    {
        var w = MainWindow.Current;
        if (w is null) return;
        var count = new TextBox { Width = 100, Text = "10" };
        w.Dialog(I18n.T("v31.seller.keys.title", ("title", l.Title)), Ui.Col(10, Ui.Text(I18n.T("v31.seller.keys.text"), "muted", wrap: true), Ui.Row(8, count, Ui.Text(I18n.T("v31.seller.keys.count"), "muted"))),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("v31.seller.keys.go"), async () =>
            {
                if (!int.TryParse(count.Text, out var n) || n is < 1 or > 500) { w.Toast(I18n.T("v31.seller.keys.bad"), bad: true); return; }
                try
                {
                    var codes = await Store.GenerateKeys(l.Id, n);
                    var dir = Directory.CreateDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ModLaunch", "Keys")).FullName;
                    var file = Path.Combine(dir, $"{Projects.PackageName(l.Title)}-keys-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
                    await File.WriteAllLinesAsync(file, codes);
                    w.CloseDialog();
                    w.Toast(I18n.T("v31.seller.keys.done", ("n", codes.Count)));
                    Ui.OpenUrl(file);
                }
                catch (Exception e) { w.Toast(Store.Explain(e), bad: true); }
            }, "primary", Icons.Key));
    }
}
