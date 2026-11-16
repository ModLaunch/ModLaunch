using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Social;

namespace ModLaunch.Views;

/// <summary>
/// 31.0: «Магазин» в аккаунте админа: магазин открыт или закрыт, комиссия площадки (0–29 %), цены премиума по валютам,
/// реквизиты ООО для чеков, минимальная выплата; заявки разработчиков на выплату; возврат денег по номеру платежа.
/// </summary>
public sealed class StoreAdminPanel : UserControl
{
    Store.StoreConfig? _cfg;
    List<(string Id, string Uid, string Currency, long Amount, string Details, DateTime? Created)>? _payouts;
    string? _error;
    bool _busy;

    public StoreAdminPanel()
    {
        Render();
        _ = Load();
    }

    async Task Load()
    {
        try { _cfg = await Store.Config(force: true); _payouts = await Store.PendingPayouts(); _error = null; }
        catch (Exception e) { _error = Store.Explain(e); }
        Dispatcher.UIThread.Post(Render);
    }

    void Act(Func<Task> work, string done)
    {
        if (_busy) return;
        _busy = true;
        _ = Task.Run(async () =>
        {
            string? fail = null;
            try { await work(); } catch (Exception e) { fail = Store.Explain(e); }
            Dispatcher.UIThread.Post(() => { _busy = false; MainWindow.Current?.Toast(fail ?? done, bad: fail is not null); _ = Load(); });
        });
    }

    static string Major(long minor) => minor <= 0 ? "" : (minor / 100m).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    static long Minor(string? text) => decimal.TryParse((text ?? "").Replace(',', '.'), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0 ? (long)Math.Round(v * 100) : 0;

    void Render()
    {
        var col = Ui.Col(16);
        if (!Account.Get().Admin) { col.Children.Add(Ui.Card(Ui.Text(I18n.T("v31.mod.adminOnly"), "muted"), 22)); Content = col; return; }
        if (_error is not null) col.Children.Add(Ui.Card(Ui.Text(_error, "small muted", wrap: true), 18));
        if (_cfg is not { } cfg) { col.Children.Add(Ui.Card(Ui.Text(I18n.T("common.loading"), "muted"), 22)); Content = col; return; }

        // ---- настройки
        var enabled = new CheckBox { Content = I18n.T("v31.adm.store.enabled"), IsChecked = cfg.Enabled };
        var fee = new TextBox { Width = 80, Text = cfg.Fee.ToString(I18n.Culture) };
        var premium = new Dictionary<string, (TextBox Month, TextBox Year)>();
        var minPayout = new Dictionary<string, TextBox>();
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("70,130,130,130"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"), ColumnSpacing = 10, RowSpacing = 6 };
        void Cell(Control c, int row, int column) { Grid.SetRow(c, row); Grid.SetColumn(c, column); grid.Children.Add(c); }
        Cell(Ui.Text("", "small"), 0, 0);
        Cell(Ui.Text(I18n.T("v31.adm.store.month"), "small muted"), 0, 1);
        Cell(Ui.Text(I18n.T("v31.adm.store.year"), "small muted"), 0, 2);
        Cell(Ui.Text(I18n.T("v31.adm.store.minPayout"), "small muted"), 0, 3);
        var r = 1;
        foreach (var c in Store.Currencies)
        {
            var (m, y) = cfg.Premium.TryGetValue(c, out var p) ? p : (0L, 0L);
            var month = new TextBox { Text = Major(m), Watermark = "0" };
            var year = new TextBox { Text = Major(y), Watermark = "0" };
            var min = new TextBox { Text = Major(cfg.MinPayout.GetValueOrDefault(c)), Watermark = "0" };
            premium[c] = (month, year);
            minPayout[c] = min;
            Cell(Ui.Text(c, "h3"), r, 0);
            Cell(month, r, 1);
            Cell(year, r, 2);
            Cell(min, r, 3);
            r++;
        }
        var company = new TextBox { Text = cfg.Company, Watermark = I18n.T("v31.adm.store.company"), Width = 360 };
        var unp = new TextBox { Text = cfg.Unp, Watermark = I18n.T("v31.store.receipt.unp"), Width = 160 };
        var address = new TextBox { Text = cfg.Address, Watermark = I18n.T("v31.adm.store.address"), Width = 520 };
        var email = new TextBox { Text = cfg.Email, Watermark = "support@modlaunchapp.com", Width = 300 };
        var save = Ui.Button(I18n.T("v31.adm.store.save"), () =>
        {
            if (!int.TryParse(fee.Text, out var f) || f is < 0 or > Market.MaxFeePercent) { MainWindow.Current?.Toast(I18n.T("v31.adm.store.feeBad"), bad: true); return; }
            var next = new Store.StoreConfig(enabled.IsChecked == true, f,
                premium.Where(kv => Minor(kv.Value.Month.Text) > 0 || Minor(kv.Value.Year.Text) > 0).ToDictionary(kv => kv.Key, kv => (Minor(kv.Value.Month.Text), Minor(kv.Value.Year.Text))),
                company.Text ?? "", unp.Text ?? "", address.Text ?? "", email.Text ?? "",
                minPayout.Where(kv => Minor(kv.Value.Text) > 0).ToDictionary(kv => kv.Key, kv => Minor(kv.Value.Text)));
            Act(() => Store.SaveConfig(next), I18n.T("adm.done"));
        }, "primary", Icons.Save);
        col.Children.Add(Ui.Card(Ui.Col(10,
            Ui.Text(I18n.T("v31.adm.store.title"), "h3"),
            Ui.Text(I18n.T("v31.adm.store.text"), "small muted", wrap: true),
            enabled,
            Ui.Row(8, Ui.Text(I18n.T("v31.adm.store.fee"), "small"), fee, Ui.Text("% (0–29)", "muted")),
            Ui.Text(I18n.T("v31.adm.store.premium"), "small"), grid,
            Ui.Text(I18n.T("v31.adm.store.requisites"), "small"), Ui.Row(8, company, unp), address, email,
            save), 22));

        // ---- выплаты разработчикам
        var outs = Ui.Col(6);
        if (_payouts is not { Count: > 0 }) outs.Children.Add(Ui.Text(I18n.T("adm.none"), "muted"));
        foreach (var p in _payouts ?? [])
        {
            var item = p;
            var note = new TextBox { Watermark = I18n.T("adm.note"), Width = 200, MaxLength = 300 };
            var line = new DockPanel();
            var buttons = Ui.Row(6, note,
                Ui.Button(I18n.T("adm.paid"), () => Act(() => Store.ResolvePayout(item.Id, true, note.Text ?? ""), I18n.T("adm.done")), "primary", Icons.Check),
                Ui.Button(I18n.T("adm.reject"), () => Act(() => Store.ResolvePayout(item.Id, false, note.Text ?? ""), I18n.T("adm.done")), "ghost", Icons.Close));
            DockPanel.SetDock(buttons, Dock.Right);
            line.Children.Add(buttons);
            line.Children.Add(Ui.Col(2, Ui.Text(Store.Money(item.Amount, item.Currency), "h3"),
                new SelectableTextBlock { Text = $"{item.Details}\n{item.Uid} · {Ui.Ago(item.Created)}", TextWrapping = Avalonia.Media.TextWrapping.Wrap, Foreground = Ui.Res("Muted"), FontSize = 12 }));
            outs.Children.Add(Ui.Card(line, 12));
        }
        col.Children.Add(Ui.Card(Ui.Col(10, Ui.Text(I18n.T("v31.adm.payouts"), "h3"), Ui.Text(I18n.T("v31.adm.payouts.text"), "small muted", wrap: true), outs), 22));

        // ---- возврат по номеру платежа
        var pid = new TextBox { Watermark = "p_1794757111015_fe4bfac09cec", Width = 320 };
        var reason = new TextBox { Watermark = I18n.T("v31.adm.refund.reason"), Width = 300, MaxLength = 300 };
        var refundUid = new TextBox { Watermark = I18n.T("v31.adm.refund.webpay"), Width = 260 };
        col.Children.Add(Ui.Card(Ui.Col(10, Ui.Text(I18n.T("v31.adm.refund"), "h3"), Ui.Text(I18n.T("v31.adm.refund.text"), "small muted", wrap: true),
            Ui.Row(8, pid, reason), Ui.Row(8, refundUid, Ui.Button(I18n.T("v30.adm.refund.ok"), () =>
            {
                var id = (pid.Text ?? "").Trim();
                if (!System.Text.RegularExpressions.Regex.IsMatch(id, @"^p_\d+_[0-9a-f]{12}$")) { MainWindow.Current?.Toast(I18n.T("v31.adm.refund.badId"), bad: true); return; }
                Act(() => Store.Refund(id, reason.Text ?? "", refundUid.Text), I18n.T("v31.adm.refund.done"));
            }, "primary", Icons.Undo))), 22));
        Content = col;
    }
}
