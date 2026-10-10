using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

/// <summary>
/// Студия автора на рынке (как панель разработчика Steamworks): баланс и доход, график продаж
/// за 30 дней, лоты с действиями (открыть, изменить, скрыть, удалить) и последние продажи.
/// </summary>
public sealed partial class MarketPage
{
    sealed record StudioData(List<Listing> Listings, List<Sale> Sales, Wallet Wallet);

    static StudioData? _studio;
    static string? _studioError;
    static bool _studioLoading;

    /// <summary>Для снимков экрана: студия с примером лотов и продаж.</summary>
    public static void DemoStudio(List<Listing> listings, List<Sale> sales, Wallet wallet) => _studio = new StudioData(listings, sales, wallet);

    void ReloadStudio() { _studio = null; _studioError = null; MarketData.Reload(); Build(); }

    Control Studio()
    {
        var col = Ui.Col(20);
        if (!Social.Account.SignedIn && _studio is null) { col.Children.Add(SignIn(I18n.T("st.signin.text"))); return col; }
        if (_studio is null && !_studioLoading && _studioError is null && !Program.Screenshot)
        {
            _studioLoading = true;
            _ = Task.Run(async () =>
            {
                try
                {
                    var mine = await Market.Mine();
                    _studio = new StudioData(mine, await Market.AllSales(mine), await Market.MyWallet());
                }
                catch (Exception e) { _studioError = Market.Explain(e); }
                _studioLoading = false;
                Dispatcher.UIThread.Post(() => { if (MainWindow.Current?.CurrentPage == this) Build(); });
            });
        }
        if (_studioError is not null) { col.Children.Add(Ui.Card(Ui.Col(10, Ui.Text(_studioError, "muted", wrap: true), Ui.Button(I18n.T("cr.refresh"), ReloadStudio, "", Icons.Refresh)), 22)); return col; }
        if (_studio is null) { col.Children.Add(Skeleton()); return col; }
        var (listings, sales, wallet) = (_studio.Listings, _studio.Sales, _studio.Wallet);
        var month = sales.Where(s => s.Created > DateTime.UtcNow.AddDays(-30)).ToList();

        // Счётчики — крупными цифрами, как табло.
        var stats = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), ColumnSpacing = StoreKit.Gap };
        Control Tile(string icon, string label, string value, string hint, bool accent = false)
        {
            var head = Ui.Row(8, Ui.Icon(icon, 16, Ui.Res("Brand2")), new TextBlock { Text = label.ToUpper(I18n.Culture), FontSize = 10.5, FontWeight = FontWeight.Bold, LetterSpacing = 1.1, Foreground = Ui.Res("Muted"), VerticalAlignment = VerticalAlignment.Center });
            var num = new TextBlock { Text = value, FontFamily = Gx.Display, FontSize = 26, FontWeight = FontWeight.Bold, Foreground = accent ? Ui.Res("Brand2") : Ui.Res("Text") };
            var card = new Border { Classes = { "card" }, Padding = new Thickness(18, 16), Child = Ui.Col(8, head, num, Ui.Text(hint, "small muted", wrap: true)) };
            if (accent) card.BorderBrush = Ui.Res("Brand");
            return card;
        }
        var cells = new[]
        {
            Tile(Icons.Bag, I18n.T("st.balance"), Market.Money(wallet.Balance), I18n.T("st.balance.hint"), true),
            Tile(Icons.Chart, I18n.T("st.earned"), Market.Money(wallet.Earned), I18n.T("st.month", ("sum", Market.Money(month.Sum(s => s.Net))))),
            Tile(Icons.Trophy, I18n.T("st.sales"), sales.Count.ToString(I18n.Culture), I18n.T("st.month.n", ("n", month.Count))),
            Tile(Icons.Layers, I18n.T("st.listings"), listings.Count(l => l.Active).ToString(I18n.Culture), I18n.T("st.hidden", ("n", listings.Count(l => !l.Active)))),
        };
        for (var i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i); stats.Children.Add(cells[i]); }
        col.Children.Add(stats);

        var actions = Ui.Row(10,
            Ui.Button(I18n.T("mk.new"), () => MarketViews.Editor(null, ReloadStudio), "primary", Icons.Plus),
            Ui.Button(I18n.T("st.payout"), () => MainWindow.Current?.Navigate(() => new AccountPage("wallet")), "", Icons.Upload),
            Ui.Button(I18n.T("v93.mk.studio.workspace"), () => MainWindow.Current?.Navigate(() => new CreatorPage("mine")), "", Icons.Edit),
            Ui.Button("", ReloadStudio, "icon ghost", Icons.Refresh, I18n.T("cr.refresh")));
        col.Children.Add(actions);

        col.Children.Add(Ui.Col(12, StoreKit.Header(I18n.T("v93.mk.studio.chart")), new Border { Classes = { "card" }, Padding = new Thickness(18, 16), Child = Chart(sales) }));

        // Лоты.
        var rows = Ui.Col(6);
        if (listings.Count == 0) rows.Children.Add(Ui.Text(I18n.T("st.noListings"), "muted", wrap: true));
        foreach (var l in listings)
        {
            var item = l;
            var buttons = Ui.Row(4,
                Ui.Button("", () => MarketTiles.Open(item), "icon ghost", Icons.Eye, I18n.T("st.open")),
                Ui.Button("", () => MarketViews.Editor(item, ReloadStudio), "icon ghost", Icons.Edit, I18n.T("mk.edit")),
                Ui.Button("", async () =>
                {
                    try { await Market.SetHidden(item, item.Active); MainWindow.Current?.Toast(I18n.T(item.Active ? "st.hiddenNow" : "st.shownNow")); }
                    catch (Exception e) { MainWindow.Current?.Toast(Market.Explain(e), bad: true); }
                    ReloadStudio();
                }, "icon ghost", item.Active ? Icons.EyeOff : Icons.Eye, I18n.T(item.Active ? "st.hide" : "st.show")),
                Ui.Button("", () => DeleteListing(item), "icon ghost", Icons.Trash, I18n.T("st.delete")));
            buttons.VerticalAlignment = VerticalAlignment.Center;
            var status = item.Active ? Ui.Text("● " + I18n.T("st.active"), "small", color: Ui.Res("Good")) : Ui.Text("● " + I18n.T("st.hiddenState"), "small muted");
            var revenue = sales.Where(s => s.ListingId == item.Id).Sum(s => s.Net);
            var info = Ui.Row(14, status, Ui.Text(MarketViews.PriceText(item), "small brand"), Ui.Text(I18n.T("mk.sold", ("n", item.Sales)), "small muted"),
                Ui.Text(I18n.T("st.revenue", ("sum", Market.Money(revenue))), "small muted"), Ui.Text("v" + item.Version + " · " + Ui.Ago(item.Updated), "small muted"));
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 14 };
            grid.Children.Add(new Border { Width = 96, Height = 54, CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = MarketTiles.Art(item, 256, hero: false) });
            var words = Ui.Col(4, Ui.Text(item.Title, "h3"), info);
            words.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(words, 1);
            grid.Children.Add(words);
            Grid.SetColumn(buttons, 2);
            grid.Children.Add(buttons);
            rows.Children.Add(new Border { Classes = { "card" }, Padding = new Thickness(10), Child = grid });
        }
        col.Children.Add(Ui.Col(12, StoreKit.Header(I18n.T("st.myListings"), count: listings.Count.ToString()), rows));

        var feed = Ui.Col(6);
        if (sales.Count == 0) feed.Children.Add(Ui.Text(I18n.T("st.noSales"), "muted"));
        foreach (var s in sales.Take(20))
        {
            var line = new DockPanel();
            var money = new TextBlock { Text = "+" + Market.Money(s.Net), FontFamily = Gx.Display, FontWeight = FontWeight.Bold, FontSize = 15, Foreground = Ui.Res("Good"), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(money, Dock.Right);
            line.Children.Add(money);
            line.Children.Add(Ui.Row(12, Ui.Thumb(null, s.BuyerName, 34, 17, person: true), Ui.Col(2, Ui.Text(s.Title, ""),
                Ui.Text(I18n.T("st.saleLine", ("buyer", s.BuyerName), ("price", Market.Money(s.Price)), ("fee", Market.Money(s.Fee))) + " · " + Ui.Ago(s.Created), "small muted"))));
            feed.Children.Add(new Border { Classes = { "card" }, Padding = new Thickness(14, 10), Child = line });
        }
        col.Children.Add(Ui.Col(12, StoreKit.Header(I18n.T("st.recentSales")), feed));
        return col;
    }

    /// <summary>Столбики дохода по дням за 30 дней; наведите — сумма и число продаж.</summary>
    static Control Chart(List<Sale> sales)
    {
        var today = DateTime.UtcNow.Date;
        var days = Enumerable.Range(0, 30).Select(i => today.AddDays(i - 29)).ToList();
        var sums = days.Select(d => sales.Where(s => s.Created?.Date == d).Sum(s => s.Net)).ToList();
        var counts = days.Select(d => sales.Count(s => s.Created?.Date == d)).ToList();
        var max = Math.Max(1, sums.Max());
        var grid = new Grid { Height = 150, ColumnSpacing = 4 };
        for (var i = 0; i < days.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var h = sums[i] == 0 ? 3 : Math.Max(6, 150 * sums[i] / (double)max);
            var bar = new Border
            {
                Height = h, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(3, 3, 1, 1),
                Background = sums[i] == 0 ? Ui.Res("Surface3") : Ui.Res("Brand"),
            };
            if (sums[i] > 0 && sums[i] == max) bar.BoxShadow = Gx.Glow(150, 14);
            var hit = new Border { Background = Brushes.Transparent, Child = bar };
            ToolTip.SetTip(hit, $"{days[i].ToLocalTime().ToString("d MMMM", I18n.Culture)} · {Market.Money(sums[i])} · {I18n.T("mk.sold", ("n", counts[i]))}");
            Grid.SetColumn(hit, i);
            grid.Children.Add(hit);
        }
        var axis = new DockPanel();
        var right = Ui.Text(I18n.T("time.today"), "small muted");
        DockPanel.SetDock(right, Dock.Right);
        axis.Children.Add(right);
        axis.Children.Add(Ui.Text(days[0].ToLocalTime().ToString("d MMMM", I18n.Culture), "small muted"));
        var total = Ui.Row(28, Gx.Stat(Market.Money(sums.Sum()), I18n.T("v93.mk.studio.month"), Ui.Res("Brand2"), 20), Gx.Stat(counts.Sum().ToString(), I18n.T("st.sales"), null, 20));
        return Ui.Col(14, total, grid, axis);
    }

    void DeleteListing(Listing l)
    {
        var w = MainWindow.Current!;
        if (l.Sales > 0)
        {
            w.Dialog(I18n.T("st.delete"), Ui.Text(I18n.T("st.delete.hasSales"), "muted", wrap: true),
                Ui.Button(I18n.T("common.close"), w.CloseDialog),
                Ui.Button(I18n.T("st.hide"), async () => { w.CloseDialog(); try { await Market.SetHidden(l, true); } catch (Exception e) { w.Toast(Market.Explain(e), bad: true); } ReloadStudio(); }, "primary", Icons.EyeOff));
            return;
        }
        w.Dialog(I18n.T("st.delete"), Ui.Text(I18n.T("st.delete.text", ("title", l.Title)), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("st.delete"), async () =>
            {
                w.CloseDialog();
                try { await Market.Delete(l); w.Toast(I18n.T("st.deleted")); } catch (Exception e) { w.Toast(Market.Explain(e), bad: true); }
                ReloadStudio();
            }, "primary", Icons.Trash));
    }
}
