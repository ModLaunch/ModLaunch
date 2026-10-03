using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

public sealed partial class CreatorPage
{
    // ---------------------------------------------------------------- маркет

    static List<Listing>? _market;
    static string? _marketError;
    static bool _marketLoading;
    string _mKind = "all";
    string _mSort = "new";

    async Task LoadMarket(bool force = false)
    {
        if (_marketLoading) return;
        _marketLoading = true;
        _marketError = null;
        Build();
        await Task.Yield();
        try { _market = await Market.All(force); }
        catch (Exception e) { _market = []; _marketError = Market.Explain(e); }
        _marketLoading = false;
        Build();
    }

    /// <summary>Маркет: модели, части кода, скрипты и ассеты авторов — платно или бесплатно.</summary>
    Control MarketView()
    {
        if (_market is null && !_marketLoading && !Program.Screenshot) _ = LoadMarket();
        var col = new StackPanel { Spacing = 16 };

        // Шапка-объяснение: как работает маркет и комиссия.
        var hero = new DockPanel();
        var sell = Ui.Button(I18n.T("mk.sell"), () => MarketViews.Editor(null, () => { _ = LoadMarket(force: true); _studio = null; }), "primary", Icons.Plus);
        sell.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(sell, Dock.Right);
        hero.Children.Add(sell);
        hero.Children.Add(Ui.Row(14,
            new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(22), Background = Ui.Res("BrandSoft"), Child = Ui.Icon(Icons.Bag, 20, Ui.Res("Brand2")) },
            Ui.Col(3, Ui.Text(I18n.T("mk.hero"), "h3"), Ui.Text(I18n.T("mk.hero.text", ("fee", Market.FeePercent)), "small muted", wrap: true))));
        col.Children.Add(Ui.Card(hero, 18));

        var kinds = Ui.Row(6);
        foreach (var k in new[] { "all" }.Concat(Market.Kinds))
        {
            var id = k;
            var chip = Ui.Button(I18n.T("mk.kind." + k), () => { _mKind = id; Build(); }, "chip", k == "all" ? null : MarketViews.KindIcon(k));
            if (_mKind == k) chip.Classes.Add("active");
            kinds.Children.Add(chip);
        }
        var sorts = new[] { "new", "popular", "cheap", "expensive" };
        var sort = new ComboBox { Width = 170, ItemsSource = sorts.Select(s => I18n.T("mk.sort." + s)).ToList(), SelectedIndex = Array.IndexOf(sorts, _mSort) };
        sort.SelectionChanged += (_, _) => { var v = sorts[Math.Max(0, sort.SelectedIndex)]; if (v != _mSort) { _mSort = v; Build(); } };
        var right = Ui.Row(8, sort, Ui.Button("", () => _ = LoadMarket(force: true), "icon ghost", Icons.Refresh, I18n.T("cr.refresh")));
        var bar = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(right);
        kinds.VerticalAlignment = VerticalAlignment.Center;
        bar.Children.Add(kinds);
        col.Children.Add(bar);

        if (_marketLoading && _market is null)
        {
            var sk = new UniformGrid { Columns = 3 };
            for (var i = 0; i < 6; i++) sk.Children.Add(Skeleton());
            col.Children.Add(sk);
            return col;
        }
        if (_marketError is not null) { col.Children.Add(Ui.Text(_marketError, "muted", wrap: true)); return col; }

        var list = Market.Sort((_market ?? []).Where(l =>
            (_mKind == "all" || l.Kind == _mKind) &&
            (_filter == "" || l.Title.Contains(_filter, StringComparison.OrdinalIgnoreCase) || l.Author.Contains(_filter, StringComparison.OrdinalIgnoreCase)
                || l.Summary.Contains(_filter, StringComparison.OrdinalIgnoreCase))), _mSort).ToList();
        if (list.Count == 0)
        {
            col.Children.Add(Ui.Card(Ui.Col(10, Ui.Text(I18n.T("mk.empty"), "h3"), Ui.Text(I18n.T("mk.empty.text"), "muted", wrap: true),
                Ui.Button(I18n.T("mk.sell"), () => MarketViews.Editor(null, () => _ = LoadMarket(force: true)), "primary", Icons.Plus)), 28));
            return col;
        }
        var grid = new UniformGrid { Columns = 3 };
        foreach (var l in list) grid.Children.Add(MarketViews.Card(l, () => { _studio = null; _ = LoadMarket(force: true); }));
        col.Children.Add(grid);
        return col;
    }

    // ---------------------------------------------------------------- студия продавца

    sealed record StudioData(List<Listing> Listings, List<Sale> Sales, Wallet Wallet);

    static StudioData? _studio;
    static string? _studioError;
    static bool _studioLoading;

    async Task LoadStudio()
    {
        if (_studioLoading) return;
        _studioLoading = true;
        _studioError = null;
        Build();
        await Task.Yield();
        try
        {
            var mine = await Market.Mine();
            _studio = new StudioData(mine, await Market.AllSales(mine), await Market.MyWallet());
        }
        catch (Exception e) { _studioError = Market.Explain(e); }
        _studioLoading = false;
        Build();
    }

    void ReloadStudio() { _studio = null; _studioError = null; _ = LoadStudio(); }

    static Control Stat(string icon, string label, string value, string? hint = null)
    {
        var col = Ui.Col(4,
            Ui.Row(8, Ui.Icon(icon, 16, Ui.Res("Brand2")), Ui.Text(label, "small muted")),
            Ui.Text(value, "h2"));
        if (hint is not null) col.Children.Add(Ui.Text(hint, "small muted", wrap: true));
        var card = Ui.Card(col, 18);
        card.Margin = new Thickness(0, 0, 14, 14);
        return card;
    }

    /// <summary>Студия продавца: доход, лоты, продажи и управление ими.</summary>
    Control Studio()
    {
        var col = new StackPanel { Spacing = 20 };
        if (!Social.Account.SignedIn)
        {
            col.Children.Add(Ui.Card(Ui.Col(10, Ui.Text(I18n.T("st.signin"), "h3"), Ui.Text(I18n.T("st.signin.text"), "muted", wrap: true),
                Ui.Button(I18n.T("acc.menu.signin"), () => MainWindow.Current?.Navigate(() => new AccountPage()), "primary", Icons.User)), 28));
            return col;
        }
        if (_studio is null && !_studioLoading && _studioError is null && !Program.Screenshot) _ = LoadStudio();
        if (_studioError is not null) { col.Children.Add(Ui.Card(Ui.Col(10, Ui.Text(_studioError, "muted", wrap: true), Ui.Button(I18n.T("cr.refresh"), ReloadStudio, "", Icons.Refresh)), 22)); return col; }
        if (_studio is null)
        {
            var sk = new UniformGrid { Columns = 4 };
            for (var i = 0; i < 4; i++) sk.Children.Add(Skeleton());
            col.Children.Add(sk);
            return col;
        }
        var (listings, sales, wallet) = (_studio.Listings, _studio.Sales, _studio.Wallet);

        var month = sales.Where(s => s.Created > DateTime.UtcNow.AddDays(-30)).ToList();
        var stats = new UniformGrid { Columns = 4 };
        stats.Children.Add(Stat(Icons.Bag, I18n.T("st.balance"), Market.Money(wallet.Balance), I18n.T("st.balance.hint")));
        stats.Children.Add(Stat(Icons.Chart, I18n.T("st.earned"), Market.Money(wallet.Earned), I18n.T("st.month", ("sum", Market.Money(month.Sum(s => s.Net))))));
        stats.Children.Add(Stat(Icons.Trophy, I18n.T("st.sales"), sales.Count.ToString(I18n.Culture), I18n.T("st.month.n", ("n", month.Count))));
        stats.Children.Add(Stat(Icons.Layers, I18n.T("st.listings"), listings.Count(l => l.Active).ToString(I18n.Culture), I18n.T("st.hidden", ("n", listings.Count(l => !l.Active)))));
        col.Children.Add(stats);

        var actions = Ui.Row(10,
            Ui.Button(I18n.T("mk.new"), () => MarketViews.Editor(null, ReloadStudio), "primary", Icons.Plus),
            Ui.Button(I18n.T("st.payout"), () => MainWindow.Current?.Navigate(() => new AccountPage("wallet")), "", Icons.Upload),
            Ui.Button("", ReloadStudio, "icon ghost", Icons.Refresh, I18n.T("cr.refresh")));
        col.Children.Add(actions);

        // Лоты: строка на каждый, с действиями.
        var rows = Ui.Col(8);
        if (listings.Count == 0) rows.Children.Add(Ui.Text(I18n.T("st.noListings"), "muted", wrap: true));
        foreach (var l in listings)
        {
            var item = l;
            var buttons = Ui.Row(6,
                Ui.Button("", () => MarketViews.Open(item, ReloadStudio), "icon ghost", Icons.Eye, I18n.T("st.open")),
                Ui.Button("", () => MarketViews.Editor(item, ReloadStudio), "icon ghost", Icons.Edit, I18n.T("mk.edit")),
                Ui.Button("", async () =>
                {
                    try { await Market.SetHidden(item, item.Active); MainWindow.Current?.Toast(I18n.T(item.Active ? "st.hiddenNow" : "st.shownNow")); }
                    catch (Exception e) { MainWindow.Current?.Toast(Market.Explain(e), bad: true); }
                    ReloadStudio();
                }, "icon ghost", item.Active ? Icons.EyeOff : Icons.Eye, I18n.T(item.Active ? "st.hide" : "st.show")),
                Ui.Button("", () => DeleteListing(item), "icon ghost", Icons.Trash, I18n.T("st.delete")));
            DockPanel.SetDock(buttons, Dock.Right);
            var line = new DockPanel();
            line.Children.Add(buttons);
            var status = item.Active ? Ui.Text("● " + I18n.T("st.active"), "small", color: Ui.Res("Good")) : Ui.Text("● " + I18n.T("st.hiddenState"), "small muted");
            line.Children.Add(Ui.Row(14,
                new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(10), Background = Ui.Res("Surface3"), Child = Ui.Icon(MarketViews.KindIcon(item.Kind), 18) },
                Ui.Col(2, Ui.Text(item.Title, "h3"),
                    Ui.Row(12, status, Ui.Text(MarketViews.PriceText(item), "small brand"), Ui.Text(I18n.T("mk.sold", ("n", item.Sales)), "small muted"),
                        Ui.Text(I18n.T("st.revenue", ("sum", Market.Money(sales.Where(s => s.ListingId == item.Id).Sum(s => s.Net)))), "small muted"),
                        Ui.Text("v" + item.Version + " · " + Ui.Ago(item.Updated), "small muted")))));
            rows.Children.Add(Ui.Card(line, 14));
        }
        col.Children.Add(Section(I18n.T("st.myListings"), null, rows));

        // Последние продажи.
        var feed = Ui.Col(6);
        if (sales.Count == 0) feed.Children.Add(Ui.Text(I18n.T("st.noSales"), "muted"));
        foreach (var s in sales.Take(30))
        {
            var line = new DockPanel();
            var money = Ui.Text("+" + Market.Money(s.Net), "h3", color: Ui.Res("Good"));
            DockPanel.SetDock(money, Dock.Right);
            line.Children.Add(money);
            line.Children.Add(Ui.Col(2, Ui.Text(s.Title, ""),
                Ui.Text(I18n.T("st.saleLine", ("buyer", s.BuyerName), ("price", Market.Money(s.Price)), ("fee", Market.Money(s.Fee))) + " · " + Ui.Ago(s.Created), "small muted")));
            feed.Children.Add(Ui.Card(line, 12));
        }
        col.Children.Add(Section(I18n.T("st.recentSales"), null, feed));
        return col;
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
