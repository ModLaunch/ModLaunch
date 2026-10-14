using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Games;

namespace ModLaunch.Views;

/// <summary>Маркет: карточка лота, окно товара с покупкой и редактор лота продавца.</summary>
public static class MarketViews
{
    public static string KindIcon(string kind) => kind switch
    {
        "model" => Icons.Layers,
        "code" => Icons.Code,
        "script" => Icons.Wand,
        "asset" => Icons.Image,
        _ => Icons.Package,
    };

    static readonly FontFamily Mono = new("Cascadia Mono, Consolas, JetBrains Mono, DejaVu Sans Mono, monospace");

    static Border Pill(string text, IBrush? bg = null, IBrush? fg = null) => new()
    {
        Background = bg ?? new SolidColorBrush(Color.FromArgb(210, 15, 17, 22)), CornerRadius = new CornerRadius(999), Padding = new Thickness(9, 3),
        Child = new TextBlock { Text = text, FontSize = 11, Foreground = fg ?? Brushes.White, FontWeight = FontWeight.SemiBold },
    };

    public static string PriceText(Listing l) => l.Free ? I18n.T("mk.free") : Market.Money(l.Price);

    /// <summary>Карточка лота: обложка, тип, цена, автор, продажи.</summary>
    public static Control Card(Listing l, Action? changed = null)
    {
        var game = GameCatalog.ById(l.Game);
        Control cover = l.Images.Count > 0
            ? Ui.Thumb(l.Images[0], l.Title, 400, 0, 600)
            : new Border
            {
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse(game?.Accent ?? "#7C5CFF"), 0), new GradientStop(Color.Parse("#141620"), 1) },
                },
                Child = Ui.Icon(KindIcon(l.Kind), 34, Brushes.White),
            };
        if (cover is Border cb && l.Images.Count > 0) { cb.Width = double.NaN; cb.Height = double.NaN; }
        var kind = Pill(I18n.T("mk.kind." + l.Kind) + (game is null ? "" : " · " + game.ShortName));
        kind.Margin = new Thickness(10);
        kind.HorizontalAlignment = HorizontalAlignment.Left;
        kind.VerticalAlignment = VerticalAlignment.Top;
        var price = Pill(PriceText(l), l.Free ? Ui.Res("Good") : Ui.Res("Brand"));
        price.Margin = new Thickness(10);
        price.HorizontalAlignment = HorizontalAlignment.Right;
        price.VerticalAlignment = VerticalAlignment.Bottom;
        var top = new Border { Height = 118, ClipToBounds = true, CornerRadius = new CornerRadius(16, 16, 0, 0), Child = new Panel { Children = { cover, kind, price } } };
        var body = Ui.Col(5,
            Ui.Text(l.Title, "h3"),
            Ui.Text(I18n.T("mod.by", ("author", l.Author)) + $" · {l.Version}", "small brand"),
            new TextBlock { Text = l.Summary, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Ui.Res("Muted"), FontSize = 13, Height = 36 },
            Ui.Row(14,
                Ui.Row(5, Ui.Icon(Icons.Bag, 12, Ui.Res("Muted")), Ui.Text(I18n.Compact(l.Sales), "small muted")),
                Ui.Text(I18n.T("mk.license." + l.License), "small muted"),
                Ui.Text(Ui.Ago(l.Updated), "small muted")));
        body.Margin = new Thickness(14, 12, 14, 14);
        var card = new Button
        {
            Classes = { "tile" }, Margin = new Thickness(0, 0, 14, 14), Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel { Children = { top, body } },
        };
        card.Click += (_, _) => Open(l, changed);
        Ctx.Attach(card, () => Ctx.Menu(
            Ctx.Item(I18n.T("ctx.open"), Icons.Eye, () => Open(l, changed)),
            l.Mine ? Ctx.Item(I18n.T("mk.edit"), Icons.Edit, () => Editor(l, changed)) : null,
            Market.Owned.Contains(l.Id) || l.Mine ? Ctx.Item(I18n.T("mk.download"), Icons.Download, () => Download(l)) : Ctx.Item(l.Free ? I18n.T("mk.getFree") : I18n.T("mk.buy", ("price", Market.Money(l.Price))), Icons.Bag, () => Open(l, changed)),
            "-",
            l.Preview != "" ? Ctx.Copy(I18n.T("ctx.copyCode"), l.Preview) : null,
            Ctx.Copy(I18n.T("ctx.copyName"), l.Title),
            Ctx.Copy(I18n.T("ctx.copyId"), l.Id)));
        return card;
    }

    /// <summary>Окно товара: описание, превью кода, отзывы, покупка или скачивание.</summary>
    public static async void Open(Listing l, Action? changed = null)
    {
        var w = MainWindow.Current!;
        var owned = false;
        try { owned = await Market.Bought(l); } catch { }

        var info = Ui.Col(12);
        info.Children.Add(Ui.Row(8, Pill(I18n.T("mk.kind." + l.Kind)), Pill(PriceText(l), l.Free ? Ui.Res("Good") : Ui.Res("Brand")),
            Pill(I18n.T("mk.license." + l.License)), Pill(I18n.T("mk.sold", ("n", l.Sales)))));
        info.Children.Add(Ui.Text(I18n.T("mod.by", ("author", l.Author)) + $" · v{l.Version} · {(l.Size > 0 ? GamePage.Size(l.Size) : "—")}" +
            (GameCatalog.ById(l.Game) is { } g ? " · " + g.Name : ""), "small muted"));
        if (l.Summary != "") info.Children.Add(Ui.Text(l.Summary, "", wrap: true));
        if (l.Description != "") info.Children.Add(Ui.Text(l.Description, "small muted", wrap: true));
        if (l.Preview != "")
            info.Children.Add(Ui.Col(6, Ui.Text(I18n.T("mk.preview"), "h3"), new Border
            {
                Classes = { "card" }, Padding = new Thickness(12), MaxHeight = 260,
                Child = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    Content = new SelectableTextBlock { Text = l.Preview, FontFamily = Mono, FontSize = 12.5 },
                },
            }));
        if (!l.Free && !owned && !l.Mine)
            info.Children.Add(Ui.Text(I18n.T("mk.feeNote", ("fee", Market.FeePercent)), "small muted", wrap: true));

        var reviews = Ui.Col(8, Ui.Text(I18n.T("mk.reviews"), "h3"), Ui.Text(I18n.T("common.loading"), "small muted"));
        info.Children.Add(reviews);
        _ = FillReviews(l, reviews, owned);

        var actions = new List<Control> { Ui.Button(I18n.T("common.close"), w.CloseDialog) };
        if (l.Mine)
            actions.Add(Ui.Button(I18n.T("mk.edit"), () => { w.CloseDialog(); Editor(l, changed); }, "", Icons.Edit));
        if (owned || l.Mine)
            actions.Add(Ui.Button(I18n.T("mk.download"), () => Download(l), "primary", Icons.Download));
        else
            actions.Add(Ui.Button(l.Free ? I18n.T("mk.getFree") : I18n.T("mk.buy", ("price", Market.Money(l.Price))), () => Buy(l, changed), "primary", Icons.Bag));

        w.Dialog(l.Title, new ScrollViewer { MaxHeight = 560, Content = info }, 640, actions.ToArray());
    }

    static async Task FillReviews(Listing l, StackPanel box, bool owned)
    {
        List<ListingReview> list;
        try { list = await Market.Reviews(l); } catch { list = []; }
        box.Children.RemoveAt(1);
        if (list.Count > 0)
            box.Children.Add(Ui.Text(I18n.T("mk.rating", ("avg", list.Average(r => r.Stars).ToString("0.0", I18n.Culture)), ("n", list.Count)), "small brand"));
        foreach (var r in list.Take(10))
            box.Children.Add(Ui.Col(2, Ui.Text($"{new string('★', r.Stars)}{new string('☆', 5 - r.Stars)}  {r.Author} · {Ui.Ago(r.Created)}", "small muted"), Ui.Text(r.Text, "small", wrap: true)));
        if (list.Count == 0) box.Children.Add(Ui.Text(I18n.T("mk.noReviews"), "small muted"));
        if (!owned || l.Mine) return;
        var stars = new ComboBox { Width = 110, ItemsSource = new[] { "★★★★★", "★★★★", "★★★", "★★", "★" }, SelectedIndex = 0 };
        var text = new TextBox { Watermark = I18n.T("mk.review.hint"), MaxLength = 1000, Width = 330 };
        box.Children.Add(Ui.Row(8, stars, text, Ui.Button(I18n.T("mk.review.send"), async () =>
        {
            try { await Market.Review(l, 5 - stars.SelectedIndex, text.Text ?? ""); MainWindow.Current?.Toast(I18n.T("mk.review.done")); }
            catch (Exception e) { MainWindow.Current?.Toast(Market.Explain(e), bad: true); }
        })));
    }

    static async void Buy(Listing l, Action? changed)
    {
        var w = MainWindow.Current!;
        if (!Social.Account.SignedIn) { w.CloseDialog(); w.Navigate(() => new AccountPage()); return; }
        w.CloseDialog();
        try
        {
            await Market.Buy(l);
            w.Toast(I18n.T("mk.bought", ("title", l.Title)));
            changed?.Invoke();
            Open(l, changed);
        }
        catch (Exception e)
        {
            if (e is Social.ServiceError { Code: "NO_MONEY" })
                w.Dialog(I18n.T("mk.noMoney.title"), Ui.Text(I18n.T("mk.noMoney.text", ("price", Market.Money(l.Price))), "muted", wrap: true),
                    Ui.Button(I18n.T("common.close"), w.CloseDialog),
                    Ui.Button(I18n.T("acc.wallet.topup"), () => { w.CloseDialog(); w.Navigate(() => new MarketPage("wallet")); }, "primary", Icons.Plus));
            else w.Toast(Market.Explain(e), bad: true);
        }
    }

    public static async void Download(Listing l)
    {
        var w = MainWindow.Current!;
        w.Toast(I18n.T("mk.downloading", ("title", l.Title)));
        try
        {
            var file = await Market.Download(l);
            w.Toast(I18n.T("mk.downloaded", ("title", l.Title)));
            Actions.OpenFolder(Path.GetDirectoryName(file)!);
        }
        catch (Exception e) { w.Toast(Market.Explain(e), bad: true); }
    }

    // ---------------------------------------------------------------- редактор лота

    /// <summary>Выставить новый лот или изменить свой.</summary>
    /// <summary>9.5: выставить или изменить лот — пошаговый мастер с живым превью (ListingWizard).</summary>
    public static void Editor(Listing? l, Action? done = null) => ListingWizard.Show(l, done);
}
