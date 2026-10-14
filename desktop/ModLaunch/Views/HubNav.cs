using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

/// <summary>
/// 9.5: одна навигация Creator Hub для рынка (MarketPage) и мастерской (CreatorPage) — раньше это
/// были два разных экрана со своими вкладками. Три раздела, как в магазинах приложений:
/// «Магазин» (витрина, обзор, Мастерская, авторы, очередь), «Моё» (библиотека, желаемое,
/// кошелёк) и «Автору» (студия продаж, проекты, шаблоны, справка). Выбранный раздел — заливкой,
/// его страницы — рядом; справа — баланс и «Продать работу».
/// </summary>
public static class HubNav
{
    public enum Group { Shop, Mine, Author }

    sealed record Item(string Id, string Key, string Icon, Group Group);

    static readonly Item[] Items =
    [
        new("store", "v93.mk.tab.store", Icons.Home, Group.Shop),
        new("browse", "v93.mk.tab.browse", Icons.Search, Group.Shop),
        new("workshop", "v93.mk.tab.workshop", Icons.Globe, Group.Shop),
        new("authors", "v93.mk.tab.authors", Icons.Users, Group.Shop),
        new("queue", "v93.mk.tab.queue", Icons.Shuffle, Group.Shop),
        new("library", "v93.mk.tab.library", Icons.Layers, Group.Mine),
        new("wishlist", "v93.mk.tab.wishlist", Icons.Heart, Group.Mine),
        new("wallet", "v93.mk.tab.wallet", Icons.Bag, Group.Mine),
        new("studio", "v93.mk.tab.studio", Icons.Chart, Group.Author),
        new("projects", "v95.hub.projects", Icons.Edit, Group.Author),
        new("templates", "v91.cr.tab.templates", Icons.Wand, Group.Author),
        new("docs", "cr.tab.docs", Icons.Book, Group.Author),
    ];

    /// <summary>Вкладки, которые живут на странице рынка (остальные — в мастерской).</summary>
    public static bool IsMarketTab(string id) => id is "store" or "browse" or "workshop" or "authors" or "queue" or "library" or "wishlist" or "wallet" or "studio";

    static Group GroupOf(string id) => Items.FirstOrDefault(i => i.Id == id)?.Group ?? Group.Shop;

    /// <summary>Открыть раздел: вкладку рынка — на той же странице (inPage), остальное — в мастерской.</summary>
    public static void Open(string id, Action<string>? inPage = null)
    {
        if (IsMarketTab(id))
        {
            if (inPage is not null) inPage(id);
            else MainWindow.Current?.Navigate(() => new MarketPage(id));
            return;
        }
        MainWindow.Current?.Navigate(() => id switch
        {
            "templates" => new CreatorPage("examples"),
            "docs" => new CreatorPage("docs"),
            _ => new CreatorPage("home"),
        });
    }

    /// <summary>Шапка Creator Hub: название, баланс, «Продать работу» и полоса разделов.</summary>
    public static Control Header(string current, Action<string>? inPage = null, Action? sold = null)
    {
        var title = new TextBlock { Text = "Creator Hub", FontFamily = Gx.Display, FontSize = 26, FontWeight = FontWeight.Bold, LetterSpacing = -0.6 };
        var words = Ui.Col(3, title, Ui.Text(I18n.T("v93.mk.sub", ("n", 100 - Market.FeePercent)), "muted"));
        words.VerticalAlignment = VerticalAlignment.Center;
        var left = Ui.Row(16, CreatorLogo.Tile(52), words);

        var right = Ui.Row(10);
        right.VerticalAlignment = VerticalAlignment.Center;
        if (Social.Account.SignedIn && MarketData.Wallet is { } w)
        {
            var wallet = new Button
            {
                Classes = { "chip" }, Padding = new Thickness(12, 7),
                Content = Ui.Row(8, Ui.Icon(Icons.Bag, 15, Ui.Res("Brand2")), new TextBlock { Text = Market.Money(w.Balance), FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center }),
            };
            if (current == "wallet") wallet.Classes.Add("active");
            ToolTip.SetTip(wallet, I18n.T("v93.mk.wallet"));
            wallet.Click += (_, _) => Open("wallet", inPage);
            right.Children.Add(wallet);
        }
        right.Children.Add(Ui.Button(I18n.T("mk.sell"), () => ListingWizard.Show(null, sold ?? MarketData.Reload), "primary", Icons.Plus));
        var head = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        head.Children.Add(right);
        head.Children.Add(left);
        return Ui.Col(16, head, Bar(current, inPage));
    }

    /// <summary>Полоса: три раздела-переключателя слева, страницы выбранного раздела — справа от них.</summary>
    public static Control Bar(string current, Action<string>? inPage = null)
    {
        var group = GroupOf(current);
        var groups = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var (g, key, icon) in new[] { (Group.Shop, "v95.hub.group.shop", Icons.Bag), (Group.Mine, "v95.hub.group.mine", Icons.Layers), (Group.Author, "v95.hub.group.author", Icons.Edit) })
        {
            var target = Items.First(i => i.Group == g).Id;
            var b = Ui.Button(I18n.T(key), () => { if (g != group) Open(target, inPage); }, "hub-group", icon);
            b.Padding = new Thickness(14, 8);
            if (g == group) b.Classes.Add("active");
            groups.Children.Add(b);
        }
        var segmented = new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(10), Padding = new Thickness(3), Child = groups, VerticalAlignment = VerticalAlignment.Center };

        var pages = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var item in Items.Where(i => i.Group == group))
        {
            var label = I18n.T(item.Key);
            if (item.Id == "wishlist" && MarketLocal.WishCount > 0) label += "  " + MarketLocal.WishCount;
            var id = item.Id;
            var b = Ui.Button(label, () => Open(id, inPage), "pivot", item.Icon);
            b.Padding = new Thickness(12, 8);
            if (current == item.Id) b.Classes.Add("active");
            pages.Children.Add(b);
        }
        var divider = new Border { Width = 1, Height = 26, Background = Ui.Res("Line"), Margin = new Thickness(10, 0), VerticalAlignment = VerticalAlignment.Center };
        var row = new DockPanel();
        DockPanel.SetDock(segmented, Dock.Left);
        DockPanel.SetDock(divider, Dock.Left);
        row.Children.Add(segmented);
        row.Children.Add(divider);
        row.Children.Add(pages);
        return new Border { Classes = { "market-nav" }, Padding = new Thickness(5), CornerRadius = new CornerRadius(12), Background = Ui.Res("Surface"), BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1), Child = row };
    }
}
