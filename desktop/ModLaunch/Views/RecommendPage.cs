using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Games;

namespace ModLaunch.Views;

/// <summary>
/// 29.0: «Для вас» — игры, похожие на те, в которые вы больше всего играете (Features/GameRecommend.cs).
/// У каждой — почему («потому что вы играли в Hollow Knight»), общие теги, оценка игроков, цена; «В желаемое»,
/// страница в Steam и «Не интересно».
/// </summary>
public sealed class RecommendPage : Page
{
    public override string Title => I18n.T("v29.rec.title");

    List<GamePick>? _picks;
    string? _error;
    bool _loading;

    public RecommendPage()
    {
        AttachedToVisualTree += (_, _) => { if (_picks is null && !_loading) _ = Load(); };
    }

    async Task Load(bool force = false)
    {
        _loading = true;
        _error = null;
        Build();
        try
        {
            if (AllGames.Current is null) await AllGames.Refresh();
            _picks = await Task.Run(() => GameRecommend.Pick());
        }
        catch (Exception e) { _error = Jobs.Explain(e); }
        _loading = false;
        Dispatcher.UIThread.Post(Build);
    }

    public override void Build()
    {
        var refresh = Ui.Button(I18n.T("v29.rec.refresh"), () => _ = Load(true), "", Icons.Refresh);
        refresh.IsEnabled = !_loading;
        var wish = Ui.Button(I18n.T("v28.wish.title"), () => MainWindow.Current?.Navigate(() => new WishlistPage()), "ghost", Icons.Heart);
        var tools = Ui.Row(8, wish, refresh);
        tools.VerticalAlignment = VerticalAlignment.Center;
        var head = new DockPanel();
        DockPanel.SetDock(tools, Dock.Right);
        head.Children.Add(tools);
        var seeds = GameRecommend.Seeds(3).Select(s => s.Name).ToList();
        head.Children.Add(Ui.Col(4, Ui.Text(I18n.T("v29.rec.title"), "h1"),
            Ui.Text(seeds.Count > 0 ? I18n.T("v29.rec.lead", ("games", string.Join(", ", seeds))) : I18n.T("v29.rec.leadEmpty"), "muted", wrap: true)));

        var body = Ui.Col(16, head);
        if (_loading && _picks is null)
            body.Children.Add(Ui.Card(Ui.Col(6, Ui.Text(I18n.T("v29.rec.loading"), "h3"), Ui.Text(I18n.T("v29.rec.loading.text"), "muted", wrap: true)), 22));
        else if (_error is not null)
            body.Children.Add(Ui.Card(Ui.Col(8, Ui.Text(I18n.T("v29.rec.failed"), "h3"), Ui.Text(_error, "muted", wrap: true),
                Ui.Button(I18n.T("v29.rec.retry"), () => _ = Load(true), "primary", Icons.Refresh)), 22));
        else if (_picks is { Count: 0 })
            body.Children.Add(Ui.Card(Ui.Col(6, Ui.Text(I18n.T("v29.rec.none"), "h3"), Ui.Text(I18n.T("v29.rec.none.text"), "muted", wrap: true)), 22));
        else if (_picks is not null)
        {
            var grid = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var p in _picks) grid.Children.Add(Card(p));
            body.Children.Add(grid);
            body.Children.Add(Ui.Text(I18n.T("v29.rec.note"), "tiny muted", wrap: true));
        }
        body.Margin = new Thickness(34, 28);
        Content = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    Control Card(GamePick p)
    {
        var art = new Border { Height = 136, CornerRadius = new CornerRadius(10, 10, 0, 0), ClipToBounds = true, Child = HomePage.WebArt(GameDef.SteamArt(p.AppId), null, p.Name, 600) };
        var score = ModRow.Tag(I18n.T("v29.rec.score", ("pct", p.Score)), p.Score >= 80 ? Ui.Soft("#16351F", "#3FB950") : Ui.Res("Surface3"), p.Score >= 80 ? Ui.Ink("#3FB950") : Ui.Res("Muted"));
        var priceLine = Ui.Row(8, score);
        if (p.PriceUsd is decimal usd)
        {
            if (p.Discount > 0) priceLine.Children.Add(ModRow.Tag($"−{p.Discount}%", Ui.Soft("#16351F", "#3FB950"), Ui.Ink("#3FB950")));
            priceLine.Children.Add(Ui.Text(PriceCompare.Usd(usd), "small muted"));
        }
        else priceLine.Children.Add(Ui.Text(I18n.T("v28.wish.free"), "small muted"));
        foreach (var c in priceLine.Children) c.VerticalAlignment = VerticalAlignment.Center;

        var why = p.Because != "" ? I18n.T("v29.rec.because", ("game", p.Because)) : I18n.T("v29.rec.popular");
        var tags = Ui.Text(string.Join(" · ", p.Tags.Take(3)), "tiny muted", wrap: true);
        var have = Wishlist.Has(p.AppId);
        var add = Ui.Button(have ? I18n.T("v28.wish.added") : I18n.T("v28.wish.add"), () =>
        {
            Wishlist.Add(p.AppId, p.Name);
            MainWindow.Current?.Toast(I18n.T("v28.wish.addedToast", ("name", p.Name)));
            Build();
        }, have ? "ghost" : "primary", have ? Icons.Check : Icons.Heart);
        add.IsEnabled = !have;
        var store = Ui.Button("", () => SteamLibrary.StorePage(p.AppId), "icon ghost", Icons.Globe, I18n.T("v12w.steam.page"));
        var hide = Ui.Button("", () =>
        {
            GameRecommend.Dismiss(p.AppId);
            _picks?.RemoveAll(x => x.AppId == p.AppId);
            MainWindow.Current?.Toast(I18n.T("v29.rec.hidden", ("name", p.Name)));
            Build();
        }, "icon ghost", Icons.EyeOff, I18n.T("v29.rec.hide"));
        var buttons = Ui.Row(4, add, store, hide);

        var col = Ui.Col(6, new TextBlock { Text = p.Name, FontWeight = FontWeight.SemiBold, FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis },
            Ui.Text(why, "small", color: Ui.Res("Brand2"), wrap: true), tags, priceLine, buttons);
        col.Margin = new Thickness(14, 10, 14, 14);
        return new Border
        {
            Classes = { "card" }, Width = 292, Margin = new Thickness(0, 0, 14, 14), Padding = new Thickness(0),
            Child = new StackPanel { Children = { art, col } },
        };
    }
}
