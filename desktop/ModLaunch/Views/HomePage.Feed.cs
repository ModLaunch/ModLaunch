using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>Что лежит в плитке ленты.</summary>
public enum FeedKind { Carousel, Game, Mod, Top, Listing, Hub, Promo, Stats }

/// <summary>Одна плитка ленты. Tag — откуда мод: pick (выбор ModLaunch), popular, recent, favorite.</summary>
public sealed record FeedItem(FeedKind Kind, GameState? Game = null, ModInfo? Mod = null, Listing? Listing = null, HubMod? Hub = null, string Promo = "", string Tag = "");

/// <summary>Блок ленты: раскладка, подпись над ней и плитки по клеткам (null — ещё грузится).</summary>
public sealed class FeedBlock(FeedLayout layout, string theme)
{
    public FeedLayout Layout { get; } = layout;
    public string Theme { get; } = theme;
    public FeedItem?[] Items { get; } = new FeedItem?[layout.Cells.Length];
}

/// <summary>
/// 9.3: главная — «Лента» рекомендаций. Блоки плиток идут друг за другом, у каждого — своя
/// раскладка из 25 (наугад, одна и та же не больше трёх раз подряд). Наполнение — ваши игры,
/// популярные моды и «Выбор ModLaunch» для них, лоты рынка креаторов, моды Мастерской и
/// подсказки. Пока листаете вниз, новые блоки «вшиваются» снизу — сразу, без ожидания:
/// всё, из чего они строятся, загружено заранее. Лента живёт весь сеанс (перерисовка не
/// перемешивает её), «Перемешать» — собрать заново.
/// </summary>
public sealed partial class HomePage
{
    const int FeedStart = 5, FeedStep = 3, FeedMax = 40;

    static readonly List<FeedBlock> _plan = [];
    static readonly HashSet<string> _used = [];
    static readonly Dictionary<string, int> _gameUses = [];
    static readonly Dictionary<string, int> _promoAt = [];
    static readonly List<string> _recentThemes = [];
    static List<HubMod>? _feedHub;
    static List<Listing>? _feedMarket;
    static bool _feedExtrasLoading, _feedReady, _feedTimer;
    static string _feedFilter = "all";
    static int _planGames = -1;
    static readonly Random FeedRng = new();

    StackPanel? _feedColumn;
    ScrollViewer? _feedScroll;
    Control? _feedEnd;
    int _feedShown;

    /// <summary>Для снимков: собрать ленту заново с заданными раскладками и раскрытыми топами.</summary>
    public static void DemoLayouts(bool openTops, params string[] ids)
    {
        _plan.Clear(); _used.Clear(); _gameUses.Clear(); _promoAt.Clear(); _recentThemes.Clear();
        _openTops.Clear();
        if (openTops) foreach (var g in AppState.Games) _openTops.Add(g.Def.Id);
        FeedLayouts.Force(ids);
    }

    /// <summary>Данные для снимков экрана: лоты рынка и моды Мастерской без сети.</summary>
    public static void DemoFeed(List<Listing> market, List<HubMod> hub) { _feedMarket = market; _feedHub = hub; }

    static List<GameState> FeedGames() => MainWindow.OrderedGames()
        .Where(g => g.Status == Detect.Found && !Features.GameCollections.IsHidden(g.Def.Id))
        .OrderByDescending(g => Features.PlayTime.Get(g.Def.Id).LastPlayed ?? DateTime.MinValue).ToList();

    /// <summary>Собрать ленту заново (кнопка «Перемешать», смена фильтра).</summary>
    void Reshuffle()
    {
        _plan.Clear(); _used.Clear(); _gameUses.Clear(); _promoAt.Clear(); _recentThemes.Clear();
        FeedLayouts.Reset();
        Shown = false;
        _feedShownMax = 0;
        if (_feedScroll is not null) _feedScroll.Offset = default;
        Build();
        Shown = true;
    }

    void BuildFeed()
    {
        var mine = FeedGames();
        // Данные ленты: подборки и популярные моды (грузит LoadFeatured), рынок и Мастерская.
        if (_featured is null && !_featuredLoading && mine.Count > 0) { _featuredLoading = true; Dispatcher.UIThread.Post(() => _ = LoadFeatured(mine)); }
        if (!_feedExtrasLoading && (_feedHub is null || _feedMarket is null)) { _feedExtrasLoading = true; _ = LoadFeedExtras(); }
        // Сеть медленная — не ждём больше трёх секунд: лента строится из того, что уже есть
        // (но не раньше, чем закончится поиск игр — иначе лента вышла бы из одних подсказок).
        if (!_feedReady && !_feedTimer) { _feedTimer = true; ArmFeedTimer(); }
        if (mine.Count == 0 && !AppState.Games.Any(g => g.Status == Detect.Searching)) _feedReady = true;
        // Лента собиралась, когда игр ещё не было, а теперь они нашлись, — собрать заново.
        if (_planGames == 0 && mine.Count > 0 && _plan.Count > 0) { _plan.Clear(); _used.Clear(); _gameUses.Clear(); _promoAt.Clear(); _recentThemes.Clear(); }
        _planGames = mine.Count;

        if (_plan.Count == 0) _plan.Add(PlanFirst(mine));
        if (_feedReady) { FillPending(mine); while (_plan.Count < FeedStart) _plan.Add(Plan(_plan.Count, mine)); }

        var col = StoreKit.Column(spacing: 26, top: 22);
        if (Starter(mine) is { } starter) col.Children.Add(Intro(starter, 0));
        col.Children.Add(FeedHeader(mine));
        _feedColumn = col;
        _feedShown = 0;
        var visible = Math.Max(Math.Min(_plan.Count, _feedShownMax), Math.Min(_plan.Count, FeedStart));
        for (var i = 0; i < visible; i++) AppendBlock(i, animate: !Shown);
        if (!_feedReady) for (var i = 0; i < 2; i++) col.Children.Add(Skeleton());
        _feedEnd = FeedEnd();
        col.Children.Add(_feedEnd);
        if (Ads.Enabled)
        {
            _ad ??= AdSlot.Banner();
            if (_ad.Parent is Panel was) was.Children.Remove(_ad);
            col.Children.Insert(Math.Min(col.Children.Count - 1, 5), _ad);
        }

        // Прокрутка остаётся на месте, когда лента перерисовывается (нашлась игра, пришли моды).
        var offset = _feedScroll?.Offset ?? default;
        if (_feedScroll is null)
        {
            _feedScroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            _feedScroll.ScrollChanged += (_, _) => MaybeMore();
        }
        _feedScroll.Content = col;
        Content = _feedScroll;
        if (offset.Y > 0) Dispatcher.UIThread.Post(() => _feedScroll.Offset = offset, DispatcherPriority.Loaded);
    }

    static void ArmFeedTimer() => DispatcherTimer.RunOnce(() =>
    {
        if (_feedReady) return;
        if (AppState.Games.Any(g => g.Status == Detect.Searching)) { ArmFeedTimer(); return; }
        _feedReady = true;
        RebuildCurrent();
    }, TimeSpan.FromSeconds(Program.Screenshot ? 0.3 : 3));

    /// <summary>Сколько блоков было на экране до перерисовки — столько и показать снова.</summary>
    int _feedShownMax;

    /// <summary>Близко к низу — добавляем следующие блоки (без перерисовки остальной ленты).</summary>
    void MaybeMore()
    {
        if (_feedScroll is null || _feedColumn is null || !_feedReady) return;
        var left = _feedScroll.Extent.Height - (_feedScroll.Offset.Y + _feedScroll.Viewport.Height);
        if (left > 1400 || _feedShown >= FeedMax) return;
        var mine = FeedGames();
        for (var i = 0; i < FeedStep && _feedShown < FeedMax; i++)
        {
            if (_plan.Count <= _feedShown) _plan.Add(Plan(_plan.Count, mine));
            AppendBlock(_feedShown, animate: true);
        }
        UpdateEnd();
    }

    void AppendBlock(int index, bool animate)
    {
        if (_feedColumn is null || index >= _plan.Count) return;
        var view = RenderBlock(_plan[index], index, animate);
        var at = _feedEnd is not null && _feedColumn.Children.Contains(_feedEnd) ? _feedColumn.Children.IndexOf(_feedEnd) : _feedColumn.Children.Count;
        _feedColumn.Children.Insert(at, view);
        _feedShown = index + 1;
        _feedShownMax = Math.Max(_feedShownMax, _feedShown);
    }

    void UpdateEnd()
    {
        if (_feedEnd is null || _feedColumn is null) return;
        var i = _feedColumn.Children.IndexOf(_feedEnd);
        if (i < 0) return;
        _feedEnd = FeedEnd();
        _feedColumn.Children[i] = _feedEnd;
    }

    static async Task LoadFeedExtras()
    {
        // Рынок и Мастерская — та же загрузка, что у Creator Hub (один раз на всех).
        try { await MarketData.Load(); } catch { }
        _feedHub ??= MarketData.Workshop ?? [];
        _feedMarket ??= MarketData.Listings ?? [];
        _feedExtrasLoading = false;
        Dispatcher.UIThread.Post(RebuildCurrent);
    }

    // ---------------------------------------------------------------- планировщик: что куда поставить

    static FeedBlock PlanFirst(List<GameState> mine)
    {
        var block = new FeedBlock(FeedLayouts.Next(heroOnly: true), "you");
        // Большое место первого блока — карусель ваших игр (или приветствие, если игр нет).
        var big = Array.FindIndex(block.Layout.Cells, c => c.Slot == Slot.XL);
        if (big >= 0) block.Items[big] = new FeedItem(FeedKind.Carousel);
        foreach (var g in mine.Take(5)) Use(g);
        return block;
    }

    /// <summary>Клетки первого блока, которые ждали данных, — заполнить.</summary>
    static void FillPending(List<GameState> mine)
    {
        foreach (var b in _plan)
            for (var i = 0; i < b.Items.Length; i++)
                b.Items[i] ??= Take(b, b.Layout.Cells[i].Slot, mine);
    }

    static FeedBlock Plan(int index, List<GameState> mine)
    {
        var block = new FeedBlock(FeedLayouts.Next(), Theme(mine));
        // Сначала большие места: баннер игры темы встаёт в самую крупную клетку, моды — вокруг.
        foreach (var i in Enumerable.Range(0, block.Items.Length).OrderBy(i => SlotRank(block.Layout.Cells[i].Slot)))
            block.Items[i] = Take(block, block.Layout.Cells[i].Slot, mine);
        _recentThemes.Add(block.Theme);
        return block;
    }

    static int SlotRank(Slot s) => s switch { Slot.XL => 0, Slot.W => 1, Slot.L => 2, Slot.List => 3, Slot.T => 4, Slot.M => 5, Slot.Chip => 7, _ => 6 };

    /// <summary>Тема блока: одна игра (её моды и топ), рынок, Мастерская или «вперемешку».</summary>
    static string Theme(List<GameState> mine)
    {
        var options = new List<(string Theme, double Weight)> { ("mix", 2.2) };
        var last = _recentThemes.TakeLast(3).ToHashSet();
        foreach (var g in mine.Where(g => g.Def.HasCatalog))
            if (ModsOf(g).Any() && !last.Contains("game:" + g.Def.Id)) options.Add(("game:" + g.Def.Id, 1.3));
        if (Allowed(FeedKind.Listing) && (_feedMarket ?? []).Count(l => !_used.Contains("l:" + l.Id)) >= 2 && !last.Contains("market")) options.Add(("market", 1.1));
        if (Allowed(FeedKind.Hub) && (_feedHub ?? []).Count(h => !_used.Contains("h:" + h.Id)) >= 2 && !last.Contains("workshop")) options.Add(("workshop", 0.9));
        if (_feedFilter == "market" && options.Any(o => o.Theme == "market")) return "market";
        if (_feedFilter == "workshop" && options.Any(o => o.Theme == "workshop")) return "workshop";
        var total = options.Sum(o => o.Weight);
        var r = FeedRng.NextDouble() * total;
        foreach (var (theme, w) in options) { if ((r -= w) <= 0) return theme; }
        return "mix";
    }

    static bool Allowed(FeedKind k) => _feedFilter switch
    {
        "games" => k is FeedKind.Game or FeedKind.Carousel or FeedKind.Stats or FeedKind.Promo,
        "mods" => k is FeedKind.Mod or FeedKind.Top or FeedKind.Hub or FeedKind.Promo,
        "market" => k is FeedKind.Listing or FeedKind.Hub or FeedKind.Promo,
        "workshop" => k is FeedKind.Hub or FeedKind.Listing or FeedKind.Promo,
        _ => true,
    };

    /// <summary>Моды игры для ленты: подборка ModLaunch, популярные, недавние и избранные — ещё не показанные.</summary>
    static IEnumerable<(ModInfo Mod, string Tag)> ModsOf(GameState g)
    {
        var id = g.Def.Id;
        foreach (var (fg, m) in _featured ?? []) if (fg.Def.Id == id) yield return (m, "pick");
        foreach (var m in Views.Aside.Popular(g) ?? []) yield return (m, "popular");
        foreach (var r in Features.Recent.All().Where(r => r.Game == id).Take(6)) yield return (r.Mod, "recent");
        foreach (var f in Favorites.All().Where(f => f.GameId == id).Take(6)) yield return (f.Mod, "favorite");
    }

    static void Use(GameState g) => _gameUses[g.Def.Id] = _gameUses.GetValueOrDefault(g.Def.Id) + 1;

    /// <summary>Подходящая плитка для места: сначала по теме блока, потом что найдётся.</summary>
    static FeedItem Take(FeedBlock block, Slot slot, List<GameState> mine)
    {
        // Одна и та же игра баннером — не дважды в блоке (моды игры рядом с её баннером — можно).
        var inBlock = block.Items.Where(i => i is { Kind: FeedKind.Game, Game: not null }).Select(i => i!.Game!.Def.Id).ToHashSet();
        var themeGame = block.Theme.StartsWith("game:") ? mine.FirstOrDefault(g => "game:" + g.Def.Id == block.Theme) : null;

        FeedItem? GameItem(bool poster = false)
        {
            if (!Allowed(FeedKind.Game)) return null;
            if (themeGame is not null && !inBlock.Contains(themeGame.Def.Id)) { Use(themeGame); return new FeedItem(FeedKind.Game, themeGame, Tag: poster ? "poster" : ""); }
            var g = mine.Where(x => !inBlock.Contains(x.Def.Id)).OrderBy(x => _gameUses.GetValueOrDefault(x.Def.Id)).ThenBy(_ => FeedRng.Next()).FirstOrDefault();
            if (g is null) return null;
            Use(g);
            return new FeedItem(FeedKind.Game, g, Tag: poster ? "poster" : "");
        }
        // 9.3: крупным модам — крупные места (баннеры), мелким — «чипы» и строки, остальным — плитки.
        FeedItem? ModItem()
        {
            if (!Allowed(FeedKind.Mod)) return null;
            IEnumerable<GameState> games = themeGame is not null ? [themeGame, .. mine.Where(g => g != themeGame).OrderBy(_ => FeedRng.Next())] : mine.OrderBy(_ => FeedRng.Next());
            foreach (var g in games)
            {
                var pool = ModsOf(g).Where(x => !_used.Contains("m:" + g.Def.Id + "/" + x.Mod.Id)).GroupBy(x => x.Mod.Id).Select(x => x.First()).ToList();
                if (pool.Count == 0) continue;
                var byDownloads = pool.OrderByDescending(x => x.Mod.Downloads).ToList();
                var (m, tag) = slot switch
                {
                    Slot.XL or Slot.W or Slot.L => byDownloads[0],
                    Slot.Chip or Slot.R => byDownloads[^1],
                    _ => byDownloads[Math.Min(byDownloads.Count - 1, FeedRng.Next(Math.Max(1, byDownloads.Count / 2 + 1)))],
                };
                _used.Add("m:" + g.Def.Id + "/" + m.Id);
                return new FeedItem(FeedKind.Mod, g, m, Tag: tag);
            }
            return null;
        }
        FeedItem? TopItem()
        {
            if (!Allowed(FeedKind.Top)) return null;
            IEnumerable<GameState> games = themeGame is not null ? [themeGame] : mine.OrderBy(_ => FeedRng.Next());
            foreach (var g in games)
            {
                if ((Views.Aside.Popular(g) ?? []).Count < 3 || !_used.Add("top:" + g.Def.Id)) continue;
                // Моды из топа ниже в ленте не повторяются.
                foreach (var m in (Views.Aside.Popular(g) ?? []).OrderByDescending(m => m.Downloads).Take(block.Layout.Fold ? 10 : 5)) _used.Add("m:" + g.Def.Id + "/" + m.Id);
                return new FeedItem(FeedKind.Top, g);
            }
            return null;
        }
        FeedItem? ListingItem()
        {
            if (!Allowed(FeedKind.Listing)) return null;
            var l = (_feedMarket ?? []).Where(x => !_used.Contains("l:" + x.Id)).OrderByDescending(x => x.Sales + FeedRng.Next(4)).FirstOrDefault();
            if (l is null) return null;
            _used.Add("l:" + l.Id);
            return new FeedItem(FeedKind.Listing, Listing: l);
        }
        FeedItem? HubItem()
        {
            if (!Allowed(FeedKind.Hub)) return null;
            var h = (_feedHub ?? []).Where(x => !_used.Contains("h:" + x.Id)).OrderByDescending(x => Hub.Trend(x) + FeedRng.NextDouble() * 0.1).FirstOrDefault();
            if (h is null) return null;
            _used.Add("h:" + h.Id);
            return new FeedItem(FeedKind.Hub, Hub: h);
        }
        FeedItem? PromoItem()
        {
            if (!Allowed(FeedKind.Promo)) return null;
            string[] all = ["creator", "center", "workshop", "sell", "style", "add", "bigpicture", "friends"];
            var at = _plan.Count;
            var id = all.Where(p => !_promoAt.TryGetValue(p, out var when) || at - when >= 6).OrderBy(_ => FeedRng.Next()).FirstOrDefault();
            if (id is null) return null;
            _promoAt[id] = at;
            return new FeedItem(FeedKind.Promo, Promo: id);
        }
        FeedItem? StatsItem()
        {
            if (!Allowed(FeedKind.Stats) || mine.Count == 0 || !_used.Add("stats:" + _plan.Count / 8)) return null;
            return new FeedItem(FeedKind.Stats);
        }

        var order = new List<Func<FeedItem?>>();
        switch (slot)
        {
            case Slot.XL:
                // Большое место — то баннер игры, то самый популярный мод (крупным модам — крупные баннеры).
                if (FeedRng.NextDouble() < 0.5) order.AddRange([ModItem, () => GameItem(), ListingItem, HubItem, PromoItem]);
                else order.AddRange([() => GameItem(), ModItem, ListingItem, HubItem, PromoItem]);
                break;
            case Slot.Chip: order.AddRange([ModItem, ModItem, ListingItem, HubItem, PromoItem]); break;
            case Slot.L: order.AddRange([ModItem, () => GameItem(), ListingItem, HubItem, PromoItem]); break;
            case Slot.W: order.AddRange([() => GameItem(), ModItem, PromoItem, ListingItem, HubItem]); break;
            case Slot.T: order.AddRange([() => GameItem(poster: true), TopItem, ModItem, ListingItem]); break;
            case Slot.List: order.AddRange([TopItem, StatsItem, PromoItem, ModItem]); break;
            case Slot.R: order.AddRange([ModItem, ListingItem, HubItem, PromoItem]); break;
            default:
                order.AddRange([ModItem, ModItem, ListingItem, HubItem, PromoItem, StatsItem, () => GameItem()]);
                // Немного случайности: иногда лот рынка, мод Мастерской или подсказка — первыми.
                var r = FeedRng.NextDouble();
                if (r < 0.22) order.Insert(0, ListingItem);
                else if (r < 0.36) order.Insert(0, HubItem);
                else if (r < 0.46) order.Insert(0, PromoItem);
                else if (r < 0.52) order.Insert(0, StatsItem);
                break;
        }
        // Тема блока важнее размера места: в блоке рынка — лоты, в блоке Мастерской — её моды.
        if (block.Theme == "market") order.Insert(0, ListingItem);
        if (block.Theme == "workshop") order.Insert(0, HubItem);
        // Раскрывающийся топ — всегда топ (если есть игра с популярными модами).
        if (block.Layout.Fold) order.Insert(0, TopItem);
        foreach (var f in order) if (f() is { } item) return item;
        // Совсем пусто (новичок без игр и без сети) — подсказки по кругу.
        _promoAt.Clear();
        return PromoItem() ?? new FeedItem(FeedKind.Promo, Promo: "add");
    }

    // ---------------------------------------------------------------- отрисовка

    Control FeedHeader(List<GameState> mine)
    {
        var title = new TextBlock { Text = I18n.T("v93.feed.title"), FontFamily = Gx.Display, FontSize = 28, FontWeight = FontWeight.Bold, LetterSpacing = -0.6, VerticalAlignment = VerticalAlignment.Center };
        var mods = mine.Sum(g => g.ModCount);
        var sub = Ui.Text(mine.Count == 0 ? I18n.T("v93.feed.sub.empty") : I18n.T("v93.feed.sub", ("games", mine.Count), ("mods", mods)), "muted", wrap: true);
        var words = Ui.Col(4, title, sub);

        var chips = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var (id, key, icon) in new[] { ("all", "v93.feed.all", Icons.Sparkles), ("games", "v93.feed.games", Icons.Gamepad), ("mods", "v93.feed.mods", Icons.Package), ("market", "v93.feed.market", Icons.Bag), ("workshop", "v93.feed.workshop", Icons.Globe) })
        {
            var f = id;
            var chip = Ui.Button(I18n.T(key), () => { if (_feedFilter == f) return; _feedFilter = f; Reshuffle(); }, _feedFilter == id ? "chip active" : "chip", icon);
            chip.Margin = new Thickness(0, 0, 8, 0);
            chips.Children.Add(chip);
        }
        var shuffle = Ui.Button(I18n.T("v93.feed.shuffle"), Reshuffle, "", Icons.Shuffle);
        var right = Ui.Row(4, chips, shuffle);
        right.VerticalAlignment = VerticalAlignment.Center;
        var head = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        head.Children.Add(right);
        head.Children.Add(words);
        return head;
    }

    static Control Skeleton()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*,*"), ColumnSpacing = StoreKit.Gap, Height = 300 };
        for (var i = 0; i < 3; i++) { var p = StoreKit.Placeholder(double.NaN, double.NaN); Grid.SetColumn(p, i); grid.Children.Add(p); }
        return grid;
    }

    Control FeedEnd()
    {
        if (_feedShown < FeedMax && _feedReady) return new Border { Height = 1 };
        if (!_feedReady) return new Border { Height = 1 };
        var again = Ui.Button(I18n.T("v93.feed.shuffle"), Reshuffle, "primary", Icons.Shuffle);
        return new Border
        {
            Classes = { "card" }, Padding = new Thickness(26, 20),
            Child = Ui.Row(18, Ui.Icon(Icons.Trophy, 26, Ui.Res("Brand2")), Ui.Col(3, Ui.Text(I18n.T("v93.feed.end"), "h3"), Ui.Text(I18n.T("v93.feed.end.text"), "small muted")), again),
        };
    }

    /// <summary>Подпись блока: тема (игра, рынок, Мастерская) и ссылка «Все ›».</summary>
    static Control? Caption(FeedBlock b, int index)
    {
        if (index == 0 || b.Layout.Fold) return null;
        string text;
        Action? open = null;
        if (b.Layout.Shelf) { text = I18n.T("home.yourGames"); open = () => MainWindow.Current?.Navigate(() => new LibraryPage()); }
        else if (b.Theme.StartsWith("game:"))
        {
            var g = AppState.Games.FirstOrDefault(x => "game:" + x.Def.Id == b.Theme);
            if (g is null) return null;
            text = I18n.T("v93.feed.cap.game", ("game", g.Def.Name));
            var id = g.Def.Id;
            open = () => MainWindow.Current?.Navigate(() => new GamePage(id, "catalog"));
        }
        else if (b.Theme == "market") { text = I18n.T("v93.feed.cap.market"); open = () => MainWindow.Current?.Navigate(() => new MarketPage()); }
        else if (b.Theme == "workshop") { text = I18n.T("v93.feed.cap.workshop"); open = () => MainWindow.Current?.Navigate(() => new MarketPage("workshop")); }
        else text = I18n.T("v93.feed.cap.mix");
        var row = new DockPanel();
        if (open is not null)
        {
            var all = Ui.Button(I18n.T("home.all") + "  ›", open, "ghost");
            all.Padding = new Thickness(8, 2);
            all.FontSize = 12.5;
            all.Foreground = Ui.Res("Muted");
            DockPanel.SetDock(all, Dock.Right);
            row.Children.Add(all);
        }
        var num = new TextBlock { Text = (index + 1).ToString("00"), FontFamily = Gx.Display, FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        row.Children.Add(Ui.Row(0, num, Gx.Eyebrow(text, Ui.Res("Text"), 11.5)));
        return row;
    }

    Control RenderBlock(FeedBlock b, int index, bool animate)
    {
        Control body;
        var tiles = new List<(Control Tile, double X)>();
        if (b.Layout.Fold)
        {
            // Топ, который раскрывается в список по нажатию.
            var item = b.Items[0];
            body = item is { Kind: FeedKind.Top, Game: { } tg } ? TopFold(tg) : new Border { Height = 230, Child = item is null ? StoreKit.Placeholder(double.NaN, double.NaN) : FeedTile(item, Slot.L) };
            tiles.Add((body, 0.5));
        }
        else if (b.Layout.Shelf)
        {
            // Полка обложек: ваши игры (или игры темы) — в ряд с прокруткой.
            var posters = new List<Control>();
            var games = FeedGames();
            foreach (var g in games) { var p = StoreKit.Poster(g, 172); posters.Add(p); tiles.Add((p, 0.5)); }
            posters.Add(StoreKit.AddPoster(172));
            body = StoreKit.Shelf(posters);
        }
        else
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(b.Layout.Cols), RowDefinitions = new RowDefinitions(b.Layout.Rows), ColumnSpacing = StoreKit.Gap, RowSpacing = StoreKit.Gap, Height = b.Layout.Height };
            var cols = b.Layout.Cols.Split(',').Length;
            for (var i = 0; i < b.Layout.Cells.Length; i++)
            {
                var c = b.Layout.Cells[i];
                var tile = b.Items[i] is { } item ? FeedTile(item, c.Slot) : StoreKit.Placeholder(double.NaN, double.NaN);
                Grid.SetColumn(tile, c.Col);
                Grid.SetRow(tile, c.Row);
                Grid.SetColumnSpan(tile, c.ColSpan);
                Grid.SetRowSpan(tile, c.RowSpan);
                grid.Children.Add(tile);
                tiles.Add((tile, (c.Col + c.ColSpan / 2.0) / cols));
            }
            body = grid;
        }
        if (animate) Stitch(tiles, index);
        var caption = Caption(b, index);
        return caption is null ? body : Ui.Col(12, caption, body);
    }

    /// <summary>
    /// «Вшивание» блока: левые плитки въезжают слева, правые — справа, средние — снизу,
    /// одна за другой с небольшой задержкой — блок «застёгивается» на глазах.
    /// </summary>
    static void Stitch(List<(Control Tile, double X)> tiles, int index)
    {
        var baseDelay = index < FeedStart ? Math.Min(index, 4) * 70 : 0;
        for (var i = 0; i < tiles.Count; i++)
        {
            var (t, x) = tiles[i];
            var from = x < 0.34 ? "translateX(-46px) scale(0.96)" : x > 0.66 ? "translateX(46px) scale(0.96)" : "translateY(34px) scale(0.95)";
            Animate.From(t, from, 440, baseDelay + i * 45, new BackEaseOut());
        }
    }

    // ---------------------------------------------------------------- плитки

    Control FeedTile(FeedItem item, Slot slot)
    {
        try
        {
            return item.Kind switch
            {
                FeedKind.Carousel => Carousel(FeedGames().Take(5).ToList()),
                FeedKind.Game when slot is Slot.T || item.Tag == "poster" => GamePoster(item.Game!),
                FeedKind.Game when slot is Slot.XL or Slot.W or Slot.L => GameBanner(item.Game!, slot),
                FeedKind.Game => GameSmall(item.Game!),
                FeedKind.Mod when slot is Slot.Chip => ModChip(item.Game!, item.Mod!),
                FeedKind.Listing when slot is Slot.Chip => ListingChip(item.Listing!),
                FeedKind.Hub when slot is Slot.Chip => HubChip(item.Hub!),
                FeedKind.Mod when slot is Slot.R => StoreKit.ModItem(item.Game!, item.Mod!),
                FeedKind.Mod when slot is Slot.XL or Slot.W or Slot.L => ModBanner(item.Game!, item.Mod!, item.Tag, slot),
                FeedKind.Mod when slot is Slot.T => ModTall(item.Game!, item.Mod!, item.Tag),
                FeedKind.Mod => ModSmall(item.Game!, item.Mod!, item.Tag),
                FeedKind.Top => TopList(item.Game!),
                FeedKind.Listing => MarketTiles.Listing(item.Listing!, slot is Slot.XL or Slot.W or Slot.L ? 2 : slot is Slot.R ? 0 : 1),
                FeedKind.Hub => MarketTiles.Workshop(item.Hub!, slot is Slot.XL or Slot.W or Slot.L ? 2 : slot is Slot.R ? 0 : 1),
                FeedKind.Stats => StatsTile(),
                _ when slot is Slot.Chip => PromoChip(item.Promo),
                _ => PromoTile(item.Promo, slot is Slot.XL or Slot.W or Slot.L),
            };
        }
        catch { return StoreKit.Placeholder(double.NaN, double.NaN); }
    }

    static Border Tile(Control content, Action open, string? extra = null)
    {
        var t = new Border { Classes = { "store-tile", "feed-tile" }, Child = content };
        if (extra is not null) t.Classes.Add(extra);
        StoreKit.OnClick(t, open);
        return t;
    }

    static Control GameBanner(GameState g, Slot slot)
    {
        // Большое место — полный слайд (как в карусели); полоса и половина ряда ниже — компактный баннер.
        if (slot == Slot.XL) return new Border { Classes = { "store-tile", "feed-tile", "hero" }, Child = new Panel { Children = { Slide(g), Gx.Haze(right: true) } } };
        var id = g.Def.Id;
        var (dot, state) = StoreKit.GameState(g);
        var logo = Images.GameAsset(g.Def, Images.Art.Logo, 512);
        Control title = logo is null
            ? Gx.Title(g.Def.Name, 24, Brushes.White, 1)
            : new Image { Source = logo, MaxHeight = 58, MaxWidth = 260, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
        Control action;
        if (Features.Launcher.IsRunning(id)) action = PlayControls.RunningPill(id, big: false);
        else if (g.LoaderInstalled || g.Def.Loader is Games.LoaderKind.None or Games.LoaderKind.Minecraft)
        {
            var gs = g;
            action = Ui.Button(I18n.T("games.play"), () => Actions.Play(gs), "primary", Icons.Play);
        }
        else
        {
            var gs = g;
            action = Ui.Button(I18n.T("games.installLoader", ("loader", g.Def.LoaderName)), () => Actions.InstallLoader(gs), "primary", Icons.Download);
        }
        if (action is Button ab) ab.Padding = new Thickness(18, 9);
        var open = Ui.Button(I18n.T("v92.home.toGame"), () => MainWindow.Current?.Navigate(() => new GamePage(id)), "hero-ghost");
        open.Padding = new Thickness(16, 9);
        var words = Ui.Col(10, title, StoreKit.Pill(state, dot), Ui.Row(8, action, open));
        words.VerticalAlignment = VerticalAlignment.Center;
        words.HorizontalAlignment = HorizontalAlignment.Left;
        words.Margin = new Thickness(24, 12, 24, 12);
        var layers = new Panel
        {
            Children =
            {
                Ui.GameImage(g.Def, 1600, art: Images.Art.Hero),
                new Border { Background = Gx.ShadeLeft(0.8, 235) },
                Gx.Haze(right: true),
                words,
            },
        };
        var t = Tile(layers, () => MainWindow.Current?.Navigate(() => new GamePage(id)), "hero");
        t.ContextFlyout = GameCard.Menu(g);
        return t;
    }

    static Control GameSmall(GameState g)
    {
        var (dot, state) = StoreKit.GameState(g);
        var words = Ui.Col(6, Gx.Title(g.Def.Name, 15, lines: 2), StoreKit.Pill(state, dot));
        words.VerticalAlignment = VerticalAlignment.Bottom;
        words.Margin = new Thickness(14, 0, 14, 14);
        var layers = new Panel { Children = { Ui.GameImage(g.Def, 768, art: Images.Art.Hero), new Border { Background = Gx.ShadeUp(0.3) }, words } };
        if (g.LoaderInstalled || g.Def.Loader is Games.LoaderKind.None or Games.LoaderKind.Minecraft)
        {
            var gs = g;
            var play = Ui.Button("", () => Actions.Play(gs), "icon primary tile-play", Icons.Play, I18n.T("games.play"));
            play.Width = play.Height = 40;
            play.CornerRadius = new CornerRadius(20);
            play.HorizontalAlignment = HorizontalAlignment.Right;
            play.VerticalAlignment = VerticalAlignment.Top;
            play.Margin = new Thickness(10);
            layers.Children.Add(play);
        }
        var id = g.Def.Id;
        var t = Tile(layers, () => MainWindow.Current?.Navigate(() => new GamePage(id)));
        t.ContextFlyout = GameCard.Menu(g);
        return t;
    }

    static Control GamePoster(GameState g)
    {
        var (dot, state) = StoreKit.GameState(g);
        var words = Ui.Col(6, Gx.Title(g.Def.Name, 14, lines: 2), StoreKit.Pill(state, dot));
        words.VerticalAlignment = VerticalAlignment.Bottom;
        words.Margin = new Thickness(12, 0, 12, 12);
        var layers = new Panel { Children = { Ui.GameImage(g.Def, 512, art: Images.Art.Cover), new Border { Background = Gx.ShadeUp(0.45) }, words } };
        var id = g.Def.Id;
        var t = Tile(layers, () => MainWindow.Current?.Navigate(() => new GamePage(id)), "poster");
        t.ContextFlyout = GameCard.Menu(g);
        return t;
    }

    static string ModEyebrow(string tag) => tag switch
    {
        "pick" => I18n.T("v91.home.pick"),
        "recent" => I18n.T("mc.recent"),
        "favorite" => I18n.T("home.favorites"),
        _ => I18n.T("v93.feed.popular"),
    };

    static Control InstallButton(GameState g, ModInfo m, bool big)
    {
        var installed = Actions.IsInstalled(g, m.Id);
        var busy = Actions.IsBusy(g, m.Id);
        Button b;
        if (installed) b = Ui.Button(big ? I18n.T("mod.installed") : "", () => { }, big ? "hero-ghost" : "icon", Icons.Check, I18n.T("mod.installed"));
        else
        {
            var gs = g; var mm = m;
            b = Ui.Button(big ? I18n.T("mod.install") : "", () => _ = Actions.Install(gs, mm), big ? "primary" : "icon primary", Icons.Download, I18n.T("mod.install"));
        }
        b.IsEnabled = !installed && !busy && g.Status == Detect.Found;
        if (big) b.Padding = new Thickness(20, 11);
        else { b.Width = b.Height = 36; b.CornerRadius = new CornerRadius(18); }
        return b;
    }

    static Control ModIcon(ModInfo m, double size, double radius)
    {
        var icon = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(radius), ClipToBounds = true,
            BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), BorderThickness = new Thickness(1),
            Child = Ui.Thumb(m.Icon, m.Name, size, 0, (int)(size * 2.4)),
        };
        return new Border { CornerRadius = new CornerRadius(radius), Child = icon, BoxShadow = BoxShadows.Parse("0 14 34 0 #A0000000"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
    }

    static Control ModBanner(GameState g, ModInfo m, string tag, Slot slot)
    {
        var big = slot == Slot.XL;
        var white = Brushes.White;
        var desc = new TextBlock { Text = m.Description, FontSize = 13.5, Foreground = Ui.Hex("#D0D3DC"), TextWrapping = TextWrapping.Wrap, MaxLines = big ? 3 : 2, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left };
        var meta = Ui.Row(8, StoreKit.Pill(g.Def.ShortName));
        if (m.Downloads > 0) meta.Children.Add(StoreKit.Pill("↓ " + I18n.Compact(m.Downloads)));
        if (m.Author != "") meta.Children.Add(StoreKit.Pill(m.Author));
        var words = Ui.Col(big ? 14 : 10, Gx.Eyebrow(ModEyebrow(tag), Ui.Hex("#E6E7EE")), Gx.Title(m.Name, big ? 30 : 22, white), desc, meta, Ui.Row(10, InstallButton(g, m, true)));
        words.VerticalAlignment = VerticalAlignment.Bottom;
        words.HorizontalAlignment = HorizontalAlignment.Left;
        words.MaxWidth = 620;
        words.Margin = new Thickness(big ? 32 : 24, 0, 24, big ? 28 : 22);
        // Обложка мода во всю плитку; квадратный значок — справа, чтобы не спорить с текстом.
        var cover = ModCover.Create(g, m, 1280, big: true, iconH: HorizontalAlignment.Right, iconShare: 0.66);
        var layers = new Panel
        {
            Children =
            {
                cover,
                new Border { Background = Gx.ShadeLeft(0.75, 240) },
                new Border { Background = Gx.ShadeUp(0.55, 170) },
                Gx.Haze(right: true),
                words,
            },
        };
        var id = g.Def.Id;
        var t = Tile(layers, () => MainWindow.Current?.Navigate(() => new ModPage(id, m)), "hero");
        Ctx.Attach(t, () => ModRow.Menu(g.Def, m, Actions.IsInstalled(g, m.Id), Actions.IsBusy(g, m.Id), () => _ = Actions.Install(g, m), () => MainWindow.Current?.Navigate(() => new ModPage(id, m))));
        return t;
    }

    static Control ModSmall(GameState g, ModInfo m, string tag)
    {
        var top = new Panel { ClipToBounds = true, Children = { ModCover.Create(g, m, 768) } };
        var label = Gx.Tag(ModEyebrow(tag), null, null, 10.5);
        label.Margin = new Thickness(10);
        top.Children.Add(label);
        var foot = new DockPanel { Margin = new Thickness(12, 10, 10, 12) };
        var action = InstallButton(g, m, false);
        action.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(action, Dock.Right);
        foot.Children.Add(action);
        foot.Children.Add(Ui.Col(2,
            new TextBlock { Text = m.Name, FontWeight = FontWeight.SemiBold, FontSize = 14.5, TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = m.Downloads > 0 ? $"{g.Def.ShortName} · ↓ {I18n.Compact(m.Downloads)}" : g.Def.ShortName, FontSize = 12, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis }));
        var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        grid.Children.Add(top);
        Grid.SetRow(foot, 1);
        grid.Children.Add(foot);
        var id = g.Def.Id;
        var t = Tile(grid, () => MainWindow.Current?.Navigate(() => new ModPage(id, m)));
        Ctx.Attach(t, () => ModRow.Menu(g.Def, m, Actions.IsInstalled(g, m.Id), Actions.IsBusy(g, m.Id), () => _ = Actions.Install(g, m), () => MainWindow.Current?.Navigate(() => new ModPage(id, m))));
        return t;
    }

    static Control ModTall(GameState g, ModInfo m, string tag)
    {
        var words = Ui.Col(10,
            Gx.Eyebrow(ModEyebrow(tag), Ui.Hex("#E6E7EE"), 10.5),
            Gx.Title(m.Name, 18, Brushes.White, 3),
            new TextBlock { Text = m.Description, FontSize = 12.5, Foreground = Ui.Hex("#C9CCD6"), TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis },
            InstallButton(g, m, true));
        words.VerticalAlignment = VerticalAlignment.Bottom;
        words.Margin = new Thickness(16, 0, 16, 16);
        var cover = ModCover.Create(g, m, 768, iconV: VerticalAlignment.Top, iconShare: 0.7);
        var layers = new Panel { Children = { cover, new Border { Background = Gx.ShadeUp(0.35, 245) }, Gx.Haze(), words } };
        var id = g.Def.Id;
        return Tile(layers, () => MainWindow.Current?.Navigate(() => new ModPage(id, m)), "poster");
    }

    // ---------------------------------------------------------------- маленькие кнопки-«чипы»

    static Border Chip(Control icon, string title, string sub, Control? right, Action open)
    {
        var words = Ui.Col(2,
            new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = sub, FontSize = 11.5, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis });
        words.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10, VerticalAlignment = VerticalAlignment.Center };
        icon.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(icon);
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);
        if (right is not null)
        {
            right.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);
        }
        var t = new Border { Classes = { "store-tile", "feed-chip" }, Padding = new Thickness(10, 8), Child = grid };
        StoreKit.OnClick(t, open);
        return t;
    }

    /// <summary>Небольшой мод — компактная кнопка: значок, название, загрузки и круглая «Установить».</summary>
    static Control ModChip(GameState g, ModInfo m)
    {
        var action = InstallButton(g, m, false);
        if (action is Button b) { b.Width = b.Height = 30; b.CornerRadius = new CornerRadius(15); }
        var id = g.Def.Id;
        var t = Chip(Ui.Thumb(m.Icon, m.Name, 40, 10, 120), m.Name, m.Downloads > 0 ? $"{g.Def.ShortName} · ↓ {I18n.Compact(m.Downloads)}" : g.Def.ShortName, action,
            () => MainWindow.Current?.Navigate(() => new ModPage(id, m)));
        Ctx.Attach(t, () => ModRow.Menu(g.Def, m, Actions.IsInstalled(g, m.Id), Actions.IsBusy(g, m.Id), () => _ = Actions.Install(g, m), () => MainWindow.Current?.Navigate(() => new ModPage(id, m))));
        return t;
    }

    static Control ListingChip(Listing l)
    {
        var game = Games.GameCatalog.ById(l.Game);
        Control icon = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = game is null ? Gx.Gradient(null) : Ui.GameImage(game, 128, art: Images.Art.Cover) };
        var price = Gx.Price(MarketViews.PriceText(l), l.Free);
        return Chip(icon, l.Title, $"{I18n.T("mk.kind." + l.Kind)} · {l.Author}", price, () => MarketTiles.Open(l));
    }

    static Control HubChip(HubMod h)
    {
        var game = Games.GameCatalog.ById(h.Game);
        Control icon = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = h.Images.Count > 0 ? Ui.Thumb(h.Images[0], h.Name, 40, 10, 120) : game is null ? Gx.Gradient("#2BB673") : Ui.GameImage(game, 128, art: Images.Art.Cover) };
        return Chip(icon, h.Name, $"{I18n.T("v93.mk.workshop")} · ↓ {I18n.Compact(h.Downloads)}", Gx.Price(I18n.T("mk.free"), true), () => CreatorPage.OpenMod(h));
    }

    static Control PromoChip(string id)
    {
        var (icon, title, open) = id switch
        {
            "creator" => (Icons.Creator, "Creator Hub", (Action)(() => MainWindow.Current?.Navigate(() => new MarketPage()))),
            "center" => (Icons.Package, I18n.T("mc.title"), () => MainWindow.Current?.Navigate(() => new ModsCenterPage())),
            "workshop" => (Icons.Globe, I18n.T("v93.mk.workshop"), () => MainWindow.Current?.Navigate(() => new MarketPage("workshop"))),
            "sell" => (Icons.Bag, I18n.T("v93.promo.sell"), () => MainWindow.Current?.Navigate(() => new MarketPage("studio"))),
            "style" => (Icons.Palette, I18n.T("v92.style.title"), () => StylePicker.Show()),
            "bigpicture" => (Icons.Tv, "Big Picture", BigPictureWindow.Open),
            "friends" => (Icons.Users, I18n.T("v92.nav.friends"), () => MainWindow.Current?.Navigate(() => new FriendsPage())),
            _ => (Icons.Plus, I18n.T("add.title"), () => MainWindow.Current?.Navigate(() => new AddGamePage())),
        };
        var badge = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(10), Background = Ui.Res("BrandSoft"), Child = Ui.Icon(icon, 18, Ui.Res("Brand2")) };
        return Chip(badge, title, I18n.T("v93.feed.open"), Ui.Icon(Icons.ChevronRight, 16, Ui.Res("Muted")), open);
    }

    // ---------------------------------------------------------------- топ, который раскрывается

    /// <summary>Какие «топы» раскрыты (переживает перерисовку ленты).</summary>
    static readonly HashSet<string> _openTops = [];

    /// <summary>
    /// «Топ-10» игры: сверху пьедестал из трёх лучших модов с обложками, по нажатию «Весь топ»
    /// места 4–10 ложатся одной строкой за другой — сверху вниз, с лёгким отскоком.
    /// </summary>
    static Control TopFold(GameState g)
    {
        var mods = (Views.Aside.Popular(g) ?? []).OrderByDescending(m => m.Downloads).Take(10).ToList();
        var id = g.Def.Id;
        var podium = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = StoreKit.Gap, Height = 236 };
        string[] medals = ["#F2C25C", "#C9D1DC", "#D9894E"];
        for (var i = 0; i < Math.Min(3, mods.Count); i++)
        {
            var m = mods[i];
            var rank = new Border
            {
                Width = 46, Height = 46, CornerRadius = new CornerRadius(23), Margin = new Thickness(12), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Background = new SolidColorBrush(Color.FromArgb(220, 10, 10, 14)), BorderBrush = Ui.Hex(medals[i]), BorderThickness = new Thickness(2),
                BoxShadow = new BoxShadows(new BoxShadow { Blur = 18, Color = Gx.Alpha(Color.Parse(medals[i]), 150) }),
                Child = new TextBlock { Text = (i + 1).ToString(), FontFamily = Gx.Display, FontSize = 20, FontWeight = FontWeight.Bold, Foreground = Ui.Hex(medals[i]), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            var words = Ui.Col(2, Gx.Title(m.Name, 16, Brushes.White, 1), new TextBlock { Text = "↓ " + I18n.Compact(m.Downloads) + (m.Author != "" ? " · " + m.Author : ""), FontSize = 12, Foreground = Ui.Hex("#D0D3DC"), TextTrimming = TextTrimming.CharacterEllipsis });
            words.VerticalAlignment = VerticalAlignment.Bottom;
            words.Margin = new Thickness(14, 0, 56, 12);
            var install = InstallButton(g, m, false);
            install.HorizontalAlignment = HorizontalAlignment.Right;
            install.VerticalAlignment = VerticalAlignment.Bottom;
            install.Margin = new Thickness(12);
            var mm = m;
            var card = Tile(new Panel { Children = { ModCover.Create(g, m, 768, iconV: VerticalAlignment.Top, iconShare: 0.5), new Border { Background = Gx.ShadeUp(0.35, 235) }, rank, words, install } },
                () => MainWindow.Current?.Navigate(() => new ModPage(id, mm)));
            Grid.SetColumn(card, i);
            podium.Children.Add(card);
        }

        var list = new StackPanel { Spacing = 4 };
        var open = _openTops.Contains(id);
        void Fill(bool animate)
        {
            list.Children.Clear();
            for (var i = 3; i < mods.Count; i++)
            {
                var m = mods[i];
                var rank = new TextBlock { Text = (i + 1).ToString(), FontFamily = Gx.Display, FontSize = 18, FontWeight = FontWeight.Bold, Width = 34, Foreground = Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Center };
                var words = Ui.Col(1,
                    new TextBlock { Text = m.Name, FontWeight = FontWeight.SemiBold, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis },
                    new TextBlock { Text = (m.Categories.FirstOrDefault() is { } cat ? cat + " · " : "") + "↓ " + I18n.Compact(m.Downloads), FontSize = 12, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis });
                words.VerticalAlignment = VerticalAlignment.Center;
                var action = InstallButton(g, m, false);
                if (action is Button b) { b.Width = b.Height = 32; b.CornerRadius = new CornerRadius(16); }
                action.VerticalAlignment = VerticalAlignment.Center;
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), ColumnSpacing = 12 };
                grid.Children.Add(rank);
                var thumb = Ui.Thumb(m.Icon, m.Name, 44, 10, 120);
                Grid.SetColumn(thumb, 1);
                grid.Children.Add(thumb);
                Grid.SetColumn(words, 2);
                grid.Children.Add(words);
                Grid.SetColumn(action, 3);
                grid.Children.Add(action);
                var row = new Border { Classes = { "store-row" }, Padding = new Thickness(10, 7), Child = grid, Background = Ui.Res("Surface"), BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1) };
                var mm = m;
                StoreKit.OnClick(row, () => MainWindow.Current?.Navigate(() => new ModPage(id, mm)));
                list.Children.Add(row);
                // Строки «ложатся» сверху одна за другой.
                if (animate) Animate.From(row, "translateY(-26px) scale(0.97)", 420, (i - 3) * 60, new BackEaseOut());
            }
        }
        if (open) Fill(false);

        var toggle = new Button { Padding = new Thickness(14, 8) };
        void Label()
        {
            var chevron = Ui.Icon(Icons.ChevronDown, 16);
            if (open) chevron.RenderTransform = new RotateTransform(180);
            toggle.Content = Ui.Row(8, new TextBlock { Text = open ? I18n.T("v93.feed.top.less") : I18n.T("v93.feed.top.more", ("n", Math.Max(0, mods.Count - 3))), VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold }, chevron);
        }
        Label();
        toggle.Click += (_, _) =>
        {
            open = !open;
            if (open) { _openTops.Add(id); Fill(true); }
            else { _openTops.Remove(id); list.Children.Clear(); }
            Label();
        };
        toggle.IsVisible = mods.Count > 3;
        var head = new DockPanel();
        DockPanel.SetDock(toggle, Dock.Right);
        toggle.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(toggle);
        var title = Ui.Row(12,
            new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(9), ClipToBounds = true, Child = Ui.GameImage(g.Def, 128, art: Images.Art.Cover) },
            Ui.Col(1, Gx.Eyebrow(I18n.T("v93.feed.top10"), Ui.Res("Brand2"), 10.5), new TextBlock { Text = g.Def.Name, FontFamily = Gx.Display, FontWeight = FontWeight.Bold, FontSize = 17 }));
        head.Children.Add(title);
        return new Border { Classes = { "card" }, Padding = new Thickness(16), Child = Ui.Col(14, head, podium, list) };
    }

    /// <summary>«Топ-5» модов игры: места крупными цифрами, как таблица рекордов.</summary>
    static Control TopList(GameState g)
    {
        var mods = (Views.Aside.Popular(g) ?? []).OrderByDescending(m => m.Downloads).Take(5).ToList();
        var rows = new StackPanel { Spacing = 2 };
        for (var i = 0; i < mods.Count; i++)
        {
            var m = mods[i];
            var rank = new TextBlock
            {
                Text = (i + 1).ToString(), FontFamily = Gx.Display, FontSize = 22, FontWeight = FontWeight.Bold, Width = 30,
                Foreground = i == 0 ? Ui.Res("Brand2") : Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Center,
            };
            var words = Ui.Col(1,
                new TextBlock { Text = m.Name, FontWeight = FontWeight.SemiBold, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = "↓ " + I18n.Compact(m.Downloads), FontSize = 11.5, Foreground = Ui.Res("Muted") });
            words.VerticalAlignment = VerticalAlignment.Center;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 10 };
            grid.Children.Add(rank);
            var thumb = Ui.Thumb(m.Icon, m.Name, 40, 9, 100);
            Grid.SetColumn(thumb, 1);
            grid.Children.Add(thumb);
            Grid.SetColumn(words, 2);
            grid.Children.Add(words);
            var row = new Border { Classes = { "store-row" }, Padding = new Thickness(8, 6), Child = grid };
            var id = g.Def.Id;
            var mm = m;
            StoreKit.OnClick(row, () => MainWindow.Current?.Navigate(() => new ModPage(id, mm)));
            rows.Children.Add(row);
        }
        var head = Ui.Row(10,
            new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = Ui.GameImage(g.Def, 100, art: Images.Art.Cover) },
            Ui.Col(1, Gx.Eyebrow(I18n.T("v93.feed.top"), Ui.Res("Brand2"), 10.5), new TextBlock { Text = g.Def.Name, FontWeight = FontWeight.Bold, FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis }));
        var col = Ui.Col(10, head, rows);
        col.Margin = new Thickness(12, 14, 12, 10);
        var gid = g.Def.Id;
        var t = new Border { Classes = { "store-tile", "feed-tile" }, Child = col, Cursor = null };
        // Заголовок ведёт в каталог игры.
        StoreKit.OnClick(head, () => MainWindow.Current?.Navigate(() => new GamePage(gid, "catalog")));
        head.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
        return t;
    }

    /// <summary>«Профиль игрока»: часы в играх, игры, моды и уровень ModLaunch — как экран профиля в игре.</summary>
    static Control StatsTile()
    {
        var games = FeedGames();
        var hours = games.Sum(g => Features.PlayTime.Get(g.Def.Id).TotalMs) / 3_600_000.0;
        var mods = games.Sum(g => g.ModCount);
        var xp = (int)(hours * 10 + mods * 15 + games.Count * 25);
        var level = (int)Math.Floor(Math.Sqrt(xp / 20.0)) + 1;
        var nextAt = level * level * 20;
        var prevAt = (level - 1) * (level - 1) * 20;
        var progress = nextAt == prevAt ? 1 : (xp - prevAt) / (double)(nextAt - prevAt);
        var p = Social.Account.Get();
        var name = p.SignedIn ? p.Name ?? "ModLaunch" : I18n.T("v93.feed.player");

        var white = Brushes.White;
        var soft = Ui.Hex("#C4C7D2");
        var lvl = new Border
        {
            Width = 54, Height = 54, CornerRadius = new CornerRadius(27), BorderThickness = new Thickness(2.5), BorderBrush = Ui.Res("Brand"), BoxShadow = Gx.Glow(140, 18),
            Child = new TextBlock { Text = level.ToString(), FontFamily = Gx.Display, FontSize = 20, FontWeight = FontWeight.Bold, Foreground = white, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var who = Ui.Col(2, Gx.Eyebrow(I18n.T("v93.feed.profile"), Ui.Hex("#E6E7EE"), 10.5), new TextBlock { Text = name, FontWeight = FontWeight.Bold, FontSize = 16, Foreground = white, TextTrimming = TextTrimming.CharacterEllipsis });
        who.VerticalAlignment = VerticalAlignment.Center;
        var stats = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*") };
        var cells = new[]
        {
            Gx.Stat(hours >= 10 ? hours.ToString("0", I18n.Culture) : hours.ToString("0.#", I18n.Culture), I18n.T("v93.feed.hours"), white, 22, soft),
            Gx.Stat(games.Count.ToString(I18n.Culture), I18n.T("v93.feed.gamesN"), white, 22, soft),
            Gx.Stat(mods.ToString(I18n.Culture), I18n.T("v93.feed.modsN"), white, 22, soft),
        };
        for (var i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i); stats.Children.Add(cells[i]); }
        var xpLine = new DockPanel();
        var xpText = new TextBlock { Text = $"{xp} / {nextAt} XP", FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = soft };
        DockPanel.SetDock(xpText, Dock.Right);
        xpLine.Children.Add(xpText);
        xpLine.Children.Add(new TextBlock { Text = I18n.T("v93.feed.level", ("n", level + 1)), FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = soft });
        var col = Ui.Col(16, Ui.Row(12, lvl, who), stats, Ui.Col(6, xpLine, Gx.Bar(progress)));
        col.VerticalAlignment = VerticalAlignment.Center;
        col.Margin = new Thickness(18);
        var layers = new Panel { Children = { Gx.Gradient(null, 0.6), Gx.Scanlines(), new Border { Background = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)) }, col } };
        return Tile(layers, () => MainWindow.Current?.Navigate(() => new StatsPage()));
    }

    static Control PromoTile(string id, bool big)
    {
        var accent = Color.Parse(Look.Accent);
        Control background;
        string icon, title, text;
        Action open;
        switch (id)
        {
            case "creator":
                background = new Panel { Children = { StoreKit.AssetImage("banner-hero.jpg", 900), new Border { Background = new SolidColorBrush(Color.FromArgb(90, accent.R, accent.G, accent.B)) } } };
                (icon, title, text) = (Icons.Creator, "Creator Hub", I18n.T("v93.promo.creator"));
                open = () => MainWindow.Current?.Navigate(() => new MarketPage());
                break;
            case "center":
                var updates = Features.ModUpdates.Found.Sum(kv => kv.Value.Count) + Features.Tracking.Updates.Count;
                background = StoreKit.AssetImage("banner-broken.jpg", 900);
                (icon, title, text) = (Icons.Package, I18n.T("mc.title"), updates > 0 ? I18n.T("v92.home.center.updates", ("n", updates)) : I18n.T("v92.home.center.text"));
                open = () => MainWindow.Current?.Navigate(() => new ModsCenterPage());
                break;
            case "workshop":
                background = Gx.Gradient("#2BB673", 0.7);
                (icon, title, text) = (Icons.Globe, I18n.T("v93.mk.workshop"), I18n.T("v93.promo.workshop"));
                open = () => MainWindow.Current?.Navigate(() => new MarketPage("workshop"));
                break;
            case "sell":
                background = Gx.Gradient(Look.Accent, 0.4);
                (icon, title, text) = (Icons.Bag, I18n.T("v93.promo.sell"), I18n.T("v93.promo.sell.text", ("n", 100 - Market.FeePercent)));
                open = () => MainWindow.Current?.Navigate(() => new MarketPage("studio"));
                break;
            case "style":
                background = new Border
                {
                    Background = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                        GradientStops = { new GradientStop(Color.Parse("#5B3FD6"), 0), new GradientStop(Color.Parse("#B81E26"), 0.5), new GradientStop(Color.Parse("#16924C"), 1) },
                    },
                };
                (icon, title, text) = (Icons.Palette, I18n.T("v92.style.title"), I18n.T("v93.promo.style"));
                open = () => StylePicker.Show();
                break;
            case "bigpicture":
                background = Gx.Gradient("#1E6FD9", 0.8);
                (icon, title, text) = (Icons.Tv, "Big Picture", I18n.T("v93.promo.bigpicture"));
                open = BigPictureWindow.Open;
                break;
            case "friends":
                background = Gx.Gradient("#E0498F", 0.6);
                (icon, title, text) = (Icons.Users, I18n.T("v92.nav.friends"), I18n.T("v93.promo.friends"));
                open = () => MainWindow.Current?.Navigate(() => new FriendsPage());
                break;
            default:
                background = StoreKit.AssetImage("banner-deps.jpg", 900);
                (icon, title, text) = (Icons.Plus, I18n.T("add.title"), I18n.T("v93.promo.add"));
                open = () => MainWindow.Current?.Navigate(() => new AddGamePage());
                break;
        }
        var badge = new Border
        {
            Width = big ? 52 : 42, Height = big ? 52 : 42, CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), BorderThickness = new Thickness(1),
            Child = Ui.Icon(icon, big ? 24 : 20, Brushes.White), HorizontalAlignment = HorizontalAlignment.Left,
        };
        var words = Ui.Col(big ? 10 : 6, badge, Gx.Title(title, big ? 26 : 17, Brushes.White),
            new TextBlock { Text = text, FontSize = big ? 14 : 12.5, Foreground = Ui.Hex("#D9DBE3"), TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Left });
        words.VerticalAlignment = VerticalAlignment.Bottom;
        words.Margin = new Thickness(big ? 28 : 16, 0, 16, big ? 24 : 14);
        var layers = new Panel { Children = { background, Gx.Scanlines(), new Border { Background = Gx.ShadeUp(0.2, 220) }, words } };
        var arrow = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Margin = new Thickness(12), Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Child = Ui.Icon(Icons.ChevronRight, 15, Brushes.White),
        };
        layers.Children.Add(arrow);
        return Tile(layers, open);
    }
}
