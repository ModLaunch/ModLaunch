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
                    Ui.Button(I18n.T("acc.wallet.topup"), () => { w.CloseDialog(); w.Navigate(() => new AccountPage("wallet")); }, "primary", Icons.Plus));
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
    public static void Editor(Listing? l, Action? done = null)
    {
        var w = MainWindow.Current!;
        if (!Social.Account.SignedIn && !Program.Screenshot) { w.Navigate(() => new AccountPage()); return; }

        TextBox Box(string value, string hint, bool multi = false, int max = 200, bool mono = false)
        {
            var b = new TextBox
            {
                Text = value, Watermark = hint, AcceptsReturn = multi, TextWrapping = multi && !mono ? TextWrapping.Wrap : TextWrapping.NoWrap,
                MinHeight = multi ? 90 : 0, MaxLength = max, VerticalContentAlignment = multi ? VerticalAlignment.Top : VerticalAlignment.Center,
            };
            if (mono) { b.FontFamily = Mono; b.FontSize = 12.5; b.MinHeight = 120; }
            return b;
        }
        Control Field(string label, Control input) => Ui.Col(5, Ui.Text(label, "small muted"), input);
        ComboBox Combo(IEnumerable<string> items, int index, double width) => new() { ItemsSource = items.ToList(), SelectedIndex = Math.Max(0, index), Width = width };

        var title = Box(l?.Title ?? "", I18n.T("mk.f.title"), max: 60);
        title.IsEnabled = l is null;
        var summary = Box(l?.Summary ?? "", I18n.T("mk.f.summary"), max: 200);
        var description = Box(l?.Description ?? "", I18n.T("mk.f.description"), multi: true, max: 5000);
        var preview = Box(l?.Preview ?? "", I18n.T("mk.f.preview.hint"), multi: true, max: Market.MaxPreview, mono: true);
        var images = Box(string.Join("\n", l?.Images ?? []), I18n.T("hub.f.images"), multi: true, max: 2400);
        var version = Box(l?.Version ?? "1.0.0", "1.0.0", max: 20);
        version.Width = 120;
        var price = Box(l is null ? "0" : (l.Price / 100m).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), "0", max: 12);
        price.Width = 140;
        var kind = Combo(Market.Kinds.Select(k => I18n.T("mk.kind." + k)), Array.IndexOf(Market.Kinds, l?.Kind ?? "model"), 200);
        var license = Combo(Market.Licenses.Select(k => I18n.T("mk.license." + k)), Array.IndexOf(Market.Licenses, l?.License ?? "personal"), 240);
        var gameIds = new List<string> { "" };
        gameIds.AddRange(AppState.Games.Select(g => g.Def.Id));
        var game = Combo(new[] { I18n.T("mk.anyGame") }.Concat(AppState.Games.Select(g => g.Def.Name)), gameIds.IndexOf(l?.Game ?? ""), 240);
        var hidden = new CheckBox { Content = I18n.T("mk.f.hidden"), IsChecked = l is { Active: false } };

        var net = Ui.Text("", "small brand");
        void Net()
        {
            var p = Market.ParseMoney(price.Text);
            net.Text = p is null ? I18n.T("mk.f.price.bad")
                : p == 0 ? I18n.T("mk.f.price.free")
                : I18n.T("mk.f.net", ("net", Market.Money(p.Value - Market.Fee(p.Value))), ("fee", Market.FeePercent));
        }
        price.TextChanged += (_, _) => Net();
        Net();

        string? file = null;
        var fileText = Ui.Text(l is { FileName.Length: > 0 } ? $"{l.FileName} · {GamePage.Size(l.Size)}" : I18n.T("mk.f.noFile"), "small muted");
        var pick = Ui.Button(I18n.T("mk.f.pick"), async () =>
        {
            var f = await Pickers.AnyFile(w, I18n.T("mk.f.pick"));
            if (f is null) return;
            file = f;
            fileText.Text = $"{Path.GetFileName(f)} · {GamePage.Size(new FileInfo(f).Length)}";
        }, "", Icons.FilePlus);

        var chosen = new HashSet<string>(l?.Tags ?? []);
        var tags = new WrapPanel();
        foreach (var tag in Hub.Tags)
        {
            var t = tag;
            var chip = Ui.Button("#" + I18n.T("hub.tag." + tag), () => { }, "chip");
            chip.Margin = new Thickness(0, 0, 6, 6);
            chip.Padding = new Thickness(10, 4);
            chip.FontSize = 12;
            if (chosen.Contains(tag)) chip.Classes.Add("active");
            chip.Click += (_, _) =>
            {
                if (!chosen.Remove(t) && chosen.Count < 6) chosen.Add(t);
                chip.Classes.Set("active", chosen.Contains(t));
            };
            tags.Children.Add(chip);
        }

        var bar = new ProgressBar { Minimum = 0, Maximum = 1, IsVisible = false };
        var form = Ui.Col(12,
            Field(I18n.T("mk.f.title"), title),
            Ui.Row(12, Field(I18n.T("mk.f.kind"), kind), Field(I18n.T("mk.f.game"), game)),
            Ui.Row(12, Field(I18n.T("mk.f.price", ("cur", Market.Currency)), price), Field(I18n.T("mk.f.license"), license), Field(I18n.T("hub.f.version"), version)),
            net,
            Field(I18n.T("mk.f.summary"), summary),
            Field(I18n.T("mk.f.description"), description),
            Field(I18n.T("mk.f.preview"), preview),
            Field(I18n.T("hub.f.images"), images),
            Field(I18n.T("hub.f.tags"), tags),
            Field(I18n.T("mk.f.file"), Ui.Row(10, pick, fileText)),
            hidden,
            bar);

        Button? save = null;
        save = Ui.Button(l is null ? I18n.T("mk.f.publish") : I18n.T("common.save"), async () =>
        {
            var p = Market.ParseMoney(price.Text);
            if (p is null) { w.Toast(I18n.T("mk.f.price.bad"), bad: true); return; }
            var d = new ListingDraft
            {
                Title = title.Text ?? "", Summary = summary.Text ?? "", Description = description.Text ?? "",
                Kind = Market.Kinds[Math.Max(0, kind.SelectedIndex)], License = Market.Licenses[Math.Max(0, license.SelectedIndex)],
                Game = gameIds[Math.Max(0, game.SelectedIndex)], Price = p.Value, Version = (version.Text ?? "").Trim(),
                Preview = preview.Text ?? "", Tags = chosen.ToList(), Hidden = hidden.IsChecked == true, File = file,
                Images = (images.Text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            };
            save!.IsEnabled = false;
            bar.IsVisible = true;
            try
            {
                await Market.Save(d, l, new Progress<double>(r => bar.Value = r));
                w.CloseDialog();
                w.Toast(I18n.T(l is null ? "mk.published" : "mk.saved", ("title", d.Title)));
                done?.Invoke();
            }
            catch (Exception e) { w.Toast(Market.Explain(e), bad: true); save.IsEnabled = true; bar.IsVisible = false; }
        }, "primary", Icons.Upload);

        w.Dialog(l is null ? I18n.T("mk.new") : I18n.T("mk.edit"), new ScrollViewer { MaxHeight = 600, Content = form }, 720,
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog), save);
    }
}
