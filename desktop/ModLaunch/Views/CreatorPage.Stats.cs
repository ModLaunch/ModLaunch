using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Games;
using ModLaunch.Social;

namespace ModLaunch.Views;

/// <summary>
/// 26.0: «Мои моды → Статистика» — панель автора: итоги (скачивания, лайки, комментарии своих модов Hub, продажи
/// и выручка на рынке), рост за 14 или 30 дней, таблица модов с приростом за неделю и свежие комментарии игроков.
/// Истории по дням на сервере нет: график строится из снимков счётчиков, которые ModLaunch копит на этом ПК
/// (CreatorStats), и честно пишет, с какого дня они копятся.
/// </summary>
public sealed partial class CreatorPage
{
    /// <summary>Всё для статистики одним снимком: свои моды, лоты, продажи, свежие комментарии, история и что нового.</summary>
    internal sealed record StatsData(string Uid, List<HubMod> Mods, List<Listing> Listings, long Sales, long? Revenue,
        List<(HubMod Mod, HubComment Comment)> Comments, JsonObject Days, IReadOnlyList<CreatorNote> Fresh, string? Error, DateTime At);

    static StatsData? _stats;
    static bool _statsLoading;
    static string _statsMetric = "downloads";
    static int _statsSpan = 30;
    /// <summary>Для снимков: готовая статистика без сети и входа.</summary>
    internal static StatsData? DemoStats;

    static readonly string[] StatsMetrics = ["downloads", "likes", "comments"];

    async Task LoadStats(bool force = false)
    {
        if (_statsLoading || !Account.SignedIn || Account.Uid is not { } uid) return;
        _statsLoading = true;
        // Как у LoadHub: из Build — без второй постройки внутри первой.
        var nested = _building;
        await Task.Yield();
        if (!nested) Build();
        string? error = null;
        List<HubMod> mods = [];
        List<Listing> listings = [];
        long? sales = null, revenue = null;
        var comments = new List<(HubMod, HubComment)>();
        // Не дочитали какой-то старый мод (сеть) — показываем, что есть, но в историю такой неполный итог не пишем.
        var complete = false;
        try { (mods, complete) = await CreatorWatch.Mine(uid, force); }
        catch (Exception e) { error = Hub.LastError ?? Firebase.Explain("creator", e); }
        try
        {
            listings = await Market.Mine();
            var paid = listings.Where(l => !l.Free && l.Sales > 0).ToList();
            sales = paid.Sum(l => l.Sales);
            revenue = paid.Count == 0 ? 0 : (await Market.AllSales(paid)).Sum(s => s.Net);
        }
        catch { /* рынок недоступен — итоги без продаж, моды Hub всё равно видны */ }
        // Свежие отзывы — у модов, которые обсуждают: до четырёх последних обновлённых с комментариями.
        foreach (var m in mods.Where(m => m.Comments > 0).OrderByDescending(m => m.Updated).Take(4))
        {
            try { comments.AddRange((await Hub.Comments(m.Id)).Where(c => c.Uid != uid).Take(5).Select(c => (m, c))); }
            catch { }
        }
        comments = comments.OrderByDescending(x => x.Item2.Created).Take(6).ToList();
        Dispatcher.UIThread.Post(() =>
        {
            _statsLoading = false;
            if (Account.Uid != uid) { RebuildCurrent(); return; }
            IReadOnlyList<CreatorNote> fresh = [];
            if (error is null && complete)
            {
                // Что прибавилось с прошлого просмотра — полоской сверху; раз статистику открыли, колокольчик гаснет.
                fresh = CreatorWatch.Observe(uid, mods, sales, revenue).ToList();
                CreatorWatch.MarkSeen();
            }
            _stats = new StatsData(uid, mods, listings, sales ?? CreatorWatch.LastMarket(uid)?.Sales ?? 0, revenue ?? CreatorWatch.LastMarket(uid)?.Revenue,
                comments, CreatorWatch.Days(uid), fresh, error, DateTime.UtcNow);
            RebuildCurrent();
        });
    }

    void ReloadStats()
    {
        _stats = null;
        _ = LoadStats(force: true);
    }

    Control StatsView()
    {
        var col = new StackPanel { Spacing = 18 };
        StatsData? data;
        if (Program.Screenshot) data = DemoStats;
        else if (!Account.SignedIn || Account.Uid is not { } uid)
        {
            col.Children.Add(HubLook.Empty(Icons.Chart, I18n.T("v26.features.stats.signin.title"), I18n.T("v26.features.stats.signin.text"),
                Ui.Button(I18n.T("acc.title"), () => MainWindow.Current?.Navigate(() => new SettingsPage("accounts")), "primary", Icons.User)));
            if (JourneyCard() is { } journey) col.Children.Add(journey);
            return col;
        }
        else
        {
            // Перечитать, если данных нет, они чужие, устарели (10 минут) или в колокольчике есть новое, которого тут ещё нет;
            // пока читаются — на экране прежние.
            var stale = _stats is null || _stats.Uid != uid || DateTime.UtcNow - _stats.At > TimeSpan.FromMinutes(10)
                || CreatorWatch.Notes.Count > 0 && CreatorWatch.NotesAt > _stats.At;
            if (stale && !_statsLoading) _ = LoadStats();
            data = _stats?.Uid == uid ? _stats : null;
        }
        if (data is null)
        {
            var wait = Ui.Row(10, Ui.Icon(Icons.Hourglass, 16, HubLook.Violet), Ui.Text(I18n.T("v26.features.stats.loading"), "muted"));
            col.Children.Add(HubLook.Frame(wait, new Thickness(20)));
            return col;
        }
        if (data.Error is not null && data.Mods.Count == 0)
        {
            col.Children.Add(HubLook.Empty(Icons.WifiOff, I18n.T("v26.features.stats.error"), data.Error,
                Ui.Button(I18n.T("cr.refresh"), ReloadStats, "primary", Icons.Refresh)));
            return col;
        }
        if (data.Mods.Count == 0 && data.Listings.Count == 0)
        {
            col.Children.Add(HubLook.Empty(Icons.Chart, I18n.T("v26.features.stats.empty.title"), I18n.T("v26.features.stats.empty.text"),
                Ui.Button(I18n.T("hub.upload"), () => { _minePart = "published"; Build(); }, "primary", Icons.Upload),
                Ui.Button(I18n.T("v22.hub.mods"), () => { _minePart = "projects"; Build(); }, "ghost", Icons.Grid)));
            if (JourneyCard() is { } journey) col.Children.Add(journey);
            return col;
        }

        var today = DateTime.Now.Date;
        var intro = Ui.Text(I18n.T("v26.features.stats.intro") + "  ·  " + I18n.T("v26.features.stats.updated", ("time", data.At.ToLocalTime().ToString("t", I18n.Culture))), "small muted", wrap: true);
        col.Children.Add(intro);
        if (data.Fresh.Count > 0) col.Children.Add(FreshStrip(data.Fresh));
        col.Children.Add(StatsTiles(data, today));
        col.Children.Add(GrowthCard(data, today));
        // Таблица модов — во всю ширину (у неё шесть столбцов); под ней рядом — комментарии и рынок.
        col.Children.Add(ModsTable(data, today));
        var feed = CommentsFeed(data);
        if (data.Listings.Count > 0 || data.Sales > 0)
        {
            var pair = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,2*"), ColumnSpacing = 16 };
            feed.VerticalAlignment = VerticalAlignment.Top;
            pair.Children.Add(feed);
            var market = MarketTile(data);
            market.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(market, 1);
            pair.Children.Add(market);
            col.Children.Add(pair);
        }
        else col.Children.Add(feed);
        if (JourneyCard() is { } done) col.Children.Add(done);
        return col;
    }

    /// <summary>Полоска «С прошлого визита»: ярлыки с приростом, по одному на уведомление.</summary>
    static Control FreshStrip(IReadOnlyList<CreatorNote> fresh)
    {
        var chips = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var n in fresh)
        {
            var (icon, text) = n.Kind switch
            {
                "sales" => (Icons.Bag, "+" + I18n.Compact(n.N)),
                "comments" => (Icons.Chat, $"+{I18n.Compact(n.N)} · {n.ModName}"),
                "likes" => (Icons.Heart, "+" + I18n.Compact(n.N)),
                _ => (Icons.Download, "+" + I18n.Compact(n.N)),
            };
            var chip = HubLook.Badge(text, "accent", icon);
            chip.Margin = new Thickness(0, 2, 8, 2);
            ToolTip.SetTip(chip, NoteTitle(n));
            chips.Children.Add(chip);
        }
        var label = Ui.Row(8, Ui.Icon(Icons.Bell, 15, HubLook.Violet), Ui.Text(I18n.T("v26.features.stats.fresh"), "strong"));
        label.Margin = new Thickness(0, 0, 14, 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        var row = new DockPanel();
        DockPanel.SetDock(label, Dock.Left);
        row.Children.Add(label);
        row.Children.Add(chips);
        return new Border { Background = HubLook.AmberSoft, CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 10), Child = row };
    }

    /// <summary>Текст уведомления автора (колокольчик, подсказка ярлыка).</summary>
    internal static string NoteTitle(CreatorNote n) => n.Kind switch
    {
        "sales" => I18n.T("v26.features.note.sales", ("n", I18n.Compact(n.N))),
        "comments" => I18n.T("v26.features.note.comments", ("n", I18n.Compact(n.N)), ("name", n.ModName ?? "")),
        "likes" => I18n.T("v26.features.note.likes", ("n", I18n.Compact(n.N))),
        _ => I18n.T("v26.features.note.downloads", ("n", I18n.Compact(n.N))),
    };

    /// <summary>Итоги: скачивания, лайки и комментарии (с приростом за 7 дней), продажи и выручка.</summary>
    Control StatsTiles(StatsData d, DateTime today)
    {
        var grid = new UniformGrid { Columns = d.Listings.Count > 0 || d.Sales > 0 ? 4 : 3, Margin = new Thickness(0, 0, -14, 0) };
        foreach (var (metric, icon) in new[] { ("downloads", Icons.Download), ("likes", Icons.Heart), ("comments", Icons.Chat) })
        {
            var total = d.Mods.Sum(m => ModCounts.Of(m).Get(metric));
            grid.Children.Add(Kpi(icon, I18n.Compact(total), I18n.T("v26.features.metric." + metric), Change(CreatorStats.Change(d.Days, today, 7, metric), today)));
        }
        if (grid.Columns == 4)
        {
            var money = d.Revenue is { } r ? HubLook.Badge(I18n.T("v26.features.stats.revenue", ("sum", Market.Money(r))), "neutral") : null;
            grid.Children.Add(Kpi(Icons.Bag, d.Sales.ToString("N0", I18n.Culture), I18n.T("v26.features.metric.sales"), money));
        }
        return grid;

        static Control Kpi(string icon, string value, string label, Control? change)
        {
            var mark = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(8), Background = HubLook.AmberSoft, Child = Ui.Icon(icon, 15, HubLook.Violet) };
            var head = Ui.Row(10, mark, Ui.Text(label, "muted"));
            var number = new TextBlock { Text = value, FontFamily = Look.Display, FontSize = 28, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 2, 0, 0) };
            var body = Ui.Col(8, head, number);
            if (change is not null) body.Children.Add(change);
            var tile = HubLook.Frame(body, new Thickness(16, 14));
            tile.Margin = new Thickness(0, 0, 14, 0);
            return tile;
        }
    }

    /// <summary>Ярлык прироста за неделю: «+340 за 7 дней», при короткой истории — «+40 с 9 нояб.», без истории — «История копится».</summary>
    static Control Change((long Delta, DateTime Since)? change, DateTime today)
    {
        if (change is not { } c) return HubLook.Badge(I18n.T("v26.features.stats.nohistory"), "neutral", Icons.Hourglass);
        var n = (c.Delta >= 0 ? "+" : "−") + I18n.Compact(Math.Abs(c.Delta));
        var text = c.Since <= today.AddDays(-7)
            ? I18n.T("v26.features.stats.week", ("n", n))
            : I18n.T("v26.features.stats.weekSince", ("n", n), ("date", c.Since.ToString("d MMM", I18n.Culture)));
        return HubLook.Badge(text, c.Delta > 0 ? "good" : "neutral", Icons.ArrowUp);
    }

    /// <summary>Рост за 14 или 30 дней: выбор метрики и срока, линия итога по дням и честная подпись, с какого дня копится история.</summary>
    Control GrowthCard(StatsData d, DateTime today)
    {
        var chips = Ui.Row(6);
        foreach (var metric in StatsMetrics)
        {
            var m = metric;
            var chip = Ui.Button(I18n.T("v26.features.metric." + m), () => { _statsMetric = m; Build(); }, "chip");
            if (_statsMetric == m) chip.Classes.Add("active");
            chips.Children.Add(chip);
        }
        chips.Children.Add(new Border { Width = 1, Height = 20, Background = HubLook.Edge, Margin = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Center });
        foreach (var span in new[] { 14, 30 })
        {
            var s = span;
            var chip = Ui.Button(I18n.T("v26.features.stats.days", ("n", s)), () => { _statsSpan = s; Build(); }, "chip");
            if (_statsSpan == s) chip.Classes.Add("active");
            chips.Children.Add(chip);
        }
        var series = CreatorStats.Series(d.Days, today, _statsSpan, _statsMetric);
        var known = series.Where(p => p.Value is not null).ToList();
        var since = CreatorStats.Since(d.Days);
        var head = HubLook.Head("", I18n.T("v26.features.stats.chart", ("n", _statsSpan)), chips);
        var body = Ui.Col(14, head);
        if (series.Count(p => p.Real) >= 2 && known.Count >= 2)
        {
            var gain = known[^1].Value!.Value - known[0].Value!.Value;
            // Подпись — по нижнему краю крупного числа (по центру она висела выше строки).
            var period = Ui.Text(I18n.T("v26.features.stats.gain"), "muted");
            period.VerticalAlignment = VerticalAlignment.Bottom;
            period.Margin = new Thickness(0, 0, 0, 3);
            body.Children.Add(Ui.Row(12,
                new TextBlock { Text = (gain >= 0 ? "+" : "−") + Math.Abs(gain).ToString("N0", I18n.Culture), FontFamily = Look.Display, FontSize = 22, FontWeight = FontWeight.Bold, Foreground = HubLook.Violet },
                period));
            body.Children.Add(LineChart(series));
        }
        else
        {
            // Снимок пока один: рисовать нечего — так и говорим, без выдуманной линии.
            var wait = Ui.Col(8, Ui.Icon(Icons.Hourglass, 22, HubLook.Violet),
                new TextBlock { Text = I18n.T("v26.features.stats.short", ("date", (since ?? today).ToString("d MMMM", I18n.Culture))), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 460, Foreground = Ui.Res("Muted") });
            foreach (var c in wait.Children) c.HorizontalAlignment = HorizontalAlignment.Center;
            body.Children.Add(new Border { Background = HubLook.Raised, CornerRadius = new CornerRadius(10), Height = 190, Child = wait, Padding = new Thickness(20) });
            wait.VerticalAlignment = VerticalAlignment.Center;
        }
        body.Children.Add(Ui.Row(8, Ui.Icon(Icons.Info, 13, Ui.Res("Faint")),
            Ui.Text(I18n.T("v26.features.stats.since", ("date", (since ?? today).ToString("d MMMM yyyy", I18n.Culture))), "small muted", wrap: true)));
        return HubLook.Frame(body, new Thickness(20, 18));
    }

    /// <summary>
    /// Линия итога по дням (янтарём, с мягкой заливкой) на тонкой сетке; точки — дни, когда снимок был (в остальные
    /// линия держит прошлое значение). Наведите день — дата, итог и прирост. Перерисовывается по ширине.
    /// </summary>
    static Control LineChart(List<(DateTime Day, long? Value, bool Real)> series)
    {
        const double H = 190, Top = 10, Bottom = 10;
        var values = series.Where(p => p.Value is not null).Select(p => p.Value!.Value).ToList();
        double lo = values.Min(), hi = values.Max();
        if (hi - lo < 1) { hi = lo + Math.Max(1, lo * 0.1); lo = Math.Max(0, lo - (hi - lo)); }
        else lo = Math.Max(0, lo - (hi - lo) * 0.15);
        var canvas = new Canvas { Height = H, ClipToBounds = false };
        var amber = HubLook.AmberColor;
        double Y(long v) => Top + (H - Top - Bottom) * (1 - (v - lo) / (hi - lo));

        void Draw(double w)
        {
            canvas.Children.Clear();
            if (w <= 0) return;
            for (var i = 0; i < 4; i++)
            {
                var line = new Border { Width = w, Height = 1, Background = HubLook.Edge };
                Canvas.SetTop(line, Top + (H - Top - Bottom) * i / 3.0);
                canvas.Children.Add(line);
            }
            double X(int i) => series.Count == 1 ? w / 2 : w * i / (series.Count - 1);
            var pts = new List<Point>();
            for (var i = 0; i < series.Count; i++)
                if (series[i].Value is { } v) pts.Add(new Point(X(i), Y(v)));
            if (pts.Count == 0) return;
            var fill = new Polygon
            {
                Points = [.. pts, new Point(pts[^1].X, H), new Point(pts[0].X, H)],
                Fill = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.FromArgb(70, amber.R, amber.G, amber.B), 0), new GradientStop(Color.FromArgb(0, amber.R, amber.G, amber.B), 1) },
                },
            };
            canvas.Children.Add(fill);
            canvas.Children.Add(new Polyline { Points = pts, Stroke = HubLook.Violet, StrokeThickness = 2.5, StrokeJoin = PenLineJoin.Round, StrokeLineCap = PenLineCap.Round });
            // Подсветка дня под курсором: вертикальная черта и точка.
            var guide = new Border { Width = 1, Height = H, Background = HubLook.EdgeHi, IsVisible = false, IsHitTestVisible = false };
            var focus = new Ellipse { Width = 10, Height = 10, Fill = HubLook.Violet, Stroke = HubLook.Panel, StrokeThickness = 2, IsVisible = false, IsHitTestVisible = false };
            for (var i = 0; i < series.Count; i++)
            {
                if (series[i] is not { Value: { } v, Real: true }) continue;
                var dot = new Ellipse { Width = 7, Height = 7, Fill = HubLook.Panel, Stroke = HubLook.Violet, StrokeThickness = 2, IsHitTestVisible = false };
                Canvas.SetLeft(dot, X(i) - 3.5);
                Canvas.SetTop(dot, Y(v) - 3.5);
                canvas.Children.Add(dot);
            }
            canvas.Children.Add(guide);
            canvas.Children.Add(focus);
            var slot = series.Count == 1 ? w : w / (series.Count - 1);
            long? prev = null;
            for (var i = 0; i < series.Count; i++)
            {
                var (day, value, _) = series[i];
                var x = X(i);
                var hit = new Border { Width = slot, Height = H, Background = Brushes.Transparent };
                Canvas.SetLeft(hit, x - slot / 2);
                var tip = day.ToString("d MMMM", I18n.Culture) + " · " + (value is { } val ? val.ToString("N0", I18n.Culture) : "—");
                if (value is { } now && prev is { } before && now != before) tip += $"  ({(now > before ? "+" : "−")}{Math.Abs(now - before).ToString("N0", I18n.Culture)})";
                ToolTip.SetTip(hit, tip);
                ToolTip.SetShowDelay(hit, 0);
                if (value is { } y)
                {
                    hit.PointerEntered += (_, _) =>
                    {
                        Canvas.SetLeft(guide, x);
                        Canvas.SetLeft(focus, x - 5);
                        Canvas.SetTop(focus, Y(y) - 5);
                        guide.IsVisible = focus.IsVisible = true;
                    };
                    hit.PointerExited += (_, _) => guide.IsVisible = focus.IsVisible = false;
                }
                canvas.Children.Add(hit);
                if (value is not null) prev = value;
            }
        }
        canvas.SizeChanged += (_, e) => { if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 0.5) Draw(e.NewSize.Width); };

        // Подписи: слева — верх и низ шкалы, снизу — первый день, середина и «Сегодня».
        var scale = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Height = H, Margin = new Thickness(0, 0, 12, 0) };
        var top = HubLook.MonoText(I18n.Compact((long)Math.Round(hi)), 10.5);
        var low = HubLook.MonoText(I18n.Compact((long)Math.Round(lo)), 10.5);
        top.HorizontalAlignment = low.HorizontalAlignment = HorizontalAlignment.Right;
        top.Margin = new Thickness(0, Top - 8, 0, 0);
        low.Margin = new Thickness(0, 0, 0, Bottom - 8);
        Grid.SetRow(low, 2);
        scale.Children.Add(top);
        scale.Children.Add(low);
        var axis = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), Margin = new Thickness(0, 8, 0, 0) };
        var first = HubLook.MonoText(series[0].Day.ToString("d MMM", I18n.Culture), 10.5);
        var mid = HubLook.MonoText(series[series.Count / 2].Day.ToString("d MMM", I18n.Culture), 10.5);
        var last = HubLook.MonoText(I18n.T("v26.features.stats.today"), 10.5);
        mid.HorizontalAlignment = HorizontalAlignment.Center;
        last.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(mid, 1);
        Grid.SetColumn(last, 2);
        axis.Children.Add(first);
        axis.Children.Add(mid);
        axis.Children.Add(last);
        var chart = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto") };
        chart.Children.Add(scale);
        Grid.SetColumn(canvas, 1);
        chart.Children.Add(canvas);
        Grid.SetColumn(axis, 1);
        Grid.SetRow(axis, 1);
        chart.Children.Add(axis);
        return chart;
    }

    /// <summary>Маленькая линия скачиваний мода за 14 дней — в строке таблицы.</summary>
    static Control Spark(List<(DateTime Day, long? Value, bool Real)> series)
    {
        const double W = 84, H = 26;
        var values = series.Where(p => p.Value is not null).Select(p => p.Value!.Value).ToList();
        if (values.Count < 2 || series.Count(p => p.Real) < 2) return new Border { Width = W, Height = H, Child = new Border { Height = 1, Background = HubLook.Edge, VerticalAlignment = VerticalAlignment.Center } };
        double lo = values.Min(), hi = Math.Max(values.Max(), lo + 1);
        var pts = new List<Point>();
        for (var i = 0; i < series.Count; i++)
            if (series[i].Value is { } v) pts.Add(new Point(W * i / (series.Count - 1), H - 2 - (H - 4) * (v - lo) / (hi - lo)));
        return new Canvas { Width = W, Height = H, Children = { new Polyline { Points = pts, Stroke = HubLook.Violet, StrokeThickness = 1.8, StrokeJoin = PenLineJoin.Round } } };
    }

    /// <summary>Свои моды Hub: игра, скачивания, лайки и комментарии с приростом за неделю, линия за 14 дней, открыть и ссылка.</summary>
    Control ModsTable(StatsData d, DateTime today)
    {
        const string cols = "*,120,110,120,110,80";
        var rows = Ui.Col(0);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions(cols), ColumnSpacing = 12, Margin = new Thickness(10, 0, 10, 6) };
        var labels = new[] { "", I18n.T("v26.features.metric.downloads"), I18n.T("v26.features.metric.likes"), I18n.T("v26.features.metric.comments"), I18n.T("v26.features.stats.days", ("n", 14)) };
        for (var i = 1; i < labels.Length; i++)
        {
            var l = HubLook.Label(labels[i], size: 10.5);
            Grid.SetColumn(l, i);
            header.Children.Add(l);
        }
        rows.Children.Add(header);
        var list = d.Mods.OrderByDescending(m => m.Downloads).ThenBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        for (var n = 0; n < list.Count; n++)
        {
            var m = list[n];
            var game = GameCatalog.ById(m.Game);
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions(cols), ColumnSpacing = 12 };
            var art = new Border
            {
                Width = 40, Height = 40, CornerRadius = new CornerRadius(8), ClipToBounds = true, Background = HubLook.Raised,
                Child = m.Images.FirstOrDefault() is { } img ? Ui.Thumb(img, m.Name, 40, 8) : game is not null ? Ui.GameImage(game, 320, art: Images.Art.Hero) : Ui.Icon(Icons.Code, 18, Ui.Res("Faint")),
            };
            var name = Ui.Text(m.Name, "strong");
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            var sub = Ui.Text($"{game?.Name ?? m.Game} · v{m.Version}", "small muted");
            sub.TextTrimming = TextTrimming.CharacterEllipsis;
            var who = new DockPanel();
            art.Margin = new Thickness(0, 0, 12, 0);
            DockPanel.SetDock(art, Dock.Left);
            who.Children.Add(art);
            var words = Ui.Col(2, name, sub);
            words.VerticalAlignment = VerticalAlignment.Center;
            who.Children.Add(words);
            line.Children.Add(who);
            var c = 1;
            foreach (var (metric, icon) in new[] { ("downloads", Icons.Download), ("likes", Icons.Heart), ("comments", Icons.Chat) })
            {
                var change = CreatorStats.Change(d.Days, today, 7, metric, m.Id);
                var delta = change is { Delta: > 0 } ch
                    ? Ui.Text("+" + I18n.Compact(ch.Delta), "small", color: Ui.Res("Good"))
                    : Ui.Text("—", "small", color: Ui.Res("Faint"));
                var cell = Ui.Col(2, HubLook.Counter(icon, ModCounts.Of(m).Get(metric), Ui.Res("Text")), delta);
                cell.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(cell, c++);
                line.Children.Add(cell);
            }
            var spark = Spark(CreatorStats.Series(d.Days, today, 14, "downloads", m.Id));
            spark.VerticalAlignment = VerticalAlignment.Center;
            spark.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetColumn(spark, 4);
            line.Children.Add(spark);
            var mod = m;
            var tools = Ui.Row(2,
                Ui.Button("", () => OpenMod(mod), "icon ghost", Icons.Eye, I18n.T("hub.open")),
                Ui.Button("", () => _ = CopyShare(mod), "icon ghost", Icons.Link, I18n.T("hub.share")));
            tools.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(tools, 5);
            line.Children.Add(tools);
            var row = new Border
            {
                Padding = new Thickness(10, 9), CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Child = line,
                BorderBrush = HubLook.Edge, BorderThickness = new Thickness(0, n == 0 ? 1 : 0, 0, 0),
            };
            row.PointerEntered += (_, _) => row.Background = HubLook.Raised;
            row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
            rows.Children.Add(row);
        }
        if (list.Count == 0) rows.Children.Add(Ui.Text(I18n.T("v26.features.stats.nomods"), "muted", wrap: true));
        return HubLook.Frame(Ui.Col(12, HubLook.Head("", I18n.T("v26.features.stats.table")), rows), new Thickness(18, 16));
    }

    /// <summary>Скопировать ссылку на свой мод (как «Поделиться» на странице мода) — и отметить шаг «Поделиться».</summary>
    async Task CopyShare(HubMod m)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clip) await clip.SetTextAsync(CreatorStats.ShareUrl(m.Id));
        CreatorWatch.MarkShared();
        MainWindow.Current?.Toast(I18n.T("hub.shared"));
    }

    /// <summary>Открыть мод сразу на вкладке комментариев (если его игра есть в библиотеке).</summary>
    static void OpenComments(HubMod m)
    {
        if (AppState.Games.Any(g => g.Def.Id == m.Game)) MainWindow.Current?.Navigate(() => new ModPage(m.Game, Hub.ToModInfo(m), "reviews"));
        else OpenMod(m);
    }

    /// <summary>Свежие комментарии игроков к своим модам (свои ответы не считаются); щелчок — к комментариям мода.</summary>
    static Control CommentsFeed(StatsData d)
    {
        var list = Ui.Col(2);
        if (d.Comments.Count == 0) list.Children.Add(Ui.Text(I18n.T("v26.features.stats.comments.none"), "muted", wrap: true));
        foreach (var (m, c) in d.Comments)
        {
            var mod = m;
            var head = new DockPanel();
            var when = Ui.Text(Ui.Ago(c.Created), "small", color: Ui.Res("Faint"));
            when.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(when, Dock.Right);
            head.Children.Add(when);
            var whoText = Ui.Text(c.Author, "strong");
            var onMod = Ui.Text("· " + m.Name, "small muted");
            onMod.TextTrimming = TextTrimming.CharacterEllipsis;
            onMod.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(Ui.Row(6, whoText, onMod));
            var text = new TextBlock { Text = c.Text, TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 13 };
            var avatar = ProfileKit.Avatar(c.Author, 30);
            avatar.VerticalAlignment = VerticalAlignment.Top;
            var item = new DockPanel();
            avatar.Margin = new Thickness(0, 2, 10, 0);
            DockPanel.SetDock(avatar, Dock.Left);
            item.Children.Add(avatar);
            item.Children.Add(Ui.Col(3, head, text));
            var b = new Button { Classes = { "hub-row" }, Content = item, Padding = new Thickness(8, 8) };
            b.Click += (_, _) => OpenComments(mod);
            list.Children.Add(b);
        }
        return HubLook.Frame(Ui.Col(10, HubLook.Head("", I18n.T("v26.features.stats.comments")), list), new Thickness(18, 16));
    }

    /// <summary>Рынок: продажи и выручка своих лотов; подробности — в «Аккаунт → Заработок».</summary>
    static Control MarketTile(StatsData d)
    {
        var stats = Ui.Row(28,
            HubLook.Stat(d.Sales.ToString("N0", I18n.Culture), I18n.T("v26.features.metric.sales"), size: 22),
            HubLook.Stat(d.Revenue is { } r ? Market.Money(r) : "—", I18n.T("v26.features.stats.revenue.label"), HubLook.Violet, 22));
        var open = Ui.Button(I18n.T("v26.features.stats.earnings"), () => MainWindow.Current?.Navigate(() => new AccountPage("earnings")), "ghost", Icons.External);
        open.HorizontalAlignment = HorizontalAlignment.Left;
        return HubLook.Frame(Ui.Col(12, HubLook.Head("", I18n.T("v26.features.stats.market")), stats, open), new Thickness(18, 16));
    }
}
