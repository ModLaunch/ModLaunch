using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Games;

namespace ModLaunch.Views;

/// <summary>
/// 9.3: страница лота рынка — как страница игры в Steam: большая картинка и превью слева,
/// капсула, сводка отзывов, даты, автор и метки справа; ниже — блок покупки, описание,
/// «Что входит», отзывы, «Ещё от автора» и похожие лоты.
/// </summary>
public sealed class ListingPage : Page
{
    readonly Listing _l;
    bool? _owned;
    List<ListingReview>? _reviews;
    int _media;
    bool _busy;

    public ListingPage(Listing l)
    {
        _l = l;
        MarketLocal.Viewed(l.Id);
        if (!Program.Screenshot) _ = Load();
        else { _owned = l.Mine || Market.Owned.Contains(l.Id); _reviews = DemoReviews(l); }
        MarketLocal.Changed += OnChanged;
        DetachedFromVisualTree += (_, _) => MarketLocal.Changed -= OnChanged;
    }

    void OnChanged() { if (MainWindow.Current?.CurrentPage == this) Build(); }

    public override string Title => _l.Title;
    public override string SearchHint => I18n.T("v93.mk.search");
    public override void Search(string text) => MainWindow.Current?.Navigate(() => new MarketPage("browse", text));
    public override string? Accent => GameCatalog.ById(_l.Game)?.Accent;

    public override IEnumerable<(string Text, Action? Open)> Crumbs =>
    [
        ("Creator Hub", () => MainWindow.Current?.Navigate(() => new MarketPage())),
        (I18n.T("mk.kind." + _l.Kind), () => MainWindow.Current?.Navigate(() => new MarketPage("browse"))),
        (_l.Title, null),
    ];

    async Task Load()
    {
        try { _owned = await Market.Bought(_l); } catch { _owned = false; }
        Dispatcher.UIThread.Post(() => { if (MainWindow.Current?.CurrentPage == this) Build(); });
        try { _reviews = await Market.Reviews(_l); } catch { _reviews = []; }
        Dispatcher.UIThread.Post(() => { if (MainWindow.Current?.CurrentPage == this) Build(); });
    }

    static List<ListingReview> DemoReviews(Listing l) =>
    [
        new("a", "Kira", 5, "Подключил за пять минут, всё как в описании. Автор отвечает быстро.", DateTime.UtcNow.AddDays(-2)),
        new("b", "Vega", 5, "Отличное качество, беру второй раз у этого автора.", DateTime.UtcNow.AddDays(-5)),
        new("c", "Nox", 4, "Хорошо, но хотелось бы ещё пару вариантов.", DateTime.UtcNow.AddDays(-9)),
    ];

    bool Owned => _owned == true || _l.Mine;

    public override void Build()
    {
        var l = _l;
        var col = StoreKit.Column(spacing: 22, top: 22);

        // Заголовок: название крупно, рядом — тип, игра и автор.
        var title = Gx.Title(l.Title, 32, Ui.Res("Text"));
        var meta = Ui.Row(8, StoreKit.Pill(I18n.T("mk.kind." + l.Kind), onArt: false), StoreKit.Pill(MarketTiles.GameName(l.Game), onArt: false), StoreKit.Pill(I18n.T("mk.license." + l.License), onArt: false));
        col.Children.Add(Ui.Col(10, Gx.Eyebrow("Creator Hub · " + I18n.T("mk.kind." + l.Kind), Ui.Res("Brand2")), title, meta));

        // Медиа слева, сведения справа.
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*"), ColumnSpacing = 18 };
        top.Children.Add(Media(l));
        var side = Side(l);
        Grid.SetColumn(side, 1);
        top.Children.Add(side);
        col.Children.Add(top);

        col.Children.Add(BuyBox(l));

        // Описание и отзывы слева, «Что входит», автор и похожие справа.
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*"), ColumnSpacing = 18 };
        var left = Ui.Col(22);
        left.Children.Add(Ui.Col(10, Section(I18n.T("v93.mk.about")), new SelectableTextBlock { Text = l.Description == "" ? l.Summary : l.Description, TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 22, Foreground = Ui.Res("Text") }));
        if (l.Preview != "") left.Children.Add(Ui.Col(10, Section(I18n.T("mk.preview")), Code(l.Preview)));
        left.Children.Add(Reviews(l));
        body.Children.Add(left);
        var right = Ui.Col(16, Contents(l), AuthorBox(l), Similar(l));
        Grid.SetColumn(right, 1);
        body.Children.Add(right);
        col.Children.Add(body);

        var more = MarketData.All.Where(x => x.Uid == l.Uid && x.Id != l.Id && x.Active).OrderByDescending(x => x.Sales).Take(10).ToList();
        if (more.Count > 0)
            col.Children.Add(Ui.Col(14, StoreKit.Header(I18n.T("v93.mk.moreFrom", ("name", l.Author)), () => MainWindow.Current?.Navigate(() => new AuthorPage(l.Uid, l.Author))),
                StoreKit.Shelf(more.Select(x => MarketTiles.Card(x, 248)))));
        Content = new ScrollViewer { Content = col, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    static Control Section(string title) => Ui.Col(6, Gx.Eyebrow(title, Ui.Res("Text"), 12), new Border { Height = 1, Background = Ui.Res("Line") });

    static readonly FontFamily Mono = new("Cascadia Mono, Consolas, JetBrains Mono, DejaVu Sans Mono, monospace");

    static Control Code(string text) => new Border
    {
        Classes = { "card" }, Padding = new Thickness(16, 14), MaxHeight = 320,
        Child = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new SelectableTextBlock { Text = text, FontFamily = Mono, FontSize = 12.5, LineHeight = 19 } },
    };

    /// <summary>Большая картинка и полоска миниатюр (картинки лота и «Код», если есть превью).</summary>
    Control Media(Listing l)
    {
        var items = new List<(string Kind, string? Url)>();
        foreach (var u in l.Images) items.Add(("image", u));
        if (items.Count == 0) items.Add(("art", null));
        if (l.Preview != "") items.Add(("code", null));
        _media = Math.Clamp(_media, 0, items.Count - 1);
        var (kind, url) = items[_media];
        Control main = kind switch
        {
            "image" => StoreKit.UrlImage(url, 1600),
            "code" => new Border { Background = Ui.Res("Surface2"), Padding = new Thickness(22, 18), Child = new TextBlock { Text = l.Preview, FontFamily = Mono, FontSize = 13, LineHeight = 20, Foreground = Ui.Res("Text"), TextWrapping = TextWrapping.NoWrap } },
            _ => MarketTiles.Art(l, 1600),
        };
        var stage = new Border { Classes = { "store-tile", "hero" }, Height = 392, Cursor = null, Child = new Panel { Children = { main, kind == "code" ? new Border() : Gx.Haze(true) } } };
        if (items.Count == 1) return stage;
        var strip = Ui.Row(8);
        for (var i = 0; i < items.Count; i++)
        {
            var index = i;
            var (k, u) = items[i];
            Control thumb = k switch
            {
                "image" => StoreKit.UrlImage(u, 256),
                "code" => new Border { Background = Ui.Res("Surface2"), Child = Ui.Col(2, Ui.Icon(Icons.Code, 18, Ui.Res("Brand2")), new TextBlock { Text = I18n.T("v93.mk.code"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center }) },
                _ => MarketTiles.Art(l, 256),
            };
            if (thumb is Border { Child: StackPanel sp }) { sp.HorizontalAlignment = HorizontalAlignment.Center; sp.VerticalAlignment = VerticalAlignment.Center; }
            var b = new Border
            {
                Width = 112, Height = 63, CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = thumb, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                BorderThickness = new Thickness(2), BorderBrush = i == _media ? Ui.Res("Brand") : Brushes.Transparent, Opacity = i == _media ? 1 : 0.7,
            };
            StoreKit.OnClick(b, () => { _media = index; Build(); });
            strip.Children.Add(b);
        }
        return Ui.Col(10, stage, new ScrollViewer { Content = strip, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
    }

    /// <summary>Правая колонка: капсула, кратко, отзывы, даты, автор, метки.</summary>
    Control Side(Listing l)
    {
        var capsule = new Border { Height = 150, CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = MarketTiles.Art(l, 768, hero: false) };
        Control Line(string label, Control value)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*") };
            var t = new TextBlock { Text = label.ToUpper(I18n.Culture), FontSize = 10.5, FontWeight = FontWeight.Bold, LetterSpacing = 1, Foreground = Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Center };
            grid.Children.Add(t);
            Grid.SetColumn(value, 1);
            grid.Children.Add(value);
            return grid;
        }
        var reviews = _reviews ?? [];
        var (verdict, tone) = MarketLocal.Verdict(reviews.Count == 0 ? 0 : reviews.Average(r => r.Stars), reviews.Count);
        var toneBrush = tone switch { "good" => Ui.Res("Good"), "warn" => Ui.Res("Warn"), "bad" => Ui.Res("Bad"), _ => Ui.Res("Muted") };
        var reviewText = _reviews is null ? I18n.T("common.loading") : reviews.Count == 0 ? I18n.T(verdict) : $"{I18n.T(verdict)} ({reviews.Count})";
        var author = new TextBlock { Text = l.Author, FontWeight = FontWeight.SemiBold, Foreground = Ui.Res("Brand2"), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand), TextTrimming = TextTrimming.CharacterEllipsis };
        StoreKit.OnClick(author, () => MainWindow.Current?.Navigate(() => new AuthorPage(l.Uid, l.Author)));
        var tags = new WrapPanel();
        foreach (var t in l.Tags)
        {
            var tag = t;
            var chip = new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 3), Margin = new Thickness(0, 0, 6, 6), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand), Child = new TextBlock { Text = I18n.T("hub.tag." + t), FontSize = 11.5, Foreground = Ui.Res("Muted") } };
            tags.Children.Add(chip);
        }
        var col = Ui.Col(14,
            capsule,
            new TextBlock { Text = l.Summary, TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 20, Foreground = Ui.Res("Text") },
            Line(I18n.T("v93.mk.reviews"), new TextBlock { Text = reviewText, FontWeight = FontWeight.SemiBold, Foreground = toneBrush, TextTrimming = TextTrimming.CharacterEllipsis }),
            Line(I18n.T("v93.mk.released"), Ui.Text(l.Created?.ToLocalTime().ToString("d MMM yyyy", I18n.Culture) ?? "—", "small")),
            Line(I18n.T("v93.mk.updated"), Ui.Text(Ui.Ago(l.Updated), "small")),
            Line(I18n.T("v93.mk.author"), author));
        if (tags.Children.Count > 0) col.Children.Add(tags);
        return new Border { Classes = { "card" }, Padding = new Thickness(16), Child = col };
    }

    /// <summary>Блок покупки: «Купить „Название“», цена, кнопка; «В желаемое» и подписка на автора.</summary>
    Control BuyBox(Listing l)
    {
        var heading = Gx.Title(Owned ? I18n.T("v93.mk.yours", ("title", l.Title)) : l.Free ? I18n.T("v93.mk.get", ("title", l.Title)) : I18n.T("v93.mk.buyTitle", ("title", l.Title)), 19, Ui.Res("Text"), 1);
        var note = Ui.Text(Owned ? I18n.T("v93.mk.yours.text") : l.Free ? I18n.T("v93.mk.free.text") : I18n.T("mk.feeNote", ("fee", Market.FeePercent)), "small muted", wrap: true);
        Button main;
        if (Owned) main = Ui.Button(I18n.T("mk.download"), () => MarketViews.Download(l), "primary", Icons.Download);
        else main = Ui.Button(l.Free ? I18n.T("mk.getFree") : I18n.T("v93.mk.buyNow"), Buy, "primary", l.Free ? Icons.Download : Icons.Bag);
        main.Padding = new Thickness(26, 12);
        main.IsEnabled = !_busy && _owned is not null;
        var wished = MarketLocal.Wished(l.Id);
        var wish = Ui.Button(I18n.T(wished ? "v93.mk.wish.in" : "v93.mk.wish.add"), () => MarketLocal.ToggleWish(l.Id), "", Icons.Heart);
        if (wished) wish.Foreground = Ui.Res("Bad");
        var price = Owned ? Gx.Tag(I18n.T("v93.mk.owned"), Ui.Res("Surface3"), Ui.Res("Text"), 15) : Gx.Price(MarketViews.PriceText(l), l.Free);
        if (price is Border pb) pb.Padding = new Thickness(14, 7);
        price.VerticalAlignment = VerticalAlignment.Center;
        var right = Ui.Row(12, wish, price, main);
        right.VerticalAlignment = VerticalAlignment.Center;
        var words = Ui.Col(4, heading, note);
        words.VerticalAlignment = VerticalAlignment.Center;
        var dock = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(right);
        dock.Children.Add(words);
        var card = new Border { Classes = { "card", "buy-box" }, Padding = new Thickness(22, 18), Child = dock, BorderBrush = Ui.Res("Brand"), BoxShadow = Gx.Glow(60, 30, -6) };
        return card;
    }

    async void Buy()
    {
        var w = MainWindow.Current!;
        if (!Social.Account.SignedIn) { w.Navigate(() => new AccountPage()); return; }
        _busy = true;
        Build();
        try
        {
            await Market.Buy(_l);
            _owned = true;
            w.Toast(I18n.T("mk.bought", ("title", _l.Title)));
            // Купили — та же анимация «получено», что и у установки мода.
            InstallFx.Play(new FxCard(_l.Title, I18n.T("v93.mk.fx.bought"), _l.Images.FirstOrDefault(), GameCatalog.ById(_l.Game), Icons.Bag), null, null);
            MarketData.Reload();
        }
        catch (Exception e)
        {
            if (e is Social.ServiceError { Code: "NO_MONEY" })
                w.Dialog(I18n.T("mk.noMoney.title"), Ui.Text(I18n.T("mk.noMoney.text", ("price", Market.Money(_l.Price))), "muted", wrap: true),
                    Ui.Button(I18n.T("common.close"), w.CloseDialog),
                    Ui.Button(I18n.T("acc.wallet.topup"), () => { w.CloseDialog(); w.Navigate(() => new AccountPage("wallet")); }, "primary", Icons.Plus));
            else w.Toast(Market.Explain(e), bad: true);
        }
        _busy = false;
        Build();
    }

    static Control Contents(Listing l)
    {
        Control Row(string icon, string label, string value)
        {
            var d = new DockPanel();
            var v = Ui.Text(value, "small");
            v.TextTrimming = TextTrimming.CharacterEllipsis;
            DockPanel.SetDock(v, Dock.Right);
            d.Children.Add(v);
            d.Children.Add(Ui.Row(8, Ui.Icon(icon, 14, Ui.Res("Muted")), Ui.Text(label, "small muted")));
            return d;
        }
        var col = Ui.Col(10, Gx.Eyebrow(I18n.T("v93.mk.contents"), Ui.Res("Text"), 11),
            Row(Icons.Package, I18n.T("v93.mk.file"), l.FileName == "" ? "—" : l.FileName),
            Row(Icons.Save, I18n.T("v93.mk.size"), l.Size > 0 ? GamePage.Size(l.Size) : "—"),
            Row(Icons.Info, I18n.T("hub.f.version"), l.Version),
            Row(Icons.Shield, I18n.T("mk.f.license"), I18n.T("mk.license." + l.License)),
            Row(Icons.Gamepad, I18n.T("mk.f.game"), MarketTiles.GameName(l.Game)),
            Row(Icons.Trophy, I18n.T("st.sales"), l.Sales.ToString("N0", I18n.Culture)));
        return new Border { Classes = { "card" }, Padding = new Thickness(16), Child = col };
    }

    static Control AuthorBox(Listing l)
    {
        var a = MarketLocal.Authors(MarketData.All.Where(x => x.Active), MarketData.Mods).FirstOrDefault(x => x.Uid == l.Uid)
            ?? new MarketLocal.Author(l.Uid, l.Author, 1, l.Sales, l.Free ? 1 : 0, 0, 0, l.Created, [l.Game]);
        var who = Ui.Row(12, Ui.Thumb(null, a.Name, 48, 24, person: true), Ui.Col(2, Ui.Text(a.Name, "h3"), Ui.Text(I18n.T("v93.mk.author.stats", ("n", a.Items), ("sales", I18n.Compact(a.Sales))), "small muted")));
        var link = new Border { Background = Brushes.Transparent, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand), Child = who };
        StoreKit.OnClick(link, () => MainWindow.Current?.Navigate(() => new AuthorPage(a.Uid, a.Name)));
        return new Border { Classes = { "card" }, Padding = new Thickness(16), Child = Ui.Col(12, Gx.Eyebrow(I18n.T("v93.mk.author"), Ui.Res("Text"), 11), link, MarketPage.FollowButton(a.Uid, a.Name)) };
    }

    static Control Similar(Listing l)
    {
        var list = MarketData.All.Where(x => x.Active && x.Id != l.Id)
            .OrderByDescending(x => (x.Kind == l.Kind ? 2 : 0) + (x.Game == l.Game ? 2 : 0) + x.Tags.Count(l.Tags.Contains) + Math.Log10(x.Sales + 1) * 0.3)
            .Take(4).ToList();
        if (list.Count == 0) return new Border();
        var rows = Ui.Col(4);
        foreach (var x in list) rows.Children.Add(MarketTiles.Row(x));
        return Ui.Col(10, Gx.Eyebrow(I18n.T("v93.mk.similar"), Ui.Res("Text"), 11), rows);
    }

    /// <summary>Отзывы: сводка как в Steam (полоса по звёздам), список и форма для купивших.</summary>
    Control Reviews(Listing l)
    {
        var col = Ui.Col(12, Section(I18n.T("mk.reviews")));
        if (_reviews is null) { col.Children.Add(Ui.Text(I18n.T("common.loading"), "muted")); return col; }
        var list = _reviews;
        if (list.Count > 0)
        {
            var avg = list.Average(r => r.Stars);
            var (verdict, tone) = MarketLocal.Verdict(avg, list.Count);
            var toneBrush = tone switch { "good" => Ui.Res("Good"), "warn" => Ui.Res("Warn"), "bad" => Ui.Res("Bad"), _ => Ui.Res("Muted") };
            var bars = Ui.Col(4);
            for (var s = 5; s >= 1; s--)
            {
                var n = list.Count(r => r.Stars == s);
                var line = new Grid { ColumnDefinitions = new ColumnDefinitions("26,*,34"), ColumnSpacing = 8 };
                line.Children.Add(Ui.Text(s + "★", "small muted"));
                var bar = Gx.Bar(n / (double)list.Count, 6);
                bar.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(bar, 1);
                line.Children.Add(bar);
                var count = Ui.Text(n.ToString(), "small muted");
                Grid.SetColumn(count, 2);
                line.Children.Add(count);
                bars.Children.Add(line);
            }
            var summary = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 28 };
            summary.Children.Add(Ui.Col(4, new TextBlock { Text = avg.ToString("0.0", I18n.Culture), FontFamily = Gx.Display, FontSize = 40, FontWeight = FontWeight.Bold },
                new TextBlock { Text = I18n.T(verdict), FontWeight = FontWeight.Bold, Foreground = toneBrush }, Ui.Text(I18n.T("v93.mk.reviews.n", ("n", list.Count)), "small muted")));
            Grid.SetColumn(bars, 1);
            summary.Children.Add(bars);
            col.Children.Add(new Border { Classes = { "card" }, Padding = new Thickness(18), Child = summary });
        }
        else col.Children.Add(Ui.Text(I18n.T("mk.noReviews"), "muted"));
        foreach (var r in list.Take(12))
        {
            var stars = new TextBlock { Text = new string('★', r.Stars) + new string('☆', 5 - r.Stars), Foreground = r.Stars >= 4 ? Ui.Res("Good") : r.Stars >= 3 ? Ui.Res("Warn") : Ui.Res("Bad"), FontSize = 13 };
            var head = Ui.Row(10, Ui.Thumb(null, r.Author, 32, 16, person: true), Ui.Col(1, Ui.Text(r.Author, "h3"), Ui.Row(8, stars, Ui.Text(Ui.Ago(r.Created), "small muted"))));
            col.Children.Add(new Border { Classes = { "card" }, Padding = new Thickness(16, 12), Child = Ui.Col(8, head, Ui.Text(r.Text, "", wrap: true)) });
        }
        if (Owned && !l.Mine && !Program.Screenshot)
        {
            var stars = new ComboBox { Width = 120, ItemsSource = new[] { "★★★★★", "★★★★", "★★★", "★★", "★" }, SelectedIndex = 0 };
            var text = new TextBox { Watermark = I18n.T("mk.review.hint"), MaxLength = 1000 };
            var send = Ui.Button(I18n.T("mk.review.send"), async () =>
            {
                try { await Market.Review(l, 5 - stars.SelectedIndex, text.Text ?? ""); MainWindow.Current?.Toast(I18n.T("mk.review.done")); _reviews = null; _ = Load(); }
                catch (Exception e) { MainWindow.Current?.Toast(Market.Explain(e), bad: true); }
            }, "primary", Icons.Star);
            var form = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
            form.Children.Add(stars);
            Grid.SetColumn(text, 1);
            form.Children.Add(text);
            Grid.SetColumn(send, 2);
            form.Children.Add(send);
            col.Children.Add(Ui.Col(8, Ui.Text(I18n.T("v93.mk.review.yours"), "h3"), form));
        }
        return col;
    }
}

/// <summary>9.3: профиль автора на рынке — баннер, аватар, счётчики, подписка, лоты и моды Мастерской.</summary>
public sealed class AuthorPage : Page
{
    readonly string _uid, _name;
    string _part = "items";

    public AuthorPage(string uid, string name)
    {
        _uid = uid;
        _name = name;
        if (MarketData.Listings is null) _ = MarketData.Load();
        MarketData.Changed += OnChanged;
        MarketLocal.Changed += OnChanged;
        DetachedFromVisualTree += (_, _) => { MarketData.Changed -= OnChanged; MarketLocal.Changed -= OnChanged; };
    }

    void OnChanged() { if (MainWindow.Current?.CurrentPage == this) Build(); }

    public override string Title => _name;
    public override string SearchHint => I18n.T("v93.mk.search");
    public override void Search(string text) => MainWindow.Current?.Navigate(() => new MarketPage("browse", text));
    public override IEnumerable<(string Text, Action? Open)> Crumbs =>
    [
        ("Creator Hub", () => MainWindow.Current?.Navigate(() => new MarketPage())),
        (I18n.T("v93.mk.tab.authors"), () => MainWindow.Current?.Navigate(() => new MarketPage("authors"))),
        (_name, null),
    ];

    public override void Build()
    {
        var items = MarketData.All.Where(l => l.Uid == _uid && l.Active).OrderByDescending(l => l.Sales).ToList();
        var mods = MarketData.Mods.Where(m => m.Uid == _uid).OrderByDescending(m => m.Downloads).ToList();
        var a = MarketLocal.Authors(items, mods).FirstOrDefault() ?? new MarketLocal.Author(_uid, _name, 0, 0, 0, 0, 0, null, []);
        var col = StoreKit.Column(spacing: 22, top: 22);

        // Баннер профиля: цвет — из имени (у каждого автора свой), сетка HUD и аватар.
        var hue = (int)(_name.Aggregate(17u, (h, c) => h * 31 + c) % 360);
        var banner = new Border
        {
            Height = 190,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Ui.HslColor(hue, 0.6, 0.45), 0), new GradientStop(Ui.HslColor((hue + 70) % 360, 0.55, 0.2), 0.7), new GradientStop(Color.Parse("#0B0B10"), 1) },
            },
        };
        var avatar = new Border
        {
            Width = 112, Height = 112, CornerRadius = new CornerRadius(56), BorderThickness = new Thickness(4), BorderBrush = Ui.Res("Layer"), BoxShadow = Gx.Glow(120, 26),
            Child = Ui.Thumb(null, _name, 104, 52, person: true), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(32, 0, 0, -56),
        };
        var stage = new Border { Classes = { "store-tile", "hero" }, Cursor = null, ClipToBounds = false, Child = new Panel { Children = { new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = new Panel { Children = { banner, Gx.Scanlines(), Gx.Haze(true) } } }, avatar } } };
        col.Children.Add(stage);

        var since = a.Since is DateTime d ? I18n.T("v93.mk.author.since", ("date", d.ToLocalTime().ToString("MMMM yyyy", I18n.Culture))) : I18n.T("v93.mk.author");
        var who = Ui.Col(4, Gx.Title(_name, 30, Ui.Res("Text"), 1), Ui.Text(since, "muted"));
        var stats = Ui.Row(34,
            Gx.Stat(a.Items.ToString(), I18n.T("v93.mk.items.short")),
            Gx.Stat(I18n.Compact(a.Sales), I18n.T("st.sales")),
            Gx.Stat(a.Workshop.ToString(), I18n.T("v93.mk.workshop")),
            Gx.Stat(I18n.Compact(a.Downloads), I18n.T("hub.stat.downloads")));
        var follow = MarketPage.FollowButton(_uid, _name);
        follow.VerticalAlignment = VerticalAlignment.Center;
        var right = Ui.Row(30, stats, follow);
        var head = new DockPanel { Margin = new Thickness(160, 6, 0, 0) };
        DockPanel.SetDock(right, Dock.Right);
        head.Children.Add(right);
        head.Children.Add(who);
        col.Children.Add(head);

        var pills = Ui.Row(6,
            Ui.Button(I18n.T("v93.mk.author.items", ("n", items.Count)), () => { _part = "items"; Build(); }, _part == "items" ? "chip active" : "chip", Icons.Bag),
            Ui.Button(I18n.T("v93.mk.author.workshop", ("n", mods.Count)), () => { _part = "workshop"; Build(); }, _part == "workshop" ? "chip active" : "chip", Icons.Globe));
        pills.Margin = new Thickness(0, 14, 0, 0);
        col.Children.Add(pills);
        var cards = _part == "items" ? items.Select(l => MarketTiles.Listing(l, 1)).ToList() : mods.Select(m => MarketTiles.Workshop(m, 1)).ToList();
        if (cards.Count == 0) col.Children.Add(Ui.Card(Ui.Text(I18n.T("v93.mk.author.empty"), "muted"), 22));
        else col.Children.Add(StoreKit.Tiles(cards, 260));
        Content = new ScrollViewer { Content = col, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }
}
