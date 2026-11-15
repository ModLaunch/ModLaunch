using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>30.8: «Новости» на странице игры — три последних объявления разработчиков (Features/GameNews.cs).</summary>
public sealed class NewsCard : ContentControl
{
    readonly int _appId;
    List<NewsItem>? _items;
    bool _failed;

    public NewsCard(int steamAppId)
    {
        _appId = steamAppId;
        if (Program.Screenshot || Program.Demo) { _items = Demo(); Render(); return; }
        Render();
        _ = Task.Run(async () =>
        {
            List<NewsItem>? items = null;
            try { items = await GameNews.For(_appId); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException) { }
            Dispatcher.UIThread.Post(() => { _items = items; _failed = items is null; Render(); });
        });
    }

    void Render()
    {
        // Нет сети или новостей — карточку не показываем вовсе: это дополнение, а не главное на странице.
        if (_failed || _items is { Count: 0 }) { Content = null; IsVisible = false; return; }
        IsVisible = true;
        var all = Ui.Button(I18n.T("v30.news.all"), () => Ui.OpenUrl($"https://store.steampowered.com/news/app/{_appId}"), "ghost", Icons.External);
        all.VerticalAlignment = VerticalAlignment.Center;
        var head = new DockPanel();
        DockPanel.SetDock(all, Dock.Right);
        head.Children.Add(all);
        head.Children.Add(Ui.Row(8, Ui.Icon(Icons.Bell, 16, Ui.Res("Brand2")), Ui.Text(I18n.T("v30.news.title"), "h3")));
        var col = Ui.Col(10, head);
        if (_items is null) col.Children.Add(Ui.Text(I18n.T("v30.news.loading"), "small muted"));
        else
            foreach (var n in _items)
            {
                var item = n;
                var body = Ui.Col(3,
                    Ui.Row(8, Ui.Text(n.Title, "h3"), Ui.Text(Ui.Ago(n.When), "small muted")),
                    Ui.Text(n.Summary, "small muted", wrap: true));
                var row = new Button
                {
                    Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(12, 10), Content = body,
                };
                foreach (var c in ((StackPanel)body.Children[0]).Children) c.VerticalAlignment = VerticalAlignment.Center;
                ToolTip.SetTip(row, n.Url);
                row.Click += (_, _) => Ui.OpenUrl(item.Url);
                col.Children.Add(row);
            }
        Content = Ui.Card(col, 18);
    }

    static List<NewsItem> Demo() =>
    [
        new("Patch 1.16: performance and fixes", "https://store.steampowered.com/news/app/1", DateTime.UtcNow.AddDays(-2), "Faster loading on HDDs, fixed a rare crash when opening the map, rebalanced two late-game bosses and improved controller support."),
        new("Free weekend and 50% sale", "https://store.steampowered.com/news/app/1", DateTime.UtcNow.AddDays(-9), "Play for free from Thursday to Sunday; your progress carries over if you buy the game during the sale."),
        new("Roadmap for next year", "https://store.steampowered.com/news/app/1", DateTime.UtcNow.AddDays(-31), "Two free updates, a new region and co-op improvements are coming — here is what we are working on."),
    ];
}
