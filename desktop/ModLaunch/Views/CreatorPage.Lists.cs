using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Games;

namespace ModLaunch.Views;

/// <summary>
/// 9.1: вкладки «Коды» и «Модели» (эскиз: список строками под вкладками).
/// «Коды» — скрипты ModScript из Hub и части кода/скрипты из маркета; «Модели» — модели, ассеты и сборки из маркета.
/// </summary>
public sealed partial class CreatorPage
{
    string _shelfSource = "all";

    static readonly string[] CodeKinds = ["code", "script"];
    static readonly string[] ModelKinds = ["model", "asset", "pack"];

    /// <summary>Строка списка: что-то из Hub или из маркета.</summary>
    sealed record ShelfItem(string Title, string Author, string Game, string Kind, string Meta, string? Image, DateTime? When, bool FromHub, Action Open);

    Control Shelf(string which)
    {
        var codes = which == "codes";
        if (_market is null && !_marketLoading && !Program.Screenshot) _ = LoadMarket();
        if (codes && _hub is null && !_hubLoading && !Program.Screenshot) _ = LoadHub();

        var col = new StackPanel { Spacing = 14 };

        // Шапка: пояснение, источники и главное действие.
        var head = new DockPanel();
        var action = codes
            ? Ui.Button(I18n.T("v91.cr.publish"), PublishArchive, "primary", Icons.Upload)
            : Ui.Button(I18n.T("v91.cr.sell"), () => MarketViews.Editor(null, () => { _ = LoadMarket(force: true); _studio = null; }), "primary", Icons.Plus);
        action.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(action, Dock.Right);
        head.Children.Add(action);
        var sources = Ui.Row(6);
        if (codes)
            foreach (var (id, key) in new[] { ("all", "v91.cr.src.all"), ("hub", "v91.cr.src.hub"), ("market", "v91.cr.src.market") })
            {
                var src = id;
                var chip = Ui.Button(I18n.T(key), () => { _shelfSource = src; Build(); }, "chip");
                if (_shelfSource == id) chip.Classes.Add("active");
                sources.Children.Add(chip);
            }
        var hint = Ui.Text(I18n.T(codes ? "v91.cr.codes.hint" : "v91.cr.models.hint"), "muted", wrap: true);
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.Margin = new Thickness(codes ? 14 : 0, 0, 16, 0);
        sources.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(Ui.Row(0, sources, hint));
        col.Children.Add(head);

        var loading = (_marketLoading && _market is null) || (codes && _hubLoading && _hub is null);
        if (loading)
        {
            for (var i = 0; i < 5; i++) col.Children.Add(new Border { Classes = { "card", "skeleton" }, Height = 72 });
            return col;
        }

        var items = new List<ShelfItem>();
        var source = codes ? _shelfSource : "market";
        if (codes && source is "all" or "hub")
            foreach (var m in (_hub ?? []).Where(m => m.Kind == "script"))
            {
                var mm = m;
                items.Add(new ShelfItem(m.Name, m.Author, m.Game, "script",
                    $"↓ {I18n.Compact(m.Downloads)}   ♥ {I18n.Compact(m.Likes)}", m.Images.FirstOrDefault(), m.Updated, true, () => OpenMod(mm)));
            }
        if (source is "all" or "market")
            foreach (var l in (_market ?? []).Where(l => (codes ? CodeKinds : ModelKinds).Contains(l.Kind)))
            {
                var ll = l;
                items.Add(new ShelfItem(l.Title, l.Author, l.Game, l.Kind, MarketViews.PriceText(l), l.Images.FirstOrDefault(), l.Updated ?? l.Created, false,
                    () => MarketViews.Open(ll, () => { _studio = null; _ = LoadMarket(force: true); })));
            }
        if (_filter != "")
            items = items.Where(i => i.Title.Contains(_filter, StringComparison.OrdinalIgnoreCase) || i.Author.Contains(_filter, StringComparison.OrdinalIgnoreCase)).ToList();
        items = items.OrderByDescending(i => i.When ?? DateTime.MinValue).ToList();

        if (items.Count == 0)
        {
            col.Children.Add(Ui.Card(Ui.Col(10,
                Ui.Row(12, new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(22), Background = Ui.Res("BrandSoft"), Child = Ui.Icon(codes ? Icons.Code : Icons.Cube, 20, Ui.Res("Brand2")) },
                    Ui.Text(I18n.T(codes ? "v91.cr.codes.empty" : "v91.cr.models.empty"), "h3", wrap: true))), 24));
            if (_marketError is not null) col.Children.Add(Ui.Text(_marketError, "small muted", wrap: true));
            return col;
        }

        var n = 0;
        foreach (var item in items)
        {
            var row = ShelfRow(item);
            if (!Shown) Animate.Rise(row, n++);
            col.Children.Add(row);
        }
        return col;
    }

    static Control ShelfRow(ShelfItem item)
    {
        var game = GameCatalog.ById(item.Game);
        Control pic;
        if (item.Image is string url)
            pic = Ui.Thumb(url, item.Title, 52, 14, 120);
        else
            pic = new Border
            {
                Width = 52, Height = 52, CornerRadius = new CornerRadius(14),
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse(game?.Accent ?? "#7C5CFF"), 0), new GradientStop(Color.Parse("#141620"), 1) },
                },
                Child = Ui.Icon(item.FromHub ? Icons.Code : MarketViews.KindIcon(item.Kind), 22, Brushes.White),
            };
        var kind = item.FromHub ? I18n.T("hub.kind.script") : I18n.T("mk.kind." + item.Kind);
        var source = new Border
        {
            Background = item.FromHub ? Ui.Res("BrandSoft") : Ui.Res("Surface3"), CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 2), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = item.FromHub ? "Hub" : I18n.T("mk.tab"), FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = item.FromHub ? Ui.Res("Brand2") : Ui.Res("Muted") },
        };
        var words = Ui.Col(3,
            Ui.Row(8, Ui.Text(item.Title, "h3"), source),
            Ui.Text(string.Join(" · ", new[] { item.Author, game?.Name ?? item.Game, kind }.Where(s => !string.IsNullOrEmpty(s))), "small muted"));
        words.VerticalAlignment = VerticalAlignment.Center;
        var meta = Ui.Text(item.Meta, "small", color: item.FromHub ? Ui.Res("Muted") : Ui.Res("Brand2"));
        meta.VerticalAlignment = VerticalAlignment.Center;
        meta.FontWeight = FontWeight.SemiBold;
        var open = Ui.Button(I18n.T("v91.cr.open"), item.Open, "", Icons.Forward);
        open.VerticalAlignment = VerticalAlignment.Center;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 16 };
        grid.Children.Add(pic);
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);
        Grid.SetColumn(meta, 2);
        grid.Children.Add(meta);
        Grid.SetColumn(open, 3);
        grid.Children.Add(open);
        var row = new Button
        {
            Classes = { "card-btn" }, Padding = new Thickness(14, 12), HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1),
            Content = grid,
        };
        row.Click += (_, _) => item.Open();
        return row;
    }

    /// <summary>«Опубликовать» откуда угодно (выдвижная полоска): окно публикации, потом — «Мои → Опубликованные».</summary>
    public static void Publish()
    {
        var game = AppState.Games.FirstOrDefault(g => g.Status == Detect.Found)?.Def.Id ?? "";
        HubPublish.Show(new HubDraft { Game = game }, fromProject: false, pack: null,
            done: () => MainWindow.Current?.Navigate(() => new CreatorPage("published")));
    }

    /// <summary>Для снимков: маркет без сети.</summary>
    public static void DemoMarket(List<Listing> list) { _market = list; _marketError = null; }
}
