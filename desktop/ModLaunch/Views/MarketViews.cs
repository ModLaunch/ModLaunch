using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Games;
using ModLaunch.Social;

namespace ModLaunch.Views;

/// <summary>Рынок Creator Hub: ассеты (модели, текстуры, звуки, код), кошелёк, обмены.</summary>
public sealed partial class CreatorPage
{
    static List<Asset>? _assets;
    static string? _assetsError;
    static bool _assetsLoading;
    static Wallet? _wallet;
    string _assetKind = "all";
    string _assetSort = "new";
    string _assetPrice = "all";

    static MainWindow W => MainWindow.Current!;

    public static readonly Dictionary<string, (string Icon, string Color)> KindLook = new()
    {
        ["model"] = (Icons.Package, "#8B5CF6"),
        ["texture"] = (Icons.Image, "#EC4899"),
        ["sound"] = (Icons.Music, "#06B6D4"),
        ["code"] = (Icons.Code, "#3478F6"),
        ["other"] = (Icons.Layers, "#64748B"),
    };

    static string Explain(Exception e) => Firebase.Explain("market", e);

    /// <summary>Действие с рынком: ошибки — тостом, успех — тостом и перерисовкой.</summary>
    void Run(Func<Task> act, string? ok = null)
    {
        _ = Go();
        async Task Go()
        {
            try
            {
                await act();
                if (ok is not null) W.Toast(ok);
            }
            catch (Exception e) { W.Toast(Explain(e), bad: true); }
            Build();
        }
    }

    /// <summary>Рынок — только с аккаунтом ModLaunch.</summary>
    static bool NeedAccount()
    {
        if (Account.SignedIn || Program.Screenshot) return false;
        W.Dialog(I18n.T("mk.signin.title"), Ui.Text(I18n.T("mk.signin.text"), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), W.CloseDialog),
            Ui.Button(I18n.T("acc.title"), () => { W.CloseDialog(); W.Navigate(() => new SettingsPage("accounts")); }, "primary", Icons.User));
        return true;
    }

    public static string Credits(long n) => $"{Market.Coin} {n:N0}";

    static TextBlock Price(Asset a, double size = 13) => new()
    {
        Text = a.Free ? I18n.T("mk.free") : Credits(a.Price),
        FontSize = size, FontWeight = FontWeight.Bold,
        Foreground = a.Free ? Ui.Res("Good") : Ui.Res("Brand2"),
        VerticalAlignment = VerticalAlignment.Center,
    };

    async Task LoadAssets(bool force = false)
    {
        if (_assetsLoading) return;
        _assetsLoading = true;
        _assetsError = null;
        Build();
        try { _assets = await Market.Assets(force); }
        catch (Exception e) { _assets ??= []; _assetsError = Explain(e); }
        _assetsLoading = false;
        Build();
    }

    static bool _walletTried;

    async Task LoadWallet()
    {
        if ((!Account.SignedIn && !Program.Demo) || _walletTried) return;
        _walletTried = true;
        try { _wallet = await Market.MyWallet(); Build(); } catch { }
    }

    // ---------------------------------------------------------------- шапка: кошелёк

    /// <summary>Кошелёк в шапке Creator Hub: баланс и бонус, по щелчку — история.</summary>
    Control WalletChip()
    {
        if (_wallet is null && (Account.SignedIn || Program.Demo) && !Program.Screenshot) _ = LoadWallet();
        var w = _wallet;
        var content = Ui.Row(8,
            new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Ui.Hex("#F5B841"), Child = new TextBlock { Text = Market.Coin, FontSize = 14, FontWeight = FontWeight.Bold, Foreground = Ui.Hex("#5A3B00"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } },
            Ui.Col(0, Ui.Text(w is null ? (Account.SignedIn ? "…" : I18n.T("mk.wallet.none")) : w.Balance.ToString("N0"), "h3"), Ui.Text(I18n.T("mk.credits"), "small muted")));
        if (w?.BonusReady == true)
            content.Children.Add(new Border { Classes = { "pill" }, Background = Ui.Res("BrandSoft"), VerticalAlignment = VerticalAlignment.Center, Child = Ui.Text("+" + Market.Bonus, "small brand") });
        var b = new Button { Classes = { "tile" }, Padding = new Thickness(12, 7), Content = content, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(b, I18n.T("mk.wallet.tip"));
        b.Click += (_, _) => { if (!NeedAccount()) ShowWallet(); };
        return b;
    }

    void ShowWallet()
    {
        var list = new StackPanel { Spacing = 6 };
        list.Children.Add(Ui.Text(I18n.T("mk.loading"), "small muted"));
        var balance = Ui.Text(_wallet is null ? "…" : Credits(_wallet.Balance), "display");
        var bonus = Ui.Button(I18n.T("mk.bonus", ("n", Market.Bonus)), () => { }, "primary", Icons.Sparkles);
        void Refresh()
        {
            balance.Text = _wallet is null ? "…" : Credits(_wallet.Balance);
            bonus.IsEnabled = _wallet?.BonusReady == true;
            if (_wallet is { BonusReady: false } wl) ToolTip.SetTip(bonus, I18n.T("mk.bonus.later", ("when", wl.NextBonus.ToLocalTime().ToString("g"))));
        }
        bonus.Click += async (_, _) =>
        {
            bonus.IsEnabled = false;
            try { _wallet = await Market.ClaimBonus(); W.Toast(I18n.T("mk.bonus.done", ("n", Market.Bonus))); }
            catch (Exception e) { W.Toast(Explain(e), bad: true); }
            Refresh();
            Build();
        };
        Refresh();
        _ = Fill();
        async Task Fill()
        {
            try
            {
                _wallet = await Market.MyWallet();
                Refresh();
                var ops = await Market.History();
                list.Children.Clear();
                if (ops.Count == 0) list.Children.Add(Ui.Text(I18n.T("mk.history.empty"), "small muted", wrap: true));
                foreach (var op in ops.Take(40))
                {
                    var row = new DockPanel();
                    var amount = Ui.Text((op.Income ? "+" : "−") + op.Amount.ToString("N0"), "strong", color: op.Income ? Ui.Res("Good") : Ui.Res("Text"));
                    DockPanel.SetDock(amount, Dock.Right);
                    row.Children.Add(amount);
                    row.Children.Add(Ui.Col(1, Ui.Text(I18n.T("mk.op." + op.Kind), "small"), Ui.Text(Ui.Ago(op.At), "small muted")));
                    list.Children.Add(row);
                }
            }
            catch (Exception e) { list.Children.Clear(); list.Children.Add(Ui.Text(Explain(e), "small muted", wrap: true)); }
        }
        var body = Ui.Col(14,
            Ui.Col(2, Ui.Text(I18n.T("mk.balance"), "eyebrow"), balance),
            Ui.Text(I18n.T("mk.wallet.text"), "small muted", wrap: true),
            new Border { Classes = { "inset" }, Padding = new Thickness(14, 10), Child = new ScrollViewer { MaxHeight = 260, Content = list } });
        W.Dialog(I18n.T("mk.wallet"), body, Ui.Button(I18n.T("common.close"), W.CloseDialog), bonus);
    }

    // ---------------------------------------------------------------- витрина ассетов

    Control AssetsView()
    {
        if (_assets is null && !_assetsLoading && !Program.Screenshot) _ = LoadAssets();
        var col = new StackPanel { Spacing = 16 };

        // Шапка витрины: что это и кнопка «Выложить».
        var hero = new Border
        {
            Classes = { "card", "hero" }, Padding = new Thickness(24, 20),
            Child = new DockPanel
            {
                Children =
                {
                    DockRight(Ui.Row(8,
                        Ui.Button("", () => _ = LoadAssets(force: true), "icon ghost", Icons.Refresh, I18n.T("cr.refresh")),
                        Ui.Button(I18n.T("mk.publish"), () => { if (!NeedAccount()) PublishAsset(new AssetDraft()); }, "primary", Icons.Upload))),
                    Ui.Col(4, Ui.Text(I18n.T("mk.assets.eyebrow"), "eyebrow"), Ui.Text(I18n.T("mk.assets.title"), "h2"), Ui.Text(I18n.T("mk.assets.text"), "muted", wrap: true)),
                },
            },
        };
        col.Children.Add(hero);

        var kinds = Ui.Row(6);
        foreach (var k in new[] { "all" }.Concat(Inventory.Kinds))
        {
            var kk = k;
            var chip = Ui.Button(I18n.T("mk.kind." + k), () => { _assetKind = kk; Build(); }, "chip", k == "all" ? null : KindLook[k].Icon);
            if (_assetKind == k) chip.Classes.Add("active");
            kinds.Children.Add(chip);
        }
        var price = new ComboBox { Width = 160 };
        var prices = new[] { "all", "free", "paid", "limited" };
        foreach (var p in prices) price.Items.Add(I18n.T("mk.price." + p));
        price.SelectedIndex = Array.IndexOf(prices, _assetPrice);
        price.SelectionChanged += (_, _) => { var v = prices[Math.Max(0, price.SelectedIndex)]; if (v != _assetPrice) { _assetPrice = v; Build(); } };
        var sort = new ComboBox { Width = 170 };
        var sorts = new[] { "new", "popular", "cheap" };
        foreach (var s in sorts) sort.Items.Add(I18n.T("mk.sort." + s));
        sort.SelectedIndex = Array.IndexOf(sorts, _assetSort);
        sort.SelectionChanged += (_, _) => { var v = sorts[Math.Max(0, sort.SelectedIndex)]; if (v != _assetSort) { _assetSort = v; Build(); } };
        var bar = new DockPanel();
        var right = Ui.Row(8, price, sort);
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(right);
        bar.Children.Add(new ScrollViewer { Content = kinds, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        col.Children.Add(bar);

        if (_assetsLoading && _assets is null)
        {
            var sk = new TileGrid { MinItemWidth = 230 };
            for (var i = 0; i < 8; i++) sk.Children.Add(new Border { Classes = { "card", "skeleton" }, Height = 250 });
            col.Children.Add(sk);
            return col;
        }
        if (_assetsError is not null && (_assets?.Count ?? 0) == 0)
        {
            col.Children.Add(Ui.Card(Ui.Col(8, Ui.Row(10, Ui.Icon(Icons.Alert, 20, Ui.Res("Warn")), Ui.Text(I18n.T("mk.error"), "h3")),
                Ui.Text(_assetsError, "muted", wrap: true), Ui.Button(I18n.T("hub.retry"), () => _ = LoadAssets(force: true), "", Icons.Refresh)), 24));
            return col;
        }
        var all = (_assets ?? []).Where(a => a.Listed || a.Mine).ToList();
        var list = all.Where(a =>
            (_assetKind == "all" || a.Kind == _assetKind) &&
            (_assetPrice switch { "free" => a.Free, "paid" => !a.Free, "limited" => a.Limited, _ => true }) &&
            (_filter == "" || a.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase) || a.Summary.Contains(_filter, StringComparison.OrdinalIgnoreCase)
                || a.Author.Contains(_filter, StringComparison.OrdinalIgnoreCase) || a.Tags.Any(t => t.Contains(_filter, StringComparison.OrdinalIgnoreCase))));
        list = _assetSort switch
        {
            "popular" => list.OrderByDescending(a => a.Owners).ThenByDescending(a => a.Updated),
            "cheap" => list.OrderBy(a => a.Price).ThenByDescending(a => a.Owners),
            _ => list.OrderByDescending(a => a.Updated),
        };
        var shown = list.ToList();
        if (all.Count > 0)
            col.Children.Add(Ui.Row(22,
                Stat(Icons.Package, all.Count.ToString("N0"), I18n.T("mk.stat.assets")),
                Stat(Icons.Users, all.Select(a => a.Uid).Distinct().Count().ToString("N0"), I18n.T("hub.stat.authors")),
                Stat(Icons.Download, I18n.Compact(all.Sum(a => a.Owners)), I18n.T("mk.stat.owners")),
                Stat(Icons.Star, all.Count(a => a.Limited).ToString("N0"), I18n.T("mk.stat.limited"))));
        if (shown.Count == 0)
        {
            col.Children.Add(Ui.Card(Ui.Col(8, Ui.Text(I18n.T(all.Count == 0 ? "mk.assets.empty" : "hub.nothing"), "h3"),
                Ui.Text(I18n.T(all.Count == 0 ? "mk.assets.empty.text" : "hub.nothing.text"), "small muted", wrap: true)), 24));
            return col;
        }
        var grid = new TileGrid { MinItemWidth = 230 };
        foreach (var a in shown) grid.Children.Add(AssetTile(a));
        col.Children.Add(grid);
        return col;
    }

    static Control DockRight(Control c)
    {
        DockPanel.SetDock(c, Dock.Right);
        c.VerticalAlignment = VerticalAlignment.Center;
        return c;
    }

    /// <summary>Обложка ассета: картинка автора или цветная заглушка со значком вида.</summary>
    static Control AssetCover(Asset a, double height, double radius = 16)
    {
        var (icon, color) = KindLook.GetValueOrDefault(a.Kind, KindLook["other"]);
        Control art = a.Images.Count > 0 ? Tiles.Art(a.Images[0], a.Name, 520) : new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse(color), 0), new GradientStop(Color.Parse("#15151A"), 1) },
            },
            Child = Ui.Icon(icon, 40, Brushes.White),
        };
        var label = Tiles.Label(I18n.T("mk.kind." + a.Kind), color);
        label.Margin = new Thickness(10);
        label.HorizontalAlignment = HorizontalAlignment.Left;
        label.VerticalAlignment = VerticalAlignment.Top;
        var panel = new Panel { Children = { art, label } };
        if (a.Limited)
        {
            var left = Tiles.Sticker(a.SoldOut ? I18n.T("mk.soldOut") : I18n.T("mk.left", ("n", a.Left), ("of", a.Supply)));
            panel.Children.Add(left);
        }
        return new Border { Height = height, ClipToBounds = true, CornerRadius = new CornerRadius(radius, radius, 0, 0), Child = panel };
    }

    Control AssetTile(Asset a)
    {
        var games = a.Games.Count == 0 ? I18n.T("mk.engine." + (a.Engine is "" ? "any" : a.Engine))
            : string.Join(", ", a.Games.Take(2).Select(g => GameCatalog.ById(g)?.ShortName ?? g)) + (a.Games.Count > 2 ? $" +{a.Games.Count - 2}" : "");
        var foot = new DockPanel();
        var price = Price(a, 14);
        DockPanel.SetDock(price, Dock.Right);
        foot.Children.Add(price);
        foot.Children.Add(Ui.Row(5, Ui.Icon(Icons.Users, 12, Ui.Res("Muted")), Ui.Text(I18n.Compact(a.Owners), "small muted")));
        var body = Ui.Col(5,
            new TextBlock { Text = a.Name, FontSize = 15, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
            Ui.Text(I18n.T("mod.by", ("author", a.Author)) + " · " + games, "small brand"),
            new TextBlock { Text = a.Summary, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Ui.Res("Muted"), FontSize = 12.5, Height = 34 },
            foot);
        body.Margin = new Thickness(14, 12, 14, 14);
        var b = new Button { Classes = { "tile" }, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch, Content = new StackPanel { Children = { AssetCover(a, 130), body } } };
        b.Click += (_, _) => OpenAsset(a);
        return b;
    }

    /// <summary>Карточка ассета: описание, превью кода, копии на перепродаже и действия.</summary>
    void OpenAsset(Asset a)
    {
        var actions = new List<Control> { Ui.Button(I18n.T("common.close"), W.CloseDialog) };
        var extra = new StackPanel { Spacing = 8 };
        var status = Ui.Text("", "small muted", wrap: true);
        var meta = new WrapPanel();
        void Meta(string icon, string text) => meta.Children.Add(new Border { Classes = { "pill" }, Margin = new Thickness(0, 0, 6, 6), Child = Ui.Row(5, Ui.Icon(icon, 12, Ui.Res("Muted")), Ui.Text(text, "small")) });
        Meta(KindLook.GetValueOrDefault(a.Kind, KindLook["other"]).Icon, I18n.T("mk.kind." + a.Kind));
        Meta(Icons.Gamepad, a.Games.Count == 0 ? I18n.T("mk.engine." + (a.Engine is "" ? "any" : a.Engine)) : string.Join(", ", a.Games.Select(g => GameCatalog.ById(g)?.ShortName ?? g)));
        if (a.Size > 0) Meta(Icons.Download, GamePage.Size(a.Size));
        Meta(Icons.Users, I18n.T("mk.owners", ("n", a.Owners)));
        if (a.Limited) Meta(Icons.Star, a.SoldOut ? I18n.T("mk.soldOut") : I18n.T("mk.left", ("n", a.Left), ("of", a.Supply)));
        if (a.Version != "") Meta(Icons.Clock, a.Version + " · " + Ui.Ago(a.Updated));

        var body = Ui.Col(12,
            new Border { CornerRadius = new CornerRadius(14), ClipToBounds = true, Child = AssetCover(a, 150, 14) },
            Ui.Row(10, Price(a, 20), Ui.Text(I18n.T("mod.by", ("author", a.Author)), "muted")),
            Ui.Text(a.Summary, "", wrap: true),
            meta);
        if (a.Description != "") body.Children.Add(Ui.Text(a.Description, "small muted", wrap: true));
        if (a.Preview != "")
            body.Children.Add(Ui.Col(6, Ui.Text(I18n.T(a.Free ? "mk.code" : "mk.preview"), "eyebrow"), new Border
            {
                Classes = { "inset" }, Padding = new Thickness(12),
                Child = new ScrollViewer { MaxHeight = 200, Content = new SelectableTextBlock { Text = a.Preview, FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap } },
            }));
        body.Children.Add(extra);
        body.Children.Add(status);

        if (a.Mine)
        {
            actions.Add(Ui.Button(I18n.T("mk.edit"), () => PublishAsset(new AssetDraft
            {
                Name = a.Name, Summary = a.Summary, Description = a.Description, Kind = a.Kind, Games = [.. a.Games], Engine = a.Engine, Tags = [.. a.Tags],
                Images = [.. a.Images], Price = a.Price, Supply = a.Supply, Listed = a.Listed, Version = BumpVersion(a.Version), Preview = a.Preview,
            }, a), "", Icons.Edit));
            actions.Add(Ui.Button(I18n.T(a.Listed ? "mk.unlist" : "mk.relist"), () => { W.CloseDialog(); Run(() => Market.SetListed(a, !a.Listed), I18n.T("mk.saved")); }, "", a.Listed ? Icons.EyeOff : Icons.Eye));
            if (a.Owners == 0) actions.Add(Ui.Button("", () => { W.CloseDialog(); Run(async () => { await Market.DeleteAsset(a); _assets = null; }, I18n.T("mk.deleted")); }, "icon ghost", Icons.Trash, I18n.T("mk.delete")));
        }
        else
        {
            var get = Ui.Button(a.Free ? I18n.T("mk.take") : I18n.T("mk.buy", ("price", Credits(a.Price))), () => { }, "primary", a.Free ? Icons.Download : Icons.Bag);
            get.IsEnabled = false;
            actions.Add(get);
            _ = Fill();
            async Task Fill()
            {
                if (!Account.SignedIn && !Program.Demo) { get.IsEnabled = !a.SoldOut; get.Click += (_, _) => NeedAccount(); return; }
                try
                {
                    var mine = await Market.MyCopy(a.Id);
                    if (mine is not null)
                    {
                        get.Content = Ui.Row(6, Ui.Icon(Icons.Check, 14, Brushes.White), Ui.Text(I18n.T("mk.have", ("n", mine.Serial)), "strong", color: Brushes.White));
                        get.IsEnabled = true;
                        get.Click += (_, _) => { W.CloseDialog(); _tab = "inventory"; Build(); };
                        extra.Children.Add(Ui.Row(8,
                            Ui.Button(I18n.T("mk.download"), () => Run(() => Download(a, mine.Serial), I18n.T("mk.downloaded", ("name", a.Name))), "", Icons.Download),
                            Ui.Button(I18n.T("mk.sell"), () => SellCopy(mine, a), "", Icons.Bag)));
                    }
                    else
                    {
                        get.IsEnabled = !a.SoldOut;
                        get.Click += (_, _) => { if (NeedAccount()) return; W.CloseDialog(); Run(async () => { var o = await Market.Acquire(a.Id); await Download(a, o.Serial); }, I18n.T("mk.got", ("name", a.Name))); };
                    }
                    var listings = await Market.Listings(a.Id);
                    if (listings.Count > 0)
                    {
                        extra.Children.Add(Ui.Text(I18n.T("mk.resale"), "eyebrow"));
                        foreach (var l in listings.Take(6))
                        {
                            var row = new DockPanel();
                            var buy = Ui.Button(I18n.T("mk.buy", ("price", Credits(l.Price))), () => { if (NeedAccount()) return; W.CloseDialog(); Run(async () => { var o = await Market.BuyListing(l); await Download(a, o.Serial); }, I18n.T("mk.got", ("name", a.Name))); }, "", Icons.Bag);
                            buy.IsEnabled = l.Uid != Market.Me && mine is null;
                            DockPanel.SetDock(buy, Dock.Right);
                            row.Children.Add(buy);
                            row.Children.Add(Ui.Text(I18n.T("mk.copy", ("n", l.Serial)), "small"));
                            extra.Children.Add(row);
                        }
                    }
                    if (a.Limited && mine is null && a.Owners > 0)
                        extra.Children.Add(Ui.Button(I18n.T("mk.trade"), () => OfferTrade(a), "ghost", Icons.Refresh));
                }
                catch (Exception e) { status.Text = Explain(e); get.IsEnabled = !a.SoldOut; }
            }
        }
        W.Dialog(a.Name, new ScrollViewer { MaxHeight = 560, Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, actions.ToArray());
    }

    static string BumpVersion(string v)
    {
        var p = v.Split('.').Select(x => int.TryParse(x, out var n) ? n : 0).ToArray();
        return p.Length == 3 ? $"{p[0]}.{p[1]}.{p[2] + 1}" : "1.0.1";
    }

    /// <summary>Скачать свою копию в инвентарь.</summary>
    static async Task Download(Asset a, long serial)
    {
        if (Program.Demo) return;
        var bytes = await Market.Fetch(a);
        Inventory.AddAsset(a, bytes, serial);
    }

    void SellCopy(Owned copy, Asset a)
    {
        var price = new NumericUpDown { Minimum = 1, Maximum = 1_000_000, Value = Math.Max(1, a.Price == 0 ? 10 : a.Price), Increment = 5, FormatString = "N0", Width = 160 };
        W.Dialog(I18n.T("mk.sell.title", ("name", a.Name)), Ui.Col(10,
                Ui.Text(I18n.T("mk.sell.text", ("n", copy.Serial)), "muted", wrap: true),
                Ui.Row(10, Ui.Text(Market.Coin, "h2"), price)),
            Ui.Button(I18n.T("common.cancel"), W.CloseDialog),
            Ui.Button(I18n.T("mk.unsell"), () => { W.CloseDialog(); Run(() => Market.Unlist(a.Id), I18n.T("mk.saved")); }, "ghost"),
            Ui.Button(I18n.T("mk.sell"), () => { W.CloseDialog(); Run(() => Market.ListForSale(copy, (long)(price.Value ?? 1)), I18n.T("mk.listed")); }, "primary", Icons.Bag));
    }

    /// <summary>Обмен с владельцем тиражной копии: своя копия или кредиты.</summary>
    void OfferTrade(Asset a)
    {
        if (NeedAccount()) return;
        var owners = new ComboBox { MinWidth = 220 };
        var mine = new ComboBox { MinWidth = 220 };
        var credits = new NumericUpDown { Minimum = 0, Maximum = 1_000_000, Value = Math.Max(1, a.Price), Increment = 5, FormatString = "N0", Width = 150 };
        var text = new TextBox { Watermark = I18n.T("mk.trade.note"), MaxLength = 500 };
        var status = Ui.Text("", "small muted", wrap: true);
        List<Owned> theirs = [];
        List<(Owned O, Asset A)> myCopies = [];
        _ = Fill();
        async Task Fill()
        {
            try
            {
                theirs = (await Market.OwnersOf(a.Id)).Where(o => o.Uid != Market.Me).ToList();
                foreach (var o in theirs) owners.Items.Add(I18n.T("mk.copy", ("n", o.Serial)));
                owners.SelectedIndex = theirs.Count > 0 ? 0 : -1;
                var assets = await Market.Assets();
                myCopies = (await Market.MyItems()).Select(o => (o, assets.FirstOrDefault(x => x.Id == o.Asset))).Where(x => x.Item2 is not null).Select(x => (x.o, x.Item2!)).ToList();
                mine.Items.Add(I18n.T("mk.trade.credits"));
                foreach (var (o, x) in myCopies) mine.Items.Add($"{x.Name} · №{o.Serial}");
                mine.SelectedIndex = 0;
            }
            catch (Exception e) { status.Text = Explain(e); }
        }
        mine.SelectionChanged += (_, _) => credits.IsEnabled = mine.SelectedIndex <= 0;
        W.Dialog(I18n.T("mk.trade.title", ("name", a.Name)), Ui.Col(12,
                Ui.Text(I18n.T("mk.trade.text"), "muted", wrap: true),
                Ui.Col(5, Ui.Text(I18n.T("mk.trade.want"), "small muted"), owners),
                Ui.Col(5, Ui.Text(I18n.T("mk.trade.give"), "small muted"), mine),
                Ui.Col(5, Ui.Text(I18n.T("mk.credits"), "small muted"), Ui.Row(8, Ui.Text(Market.Coin, "h3"), credits)),
                text, status),
            Ui.Button(I18n.T("common.cancel"), W.CloseDialog),
            Ui.Button(I18n.T("mk.trade.send"), () =>
            {
                if (owners.SelectedIndex < 0) return;
                var their = theirs[owners.SelectedIndex];
                var give = mine.SelectedIndex > 0 ? myCopies[mine.SelectedIndex - 1].O : null;
                W.CloseDialog();
                Run(() => Market.Offer(their, I18n.T("mk.copy", ("n", their.Serial)), give, give is null ? (long)(credits.Value ?? 0) : 0, text.Text ?? ""), I18n.T("mk.trade.sent"));
            }, "primary", Icons.Forward));
    }

    // ---------------------------------------------------------------- выложить ассет

    void PublishAsset(AssetDraft d, Asset? existing = null)
    {
        TextBox Box(string value, string hint, bool multi = false, int max = 200) => new()
        {
            Text = value, Watermark = hint, AcceptsReturn = multi, TextWrapping = multi ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multi ? 80 : 0, MaxLength = max, VerticalContentAlignment = multi ? VerticalAlignment.Top : VerticalAlignment.Center,
        };
        Control Field(string label, Control input) => Ui.Col(5, Ui.Text(label, "small muted"), input);

        var name = Box(d.Name, I18n.T("mk.f.name"), max: 60);
        name.IsEnabled = existing is null;
        var summary = Box(d.Summary, I18n.T("mk.f.summary"), max: 200);
        var description = Box(d.Description, I18n.T("hub.f.description"), multi: true, max: 5000);
        var images = Box(string.Join("\n", d.Images), I18n.T("hub.f.images"), multi: true, max: 2400);
        var code = Box(d.Code, I18n.T("mk.f.code"), multi: true, max: Hub.MaxCode);
        code.FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace");
        code.MinHeight = 140;
        var preview = Box(d.Preview, I18n.T("mk.f.preview"), multi: true, max: 4000);
        var kind = new ComboBox { Width = 180 };
        foreach (var k in Inventory.Kinds) kind.Items.Add(I18n.T("mk.kind." + k));
        kind.SelectedIndex = Math.Max(0, Array.IndexOf(Inventory.Kinds, d.Kind));
        kind.IsEnabled = existing is null;
        var engine = new ComboBox { Width = 180 };
        var engines = new[] { "any", "unity", "stardew" };
        foreach (var e in engines) engine.Items.Add(I18n.T("mk.engine." + e));
        engine.SelectedIndex = Math.Max(0, Array.IndexOf(engines, d.Engine));
        var price = new NumericUpDown { Minimum = 0, Maximum = 100_000, Value = d.Price, Increment = 5, FormatString = "N0", Width = 150 };
        var supply = new NumericUpDown { Minimum = 0, Maximum = 10_000, Value = d.Supply, Increment = 1, FormatString = "N0", Width = 150, IsEnabled = existing is null };
        var listed = new CheckBox { Content = I18n.T("mk.f.listed"), IsChecked = d.Listed };

        var games = new HashSet<string>(d.Games);
        var gameChips = new WrapPanel();
        foreach (var g in AppState.Games.Where(g => g.Status == Detect.Found).Select(g => g.Def).DefaultIfEmpty(null).OfType<GameDef>().Concat(GameCatalog.Builtin).DistinctBy(g => g.Id).Take(18))
        {
            var id = g.Id;
            var chip = Ui.Button(g.ShortName, () => { }, "chip");
            chip.Margin = new Thickness(0, 0, 6, 6);
            chip.Padding = new Thickness(10, 4);
            chip.FontSize = 12;
            if (games.Contains(id)) chip.Classes.Add("active");
            chip.Click += (_, _) => { if (!games.Remove(id) && games.Count < 12) games.Add(id); chip.Classes.Set("active", games.Contains(id)); };
            gameChips.Children.Add(chip);
        }

        // Файл: свой с диска или предмет из инвентаря.
        var file = d.File;
        var fileText = Ui.Text(file is null ? I18n.T("hub.f.noFile") : Path.GetFileName(file), "small muted");
        var pick = Ui.Button(I18n.T("hub.f.pick"), async () =>
        {
            var path = await W.PickFile(I18n.T("hub.f.pick"));
            if (path is null) return;
            if (new FileInfo(path).Length > Hub.MaxFile) { W.Toast(I18n.T("err.creator.FILE_TOO_BIG"), bad: true); return; }
            file = path;
            fileText.Text = $"{Path.GetFileName(path)} · {GamePage.Size(new FileInfo(path).Length)}";
            if (string.IsNullOrWhiteSpace(name.Text)) name.Text = Path.GetFileNameWithoutExtension(path);
            var guess = Inventory.KindOf(path);
            if (existing is null) kind.SelectedIndex = Array.IndexOf(Inventory.Kinds, guess);
        }, "", Icons.FilePlus);
        var fileRow = Field(I18n.T("mk.f.file"), Ui.Row(10, pick, fileText));
        var codeRow = Field(I18n.T("mk.f.codeLabel"), code);
        var previewRow = Field(I18n.T("mk.f.previewLabel"), preview);
        void Sync()
        {
            var isCode = Inventory.Kinds[Math.Max(0, kind.SelectedIndex)] == "code";
            fileRow.IsVisible = !isCode;
            codeRow.IsVisible = isCode;
            previewRow.IsVisible = isCode && (price.Value ?? 0) > 0;
        }
        kind.SelectionChanged += (_, _) => Sync();
        price.ValueChanged += (_, _) => Sync();
        Sync();

        var bar = new ProgressBar { Minimum = 0, Maximum = 1, IsVisible = false };
        var status = Ui.Text("", "small muted", wrap: true);
        var form = Ui.Col(12,
            Field(I18n.T("hub.f.name.label"), name),
            Field(I18n.T("hub.f.summary.label"), summary),
            Ui.Row(12, Field(I18n.T("mk.f.kind"), kind), Field(I18n.T("mk.f.engine"), engine)),
            fileRow, codeRow,
            Ui.Row(12, Field(I18n.T("mk.f.price"), price), Field(I18n.T("mk.f.supply"), supply)),
            Ui.Text(I18n.T("mk.f.priceHint"), "small muted", wrap: true),
            previewRow,
            Field(I18n.T("mk.f.games"), gameChips),
            Field(I18n.T("hub.f.description.label"), description),
            Field(I18n.T("hub.f.images.label"), images),
            listed,
            new Border { Classes = { "inset" }, Padding = new Thickness(12), Child = Ui.Row(10, Ui.Icon(Icons.Shield, 16, Ui.Res("Muted")), Ui.Text(I18n.T("mk.f.rules"), "small muted", wrap: true)) },
            bar, status);
        Button? go = null;
        go = Ui.Button(I18n.T(existing is null ? "mk.publish" : "mk.update"), async () =>
        {
            d.Name = name.Text ?? "";
            d.Summary = summary.Text ?? "";
            d.Description = description.Text ?? "";
            d.Kind = Inventory.Kinds[Math.Max(0, kind.SelectedIndex)];
            d.Engine = engines[Math.Max(0, engine.SelectedIndex)];
            d.Price = (long)(price.Value ?? 0);
            d.Supply = (long)(supply.Value ?? 0);
            d.Listed = listed.IsChecked == true;
            d.Games = [.. games];
            d.Code = code.Text ?? "";
            d.Preview = preview.Text ?? "";
            d.File = d.Kind == "code" ? null : file;
            d.Images = (images.Text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (d.Images.Any(u => !u.StartsWith("https://"))) { status.Text = I18n.T("hub.f.imagesBad"); return; }
            if (d.Kind != "code" && d.File is null && existing is null) { status.Text = I18n.T("err.creator.NO_FILE"); return; }
            go!.IsEnabled = false;
            bar.IsVisible = true;
            status.Text = I18n.T("hub.f.uploading");
            try
            {
                if (existing is not null && d.Kind != "code" && d.File is null)
                {
                    // Карточку меняем, файл прежний — скачиваем и выкладываем его же.
                    var tmp = Path.Combine(Paths.DownloadsTemp, existing.FileName is { Length: > 0 } fn ? fn : "asset.bin");
                    await File.WriteAllBytesAsync(tmp, await Market.Fetch(existing));
                    d.File = tmp;
                }
                if (existing is not null && d.Kind == "code" && d.Code.Trim() == "")
                    d.Code = System.Text.Encoding.UTF8.GetString(await Market.Fetch(existing));
                await Market.Publish(d, new Progress<double>(r => bar.Value = r));
                W.CloseDialog();
                W.Toast(I18n.T("mk.published", ("name", d.Name)));
                _assets = null;
                _ = LoadAssets(force: true);
            }
            catch (Exception e)
            {
                status.Text = Explain(e);
                go.IsEnabled = true;
                bar.IsVisible = false;
            }
        }, "primary", Icons.Upload);
        W.Dialog(I18n.T(existing is null ? "mk.publish.title" : "mk.update"), new ScrollViewer { Content = form, MaxHeight = 540, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
            Ui.Button(I18n.T("common.cancel"), W.CloseDialog), go);
    }
}
