using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

/// <summary>
/// 20.0: «Аккаунт → Заработок» — единственное место, где автор видит свои деньги: сколько заработано всего и за
/// 30 дней (со сравнением с прошлыми 30), продажи, покупатели, скачивания, лайки, средний чек, лучший день,
/// график по дням, свои работы (лоты рынка и моды Hub одной таблицей; у лотов — изменить, скрыть, удалить)
/// и последние продажи. Бывшая «Студия продавца» рынка, «Продажи» аккаунта и строка баланса в Creator Hub
/// повторяли друг друга — убраны. Баланс, пополнение и вывод — во вкладке «Кошелёк».
/// </summary>
public sealed partial class AccountPage
{
    /// <summary>Всё для «Заработка» одним снимком: лоты, их продажи, кошелёк и свои моды в Hub.</summary>
    sealed record EarnData(List<Listing> Listings, List<Sale> Sales, Wallet Wallet, List<HubMod> Mods);

    EarnData? _earn;

    /// <summary>Строка «Моих работ»: что, откуда, сколько продаж или скачиваний и сколько принесла; Lot — лот рынка (у мода Hub нет).</summary>
    internal sealed record Work(string Title, string Source, long Sales, long Downloads, long Likes, long Revenue, Listing? Lot = null);

    /// <summary>Лоты и моды — одной таблицей: сначала по выручке, затем по скачиваниям.</summary>
    internal static List<Work> BestWorks(IEnumerable<Listing> listings, IEnumerable<Sale> sales, IEnumerable<HubMod> mods)
    {
        var net = sales.GroupBy(s => s.ListingId).ToDictionary(g => g.Key, g => g.Sum(s => s.Net));
        var works = listings.Select(l => new Work(l.Title, "market", l.Free ? 0 : l.Sales, l.Free ? l.Sales : 0, 0, net.GetValueOrDefault(l.Id), l))
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

    void ReloadEarn() { _earn = null; _tried.Remove("earnings"); MarketData.Reload(); Build(); }

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

        col.Children.Add(Hero(wallet.Earned, month.Sum(s => s.Net), Growth(month.Sum(s => s.Net), before.Sum(s => s.Net)), Spark(sales)));

        // Статистика автора — шесть цифр: продажи и покупатели, скачивания и лайки, средний чек и лучший день.
        var downloads = mods.Sum(m => m.Downloads) + listings.Where(l => l.Free).Sum(l => l.Sales);
        var best = BestDay(sales);
        var grid = new UniformGrid { Columns = 3 };
        foreach (var (icon, label, value, hint) in new[]
        {
            (Icons.Bag, I18n.T("st.sales"), sales.Count.ToString("N0", I18n.Culture), I18n.T("st.month.n", ("n", month.Count))),
            (Icons.Users, I18n.T("v20.acc.buyers"), sales.Select(s => s.Buyer).Distinct().Count().ToString("N0", I18n.Culture), I18n.T("v20.acc.buyers.hint")),
            (Icons.Download, I18n.T("v20.acc.downloads"), downloads.ToString("N0", I18n.Culture), I18n.T("v20.acc.downloads.hint")),
            (Icons.Heart, I18n.T("v20.acc.likes"), mods.Sum(m => m.Likes).ToString("N0", I18n.Culture), I18n.T("v20.acc.comments", ("n", mods.Sum(m => m.Comments)))),
            (Icons.Percent, I18n.T("v20.acc.average"), sales.Count == 0 ? "—" : Market.Money(sales.Sum(s => s.Price) / sales.Count), I18n.T("v20.acc.fees", ("sum", Market.Money(sales.Sum(s => s.Fee))))),
            (Icons.Trophy, I18n.T("v20.acc.best"), best is { } d ? Market.Money(d.Sum) : "—", best is { } bd ? bd.Day.ToLocalTime().ToString("d MMMM yyyy", I18n.Culture) : I18n.T("st.noSales")),
        })
        {
            var badge = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(10), Background = Tint(36), Child = Ui.Icon(icon, 16, Ui.Res("Brand2")) };
            var words = Ui.Col(2, Ui.Text(label, "small muted"),
                new TextBlock { Text = value, FontFamily = Gx.Display, FontSize = 22, FontWeight = FontWeight.Bold },
                Ui.Text(hint, "small muted", wrap: true));
            var dock = new DockPanel();
            badge.Margin = new Thickness(0, 0, 14, 0);
            badge.VerticalAlignment = VerticalAlignment.Top;
            DockPanel.SetDock(badge, Dock.Left);
            dock.Children.Add(badge);
            dock.Children.Add(words);
            var tile = Ui.Card(dock, 16);
            tile.Margin = new Thickness(0, 0, 12, 12);
            grid.Children.Add(tile);
        }
        col.Children.Add(Ui.Col(10, Ui.Text(I18n.T("v20.acc.stats"), "h2"), grid));

        col.Children.Add(Card(I18n.T("v93.mk.studio.chart"), null, Chart(sales)));

        // Свои работы и последние продажи — рядом.
        var pair = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,2*"), ColumnSpacing = 16 };
        pair.Children.Add(Works(listings, sales, mods));
        var right = Recent(sales);
        Grid.SetColumn(right, 1);
        pair.Children.Add(right);
        col.Children.Add(pair);
        col.Children.Add(Ui.Row(10,
            Ui.Button(I18n.T("cr.refresh"), ReloadEarn, "ghost", Icons.Refresh),
            Ui.Text(I18n.T("v20.acc.fee", ("fee", Market.FeePercent)), "small muted")));
        return col;
    }

    /// <summary>Цвет бренда с прозрачностью — подложки значков и героя.</summary>
    static IBrush Tint(byte alpha) => Ui.Res("Brand") is ISolidColorBrush b ? new SolidColorBrush(Color.FromArgb(alpha, b.Color.R, b.Color.G, b.Color.B)) : Ui.Res("Surface2");

    /// <summary>Герой: заработано всего крупно, за 30 дней с ростом, справа — линия дохода за 30 дней и главные кнопки.</summary>
    Control Hero(long earned, long month, int? growth, Control spark)
    {
        var trend = growth is { } g
            ? new Border
            {
                Background = new SolidColorBrush(g >= 0 ? Color.FromArgb(40, 52, 199, 120) : Color.FromArgb(40, 240, 80, 80)), CornerRadius = new CornerRadius(20), Padding = new Thickness(10, 3),
                Child = Ui.Text((g >= 0 ? "▲ " : "▼ ") + I18n.T("v20.acc.growth", ("n", Math.Abs(g))), "small", color: g >= 0 ? Ui.Res("Good") : Ui.Res("Bad")),
            }
            : (Control)Ui.Text(I18n.T("v20.acc.growth.none"), "small muted");
        var left = Ui.Col(8,
            Ui.Text(I18n.T("v20.acc.total").ToUpper(I18n.Culture), "small muted"),
            new TextBlock { Text = Market.Money(earned), FontFamily = Gx.Display, FontSize = 46, FontWeight = FontWeight.Black, LetterSpacing = -1, Foreground = Ui.Res("Text") },
            Ui.Row(12, Ui.Text(I18n.T("v20.acc.month", ("sum", Market.Money(month))), "strong"), trend),
            Ui.Row(10,
                Ui.Button(I18n.T("acc.payout"), () => { _tab = "wallet"; Build(); }, "primary", Icons.Upload),
                Ui.Button(I18n.T("mk.sell"), () => ListingWizard.Show(null, ReloadEarn), "", Icons.Plus)));
        left.Children[^1].Margin = new Thickness(0, 8, 0, 0);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 28 };
        grid.Children.Add(left);
        spark.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(spark, 1);
        grid.Children.Add(spark);
        var brand = (Ui.Res("Brand") as ISolidColorBrush)?.Color ?? Colors.MediumPurple;
        return new Border
        {
            Classes = { "card" }, Padding = new Thickness(28, 24), ClipToBounds = true, BorderBrush = Tint(110), BorderThickness = new Thickness(1),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(70, brand.R, brand.G, brand.B), 0), new GradientStop(Color.FromArgb(10, brand.R, brand.G, brand.B), 0.7) },
            },
            Child = grid,
        };
    }

    /// <summary>Линия дохода по дням за 30 дней с заливкой под ней — в герое, без подписей.</summary>
    static Control Spark(List<Sale> sales)
    {
        var today = DateTime.UtcNow.Date;
        var sums = Enumerable.Range(0, 30).Select(i => (double)sales.Where(s => s.Created?.Date == today.AddDays(i - 29)).Sum(s => s.Net)).ToList();
        // Скользящее среднее за 3 дня — линия читается как тренд, а не как частокол.
        var smooth = sums.Select((_, i) => sums.Skip(Math.Max(0, i - 1)).Take(i == 0 ? 2 : 3).Average()).ToList();
        var max = Math.Max(1, smooth.Max());
        const double w = 300, h = 110;
        var pts = smooth.Select((v, i) => new Point(w * i / 29, h - 6 - (h - 12) * v / max)).ToList();
        var line = new Polyline { Points = pts, Stroke = Ui.Res("Brand2"), StrokeThickness = 2.5, StrokeJoin = PenLineJoin.Round };
        var fillPts = new List<Point>(pts) { new(w, h), new(0, h) };
        var brand = (Ui.Res("Brand") as ISolidColorBrush)?.Color ?? Colors.MediumPurple;
        var fill = new Polygon
        {
            Points = fillPts,
            Fill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(90, brand.R, brand.G, brand.B), 0), new GradientStop(Color.FromArgb(0, brand.R, brand.G, brand.B), 1) },
            },
        };
        var canvas = new Canvas { Width = w, Height = h, Children = { fill, line } };
        return new Viewbox { Stretch = Stretch.Fill, Height = h, Child = canvas };
    }

    /// <summary>«Мои работы»: лоты рынка и моды Hub; у лота наведите строку — изменить, скрыть или показать, удалить.</summary>
    Control Works(List<Listing> listings, List<Sale> sales, List<HubMod> mods)
    {
        var rows = Ui.Col(2);
        var all = BestWorks(listings, sales, mods);
        if (all.Count == 0)
            rows.Children.Add(Ui.Col(10, Ui.Text(I18n.T("v20.acc.noWorks"), "muted", wrap: true),
                Ui.Button(I18n.T("mk.sell"), () => ListingWizard.Show(null, ReloadEarn), "primary", Icons.Upload)));
        var top = all.Count > 0 ? Math.Max(1, all.Max(w => w.Revenue)) : 1;
        foreach (var w in all.Take(12))
        {
            var work = w;
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,96"), ColumnSpacing = 12 };
            var tag = Ui.Text(I18n.T(w.Source == "hub" ? "v20.acc.src.hub" : "v20.acc.src.market"), "small muted");
            var title = Ui.Row(8, Ui.Text(w.Title, "strong"), tag);
            if (w.Lot is { Active: false }) title.Children.Add(Ui.Text("· " + I18n.T("st.hiddenState"), "small muted"));
            var name = Ui.Col(5, title,
                new Border { Height = 3, CornerRadius = new CornerRadius(2), Background = Ui.Res("Brand"), HorizontalAlignment = HorizontalAlignment.Left, Width = w.Revenue == 0 ? 3 : Math.Max(6, 220.0 * w.Revenue / top) });
            line.Children.Add(name);
            // Действия лота — только при наведении (как плитки «Моих проектов»).
            var tools = Ui.Row(2);
            if (w.Lot is { } lot)
            {
                tools.Children.Add(Ui.Button("", () => MarketViews.Editor(lot, ReloadEarn), "icon ghost", Icons.Edit, I18n.T("mk.edit")));
                tools.Children.Add(Ui.Button("", async () =>
                {
                    try { await Market.SetHidden(lot, lot.Active); MainWindow.Current?.Toast(I18n.T(lot.Active ? "st.hiddenNow" : "st.shownNow")); }
                    catch (Exception e) { MainWindow.Current?.Toast(Market.Explain(e), bad: true); }
                    ReloadEarn();
                }, "icon ghost", lot.Active ? Icons.EyeOff : Icons.Eye, I18n.T(lot.Active ? "st.hide" : "st.show")));
                tools.Children.Add(Ui.Button("", () => DeleteListing(lot), "icon ghost", Icons.Trash, I18n.T("st.delete")));
            }
            tools.Opacity = 0;
            tools.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(tools, 1);
            line.Children.Add(tools);
            var count = Ui.Text(w.Sales > 0 ? I18n.T("mk.sold", ("n", w.Sales)) : I18n.T("v20.acc.dl", ("n", w.Downloads.ToString("N0", I18n.Culture))), "small muted");
            count.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(count, 2);
            line.Children.Add(count);
            var money = Ui.Text(w.Revenue > 0 ? "+" + Market.Money(w.Revenue) : "—", "strong", color: w.Revenue > 0 ? Ui.Res("Good") : null);
            money.HorizontalAlignment = HorizontalAlignment.Right;
            money.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(money, 3);
            line.Children.Add(money);
            var row = new Border { Padding = new Thickness(10, 8), CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Child = line };
            row.PointerEntered += (_, _) => { tools.Opacity = 1; row.Background = Ui.Res("Surface2"); };
            row.PointerExited += (_, _) => { tools.Opacity = 0; row.Background = Brushes.Transparent; };
            if (work.Lot is { } open) Ctx.Attach(row, () => Ctx.Menu(Ctx.Item(I18n.T("st.open"), Icons.Eye, () => MarketTiles.Open(open))));
            rows.Children.Add(row);
        }
        return Card(I18n.T("v20.acc.works"), null, rows);
    }

    static Control Recent(List<Sale> sales)
    {
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
        return Card(I18n.T("st.recentSales"), null, recent);
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

    /// <summary>Удалить лот; с продажами — только скрыть (покупатели сохраняют доступ).</summary>
    void DeleteListing(Listing l)
    {
        var w = MainWindow.Current!;
        if (l.Sales > 0)
        {
            w.Dialog(I18n.T("st.delete"), Ui.Text(I18n.T("st.delete.hasSales"), "muted", wrap: true),
                Ui.Button(I18n.T("common.close"), w.CloseDialog),
                Ui.Button(I18n.T("st.hide"), async () => { w.CloseDialog(); try { await Market.SetHidden(l, true); } catch (Exception e) { w.Toast(Market.Explain(e), bad: true); } ReloadEarn(); }, "primary", Icons.EyeOff));
            return;
        }
        w.Dialog(I18n.T("st.delete"), Ui.Text(I18n.T("st.delete.text", ("title", l.Title)), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("st.delete"), async () =>
            {
                w.CloseDialog();
                try { await Market.Delete(l); w.Toast(I18n.T("st.deleted")); } catch (Exception e) { w.Toast(Market.Explain(e), bad: true); }
                ReloadEarn();
            }, "primary", Icons.Trash));
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
        if (best[0].Title != "Alpha" || best[0].Revenue != 18000 || best[0].Lot != a) throw new Exception("best work " + best[0]);
        if (best.First(w => w.Title == "Free").Downloads != 40 || best.First(w => w.Title == "Mod") is not { Downloads: 120, Lot: null }) throw new Exception("downloads");
        if (Growth(150, 100) != 50 || Growth(50, 100) != -50 || Growth(10, 0) is not null) throw new Exception("growth");
        if (BestDay(sales) is not { Sum: 9000 }) throw new Exception("best day");
        return "works by revenue then downloads (market rows keep their lot); growth vs previous 30 days; best day";
    }
}
