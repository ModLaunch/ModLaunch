using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

/// <summary>
/// 14.0: «Комиссия» в магазине Creator Hub — только для владельца. Сверху четыре цифры (процент, всего,
/// к получению, оборот), ниже — процент ползунком и «Забрать на кошелёк», затем все продажи с удержанной
/// долей и приём оплаты (какой платёжный провайдер подключить — решает владелец).
/// </summary>
public sealed class CommissionPanel : UserControl
{
    static List<FeeSale>? _sales;
    static string? _error;
    static bool _loading;
    string? _note;
    bool _busy;

    public CommissionPanel() => Render();

    public static void Reset() { _sales = null; _error = null; }

    void Load()
    {
        if (_loading) return;
        _loading = true;
        _ = Task.Run(async () =>
        {
            try { await Market.LoadFee(force: true); _sales = await Commission.AllSales(); _error = null; }
            catch (Exception e) { _error = Market.Explain(e); _sales = null; }
            _loading = false;
            Dispatcher.UIThread.Post(Render);
        });
    }

    void Render()
    {
        var col = Ui.Col(18);
        if (!Social.Account.Owner)
        {
            col.Children.Add(Ui.Card(Ui.Text(I18n.T("v14.fee.ownerOnly"), "muted", wrap: true), 22));
            Content = col;
            return;
        }
        if (_sales is null && _error is null) { Load(); col.Children.Add(Ui.Card(Ui.Row(12, new ProgressBar { IsIndeterminate = true, Width = 120 }, Ui.Text(I18n.T("v14.fee.loading"), "muted")), 22)); Content = col; return; }
        if (_error is not null)
        {
            col.Children.Add(Ui.Card(Ui.Col(10, Ui.Text(I18n.T("v14.fee.error"), "h3"), Ui.Text(_error, "small muted", wrap: true),
                Ui.Text(I18n.T("v14.fee.rules"), "small muted", wrap: true),
                Ui.Button(I18n.T("cr.refresh"), () => { Reset(); Render(); }, "", Icons.Refresh)), 22));
            Content = col;
            return;
        }
        var s = Commission.Summary(_sales!);

        var stats = new UniformGrid { Columns = 4 };
        stats.Children.Add(Stat(I18n.T("v14.fee.percent"), s.Percent + " %", I18n.T("v14.fee.percent.sub")));
        stats.Children.Add(Stat(I18n.T("v14.fee.total"), Market.Money(s.Total), I18n.T("v14.fee.total.sub", ("n", s.Sales))));
        stats.Children.Add(Stat(I18n.T("v14.fee.pending"), Market.Money(s.Pending), I18n.T("v14.fee.pending.sub")));
        stats.Children.Add(Stat(I18n.T("v14.fee.turnover"), Market.Money(s.Turnover), I18n.T("v14.fee.turnover.sub")));
        col.Children.Add(stats);

        // Процент: ползунок 0–50 и пример «с 199 ₽ вам — …, автору — …».
        var value = new TextBlock { FontSize = 22, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, MinWidth = 64 };
        var example = Ui.Text("", "small muted", wrap: true);
        var slider = new Slider { Minimum = 0, Maximum = 50, Value = s.Percent, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 320, VerticalAlignment = VerticalAlignment.Center };
        void Show()
        {
            var p = (int)slider.Value;
            value.Text = p + " %";
            const long price = 19900;
            var fee = price * p / 100;
            example.Text = I18n.T("v14.fee.example", ("price", Market.Money(price)), ("you", Market.Money(fee)), ("author", Market.Money(price - fee)));
        }
        slider.ValueChanged += (_, _) => Show();
        Show();
        var save = Ui.Button(I18n.T("v14.fee.save"), () => _ = Save((int)slider.Value), "primary", Icons.Check);
        save.IsEnabled = !_busy;
        var percentCard = Ui.Card(Ui.Col(12,
            Ui.Text(I18n.T("v14.fee.set"), "h3"),
            Ui.Text(I18n.T("v14.fee.set.text"), "small muted", wrap: true),
            Ui.Row(18, slider, value, save),
            example), 22);

        var claim = Ui.Button(I18n.T("v14.fee.claim", ("sum", Market.Money(s.Pending))), () => _ = Claim(), "primary", Icons.Download);
        claim.IsEnabled = s.Pending > 0 && !_busy;
        var claimCard = Ui.Card(Ui.Col(12,
            Ui.Text(I18n.T("v14.fee.wallet"), "h3"),
            Ui.Text(I18n.T("v14.fee.wallet.text"), "small muted", wrap: true),
            Ui.Row(10, claim, Ui.Button(I18n.T("v93.mk.tab.wallet"), () => MainWindow.Current?.Navigate(() => new AccountPage("wallet")), "ghost", Icons.Bag))), 22);
        var two = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 18 };
        two.Children.Add(percentCard);
        Grid.SetColumn(claimCard, 1);
        two.Children.Add(claimCard);
        col.Children.Add(two);
        if (_note is not null) col.Children.Add(Ui.Text(_note, "small muted", wrap: true));

        // Продажи: дата, лот, цена, ваша доля, статус.
        var table = Ui.Col(0);
        table.Children.Add(TableRow(I18n.T("v14.fee.col.date"), I18n.T("v14.fee.col.item"), I18n.T("v14.fee.col.price"), I18n.T("v14.fee.col.fee"), I18n.T("v14.fee.col.state"), head: true));
        foreach (var x in s.Recent)
            table.Children.Add(TableRow(x.Created?.ToLocalTime().ToString("d MMM, HH:mm", I18n.Culture) ?? "—", x.Title, Market.Money(x.Price), Market.Money(x.Fee),
                x.Taken ? I18n.T("v14.fee.taken") : I18n.T("v14.fee.waiting"), taken: x.Taken));
        if (s.Recent.Count == 0) table.Children.Add(new Border { Padding = new Thickness(0, 12), Child = Ui.Text(I18n.T("v14.fee.none"), "small muted", wrap: true) });
        col.Children.Add(Ui.Card(Ui.Col(10, Ui.Text(I18n.T("v14.fee.sales"), "h3"), table), 22));

        // Приём оплаты: провайдер ещё не выбран.
        col.Children.Add(Ui.Card(Ui.Row(16, Ui.Icon(Icons.Shield, 22, Ui.Res("Muted")),
            Ui.Col(4, Ui.Text(I18n.T("v14.fee.pay"), "h3"), Ui.Text(I18n.T("v14.fee.pay.text"), "small muted", wrap: true))), 22));
        Content = col;
    }

    async Task Save(int percent)
    {
        _busy = true;
        try { await Commission.SetPercent(percent); _note = I18n.T("v14.fee.saved", ("n", percent)); }
        catch (Exception e) { _note = Market.Explain(e); }
        _busy = false;
        Dispatcher.UIThread.Post(Render);
    }

    async Task Claim()
    {
        if (_sales is null) return;
        _busy = true;
        Dispatcher.UIThread.Post(Render);
        try
        {
            var (count, amount) = await Commission.Claim(_sales);
            _note = I18n.T("v14.fee.claimed", ("n", count), ("sum", Market.Money(amount)));
            _sales = await Commission.AllSales();
            WalletPanel.Reset();
        }
        catch (Exception e) { _note = Market.Explain(e); }
        _busy = false;
        Dispatcher.UIThread.Post(Render);
    }

    static Control Stat(string label, string value, string sub) => new Border
    {
        Classes = { "card" }, Padding = new Thickness(18, 16), Margin = new Thickness(0, 0, 14, 0),
        Child = Ui.Col(4,
            new TextBlock { Text = label.ToUpper(I18n.Culture), FontSize = 10, LetterSpacing = 1, FontWeight = FontWeight.SemiBold, Foreground = Ui.Res("Faint") },
            new TextBlock { Text = value, FontSize = 24, FontWeight = FontWeight.SemiBold, Foreground = Ui.Res("Text"), TextTrimming = TextTrimming.CharacterEllipsis },
            Ui.Text(sub, "tiny muted", wrap: true)),
    };

    static Control TableRow(string date, string item, string price, string fee, string state, bool head = false, bool taken = false)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("140,*,110,110,120"), Margin = new Thickness(0, head ? 0 : 0, 0, 0) };
        var cells = new[] { date, item, price, fee, state };
        for (var i = 0; i < cells.Length; i++)
        {
            var t = new TextBlock
            {
                Text = head ? cells[i].ToUpper(I18n.Culture) : cells[i], FontSize = head ? 10 : 13, LetterSpacing = head ? 1 : 0,
                FontWeight = head ? FontWeight.SemiBold : i == 3 ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = head ? Ui.Res("Faint") : i == 4 ? (taken ? Ui.Res("Muted") : Ui.Res("Good")) : i == 0 ? Ui.Res("Muted") : Ui.Res("Text"),
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = i is 2 or 3 ? HorizontalAlignment.Right : HorizontalAlignment.Left, Margin = new Thickness(i is 2 or 3 ? 0 : i == 4 ? 18 : 0, 0, i is 2 or 3 ? 6 : 0, 0),
            };
            Grid.SetColumn(t, i);
            grid.Children.Add(t);
        }
        return new Border { Padding = new Thickness(0, 9), BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
    }
}
