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
        var fee = Market.LoadFee(force);
        await Task.WhenAll(m, h, fee);
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

    /// <summary>
    /// Картинка лота: первое изображение, иначе арт его игры. 23.0: без значка вида посреди картинки — вид уже
    /// написан ярлыком; у лотов без игры — спокойная плашка со значком.
    /// </summary>
    public static Control Art(Listing l, int decode = 768, bool hero = true)
    {
        if (l.Images.Count > 0) return StoreKit.UrlImage(l.Images[0], decode);
        // 26.0: широкий баннер — по-прежнему арт игры; карточки и строки — своя обложка лота, а не одна и та же
        // картинка игры у всех лотов (на рынке Subnautica все карточки были одинаковыми).
        if (decode >= 1600 && GameCatalog.ById(l.Game) is { } game) return Ui.GameImage(game, decode, art: Images.Art.Hero);
        return Cover(l.Kind, l.Id, l.Game, KindIcon(l.Kind), compact: decode <= 384);
    }

    public static Control Art(HubMod h, int decode = 768)
    {
        if (h.Images.Count > 0) return StoreKit.UrlImage(h.Images[0], decode);
        if (decode >= 1600 && GameCatalog.ById(h.Game) is { } game) return Ui.GameImage(game, decode, art: Images.Art.Hero);
        return Cover(h.IsPackage ? "pack" : "code", h.Id, h.Game, h.IsPackage ? Icons.Package : Icons.Code, compact: decode <= 384);
    }

    /// <summary>Цвета обложки по виду лота: код — фиолетовый, скрипты — янтарный, ассеты — бирюзовый…</summary>
    static (Color From, Color To) KindColors(string kind) => kind switch
    {
        "code" or "snippet" => (Color.Parse("#6366F1"), Color.Parse("#7C3AED")),
        "script" or "scripts" => (Color.Parse("#F0B429"), Color.Parse("#D9480F")),
        "asset" or "assets" => (Color.Parse("#14B8A6"), Color.Parse("#0E7490")),
        "model" or "models" => (Color.Parse("#EC4899"), Color.Parse("#7E22CE")),
        "pack" or "build" or "builds" => (Color.Parse("#22C55E"), Color.Parse("#0F766E")),
        "config" => (Color.Parse("#38BDF8"), Color.Parse("#1D4ED8")),
        _ => (Color.Parse("#64748B"), Color.Parse("#1E293B")),
    };

    static Color Shift(Color c, double by)
    {
        // Небольшой сдвиг яркости по номеру лота — соседние карточки одного вида не сливаются.
        byte F(byte v) => (byte)Math.Clamp(v + by * 255, 0, 255);
        return Color.FromRgb(F(c.R), F(c.G), F(c.B));
    }

    /// <summary>
    /// 26.0: обложка лота без своих картинок — градиент вида лота (оттенок и угол зависят от номера лота), мягкие
    /// круги-блики, значок вида в стеклянном круге и маленький значок игры в углу.
    /// </summary>
    public static Control Cover(string kind, string id, string gameId, string icon, bool compact = false)
    {
        var seed = (uint)StringComparer.Ordinal.GetHashCode(id ?? "");
        var r = new Random((int)(seed & 0x7FFFFFFF));
        var (from, to) = KindColors(kind);
        var shift = (r.NextDouble() - 0.5) * 0.16;
        var angle = r.NextDouble();
        var bg = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(angle * 0.4, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1 - angle * 0.4, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Shift(from, shift), 0), new GradientStop(Shift(to, shift / 2), 1) },
        };
        var panel = new Panel();
        for (var i = 0; i < (compact ? 1 : 3); i++)
        {
            var size = compact ? 60 : 90 + r.Next(120);
            panel.Children.Add(new Avalonia.Controls.Shapes.Ellipse
            {
                Width = size, Height = size, Fill = Brushes.White, Opacity = 0.06 + r.NextDouble() * 0.07,
                HorizontalAlignment = r.Next(2) == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                VerticalAlignment = r.Next(2) == 0 ? VerticalAlignment.Top : VerticalAlignment.Bottom,
                Margin = new Thickness(-size / 3),
            });
        }
        var badgeSize = compact ? 30 : 58;
        panel.Children.Add(new Border
        {
            Width = badgeSize, Height = badgeSize, CornerRadius = new CornerRadius(badgeSize / 2),
            Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Child = Ui.Icon(icon, compact ? 15 : 26, Brushes.White),
        });
        if (!compact && GameCatalog.ById(gameId) is { } game)
            panel.Children.Add(new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(7), ClipToBounds = true, Margin = new Thickness(10),
                BorderBrush = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
                Child = Ui.GameImage(game, 96, art: Images.Art.Cover),
            });
        return new Border { Background = bg, ClipToBounds = true, Child = panel };
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
            if (now) Animate.From(b, "scale(1.3)", 360, 0, new Avalonia.Animation.Easings.QuinticEaseOut(), 1);
            MainWindow.Current?.Toast(I18n.T(now ? "v93.mk.wish.added" : "v93.mk.wish.removed", ("title", l.Title)));
        };
        return b;
    }

    static bool Owned(Listing l) => l.Mine || Market.Owned.Contains(l.Id);

    /// <summary>Зелёный текст «Бесплатно» на тёмной плашке поверх картинки: плашка тёмная в обеих темах.</summary>
    static readonly IBrush FreeOnArt = new SolidColorBrush(Color.Parse("#6EE7B7"));

    /// <summary>
    /// 26.0: ярлык цены — тот же HubLook.Badge, что и везде в хабе (раньше — скошенный Gx.Tag и синяя «пилюля»).
    /// На картинке — тёмная плашка, бесплатное — зелёным текстом; вне картинки — мягкий янтарь, бесплатное — зелёным тоном.
    /// </summary>
    public static Border PriceBadge(Listing l, bool onArt = true)
    {
        if (Owned(l)) return HubLook.Badge(I18n.T("v93.mk.owned"), onArt ? "onArt" : "neutral", Icons.Check);
        if (!onArt) return HubLook.Badge(MarketViews.PriceText(l), l.Free ? "good" : "accent");
        var b = HubLook.Badge(MarketViews.PriceText(l), "onArt");
        if (l.Free && b.Child is TextBlock t) t.Foreground = FreeOnArt;
        return b;
    }

    static Border FreeBadgeOnArt()
    {
        var b = HubLook.Badge(I18n.T("mk.free"), "onArt");
        if (b.Child is TextBlock t) t.Foreground = FreeOnArt;
        return b;
    }


    /// <summary>
    /// Лот: size 0 — строка списка, 1 — карточка (картинка сверху), 2 — широкий баннер.
    /// Для ленты и для сеток рынка.
    /// </summary>
    public static Control Listing(Listing l, int size)
    {
        if (size == 0) return Row(l);
        var kind = HubLook.Badge(I18n.T("mk.kind." + l.Kind), "onArt");
        kind.Margin = new Thickness(10);
        kind.VerticalAlignment = VerticalAlignment.Top;
        var wish = WishButton(l);
        wish.HorizontalAlignment = HorizontalAlignment.Right;
        wish.VerticalAlignment = VerticalAlignment.Top;
        wish.Margin = new Thickness(8);
        if (size == 2)
        {
            var words = Ui.Col(10,
                HubLook.Label(I18n.T("v93.mk.eyebrow"), Ui.Hex("#E6E7EE")),
                Gx.Title(l.Title, 26, Brushes.White),
                new TextBlock { Text = l.Summary, FontSize = 13.5, Foreground = Ui.Hex("#D0D3DC"), TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left },
                Ui.Row(8, HubLook.Badge(I18n.T("mk.kind." + l.Kind), "onArt"), HubLook.Badge(GameName(l.Game), "onArt"), HubLook.Badge(I18n.T("mod.by", ("author", l.Author)), "onArt")),
                Ui.Row(12, PriceBadge(l), new TextBlock { Text = I18n.T("mk.sold", ("n", l.Sales)), FontSize = 12.5, Foreground = Ui.Hex("#C9CCD6"), VerticalAlignment = VerticalAlignment.Center }));
            words.VerticalAlignment = VerticalAlignment.Bottom;
            words.Margin = new Thickness(28, 0, 24, 24);
            var layers = new Panel { Children = { Art(l, 1600), new Border { Background = Gx.ShadeLeft(0.85, 235) }, new Border { Background = Gx.ShadeUp(0.5, 150) }, words, wish } };
            var t = new Border { Classes = { "store-tile", "feed-tile", "hero" }, Child = layers };
            StoreKit.OnClick(t, () => Open(l));
            return t;
        }
        var top = new Panel { ClipToBounds = true, Children = { Art(l), kind, wish } };
        var price = PriceBadge(l);
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
    /// <param name="compact">26.0: для узкой колонки (похожие лоты): обложка меньше, цена — под названием вместо колонки справа.</param>
    public static Control Row(Listing l, int? rank = null, Control? right = null, bool compact = false)
    {
        var art = new Border { Width = compact ? 84 : 120, Height = compact ? 47 : 68, CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = Art(l, 384, hero: false) };
        Control third = compact ? PriceBadge(l, onArt: false) : new TextBlock { Text = I18n.T("mk.sold", ("n", l.Sales)), FontSize = 11.5, Foreground = Ui.Res("Faint") };
        var words = Ui.Col(3,
            new TextBlock { Text = l.Title, FontWeight = FontWeight.SemiBold, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = $"{I18n.T("mk.kind." + l.Kind)} · {GameName(l.Game)} · {l.Author}", FontSize = 12, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis },
            third);
        words.VerticalAlignment = VerticalAlignment.Center;
        // 26.0: строка стоит на фоне страницы, а не на картинке — цена мягким ярлыком хаба.
        var price = right ?? (compact ? new Border() : PriceBadge(l, onArt: false));
        price.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), ColumnSpacing = compact ? 10 : 12 };
        if (rank is int r)
            grid.Children.Add(new TextBlock { Text = r.ToString(), FontFamily = Gx.Display, FontSize = 20, FontWeight = FontWeight.Bold, Width = 30, Foreground = r == 1 ? HubLook.Violet : Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Center });
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

    /// <summary>26.0: высота подписи под картинкой карточки мода (название, автор и игра, счётчики) — для StoreKit.Tiles.</summary>
    public const double WorkshopFoot = 78;

    /// <summary>Скачивания и лайки значками (раньше — «↓ … ♥ …» одной строкой текста, которая обрезалась до «♥ …»).</summary>
    static Control Counters(HubMod h)
    {
        var row = Ui.Row(12, HubLook.Counter(Icons.Download, h.Downloads), HubLook.Counter(Icons.Heart, h.Likes));
        row.VerticalAlignment = VerticalAlignment.Center;
        return row;
    }

    /// <summary>Мод Мастерской: size 0 — строка, 1 — карточка, 2 — баннер.</summary>
    public static Control Workshop(HubMod h, int size)
    {
        if (size == 0)
        {
            var art = new Border { Width = 120, Height = 68, CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = Art(h, 384) };
            var words = Ui.Col(3,
                new TextBlock { Text = h.Name, FontWeight = FontWeight.SemiBold, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = $"{GameName(h.Game)} · {h.Author}", FontSize = 12, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis },
                Counters(h));
            words.VerticalAlignment = VerticalAlignment.Center;
            var free = HubLook.Badge(I18n.T("mk.free"), "good");
            free.VerticalAlignment = VerticalAlignment.Center;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
            grid.Children.Add(art);
            Grid.SetColumn(words, 1);
            grid.Children.Add(words);
            Grid.SetColumn(free, 2);
            grid.Children.Add(free);
            var row = new Border { Classes = { "store-row" }, Padding = new Thickness(8, 6), Child = grid };
            StoreKit.OnClick(row, () => CreatorPage.OpenMod(h));
            WorkshopMenu(row, h);
            return row;
        }
        if (size == 2)
        {
            // В смешанной ленте баннер помечен «Мастерская»; на вкладке «Моды сообщества» ярлык не нужен — там всё бесплатное.
            var tag = HubLook.Badge(I18n.T("v93.mk.workshop"), "onArt");
            tag.Margin = new Thickness(10);
            tag.VerticalAlignment = VerticalAlignment.Top;
            var words = Ui.Col(10,
                HubLook.Label(I18n.T("v93.mk.workshop.eyebrow"), Ui.Hex("#E6E7EE")),
                Gx.Title(h.Name, 26, Brushes.White),
                new TextBlock { Text = h.Summary, FontSize = 13.5, Foreground = Ui.Hex("#D0D3DC"), TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left },
                Ui.Row(8, HubLook.Badge(GameName(h.Game), "onArt"), HubLook.Badge(I18n.T("mod.by", ("author", h.Author)), "onArt"),
                    HubLook.Badge(I18n.Compact(h.Downloads), "onArt", Icons.Download), HubLook.Badge(I18n.Compact(h.Likes), "onArt", Icons.Heart)),
                Ui.Row(10, FreeBadgeOnArt()));
            words.VerticalAlignment = VerticalAlignment.Bottom;
            words.Margin = new Thickness(28, 0, 24, 24);
            var layers = new Panel { Children = { Art(h, 1600), new Border { Background = Gx.ShadeLeft(0.85, 235) }, new Border { Background = Gx.ShadeUp(0.5, 150) }, words, tag } };
            var t = new Border { Classes = { "store-tile", "feed-tile", "hero" }, Child = layers };
            StoreKit.OnClick(t, () => CreatorPage.OpenMod(h));
            WorkshopMenu(t, h);
            return t;
        }
        // 26.0: карточка без ярлыка «Мастерская» (на вкладке модов сообщества он был у каждой карточки) и с подписью
        // в две строки: «автор · игра» и счётчики значками.
        var top = new Panel { ClipToBounds = true, Children = { Art(h) } };
        var foot = Ui.Col(3,
            new TextBlock { Text = h.Name, FontWeight = FontWeight.SemiBold, FontSize = 14.5, TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = $"{h.Author} · {GameName(h.Game)}", FontSize = 12, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis },
            Counters(h));
        foot.Margin = new Thickness(12, 10, 12, 12);
        var g2 = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        g2.Children.Add(top);
        Grid.SetRow(foot, 1);
        g2.Children.Add(foot);
        var tile = new Border { Classes = { "store-tile", "feed-tile" }, Child = g2 };
        StoreKit.OnClick(tile, () => CreatorPage.OpenMod(h));
        WorkshopMenu(tile, h);
        return tile;
    }

    /// <summary>26.0: меню мода сообщества — открыть, следить за обновлениями (попадут в «Обновления» на вкладке), скопировать.</summary>
    static void WorkshopMenu(Control c, HubMod h) => Ctx.Attach(c, () =>
    {
        var info = Hub.ToModInfo(h);
        var tracked = Features.Tracking.Has(h.Game, info);
        return Ctx.Menu(
            Ctx.Item(I18n.T("ctx.open"), Icons.Eye, () => CreatorPage.OpenMod(h)),
            Ctx.Item(I18n.T(tracked ? "ctx.unfollow" : "ctx.follow"), Icons.Bell, () => Features.Tracking.Toggle(h.Game, info)),
            Ctx.Item(I18n.T("v93.mk.author.open"), Icons.User, () => MainWindow.Current?.Navigate(() => new AuthorPage(h.Uid, h.Author))),
            "-",
            Ctx.Copy(I18n.T("ctx.copyName"), h.Name),
            Ctx.Copy(I18n.T("ctx.copyId"), h.Id));
    });

    public static Control WorkshopCard(HubMod h, double width = 268)
    {
        var c = Workshop(h, 1);
        c.Width = width;
        c.Height = Math.Round(width * 9 / 16) + WorkshopFoot;
        return c;
    }
}
