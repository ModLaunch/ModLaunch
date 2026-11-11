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
    Listing _l;
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
        MarketData.Changed += OnData;
        DetachedFromVisualTree += (_, _) => { MarketLocal.Changed -= OnChanged; MarketData.Changed -= OnData; };
    }

    void OnChanged() { if (MainWindow.Current?.CurrentPage == this) Build(); }

    /// <summary>26.0: рынок перезагрузился (например, автор изменил свой лот кнопкой «Изменить») — показываем свежий лот.</summary>
    void OnData()
    {
        if (MarketData.All.FirstOrDefault(x => x.Id == _l.Id) is { } fresh) _l = fresh;
        OnChanged();
    }

    public override string Title => _l.Title;
    public override string SearchHint => I18n.T("v93.mk.search");
    public override void Search(string text) => MainWindow.Current?.Navigate(() => new MarketPage("browse", text));
    // 26.0: страница лота — внутри Creator Hub: без цвета игры (он красил ссылки и рамки не в янтарь хаба)
    // и без колонки рекламы справа, как остальные страницы хаба.
    public override string? Accent => null;
    public override bool Workspace => true;
    public override bool SameScreenAs(Page? previous) => HubNav.IsHub(previous);

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
        // 26.0: лот открывается внутри рамки хаба (HubNav.Frame): слева та же панель с «Рынком», сверху вкладки рынка,
        // всё в янтаре хаба. Раньше страница выходила из хаба в синюю тему без боковой панели.
        var col = Ui.Col(22);

        // Под названием (оно в шапке рамки) — вид, игра и лицензия ярлыками хаба.
        col.Children.Add(Ui.Row(8, HubLook.Badge(I18n.T("mk.kind." + l.Kind), "accent", MarketTiles.KindIcon(l.Kind)), HubLook.Badge(MarketTiles.GameName(l.Game)), HubLook.Badge(I18n.T("mk.license." + l.License))));

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
        if (Moderation.Sealed(l)) left.Children.Add(MarketViews.SealedNote(l));
        // 21.0: часть кода .modx — кусочек ModScript X: как он будет выглядеть в проекте и кнопка «В проект».
        if (XStudio.IsPiece(l)) left.Children.Add(XStudio.PieceNote(l));
        else if (l.Preview != "") left.Children.Add(Ui.Col(10, Section(I18n.T("mk.preview")), Code(l.Preview)));
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
        // Главная кнопка лота — покупка в блоке ниже; в шапке — тихая кнопка: свой лот — «Изменить», чужой — «Продать работу».
        var action = l.Mine
            ? Ui.Button(I18n.T("mk.edit"), () => MarketViews.Editor(l, MarketData.Reload), "", Icons.Edit)
            : Ui.Button(I18n.T("mk.sell"), () => ListingWizard.Show(null, MarketData.Reload), "", Icons.Upload);
        // Перестройка (загрузились отзывы, выбрана миниатюра) не должна прокручивать страницу наверх.
        var keep = MarketPage.HubScroller(Content as Control)?.Offset;
        Content = HubNav.Frame("store", "shop", l.Title, col, sold: MarketData.Reload, action: action);
        if (keep is { Y: > 0 } offset)
            Dispatcher.UIThread.Post(() => { if (MarketPage.HubScroller(Content as Control) is { } sv) sv.Offset = offset; }, DispatcherPriority.Loaded);
    }

    static Control Section(string title) => Ui.Col(6, HubLook.Label(title, Ui.Res("Text"), 12), new Border { Height = 1, Background = Ui.Res("Line") });

    static readonly FontFamily Mono = new("Cascadia Mono, Consolas, JetBrains Mono, DejaVu Sans Mono, monospace");

    static Control Code(string text) => new Border
    {
        Classes = { "card" }, Padding = new Thickness(16, 14), MaxHeight = 320,
        Child = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new SelectableTextBlock { Text = text, FontFamily = Mono, FontSize = 12.5, LineHeight = 19 } },
    };

    /// <summary>
    /// 26.0: большая картинка лота без своих картинок — арт игры только приглушённым фоном, по центру — значок вида
    /// в стеклянном круге. Раньше во весь кадр стоял арт игры, и лот выглядел как картинка самой игры.
    /// </summary>
    static Control Stage(Listing l)
    {
        if (GameCatalog.ById(l.Game) is not { } game) return MarketTiles.Art(l, 1280);
        var mark = new Border
        {
            Width = 88, Height = 88, CornerRadius = new CornerRadius(44), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), BorderThickness = new Thickness(1),
            Child = Ui.Icon(MarketTiles.KindIcon(l.Kind), 36, Brushes.White),
        };
        return new Panel { Children = { Ui.GameImage(game, 1600, art: Images.Art.Hero), new Border { Background = new SolidColorBrush(Color.FromArgb(185, 8, 8, 12)) }, mark } };
    }

    /// <summary>Большая картинка и полоска миниатюр (картинки лота и «Код», если есть превью).</summary>
    Control Media(Listing l)
    {
        var items = new List<(string Kind, string? Url)>();
        foreach (var u in l.Images) items.Add(("image", u));
        if (items.Count == 0) items.Add(("art", null));
        if (l.Preview != "" && !Moderation.Sealed(l)) items.Add(("code", null));
        _media = Math.Clamp(_media, 0, items.Count - 1);
        var (kind, url) = items[_media];
        Control main = kind switch
        {
            "image" => StoreKit.UrlImage(url, 1600),
            "code" => new Border { Background = Ui.Res("Surface2"), Padding = new Thickness(22, 18), Child = new TextBlock { Text = l.Preview, FontFamily = Mono, FontSize = 13, LineHeight = 20, Foreground = Ui.Res("Text"), TextWrapping = TextWrapping.NoWrap } },
            _ => Stage(l),
        };
        // 26.0: без «неоновой» дымки цвета темы по краю (в хабе она была синей или красной поверх картинки).
        var stage = new Border { Classes = { "store-tile", "hero" }, Height = 392, Cursor = null, Child = main };
        if (items.Count == 1) return stage;
        var strip = Ui.Row(8);
        for (var i = 0; i < items.Count; i++)
        {
            var index = i;
            var (k, u) = items[i];
            Control thumb = k switch
            {
                "image" => StoreKit.UrlImage(u, 256),
                "code" => new Border { Background = Ui.Res("Surface2"), Child = Ui.Col(2, Ui.Icon(Icons.Code, 18, HubLook.Violet), new TextBlock { Text = I18n.T("v93.mk.code"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center }) },
                _ => MarketTiles.Art(l, 256),
            };
            if (thumb is Border { Child: StackPanel sp }) { sp.HorizontalAlignment = HorizontalAlignment.Center; sp.VerticalAlignment = VerticalAlignment.Center; }
            var b = new Border
            {
                Width = 112, Height = 63, CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = thumb, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                BorderThickness = new Thickness(2), BorderBrush = i == _media ? HubLook.Amber : Brushes.Transparent, Opacity = i == _media ? 1 : 0.7,
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
            // 26.0: подпись обычным регистром — в хабе нет подписей капсом.
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*") };
            var t = new TextBlock { Text = label, FontSize = 12, Foreground = Ui.Res("Muted"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            grid.Children.Add(t);
            Grid.SetColumn(value, 1);
            grid.Children.Add(value);
            return grid;
        }
        var reviews = _reviews ?? [];
        var (verdict, tone) = MarketLocal.Verdict(reviews.Count == 0 ? 0 : reviews.Average(r => r.Stars), reviews.Count);
        var toneBrush = tone switch { "good" => Ui.Res("Good"), "warn" => Ui.Res("Warn"), "bad" => Ui.Res("Bad"), _ => Ui.Res("Muted") };
        var reviewText = _reviews is null ? I18n.T("common.loading") : reviews.Count == 0 ? I18n.T(verdict) : $"{I18n.T(verdict)} ({reviews.Count})";
        var author = new TextBlock { Text = l.Author, FontWeight = FontWeight.SemiBold, Foreground = HubLook.Violet, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand), TextTrimming = TextTrimming.CharacterEllipsis };
        StoreKit.OnClick(author, () => MainWindow.Current?.Navigate(() => new AuthorPage(l.Uid, l.Author)));
        // 26.0: метки — ярлыками хаба, и они работают: щелчок открывает рынок с поиском по этой метке
        // (раньше курсор-рука был, а щелчок ничего не делал).
        var tags = new WrapPanel();
        foreach (var t in l.Tags)
        {
            var label = I18n.T("hub.tag." + t);
            var chip = HubLook.Badge("#" + label);
            chip.Margin = new Thickness(0, 0, 6, 6);
            chip.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
            StoreKit.OnClick(chip, () => MainWindow.Current?.Navigate(() => new MarketPage("store", label)));
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
        var note = Ui.Text(Owned ? I18n.T(Moderation.Sealed(l) ? "v11.inj.yours" : "v93.mk.yours.text") : l.Free ? I18n.T("v93.mk.free.text") : I18n.T("mk.feeNote", ("fee", Market.FeePercent)), "small muted", wrap: true);
        Button main;
        if (Owned) main = MarketViews.OwnedAction(l);
        else main = Ui.Button(l.Free ? I18n.T("mk.getFree") : I18n.T("v93.mk.buyNow"), Buy, "primary", l.Free ? Icons.Download : Icons.Bag);
        main.Padding = new Thickness(26, 12);
        main.IsEnabled = !_busy && _owned is not null;
        var wished = MarketLocal.Wished(l.Id);
        var wish = Ui.Button(I18n.T(wished ? "v93.mk.wish.in" : "v93.mk.wish.add"), () => MarketLocal.ToggleWish(l.Id), "", Icons.Heart);
        if (wished) wish.Foreground = Ui.Res("Bad");
        // 26.0: цена — крупным текстом (бесплатное — зелёным), купленное — ярлыком хаба; без скошенной синей плашки.
        Control price = Owned ? HubLook.Badge(I18n.T("v93.mk.owned"), "good", Icons.Check)
            : new TextBlock { Text = MarketViews.PriceText(l), FontFamily = Look.Display, FontSize = 22, FontWeight = FontWeight.Bold, Foreground = l.Free ? Ui.Res("Good") : Ui.Res("Text") };
        price.VerticalAlignment = VerticalAlignment.Center;
        var right = Ui.Row(12, wish, price, main);
        right.VerticalAlignment = VerticalAlignment.Center;
        var words = Ui.Col(4, heading, note);
        words.VerticalAlignment = VerticalAlignment.Center;
        var dock = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(right);
        dock.Children.Add(words);
        // Рамка и тень — от листа хаба (HubLook.Scope), без синей рамки со свечением.
        return new Border { Classes = { "card", "buy-box" }, Padding = new Thickness(22, 18), Child = dock };
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
            if (Moderation.Sealed(_l)) MarketViews.Connect(_l);
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
        var col = Ui.Col(10, HubLook.Label(I18n.T("v93.mk.contents"), Ui.Res("Text"), 11),
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
        return new Border { Classes = { "card" }, Padding = new Thickness(16), Child = Ui.Col(12, HubLook.Label(I18n.T("v93.mk.author"), Ui.Res("Text"), 11), link, MarketPage.FollowButton(a.Uid, a.Name)) };
    }

    static Control Similar(Listing l)
    {
        var list = MarketData.All.Where(x => x.Active && x.Id != l.Id)
            .OrderByDescending(x => (x.Kind == l.Kind ? 2 : 0) + (x.Game == l.Game ? 2 : 0) + x.Tags.Count(l.Tags.Contains) + Math.Log10(x.Sales + 1) * 0.3)
            .Take(4).ToList();
        if (list.Count == 0) return new Border();
        var rows = Ui.Col(4);
        // Узкая колонка: компактные строки (цена — под названием), чтобы название не обрезалось до первого слова.
        foreach (var x in list) rows.Children.Add(MarketTiles.Row(x, compact: true));
        return Ui.Col(10, HubLook.Label(I18n.T("v93.mk.similar"), Ui.Res("Text"), 11), rows);
    }

    /// <summary>26.0: полоса доли отзывов — янтарём хаба на поднятой дорожке (общий Gx.Bar красил её синим цветом темы).</summary>
    static Control StarBar(int part, int rest)
    {
        var bar = new Grid { Height = 6, ColumnDefinitions = new ColumnDefinitions($"{part}*,{rest}*") };
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = HubLook.Raised });
        Grid.SetColumnSpan(bar.Children[0], 2);
        if (part > 0) bar.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = HubLook.Amber });
        return bar;
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
                var bar = StarBar(n, list.Count - n);
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

    // 26.0: профиль автора — внутри Creator Hub, как рынок и лот: без колонки рекламы и без въезда всей страницы.
    public override bool Workspace => true;
    public override bool SameScreenAs(Page? previous) => HubNav.IsHub(previous);

    public override void Build()
    {
        var items = MarketData.All.Where(l => l.Uid == _uid && l.Active).OrderByDescending(l => l.Sales).ToList();
        var mods = MarketData.Mods.Where(m => m.Uid == _uid).OrderByDescending(m => m.Downloads).ToList();
        var a = MarketLocal.Authors(items, mods).FirstOrDefault() ?? new MarketLocal.Author(_uid, _name, 0, 0, 0, 0, 0, null, []);
        var col = Ui.Col(18);

        // 26.0: шапка профиля — карточка хаба: аватар и четыре числа; имя и «автор с …» — в шапке рамки. Раньше тут был
        // цветной баннер со «сканлайнами» и свечением из старого игрового стиля, а сама страница выходила из хаба.
        var avatar = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(32), ClipToBounds = true, Child = Ui.Thumb(null, _name, 64, 32, person: true), VerticalAlignment = VerticalAlignment.Center };
        var stats = Ui.Row(34,
            HubLook.Stat(a.Items.ToString(), I18n.T("v93.mk.items.short"), size: 22),
            HubLook.Stat(I18n.Compact(a.Sales), I18n.T("st.sales"), size: 22),
            HubLook.Stat(a.Workshop.ToString(), I18n.T("v93.mk.workshop"), size: 22),
            HubLook.Stat(I18n.Compact(a.Downloads), I18n.T("hub.stat.downloads"), size: 22));
        stats.VerticalAlignment = VerticalAlignment.Center;
        stats.Margin = new Thickness(22, 0, 0, 0);
        var head = new DockPanel();
        DockPanel.SetDock(avatar, Dock.Left);
        head.Children.Add(avatar);
        head.Children.Add(stats);
        col.Children.Add(new Border { Classes = { "card" }, Padding = new Thickness(20, 16), Child = head });

        var tabs = new WrapPanel();
        void Tab(string id, string text)
        {
            var b = new Button { Content = text, Classes = { "pivot" }, Margin = new Thickness(0, 0, 4, 0) };
            if (_part == id) b.Classes.Add("active");
            b.Click += (_, _) => { _part = id; Build(); };
            tabs.Children.Add(b);
        }
        Tab("items", I18n.T("v93.mk.author.items", ("n", items.Count)));
        Tab("workshop", I18n.T("v93.mk.author.workshop", ("n", mods.Count)));
        col.Children.Add(tabs);

        var cards = _part == "items" ? items.Select(l => MarketTiles.Listing(l, 1)).ToList() : mods.Select(m => MarketTiles.Workshop(m, 1)).ToList();
        if (cards.Count == 0) col.Children.Add(HubLook.Empty(_part == "items" ? Icons.Bag : Icons.Globe, I18n.T("v93.mk.author.empty"), ""));
        // 26.0: у карточки мода сообщества подпись в две строки (автор · игра и счётчики) — ей нужна высота повыше.
        else col.Children.Add(StoreKit.Tiles(cards, 260, _part == "items" ? 62 : MarketTiles.WorkshopFoot));

        var since = a.Since is DateTime d ? I18n.T("v93.mk.author.since", ("date", MonthYear(d.ToLocalTime()))) : I18n.T("v93.mk.author");
        Content = HubNav.Frame("store", "shop", _name, col, sold: MarketData.Reload, action: MarketPage.FollowButton(_uid, _name), lead: since);
    }

    // «с сентября 2026», а не «с сентябрь 2026»: в ru/uk месяц после предлога — в родительном падеже.
    // Остальные языки берут свой обычный вид «месяц год» («September 2026», «septiembre de 2026», «2026年9月»).
    internal static string MonthYear(DateTime d)
    {
        var f = I18n.Culture.DateTimeFormat;
        var gen = f.MonthGenitiveNames[d.Month - 1];
        return gen.Length > 0 && gen != f.MonthNames[d.Month - 1] ? $"{gen} {d.Year}" : d.ToString("Y", I18n.Culture);
    }
}
