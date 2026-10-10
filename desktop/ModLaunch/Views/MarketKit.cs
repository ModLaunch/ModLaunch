using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Games;

namespace ModLaunch.Views;

/// <summary>
/// Данные рынка креаторов (лоты и моды Мастерской) — одна загрузка на всех: рынок, лента,
/// страницы лота и автора. Кэш на сеанс; «Обновить» — заново.
/// </summary>
public static class MarketData
{
    public static List<Listing>? Listings { get; private set; }
    public static List<HubMod>? Workshop { get; private set; }
    public static string? Error { get; private set; }
    public static Wallet? Wallet { get; private set; }
    public static bool Loading => _task is { IsCompleted: false };
    static Task? _task;

    /// <summary>Что-то загрузилось — страницы рынка перерисуются.</summary>
    public static event Action? Changed;

    public static IEnumerable<Listing> All => Listings ?? [];
    public static IEnumerable<HubMod> Mods => Workshop ?? [];

    /// <summary>Для снимков экрана: готовые данные без сети.</summary>
    public static void Demo(List<Listing> listings, List<HubMod> hub, Wallet? wallet = null) { Listings = listings; Workshop = hub; Wallet = wallet; Error = null; }

    public static Task Load(bool force = false)
    {
        if (!force && (Listings is not null && Workshop is not null || Loading)) return _task ?? Task.CompletedTask;
        _task = Run(force);
        return _task;
    }

    static async Task Run(bool force)
    {
        async Task<List<Listing>> M()
        {
            if (Program.Demo) return Core.Demo.Listings();
            try { var l = await Market.All(force); Error = null; return l; }
            catch (Exception e) { Error = Market.Explain(e); return Listings ?? []; }
        }
        async Task<List<HubMod>> H()
        {
            if (Program.Demo) return Core.Demo.Hub();
            try { return await Hub.All(force); } catch { return Workshop ?? []; }
        }
        var m = M();
        var h = H();
        await Task.WhenAll(m, h);
        Listings = m.Result;
        Workshop = h.Result;
        if (Social.Account.SignedIn && !Program.Demo)
        {
            try { Wallet = await Market.MyWallet(); } catch { }
            try { await Market.Purchases(); } catch { }
        }
        Dispatcher.UIThread.Post(() => Changed?.Invoke());
    }

    public static void Reload() { Market.Invalidate(); Hub.Invalidate(); _ = Load(force: true); }
}

/// <summary>
/// Плитки рынка креаторов: «капсула» лота (картинка, ярлыки типа и цены), карточка для сеток,
/// широкий баннер и строка списка — в одном стиле для рынка и ленты.
/// </summary>
public static class MarketTiles
{
    public static string KindIcon(string kind) => MarketViews.KindIcon(kind);

    public static void Open(Listing l) => MainWindow.Current?.Navigate(() => new ListingPage(l));

    /// <summary>Картинка лота: первое изображение, иначе арт игры, иначе градиент с значком типа.</summary>
    public static Control Art(Listing l, int decode = 768, bool hero = true)
    {
        if (l.Images.Count > 0) return StoreKit.UrlImage(l.Images[0], decode);
        var game = GameCatalog.ById(l.Game);
        // В маленькой картинке (строки списков) — значок поменьше, чтобы не закрывал её.
        var small = decode <= 384;
        var icon = new Border
        {
            Width = small ? 32 : 64, Height = small ? 32 : 64, CornerRadius = new CornerRadius(small ? 8 : 16), Background = new SolidColorBrush(Color.FromArgb(small ? (byte)110 : (byte)70, 0, 0, 0)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Child = Ui.Icon(KindIcon(l.Kind), small ? 15 : 28, Brushes.White),
        };
        if (game is null) return new Panel { Children = { Gx.Gradient(null), Gx.Scanlines(), icon } };
        return new Panel { Children = { Ui.GameImage(game, decode, art: hero ? Images.Art.Hero : Images.Art.Header), new Border { Background = new SolidColorBrush(Color.FromArgb(95, 6, 6, 10)) }, icon } };
    }

    public static Control Art(HubMod h, int decode = 768)
    {
        if (h.Images.Count > 0) return StoreKit.UrlImage(h.Images[0], decode);
        var game = GameCatalog.ById(h.Game);
        var small = decode <= 384;
        var icon = new Border
        {
            Width = small ? 32 : 60, Height = small ? 32 : 60, CornerRadius = new CornerRadius(small ? 8 : 15), Background = new SolidColorBrush(Color.FromArgb(small ? (byte)110 : (byte)70, 0, 0, 0)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Child = Ui.Icon(h.IsPackage ? Icons.Package : Icons.Code, small ? 15 : 26, Brushes.White),
        };
        if (game is null) return new Panel { Children = { Gx.Gradient("#2BB673"), Gx.Scanlines(), icon } };
        return new Panel { Children = { Ui.GameImage(game, decode, art: Images.Art.Hero), new Border { Background = new SolidColorBrush(Color.FromArgb(95, 6, 6, 10)) }, icon } };
    }

    public static string GameName(string id) => GameCatalog.ById(id)?.ShortName ?? (id == "" ? I18n.T("mk.anyGame") : id);

    /// <summary>Сердечко «в желаемое» поверх картинки.</summary>
    public static Button WishButton(Listing l, bool onArt = true)
    {
        var on = MarketLocal.Wished(l.Id);
        var b = new Button
        {
            Classes = { "icon", "wish" }, Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Padding = new Thickness(0),
            Background = onArt ? new SolidColorBrush(Color.FromArgb(150, 10, 10, 14)) : Ui.Res("Surface2"),
            Content = Ui.Icon(Icons.Heart, 15, on ? Ui.Res("Bad") : onArt ? Brushes.White : Ui.Res("Muted"), fill: on),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(b, I18n.T(on ? "v93.mk.wish.remove" : "v93.mk.wish.add"));
        b.Click += (_, _) =>
        {
            var now = MarketLocal.ToggleWish(l.Id);
            b.Content = Ui.Icon(Icons.Heart, 15, now ? Ui.Res("Bad") : onArt ? Brushes.White : Ui.Res("Muted"), fill: now);
            ToolTip.SetTip(b, I18n.T(now ? "v93.mk.wish.remove" : "v93.mk.wish.add"));
            if (now) Animate.From(b, "scale(1.5)", 420, 0, new Avalonia.Animation.Easings.BackEaseOut(), 1);
            MainWindow.Current?.Toast(I18n.T(now ? "v93.mk.wish.added" : "v93.mk.wish.removed", ("title", l.Title)));
        };
        return b;
    }

    static bool Owned(Listing l) => l.Mine || Market.Owned.Contains(l.Id);

    static Control PriceOrOwned(Listing l) => Owned(l)
        ? Gx.Tag(I18n.T("v93.mk.owned"), new SolidColorBrush(Color.FromArgb(220, 30, 34, 44)), Brushes.White, 12)
        : Gx.Price(MarketViews.PriceText(l), l.Free);

    /// <summary>
    /// Лот: size 0 — строка списка, 1 — карточка (картинка сверху), 2 — широкий баннер.
    /// Для ленты и для сеток рынка.
    /// </summary>
    public static Control Listing(Listing l, int size)
    {
        if (size == 0) return Row(l);
        var kind = Gx.Tag(I18n.T("mk.kind." + l.Kind), null, null, 10.5);
        kind.Margin = new Thickness(10);
        var wish = WishButton(l);
        wish.HorizontalAlignment = HorizontalAlignment.Right;
        wish.VerticalAlignment = VerticalAlignment.Top;
        wish.Margin = new Thickness(8);
        if (size == 2)
        {
            var words = Ui.Col(10,
                Gx.Eyebrow(I18n.T("v93.mk.eyebrow"), Ui.Hex("#E6E7EE")),
                Gx.Title(l.Title, 26, Brushes.White),
                new TextBlock { Text = l.Summary, FontSize = 13.5, Foreground = Ui.Hex("#D0D3DC"), TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left },
                Ui.Row(8, StoreKit.Pill(I18n.T("mk.kind." + l.Kind)), StoreKit.Pill(GameName(l.Game)), StoreKit.Pill(I18n.T("mod.by", ("author", l.Author)))),
                Ui.Row(12, PriceOrOwned(l), new TextBlock { Text = I18n.T("mk.sold", ("n", l.Sales)), FontSize = 12.5, Foreground = Ui.Hex("#C9CCD6"), VerticalAlignment = VerticalAlignment.Center }));
            words.VerticalAlignment = VerticalAlignment.Bottom;
            words.Margin = new Thickness(28, 0, 24, 24);
            var layers = new Panel { Children = { Art(l, 1600), new Border { Background = Gx.ShadeLeft(0.85, 235) }, new Border { Background = Gx.ShadeUp(0.5, 150) }, Gx.Haze(true), words, wish } };
            var t = new Border { Classes = { "store-tile", "feed-tile", "hero" }, Child = layers };
            StoreKit.OnClick(t, () => Open(l));
            return t;
        }
        var top = new Panel { ClipToBounds = true, Children = { Art(l), kind, wish } };
        var price = PriceOrOwned(l);
        price.HorizontalAlignment = HorizontalAlignment.Right;
        price.VerticalAlignment = VerticalAlignment.Bottom;
        price.Margin = new Thickness(10);
        top.Children.Add(price);
        var foot = Ui.Col(2,
            new TextBlock { Text = l.Title, FontWeight = FontWeight.SemiBold, FontSize = 14.5, TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = $"{l.Author} · {GameName(l.Game)}", FontSize = 12, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis });
        foot.Margin = new Thickness(12, 10, 12, 12);
        var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        grid.Children.Add(top);
        Grid.SetRow(foot, 1);
        grid.Children.Add(foot);
        var tile = new Border { Classes = { "store-tile", "feed-tile" }, Child = grid };
        StoreKit.OnClick(tile, () => Open(l));
        Menu(tile, l);
        return tile;
    }

    static void Menu(Control c, Listing l) => Ctx.Attach(c, () => Ctx.Menu(
        Ctx.Item(I18n.T("ctx.open"), Icons.Eye, () => Open(l)),
        Ctx.Item(I18n.T(MarketLocal.Wished(l.Id) ? "v93.mk.wish.remove" : "v93.mk.wish.add"), Icons.Heart, () => MarketLocal.ToggleWish(l.Id)),
        Ctx.Item(I18n.T("v93.mk.author.open"), Icons.User, () => MainWindow.Current?.Navigate(() => new AuthorPage(l.Uid, l.Author))),
        "-",
        Ctx.Copy(I18n.T("ctx.copyName"), l.Title),
        Ctx.Copy(I18n.T("ctx.copyId"), l.Id)));

    /// <summary>Карточка для сеток рынка: фиксированная ширина, картинка 16:9.</summary>
    public static Control Card(Listing l, double width = 268)
    {
        var c = Listing(l, 1);
        c.Width = width;
        c.Height = Math.Round(width * 9 / 16) + 62;
        return c;
    }

    /// <summary>Строка лота (списки «Лидеры продаж», желаемое, поиск).</summary>
    public static Control Row(Listing l, int? rank = null, Control? right = null)
    {
        var art = new Border { Width = 120, Height = 68, CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = Art(l, 384, hero: false) };
        var words = Ui.Col(3,
            new TextBlock { Text = l.Title, FontWeight = FontWeight.SemiBold, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = $"{I18n.T("mk.kind." + l.Kind)} · {GameName(l.Game)} · {l.Author}", FontSize = 12, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = I18n.T("mk.sold", ("n", l.Sales)), FontSize = 11.5, Foreground = Ui.Res("Faint") });
        words.VerticalAlignment = VerticalAlignment.Center;
        var price = right ?? PriceOrOwned(l);
        price.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), ColumnSpacing = 12 };
        if (rank is int r)
            grid.Children.Add(new TextBlock { Text = r.ToString(), FontFamily = Gx.Display, FontSize = 20, FontWeight = FontWeight.Bold, Width = 30, Foreground = r == 1 ? Ui.Res("Brand2") : Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(art, 1);
        grid.Children.Add(art);
        Grid.SetColumn(words, 2);
        grid.Children.Add(words);
        Grid.SetColumn(price, 3);
        grid.Children.Add(price);
        var row = new Border { Classes = { "store-row" }, Padding = new Thickness(8, 6), Child = grid };
        StoreKit.OnClick(row, () => Open(l));
        Menu(row, l);
        return row;
    }

    /// <summary>Мод Мастерской: size 0 — строка, 1 — карточка, 2 — баннер.</summary>
    public static Control Workshop(HubMod h, int size)
    {
        var counters = $"↓ {I18n.Compact(h.Downloads)}   ♥ {I18n.Compact(h.Likes)}";
        if (size == 0)
        {
            var art = new Border { Width = 120, Height = 68, CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = Art(h, 384) };
            var words = Ui.Col(3,
                new TextBlock { Text = h.Name, FontWeight = FontWeight.SemiBold, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = $"{GameName(h.Game)} · {h.Author}", FontSize = 12, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = counters, FontSize = 11.5, Foreground = Ui.Res("Faint") });
            words.VerticalAlignment = VerticalAlignment.Center;
            var free = Gx.Price(I18n.T("mk.free"), true);
            free.VerticalAlignment = VerticalAlignment.Center;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
            grid.Children.Add(art);
            Grid.SetColumn(words, 1);
            grid.Children.Add(words);
            Grid.SetColumn(free, 2);
            grid.Children.Add(free);
            var row = new Border { Classes = { "store-row" }, Padding = new Thickness(8, 6), Child = grid };
            StoreKit.OnClick(row, () => CreatorPage.OpenMod(h));
            return row;
        }
        var tag = Gx.Tag(I18n.T("v93.mk.workshop"), new SolidColorBrush(Color.FromArgb(220, 22, 120, 72)), Brushes.White, 10.5);
        tag.Margin = new Thickness(10);
        if (size == 2)
        {
            var words = Ui.Col(10,
                Gx.Eyebrow(I18n.T("v93.mk.workshop.eyebrow"), Ui.Hex("#E6E7EE")),
                Gx.Title(h.Name, 26, Brushes.White),
                new TextBlock { Text = h.Summary, FontSize = 13.5, Foreground = Ui.Hex("#D0D3DC"), TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left },
                Ui.Row(8, StoreKit.Pill(GameName(h.Game)), StoreKit.Pill(I18n.T("mod.by", ("author", h.Author))), StoreKit.Pill(counters)),
                Ui.Row(10, Gx.Price(I18n.T("mk.free"), true)));
            words.VerticalAlignment = VerticalAlignment.Bottom;
            words.Margin = new Thickness(28, 0, 24, 24);
            var layers = new Panel { Children = { Art(h, 1600), new Border { Background = Gx.ShadeLeft(0.85, 235) }, new Border { Background = Gx.ShadeUp(0.5, 150) }, words, tag } };
            var t = new Border { Classes = { "store-tile", "feed-tile", "hero" }, Child = layers };
            StoreKit.OnClick(t, () => CreatorPage.OpenMod(h));
            return t;
        }
        var top = new Panel { ClipToBounds = true, Children = { Art(h), tag } };
        var foot = Ui.Col(2,
            new TextBlock { Text = h.Name, FontWeight = FontWeight.SemiBold, FontSize = 14.5, TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = $"{h.Author} · {GameName(h.Game)} · {counters}", FontSize = 12, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis });
        foot.Margin = new Thickness(12, 10, 12, 12);
        var g2 = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        g2.Children.Add(top);
        Grid.SetRow(foot, 1);
        g2.Children.Add(foot);
        var tile = new Border { Classes = { "store-tile", "feed-tile" }, Child = g2 };
        StoreKit.OnClick(tile, () => CreatorPage.OpenMod(h));
        return tile;
    }

    public static Control WorkshopCard(HubMod h, double width = 268)
    {
        var c = Workshop(h, 1);
        c.Width = width;
        c.Height = Math.Round(width * 9 / 16) + 62;
        return c;
    }
}
