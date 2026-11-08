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
            // 25.0: реквизиты и лот Premium — до того, как форма заполнится (иначе «Сохранить» затёр бы их пустыми).
            try { await PayMethods.Load(force: true); } catch { }
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

        // 25.0: приём оплаты — реквизиты для пополнения кошельков и продажа ModLaunch Premium.
        var payPremium = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 18 };
        var payCard = PayCard();
        payPremium.Children.Add(payCard);
        var premiumCard = PremiumCard();
        Grid.SetColumn(premiumCard, 1);
        payPremium.Children.Add(premiumCard);
        col.Children.Add(payPremium);
        Content = col;
    }

    /// <summary>Реквизиты: по полю на способ. Пустое поле — способ не показывается в окне пополнения.</summary>
    Control PayCard()
    {
        var boxes = new Dictionary<string, TextBox>();
        var rows = Ui.Col(8);
        foreach (var m in PayMethods.All)
        {
            var box = new TextBox { Watermark = m.Example == "" ? WalletPanel.MethodName(m.Id) : m.Example, Text = PayMethods.Owner.GetValueOrDefault(m.Id, ""), MaxLength = 200 };
            boxes[m.Id] = box;
            var label = Ui.Text(WalletPanel.MethodName(m.Id), "small muted");
            label.Width = 130;
            label.VerticalAlignment = VerticalAlignment.Center;
            var row = new DockPanel();
            DockPanel.SetDock(label, Dock.Left);
            row.Children.Add(label);
            row.Children.Add(box);
            rows.Children.Add(row);
        }
        var save = Ui.Button(I18n.T("v25.pay.save"), async () =>
        {
            // Реквизиты так и не прочитались (нет сети) — сохранение затёрло бы сохранённые способы пустыми.
            if (!PayMethods.Loaded) { MainWindow.Current?.Toast(I18n.T("v14.fee.error"), bad: true); Reset(); Render(); return; }
            try
            {
                await PayMethods.Save(boxes.ToDictionary(kv => kv.Key, kv => kv.Value.Text ?? ""));
                MainWindow.Current?.Toast(I18n.T("v25.pay.saved", ("n", PayMethods.Owner.Count)));
            }
            catch (Exception e) { MainWindow.Current?.Toast(Market.Explain(e), bad: true); }
        }, "primary", Icons.Check);
        save.IsEnabled = PayMethods.Loaded;
        return Ui.Card(Ui.Col(12,
            Ui.Row(10, Ui.Icon(Icons.Bag, 20, Ui.Res("Brand2")), Ui.Text(I18n.T("v25.pay.title"), "h3")),
            Ui.Text(I18n.T("v25.pay.text"), "small muted", wrap: true),
            rows, save), 22);
    }

    /// <summary>ModLaunch Premium: какой свой лот продавать как Premium (его цена — цена Premium).</summary>
    Control PremiumCard()
    {
        var pick = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = I18n.T("v25.prem.owner.pick") };
        var status = Ui.Text(Features.Premium.ListingId is { } cur ? I18n.T("v25.prem.owner.on", ("id", cur)) : I18n.T("v25.prem.owner.off"), "small muted", wrap: true);
        List<Listing> mine = [];
        _ = Task.Run(async () =>
        {
            try { await PayMethods.Load(); mine = Program.Demo ? [] : await Market.Mine(); } catch { }
            Dispatcher.UIThread.Post(() =>
            {
                pick.Items.Clear();
                foreach (var l in mine) pick.Items.Add($"{l.Title} · {Market.Money(l.Price)}");
                pick.SelectedIndex = mine.FindIndex(l => l.Id == Features.Premium.ListingId);
                status.Text = Features.Premium.ListingId is { } now ? I18n.T("v25.prem.owner.on", ("id", now)) : I18n.T("v25.prem.owner.off");
            });
        });
        var save = Ui.Button(I18n.T("v25.prem.owner.save"), async () =>
        {
            if (pick.SelectedIndex < 0 || pick.SelectedIndex >= mine.Count) { MainWindow.Current?.Toast(I18n.T("v25.prem.owner.pick"), bad: true); return; }
            try
            {
                var l = mine[pick.SelectedIndex];
                await Features.Premium.SetOffer(l.Id);
                status.Text = I18n.T("v25.prem.owner.on", ("id", l.Id));
                MainWindow.Current?.Toast(I18n.T("v25.prem.owner.saved", ("price", Market.Money(l.Price))));
            }
            catch (Exception e) { MainWindow.Current?.Toast(Market.Explain(e), bad: true); }
        }, "primary", Icons.Sparkles);
        var newLot = Ui.Button(I18n.T("v25.prem.owner.new"), () => MainWindow.Current?.Navigate(() => new CreatorPage("shop")), "ghost", Icons.Plus);
        return Ui.Card(Ui.Col(12,
            Ui.Row(10, Ui.Icon(Icons.Sparkles, 20, Ui.Hex("#F0B429")), Ui.Text(I18n.T("v25.prem.owner.title"), "h3")),
            Ui.Text(I18n.T("v25.prem.owner.text", ("price", Market.Money(Features.Premium.Price))), "small muted", wrap: true),
            pick, status, Ui.Text(PremiumSold(), "small", color: Ui.Hex("#F0B429"), wrap: true), Ui.Row(8, save, newLot)), 22);
    }

    /// <summary>Сколько раз куплен Premium и на какую сумму (из продаж рынка).</summary>
    static string PremiumSold()
    {
        var ids = Features.Premium.AllListings.Append(Features.Premium.ListingId ?? "").ToHashSet();
        var sold = _sales?.Where(x => ids.Contains(x.ListingId) && x.Price > 0).ToList() ?? [];
        return I18n.T("v25.prem.owner.sold", ("n", sold.Count), ("sum", Market.Money(sold.Sum(x => x.Price))));
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
