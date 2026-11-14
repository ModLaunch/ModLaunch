using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Social;

namespace ModLaunch.Views;

/// <summary>
/// 29.0: «Инди-игры» — витрина игр независимых разработчиков и раздел «Для разработчиков»: сколько берёт площадка
/// (сейчас 10 %, потолок 29 %) против 30 % в Steam, свои игры с продажами и «Опубликовать игру».
/// Покупка — с баланса ModLaunch (как на рынке Creator Hub), установка — в библиотеку одной кнопкой.
/// </summary>
public sealed class IndiePage : Page
{
    public override string Title => I18n.T("v29.indie.title");

    string _tab;
    List<Listing>? _all, _mine;
    string? _error;

    public IndiePage(string tab = "store")
    {
        _tab = tab;
        AttachedToVisualTree += (_, _) => { if (_all is null) _ = Load(); };
    }

    async Task Load()
    {
        _error = null;
        try
        {
            _all = await IndieGames.All(force: true);
            _mine = Account.SignedIn || Program.Demo ? await IndieGames.Mine() : [];
        }
        catch (Exception e) { _error = Unavailable(e) ? I18n.T("v29.indie.unavailable") : Market.Explain(e); _all ??= []; _mine ??= []; }
        Dispatcher.UIThread.Post(Build);
    }

    static bool Unavailable(Exception e) => e is ServiceError { Code: "DENIED" or "NOT_CONFIGURED" or "BUSY" };

    public override void Build()
    {
        var tabs = Ui.Row(6);
        foreach (var (id, label, icon) in new[] { ("store", I18n.T("v29.indie.tab.store"), Icons.Gamepad), ("dev", I18n.T("v29.indie.tab.dev"), Icons.Code) })
        {
            var t = id;
            tabs.Children.Add(Ui.Button(label, () => { _tab = t; Build(); }, t == _tab ? "chip active" : "chip", icon));
        }
        var head = Ui.Col(6, Ui.Text(I18n.T("v29.indie.title"), "h1"), Ui.Text(I18n.T("v29.indie.lead", ("fee", Market.FeePercent)), "muted", wrap: true), tabs);
        var body = Ui.Col(18, head);
        if (_error is not null) body.Children.Add(Ui.Card(Ui.Text(_error, "muted", wrap: true), 18));
        if (_all is null) body.Children.Add(Ui.Card(Ui.Text(I18n.T("v29.indie.loading"), "muted"), 22));
        else if (_tab == "dev") body.Children.Add(DevPortal());
        else body.Children.Add(Store());
        body.Margin = new Thickness(34, 28);
        Content = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    Control Store()
    {
        var games = _all ?? [];
        if (games.Count == 0)
            return Ui.Card(Ui.Col(8, Ui.Text(I18n.T("v29.indie.empty"), "h3"), Ui.Text(I18n.T("v29.indie.empty.text"), "muted", wrap: true),
                Ui.Button(I18n.T("v29.indie.tab.dev"), () => { _tab = "dev"; Build(); }, "primary", Icons.Code)), 22);
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var l in games.OrderByDescending(l => l.Sales).ThenByDescending(l => l.Created))
        {
            var card = MarketTiles.Card(l, 300);
            card.Margin = new Thickness(0, 0, 14, 14);
            wrap.Children.Add(card);
        }
        return Ui.Col(10, wrap, Ui.Text(I18n.T("v29.indie.storeNote"), "tiny muted", wrap: true));
    }

    Control DevPortal()
    {
        var fee = Market.FeePercent;
        Control Bar(string store, int percent, bool ours)
        {
            var keep = 100 - percent;
            var bar = new Grid { ColumnDefinitions = new ColumnDefinitions($"{keep}*,{Math.Max(percent, 1)}*"), Height = 22 };
            var dev = new Border { Background = ours ? Ui.Res("Good") : Ui.Res("Brand"), CornerRadius = new CornerRadius(6, 0, 0, 6), Child = new TextBlock { Text = I18n.T("v29.indie.youGet", ("pct", keep)), FontSize = 11.5, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0) } };
            var cut = new Border { Background = Ui.Res("Surface3"), CornerRadius = new CornerRadius(0, 6, 6, 0) };
            Grid.SetColumn(cut, 1);
            bar.Children.Add(dev);
            bar.Children.Add(cut);
            var label = Ui.Text($"{store} · {percent}%", ours ? "h3" : "small muted");
            label.Width = 150;
            label.VerticalAlignment = VerticalAlignment.Center;
            var row = new DockPanel();
            DockPanel.SetDock(label, Dock.Left);
            row.Children.Add(label);
            row.Children.Add(bar);
            return row;
        }
        var compare = Ui.Card(Ui.Col(10,
            Ui.Text(I18n.T("v29.indie.fee.title"), "h2"),
            Ui.Text(I18n.T("v29.indie.fee.text", ("fee", fee), ("max", Market.MaxFeePercent)), "muted", wrap: true),
            Bar("ModLaunch", fee, true), Bar("Steam", 30, false), Bar("Epic Games Store", 12, false)), 22);

        var steps = Ui.Card(Ui.Col(8,
            Ui.Text(I18n.T("v29.indie.how.title"), "h2"),
            Ui.Text(I18n.T("v29.indie.how.1"), "small", wrap: true),
            Ui.Text(I18n.T("v29.indie.how.2", ("max", GamePage.Size(Market.MaxFile))), "small", wrap: true),
            Ui.Text(I18n.T("v29.indie.how.3"), "small", wrap: true),
            Ui.Text(I18n.T("v29.indie.how.4"), "small", wrap: true),
            Ui.Row(8,
                Ui.Button(I18n.T("v29.indie.publish"), () => Publish(null), "primary", Icons.Plus),
                Ui.Button(I18n.T("v29.indie.payouts"), () => MainWindow.Current?.Navigate(() => new AccountPage("wallet")), "ghost", Icons.Bag))), 22);

        var mine = Ui.Col(8, Ui.Text(I18n.T("v29.indie.mine"), "h2"));
        if (!Account.SignedIn && !Program.Demo)
            mine.Children.Add(Ui.Row(10, Ui.Text(I18n.T("v29.indie.signIn"), "muted", wrap: true), Ui.Button(I18n.T("aside.login"), () => MainWindow.Current?.Navigate(() => new AccountPage()), "", Icons.User)));
        else if (_mine is { Count: > 0 })
            foreach (var l in _mine)
            {
                var ll = l;
                var edit = Ui.Button(I18n.T("v29.indie.update"), () => Publish(ll), "", Icons.Upload);
                mine.Children.Add(MarketTiles.Row(l, right: Ui.Row(8, Ui.Text(I18n.T("v29.indie.sales", ("n", l.Sales)), "small muted"), edit)));
            }
        else mine.Children.Add(Ui.Text(I18n.T("v29.indie.mine.none"), "muted"));
        return Ui.Col(16, compare, steps, Ui.Card(mine, 22));
    }

    // ---------------------------------------------------------------- публикация

    void Publish(Listing? existing)
    {
        if (MainWindow.Current is not { } w) return;
        if (!Account.SignedIn) { w.Toast(I18n.T("v29.indie.signIn"), bad: true); w.Navigate(() => new AccountPage()); return; }
        var title = new TextBox { Text = existing?.Title ?? "", Watermark = I18n.T("v29.indie.f.title"), MaxLength = 60 };
        var summary = new TextBox { Text = existing?.Summary ?? "", Watermark = I18n.T("v29.indie.f.summary"), MaxLength = 200 };
        var description = new TextBox { Text = existing?.Description ?? "", Watermark = I18n.T("v29.indie.f.description"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, MaxLength = 5000 };
        var price = new TextBox { Text = existing is { } e ? (e.Price / 100m).ToString("0.##", I18n.Culture) : "", Watermark = I18n.T("v29.indie.f.price", ("currency", Market.Currency)), Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
        var version = new TextBox { Text = existing?.Version ?? "1.0.0", Watermark = "1.0.0", Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
        var images = new TextBox { Text = string.Join("\n", existing?.Images ?? []), Watermark = I18n.T("v29.indie.f.images"), AcceptsReturn = true, MinHeight = 60 };
        var url = new TextBox { Watermark = I18n.T("v29.indie.f.url") };
        var genres = new HashSet<string>(existing?.Tags ?? []);
        var genreRow = new WrapPanel();
        void RenderGenres()
        {
            genreRow.Children.Clear();
            foreach (var gname in IndieGames.Genres)
            {
                var gg = gname;
                var b = Ui.Button(I18n.T("v29.indie.g." + gg), () => { if (!genres.Remove(gg) && genres.Count < 3) genres.Add(gg); RenderGenres(); }, genres.Contains(gg) ? "chip active" : "chip");
                b.Margin = new Thickness(0, 0, 4, 4);
                genreRow.Children.Add(b);
            }
        }
        RenderGenres();
        string? build = null;
        var buildText = Ui.Text(existing is null ? I18n.T("v29.indie.f.noBuild") : I18n.T("v29.indie.f.keepBuild"), "small muted", wrap: true);
        var pick = Ui.Button(I18n.T("v29.indie.f.pick"), async () =>
        {
            var files = await w.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("ZIP / EXE") { Patterns = ["*.zip", "*.exe"] }],
            });
            if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
            build = path;
            var size = new FileInfo(path).Length;
            buildText.Text = size > Market.MaxFile
                ? I18n.T("v29.indie.f.bigBuild", ("name", Path.GetFileName(path)), ("size", GamePage.Size(size)))
                : I18n.T("v29.indie.f.smallBuild", ("name", Path.GetFileName(path)), ("size", GamePage.Size(size)));
        }, "", Icons.FilePlus);
        var error = Ui.Text("", "small", color: Ui.Res("Bad"), wrap: true);
        var progress = new ProgressBar { Minimum = 0, Maximum = 1, IsVisible = false, Height = 6 };

        Button? send = null;
        async Task Go()
        {
            error.Text = "";
            var money = string.IsNullOrWhiteSpace(price.Text) ? 0 : Market.ParseMoney(price.Text);
            if (money is null) { error.Text = I18n.T("v29.indie.e.price"); return; }
            if ((title.Text ?? "").Trim() == "") { error.Text = I18n.T("v29.indie.e.title"); return; }
            var d = new ListingDraft
            {
                Title = title.Text ?? "", Summary = summary.Text ?? "", Description = description.Text ?? "", Game = "windows", Price = money.Value,
                Version = (version.Text ?? "").Trim(), Tags = [.. genres],
                Images = (images.Text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            };
            send!.IsEnabled = false;
            progress.IsVisible = true;
            try
            {
                await IndieGames.Publish(d, existing, build, url.Text, new Progress<double>(v => progress.Value = v));
                w.CloseDialog();
                w.Toast(I18n.T(existing is null ? "v29.indie.published" : "v29.indie.updated", ("title", d.Title)));
                _all = null;
                _ = Load();
            }
            catch (ServiceError e) when (e.Code is "NEED_URL" or "BAD_BUILD" or "NO_FILE")
            {
                error.Text = I18n.T("v29.indie.e." + e.Code);
            }
            catch (ServiceError e) when (e.Code == "INVALID" && e.Message == "version") { error.Text = I18n.T("v29.indie.e.version"); }
            catch (Exception e) { error.Text = Unavailable(e) ? I18n.T("v29.indie.unavailable") : Market.Explain(e); }
            send.IsEnabled = true;
            progress.IsVisible = false;
        }
        send = Ui.Button(existing is null ? I18n.T("v29.indie.publish") : I18n.T("v29.indie.update"), () => _ = Go(), "primary", Icons.Upload);

        var form = Ui.Col(10,
            Ui.Text(I18n.T("v29.indie.f.lead", ("fee", Market.FeePercent)), "small muted", wrap: true),
            title, summary, description,
            Ui.Text(I18n.T("v29.indie.f.genres"), "small"), genreRow,
            Ui.Row(10, price, version),
            images,
            Ui.Text(I18n.T("v29.indie.f.build"), "h3"), Ui.Row(10, pick), buildText, url,
            progress, error);
        w.Dialog(existing is null ? I18n.T("v29.indie.publish") : I18n.T("v29.indie.update"), new ScrollViewer { Content = form, MaxHeight = 560 }, 640,
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog), send);
    }

    // ---------------------------------------------------------------- установка

    /// <summary>Установить купленную (или бесплатную) игру и предложить сыграть.</summary>
    public static async void Install(Listing l)
    {
        if (MainWindow.Current is not { } w) return;
        w.Toast(I18n.T("v29.indie.installing", ("title", l.Title)));
        try
        {
            var state = await Task.Run(() => IndieGames.Install(l));
            AppState.Notify();
            w.Dialog(I18n.T("v29.indie.ready", ("title", l.Title)), Ui.Text(I18n.T("v29.indie.ready.text", ("folder", IndieGames.FolderFor(l))), "muted", wrap: true),
                Ui.Button(I18n.T("common.close"), w.CloseDialog),
                Ui.Button(I18n.T("games.play"), () => { w.CloseDialog(); Actions.Play(state); }, "primary", Icons.Play));
        }
        catch (ServiceError e) when (e.Code is "NOT_BOUGHT" or "NO_EXE" or "NO_FILE" or "BAD_HASH")
        {
            w.Toast(I18n.T("v29.indie.e.install." + e.Code), bad: true);
        }
        catch (Exception e) { w.Toast(Unavailable(e) ? I18n.T("v29.indie.unavailable") : I18n.T("v29.indie.e.install", ("error", Jobs.Explain(e))), bad: true); }
    }
}
