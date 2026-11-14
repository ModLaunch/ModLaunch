using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Games;
using ModLaunch.Social;

namespace ModLaunch.Views;

/// <summary>
/// 28.0: «Желаемое» — игры, которые хочется купить: цена в Steam для своей страны, скидка, самая низкая цена
/// и «скажите, когда будет дешевле». Найти игру — поиском по каталогу Steam (без сети), забрать список — из Steam.
/// </summary>
public sealed class WishlistPage : Page
{
    public override string Title => I18n.T("v28.wish.title");
    public override string SearchHint => I18n.T("v28.wish.find");

    string _query = "";
    StackPanel _found = new() { Spacing = 4 };
    TextBox? _searchBox;
    bool _busy;

    public WishlistPage()
    {
        void Changed() => Dispatcher.UIThread.Post(() => { if (MainWindow.Current?.CurrentPage == this) Build(); });
        AttachedToVisualTree += (_, _) => { Wishlist.Changed += Changed; Wishlist.MarkSeen(); MainWindow.Current?.RefreshBell(); };
        DetachedFromVisualTree += (_, _) => Wishlist.Changed -= Changed;
    }

    public override void Search(string text)
    {
        _query = text.Trim();
        if (_searchBox is not null && _searchBox.Text != text) _searchBox.Text = text;
        RenderFound();
    }

    public override void Build()
    {
        var items = Wishlist.Items;
        var check = Ui.Button(I18n.T("v28.wish.check"), () => _ = CheckNow(), "", Icons.Refresh);
        check.IsEnabled = !_busy && items.Count > 0;
        var import = Ui.Button(I18n.T("v28.wish.import"), () => _ = Import(), "", Icons.Download);
        import.IsEnabled = !_busy;
        var country = new ComboBox { ItemsSource = Wishlist.Countries.Select(c => c.ToUpperInvariant()).ToList(), SelectedItem = Wishlist.Country.ToUpperInvariant(), MinWidth = 76 };
        ToolTip.SetTip(country, I18n.T("v28.wish.country"));
        country.SelectionChanged += (_, _) => { if (country.SelectedItem is string c) Wishlist.Country = c.ToLowerInvariant(); };
        var forYou = Ui.Button(I18n.T("v29.rec.title"), () => MainWindow.Current?.Navigate(() => new RecommendPage()), "ghost", Icons.Sparkles);
        var tools = Ui.Row(8, forYou, check, import, country);
        tools.VerticalAlignment = VerticalAlignment.Center;

        var head = new DockPanel();
        DockPanel.SetDock(tools, Dock.Right);
        head.Children.Add(tools);
        var sales = items.Count(i => i.Price is { OnSale: true });
        head.Children.Add(Ui.Col(4,
            Ui.Text(I18n.T("v28.wish.title"), "h1"),
            Ui.Text(items.Count == 0 ? I18n.T("v28.wish.lead") : I18n.T("v28.wish.summary", ("n", items.Count), ("sales", sales)), "muted", wrap: true)));

        _found = new StackPanel { Spacing = 4 };
        var search = _searchBox = new TextBox { Watermark = I18n.T("v28.wish.find"), Text = _query, Classes = { "search" } };
        search.TextChanged += (_, _) => { _query = search.Text?.Trim() ?? ""; RenderFound(); };
        var finder = Ui.Card(Ui.Col(10, search, _found), 16);

        var body = Ui.Col(16, head, finder);
        if (items.Count == 0)
            body.Children.Add(Ui.Card(Ui.Col(8, Ui.Text(I18n.T("v28.wish.empty"), "h3"), Ui.Text(I18n.T("v28.wish.empty.text"), "muted", wrap: true)), 22));
        else
        {
            var list = new StackPanel { Spacing = 8 };
            foreach (var item in items.OrderByDescending(i => i.Price?.OnSale == true).ThenByDescending(i => i.Price?.Discount ?? 0).ThenByDescending(i => i.Added))
                list.Children.Add(Row(item));
            body.Children.Add(list);
            body.Children.Add(Ui.Text(I18n.T("v28.wish.note"), "tiny muted", wrap: true));
        }
        body.Margin = new Thickness(34, 28);
        body.MaxWidth = 980;
        body.HorizontalAlignment = HorizontalAlignment.Left;
        Content = new ScrollViewer { Content = body };
        RenderFound();
    }

    void RenderFound()
    {
        _found.Children.Clear();
        if (_query.Length < 2) { _found.IsVisible = false; return; }
        _found.IsVisible = true;
        if (!SteamCatalog.Ready)
        {
            _found.Children.Add(Ui.Text(I18n.T("v28.wish.loadingCatalog"), "small muted"));
            _ = SteamCatalog.EnsureLoaded().ContinueWith(_ => Dispatcher.UIThread.Post(RenderFound));
            return;
        }
        var hits = SteamCatalog.Search(_query, 8);
        if (hits.Count == 0) { _found.Children.Add(Ui.Text(I18n.T("v28.wish.noHits"), "small muted")); return; }
        foreach (var hit in hits)
        {
            var app = hit.SteamAppId;
            var name = hit.Name;
            var have = Wishlist.Has(app);
            var add = Ui.Button(have ? I18n.T("v28.wish.added") : I18n.T("v28.wish.add"), () =>
            {
                Wishlist.Add(app, name);
                MainWindow.Current?.Toast(I18n.T("v28.wish.addedToast", ("name", name)));
            }, have ? "ghost" : "", have ? Icons.Check : Icons.Plus);
            add.IsEnabled = !have;
            var row = new DockPanel();
            DockPanel.SetDock(add, Dock.Right);
            row.Children.Add(add);
            var art = new Border { Width = 64, Height = 30, CornerRadius = new CornerRadius(4), ClipToBounds = true, Child = HomePage.WebArt(GameDef.SteamArt(app), null, name, 140), Margin = new Thickness(0, 0, 10, 0) };
            DockPanel.SetDock(art, Dock.Left);
            row.Children.Add(art);
            row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            _found.Children.Add(row);
        }
    }

    Control Row(WishItem item)
    {
        var art = new Border { Width = 184, Height = 86, CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = HomePage.WebArt(GameDef.SteamArt(item.AppId), null, item.Name, 400) };
        var price = new WrapPanel { Orientation = Orientation.Horizontal };
        switch (item.State)
        {
            case "price" when item.Price is { } p:
                if (p.OnSale)
                {
                    var pct = ModRow.Tag($"−{p.Discount}%", Ui.Soft("#16351F", "#3FB950"), Ui.Ink("#3FB950"));
                    pct.Margin = new Thickness(0, 0, 8, 0);
                    price.Children.Add(pct);
                    price.Children.Add(new TextBlock { Text = p.InitialText, TextDecorations = TextDecorations.Strikethrough, Foreground = Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                }
                price.Children.Add(new TextBlock { Text = p.FinalText, FontSize = 16, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                break;
            case "free": price.Children.Add(Ui.Text(I18n.T("v28.wish.free"), "strong")); break;
            case "unavailable": price.Children.Add(Ui.Text(I18n.T("v28.wish.unavailable", ("country", Wishlist.Country.ToUpperInvariant())), "small muted", wrap: true)); break;
            default: price.Children.Add(Ui.Text(I18n.T("v28.wish.unchecked"), "small muted")); break;
        }
        var notes = new List<string>();
        if (item.Price is { } now && item.Lowest is long low && low < now.Final) notes.Add(I18n.T("v28.wish.lowest", ("price", Money(low, now.Currency))));
        if (item.Target is long target) notes.Add(I18n.T("v28.wish.targetSet", ("price", Money(target, item.Price?.Currency ?? ""))));
        if (item.Checked is DateTime at) notes.Add(I18n.T("v28.wish.checked", ("when", Ui.Ago(at))));

        var info = Ui.Col(6, Ui.Text(item.Name, "h3"), price);
        // 29.0: где дешевле — по данным CheapShark, если уже узнавали.
        if (PriceCompare.Known(item.AppId) is { Cheapest: { } best } other && best.StoreId != "1")
        {
            var steamUsd = other.Deals.FirstOrDefault(d => d.StoreId == "1");
            var cheaper = Ui.Text(steamUsd is null
                ? I18n.T("v29.price.cheaperNoSteam", ("store", best.Store), ("price", PriceCompare.Usd(best.Price)))
                : I18n.T("v29.price.cheaper", ("store", best.Store), ("price", PriceCompare.Usd(best.Price)), ("steam", PriceCompare.Usd(steamUsd.Price))), "small", color: Ui.Res("Good"), wrap: true);
            info.Children.Add(cheaper);
        }
        if (notes.Count > 0) info.Children.Add(Ui.Text(string.Join(" · ", notes), "tiny muted", wrap: true));
        info.VerticalAlignment = VerticalAlignment.Center;
        info.Margin = new Thickness(16, 0, 12, 0);

        var target_ = Ui.Button("", () => AskTarget(item), "icon ghost", Icons.Bell, I18n.T("v28.wish.target"));
        var compare = Ui.Button("", () => PriceUi.Show(item.AppId, item.Name, item.Price), "icon ghost", Icons.Percent, I18n.T("v29.price.compare"));
        var store = Ui.Button("", () => SteamLibrary.StorePage(item.AppId), "icon ghost", Icons.Globe, I18n.T("v12w.steam.page"));
        var remove = Ui.Button("", () => { Wishlist.Remove(item.AppId); MainWindow.Current?.Toast(I18n.T("v28.wish.removed", ("name", item.Name))); }, "icon ghost", Icons.Trash, I18n.T("v28.wish.remove"));
        var tools = Ui.Row(2, compare, target_, store, remove);
        tools.VerticalAlignment = VerticalAlignment.Center;

        var row = new DockPanel();
        DockPanel.SetDock(art, Dock.Left);
        DockPanel.SetDock(tools, Dock.Right);
        row.Children.Add(art);
        row.Children.Add(tools);
        row.Children.Add(info);
        var card = Ui.Card(row, 12);
        if (!item.Seen) card.BorderBrush = Ui.Res("Good");
        return card;
    }

    /// <summary>Сумма в сотых долях валюты — так, как её пишет Steam в этой валюте.</summary>
    internal static string Money(long cents, string currency)
    {
        var v = cents / 100m;
        var c = I18n.Culture;
        return currency switch
        {
            "RUB" => v.ToString("N0", c) + " ₽",
            "UAH" => v.ToString("N0", c) + " ₴",
            "KZT" => v.ToString("N0", c) + " ₸",
            "USD" => "$" + v.ToString("N2", c),
            "GBP" => "£" + v.ToString("N2", c),
            "EUR" => v.ToString("N2", c) + " €",
            "BRL" => "R$ " + v.ToString("N2", c),
            "CNY" => "¥ " + v.ToString("N2", c),
            "PLN" => v.ToString("N2", c) + " zł",
            "" => v.ToString("N2", c),
            _ => v.ToString("N2", c) + " " + currency,
        };
    }

    void AskTarget(WishItem item)
    {
        if (MainWindow.Current is not { } w) return;
        var box = new TextBox
        {
            Watermark = item.Price is { } p ? I18n.T("v28.wish.target.hint", ("price", p.FinalText)) : I18n.T("v28.wish.target.hintNoPrice"),
            Text = item.Target is long t ? (t / 100m).ToString("0.##", I18n.Culture) : "",
            Width = 220, HorizontalAlignment = HorizontalAlignment.Left,
        };
        var error = Ui.Text("", "small", color: Ui.Res("Bad"));
        void Save()
        {
            var text = (box.Text ?? "").Trim().Replace(" ", "").Replace(" ", "");
            if (text == "") { Wishlist.SetTarget(item.AppId, null); w.CloseDialog(); return; }
            if (!decimal.TryParse(text, System.Globalization.NumberStyles.Number, I18n.Culture, out var value)
                && !decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out value) || value <= 0)
            {
                error.Text = I18n.T("v28.wish.target.bad");
                return;
            }
            Wishlist.SetTarget(item.AppId, (long)Math.Round(value * 100));
            w.CloseDialog();
            w.Toast(I18n.T("v28.wish.target.saved", ("name", item.Name)));
        }
        box.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Save(); };
        w.Dialog(I18n.T("v28.wish.target.title", ("name", item.Name)),
            Ui.Col(10, Ui.Text(I18n.T("v28.wish.target.text"), "muted", wrap: true), box, error),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("v28.wish.target.save"), Save, "primary", Icons.Check));
        Dispatcher.UIThread.Post(() => box.Focus());
    }

    async Task CheckNow()
    {
        _busy = true;
        Build();
        try
        {
            var told = await Wishlist.Check(force: true);
            // 29.0: и цены в других магазинах — по одной игре, чтобы не долбить CheapShark.
            foreach (var item in Wishlist.Items)
            {
                try { await PriceCompare.For(item.AppId, force: true); } catch { /* одна игра не ответила — остальные всё равно */ }
                await Task.Delay(250);
            }
            if (told == 0) MainWindow.Current?.Toast(I18n.T("v28.wish.checkedNone"));
        }
        catch (Exception e) { MainWindow.Current?.Toast(I18n.T("v28.wish.checkFailed", ("error", Jobs.Explain(e))), bad: true); }
        finally { _busy = false; Dispatcher.UIThread.Post(Build); }
    }

    async Task Import()
    {
        _busy = true;
        Build();
        try
        {
            var n = await Wishlist.ImportSteam();
            MainWindow.Current?.Toast(n > 0 ? I18n.T("v28.wish.imported", ("n", n)) : I18n.T("v28.wish.importedNone"));
        }
        catch (ServiceError e) when (e.Code is "NO_STEAM" or "PRIVATE")
        {
            MainWindow.Current?.Toast(I18n.T(e.Code == "NO_STEAM" ? "v28.wish.import.noSteam" : "v28.wish.import.private"), bad: true);
        }
        catch (Exception e) { MainWindow.Current?.Toast(I18n.T("v28.wish.import.failed", ("error", Jobs.Explain(e))), bad: true); }
        finally { _busy = false; Dispatcher.UIThread.Post(Build); }
    }
}
