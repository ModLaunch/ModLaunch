using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

/// <summary>
/// 20.0: «Аккаунт → Заработок» — сколько заработано и как идут дела у автора: всего и за 30 дней (со сравнением
/// с прошлыми 30), сколько можно вывести, продажи, покупатели, скачивания, лайки, средний чек, график по дням,
/// лучшие работы (лоты рынка и моды Hub вместе) и последние продажи. Раньше это было разбросано по «Продажам»
/// (три цифры), «Студии» рынка и обзору проекта.
/// </summary>
public sealed partial class AccountPage
{
    /// <summary>Всё для «Заработка» одним снимком: лоты, их продажи, кошелёк и свои моды в Hub.</summary>
    sealed record EarnData(List<Listing> Listings, List<Sale> Sales, Wallet Wallet, List<HubMod> Mods);

    EarnData? _earn;

    /// <summary>Строка «лучших работ»: что, откуда, сколько продаж или скачиваний и сколько принесла.</summary>
    internal sealed record Work(string Title, string Source, long Sales, long Downloads, long Likes, long Revenue);

    /// <summary>Лоты и моды — одной таблицей: сначала по выручке, затем по скачиваниям.</summary>
    internal static List<Work> BestWorks(IEnumerable<Listing> listings, IEnumerable<Sale> sales, IEnumerable<HubMod> mods)
    {
        var net = sales.GroupBy(s => s.ListingId).ToDictionary(g => g.Key, g => g.Sum(s => s.Net));
        var works = listings.Select(l => new Work(l.Title, "market", l.Free ? 0 : l.Sales, l.Free ? l.Sales : 0, 0, net.GetValueOrDefault(l.Id)))
            .Concat(mods.Select(m => new Work(m.Name, "hub", 0, m.Downloads, m.Likes, 0)));
        return works.OrderByDescending(w => w.Revenue).ThenByDescending(w => w.Downloads + w.Sales).ThenBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Рост за 30 дней к прошлым 30, в процентах (null — сравнивать не с чем).</summary>
    internal static int? Growth(long now, long before) => before <= 0 ? null : (int)Math.Round((now - before) * 100.0 / before);

    static EarnData DemoEarn()
    {
        var mine = Demo.Listings().Take(5).ToList();
        var paid = mine.Where(l => !l.Free).ToList();
        var buyers = new[] { "Kira", "Vega", "Nox", "Echo", "Alpin", "Mira", "Sol" };
        var sales = Enumerable.Range(0, 64).Select(i =>
        {
            var l = paid[i % paid.Count];
            return new Sale(l.Id, "b" + i % 23, buyers[i % buyers.Length], "demo", l.Title, l.Price, Market.Fee(l.Price), DateTime.UtcNow.AddDays(-(i * 13 % 58)).AddHours(-i));
        }).OrderByDescending(s => s.Created).ToList();
        var mods = Demo.Hub().Take(3).ToList();
        return new EarnData(mine, sales, new Wallet(412_300, 1_284_500, 0, 800_000), mods);
    }

    Control Earnings()
    {
        if (Program.Screenshot) _earn ??= DemoEarn();
        if (_earn is null && !_busy)
            Load("earnings", async () =>
            {
                var mine = await Market.Mine();
                var mods = (await Hub.All()).Where(m => m.Mine).ToList();
                _earn = new EarnData(mine, await Market.AllSales(mine), await Market.MyWallet(claim: true), mods);
            });
        if (_earn is null) return Card(I18n.T("v20.acc.earnings"), null, Loading());
        var (listings, sales, wallet, mods) = (_earn.Listings, _earn.Sales, _earn.Wallet, _earn.Mods);
        var now = DateTime.UtcNow;
        var month = sales.Where(s => s.Created > now.AddDays(-30)).ToList();
        var before = sales.Where(s => s.Created <= now.AddDays(-30) && s.Created > now.AddDays(-60)).ToList();
        var col = Ui.Col(18);

        // Главная цифра — заработано всего; рядом — за 30 дней с ростом и сколько можно вывести.
        var growth = Growth(month.Sum(s => s.Net), before.Sum(s => s.Net));
        var trend = growth is { } g
            ? Ui.Text((g >= 0 ? "▲ " : "▼ ") + I18n.T("v20.acc.growth", ("n", Math.Abs(g))), "small", color: g >= 0 ? Ui.Res("Good") : Ui.Res("Bad"))
            : Ui.Text(I18n.T("v20.acc.growth.none"), "small muted");
        var total = Ui.Col(6,
            Ui.Text(I18n.T("v20.acc.total").ToUpper(I18n.Culture), "small muted"),
            new TextBlock { Text = Market.Money(wallet.Earned), FontFamily = Gx.Display, FontSize = 40, FontWeight = FontWeight.Bold, Foreground = Ui.Res("Brand2") },
            Ui.Row(12, Ui.Text(I18n.T("v20.acc.month", ("sum", Market.Money(month.Sum(s => s.Net)))), "strong"), trend));
        var payout = Ui.Col(8,
            Ui.Text(I18n.T("v20.acc.available").ToUpper(I18n.Culture), "small muted"),
            new TextBlock { Text = Market.Money(wallet.Balance), FontFamily = Gx.Display, FontSize = 26, FontWeight = FontWeight.Bold },
            Ui.Text(I18n.T("v20.acc.paidOut", ("sum", Market.Money(wallet.PaidOut))), "small muted"),
            Ui.Row(8,
                Ui.Button(I18n.T("acc.payout"), () => { _tab = "wallet"; Build(); }, "primary", Icons.Upload),
                Ui.Button(I18n.T("st.tab"), () => MainWindow.Current?.Navigate(() => new MarketPage("studio")), "", Icons.Chart)));
        payout.HorizontalAlignment = HorizontalAlignment.Right;
        var hero = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 24 };
        hero.Children.Add(total);
        Grid.SetColumn(payout, 1);
        hero.Children.Add(payout);
        var heroCard = Ui.Card(hero, 24);
        heroCard.BorderBrush = Ui.Res("Brand");
        heroCard.BorderThickness = new Thickness(1);
        col.Children.Add(heroCard);

        // Статистика автора — восемь цифр: продажи и покупатели, скачивания и лайки, средний чек и работы.
        var downloads = mods.Sum(m => m.Downloads) + listings.Where(l => l.Free).Sum(l => l.Sales);
        var grid = new UniformGrid { Columns = 4 };
        foreach (var (icon, label, value, hint) in new[]
        {
            (Icons.Bag, I18n.T("st.sales"), sales.Count.ToString("N0", I18n.Culture), I18n.T("st.month.n", ("n", month.Count))),
            (Icons.Users, I18n.T("v20.acc.buyers"), sales.Select(s => s.Buyer).Distinct().Count().ToString("N0", I18n.Culture), I18n.T("v20.acc.buyers.hint")),
            (Icons.Download, I18n.T("v20.acc.downloads"), downloads.ToString("N0", I18n.Culture), I18n.T("v20.acc.downloads.hint")),
            (Icons.Heart, I18n.T("v20.acc.likes"), mods.Sum(m => m.Likes).ToString("N0", I18n.Culture), I18n.T("v20.acc.comments", ("n", mods.Sum(m => m.Comments)))),
            (Icons.Chart, I18n.T("v20.acc.average"), sales.Count == 0 ? "—" : Market.Money(sales.Sum(s => s.Price) / sales.Count), I18n.T("v20.acc.fees", ("sum", Market.Money(sales.Sum(s => s.Fee))))),
            (Icons.Layers, I18n.T("st.listings"), listings.Count(l => l.Active).ToString(I18n.Culture), I18n.T("st.hidden", ("n", listings.Count(l => !l.Active)))),
            (Icons.Globe, I18n.T("v20.acc.hub"), mods.Count.ToString(I18n.Culture), I18n.T("v20.acc.hub.hint")),
            (Icons.Trophy, I18n.T("v20.acc.best"), BestDay(sales) is { } d ? Market.Money(d.Sum) : "—", BestDay(sales) is { } bd ? bd.Day.ToLocalTime().ToString("d MMMM yyyy", I18n.Culture) : I18n.T("st.noSales")),
        })
        {
            var tile = Ui.Card(Ui.Col(6,
                Ui.Row(8, Ui.Icon(icon, 15, Ui.Res("Brand2")), Ui.Text(label, "small muted")),
                new TextBlock { Text = value, FontFamily = Gx.Display, FontSize = 22, FontWeight = FontWeight.Bold },
                Ui.Text(hint, "small muted", wrap: true)), 16);
            tile.Margin = new Thickness(0, 0, 12, 12);
            grid.Children.Add(tile);
        }
        col.Children.Add(Ui.Col(10, Ui.Text(I18n.T("v20.acc.stats"), "h2"), grid));

        col.Children.Add(Card(I18n.T("v93.mk.studio.chart"), null, MarketPage.Chart(sales)));

        // Лучшие работы и последние продажи — рядом.
        var works = Ui.Col(4);
        var best = BestWorks(listings, sales, mods).Take(8).ToList();
        if (best.Count == 0) works.Children.Add(Ui.Col(10, Ui.Text(I18n.T("v20.acc.noWorks"), "muted", wrap: true),
            Ui.Button(I18n.T("mk.sell"), () => ListingWizard.Show(null, () => { Reset(); Build(); }), "primary", Icons.Upload)));
        var top = best.Count > 0 ? Math.Max(1, best.Max(w => w.Revenue)) : 1;
        foreach (var w in best)
        {
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,90"), ColumnSpacing = 12 };
            var name = Ui.Col(3,
                Ui.Row(8, Ui.Text(w.Title, "strong"), Ui.Text(I18n.T(w.Source == "hub" ? "v20.acc.src.hub" : "v20.acc.src.market"), "small muted")),
                new Border { Height = 3, CornerRadius = new CornerRadius(2), Background = Ui.Res("Brand"), HorizontalAlignment = HorizontalAlignment.Left, Width = w.Revenue == 0 ? 3 : Math.Max(6, 220.0 * w.Revenue / top) });
            line.Children.Add(name);
            var count = Ui.Text(w.Sales > 0 ? I18n.T("mk.sold", ("n", w.Sales)) : I18n.T("v20.acc.dl", ("n", w.Downloads.ToString("N0", I18n.Culture))), "small muted");
            count.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(count, 1);
            line.Children.Add(count);
            var money = Ui.Text(w.Revenue > 0 ? "+" + Market.Money(w.Revenue) : "—", "strong", color: w.Revenue > 0 ? Ui.Res("Good") : null);
            money.HorizontalAlignment = HorizontalAlignment.Right;
            money.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(money, 2);
            line.Children.Add(money);
            works.Children.Add(new Border { Padding = new Thickness(0, 6), BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(0, 0, 0, 1), Child = line });
        }
        var recent = Ui.Col(4);
        if (sales.Count == 0) recent.Children.Add(Ui.Text(I18n.T("st.noSales"), "muted"));
        foreach (var s in sales.Take(8))
        {
            var line = new DockPanel();
            var money = Ui.Text("+" + Market.Money(s.Net), "strong", color: Ui.Res("Good"));
            money.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(money, Dock.Right);
            line.Children.Add(money);
            line.Children.Add(Ui.Row(10, ProfileKit.Avatar(s.BuyerName, 28), Ui.Col(1, Ui.Text(s.Title, ""), Ui.Text(s.BuyerName + " · " + Ui.Ago(s.Created), "small muted"))));
            recent.Children.Add(new Border { Padding = new Thickness(0, 5), Child = line });
        }
        var pair = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,2*"), ColumnSpacing = 16 };
        pair.Children.Add(Card(I18n.T("v20.acc.works"), null, works));
        var right = Card(I18n.T("st.recentSales"), null, recent);
        Grid.SetColumn(right, 1);
        pair.Children.Add(right);
        col.Children.Add(pair);
        col.Children.Add(Ui.Row(10,
            Ui.Button(I18n.T("cr.refresh"), () => { Reset(); Build(); }, "ghost", Icons.Refresh),
            Ui.Text(I18n.T("v20.acc.fee", ("fee", Market.FeePercent)), "small muted")));
        return col;
    }

    /// <summary>Самый доходный день (UTC-дата и сумма), null — продаж не было.</summary>
    static (DateTime Day, long Sum)? BestDay(List<Sale> sales) =>
        sales.Where(s => s.Created is not null).GroupBy(s => s.Created!.Value.Date).Select(g => (g.Key, g.Sum(s => s.Net))).OrderByDescending(x => x.Item2).Cast<(DateTime, long)?>().FirstOrDefault();

    [SelfTest]
    static string EarningsAddUp()
    {
        var a = new Listing("a", "u", "me", "Alpha", "", "", "code", "", 10000, "personal", "1", "", [], [], 0, "", 0, "", "active", 3, null, null);
        var f = new Listing("f", "u", "me", "Free", "", "", "code", "", 0, "personal", "1", "", [], [], 0, "", 0, "", "active", 40, null, null);
        var sales = new List<Sale> { new("a", "b1", "B1", "u", "Alpha", 10000, 1000, DateTime.UtcNow), new("a", "b2", "B2", "u", "Alpha", 10000, 1000, DateTime.UtcNow.AddDays(-40)) };
        var mod = new HubMod("m", "u", "me", "Mod", "", "", "", "1", "script", "", [], [], 0, "", 0, "", "", 5, 120, 2, null, null);
        var best = BestWorks([f, a], sales, [mod]);
        if (best[0].Title != "Alpha" || best[0].Revenue != 18000) throw new Exception("best work " + best[0]);
        if (best.First(w => w.Title == "Free").Downloads != 40 || best.First(w => w.Title == "Mod").Downloads != 120) throw new Exception("downloads");
        if (Growth(150, 100) != 50 || Growth(50, 100) != -50 || Growth(10, 0) is not null) throw new Exception("growth");
        if (BestDay(sales) is not { Sum: 9000 }) throw new Exception("best day");
        return "best works by revenue then downloads; growth vs previous 30 days; best day";
    }
}
