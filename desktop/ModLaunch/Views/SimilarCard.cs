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
/// 30.13: «Похожие игры» на странице игры — по тегам игроков SteamSpy (тот же движок, что «Для вас»): обложка, оценка,
/// цена и «В желаемое». Грузится, только когда карточку видно; нет сети или похожих — карточки нет.
/// </summary>
public sealed class SimilarCard : ContentControl
{
    readonly int _appId;
    readonly string _name;
    List<GamePick>? _picks;
    bool _started;

    public SimilarCard(int appId, string name)
    {
        _appId = appId;
        _name = name;
        IsVisible = true;
        Content = Ui.Card(Ui.Col(10, Head(), Ui.Text(I18n.T("v30.similar.loading"), "small muted")), 18);
        // SteamSpy отвечает не чаще раза в секунду — спрашиваем, только когда страницу игры правда открыли.
        AttachedToVisualTree += (_, _) => Start();
    }

    void Start()
    {
        if (_started) return;
        _started = true;
        _ = Task.Run(async () =>
        {
            List<GamePick>? picks = null;
            try { picks = await GameRecommend.Similar(_appId, _name); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or IOException) { }
            Dispatcher.UIThread.Post(() => { _picks = picks; Render(); });
        });
    }

    Control Head() => Ui.Row(8, Ui.Icon(Icons.Sparkles, 16, Ui.Res("Brand2")), Ui.Text(I18n.T("v30.similar.title"), "h3"));

    void Render()
    {
        if (_picks is not { Count: > 0 } picks) { Content = null; IsVisible = false; return; }
        var grid = new UniformGrid { Columns = 3 };
        foreach (var p in picks.Take(6)) grid.Children.Add(Tile(p));
        Content = Ui.Card(Ui.Col(12, Head(), Ui.Text(I18n.T("v30.similar.lead", ("name", _name)), "small muted", wrap: true), grid), 18);
    }

    Control Tile(GamePick p)
    {
        var art = new Border { Height = 96, CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = HomePage.WebArt(GameDef.SteamArt(p.AppId), null, p.Name, 460) };
        var price = p.PriceUsd is decimal usd ? (p.Discount > 0 ? $"-{p.Discount}% · ${usd:0.00}" : $"${usd:0.00}") : I18n.T("v30.similar.free");
        var have = Wishlist.Has(p.AppId);
        var wish = Ui.Button(have ? "♥" : "♡", () => { }, "icon ghost", null, I18n.T(have ? "v30.similar.wished" : "v28.wish.add"));
        wish.FontSize = 16;
        if (have) wish.Foreground = Ui.Res("Bad");
        wish.Click += (_, _) =>
        {
            if (Wishlist.Has(p.AppId)) return;
            Wishlist.Add(p.AppId, p.Name);
            wish.Content = "♥";
            wish.Foreground = Ui.Res("Bad");
            MainWindow.Current?.Toast(I18n.T("v30.similar.added", ("name", p.Name)));
        };
        var store = Ui.Button("", () => SteamLibrary.StorePage(p.AppId), "icon ghost", Icons.Globe, I18n.T("v12w.steam.page"));
        var tools = Ui.Row(0, wish, store);
        tools.VerticalAlignment = VerticalAlignment.Center;
        var line = new DockPanel();
        DockPanel.SetDock(tools, Dock.Right);
        line.Children.Add(tools);
        line.Children.Add(Ui.Col(1,
            new TextBlock { Text = p.Name, FontSize = 13, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Ui.Res("Text") },
            Ui.Text($"👍 {p.Score}% · {price}", "small muted")));
        var tile = Ui.Col(6, art, line);
        tile.Margin = new Thickness(0, 0, 10, 10);
        return tile;
    }
}
