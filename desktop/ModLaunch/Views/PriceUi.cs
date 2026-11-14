using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>
/// 29.0: «Где дешевле» — цены игры во всех магазинах (CheapShark, в долларах США), самая низкая за всё время и цена
/// Steam для своей страны. Покупка — на сайте магазина (ссылка CheapShark ведёт на нужную страницу).
/// </summary>
public static class PriceUi
{
    public static void Show(int appId, string name, WishPrice? steamLocal)
    {
        if (MainWindow.Current is not { } w) return;
        var list = Ui.Col(6, Ui.Text(I18n.T("v29.price.loading"), "muted"));
        var body = Ui.Col(12);
        if (steamLocal is not null)
            body.Children.Add(Ui.Text(I18n.T("v29.price.steamLocal", ("country", Wishlist.Country.ToUpperInvariant()), ("price", steamLocal.FinalText)), "small", wrap: true));
        body.Children.Add(list);
        body.Children.Add(Ui.Text(I18n.T("v29.price.note"), "tiny muted", wrap: true));
        w.Dialog(I18n.T("v29.price.title", ("name", name)), new ScrollViewer { Content = body, MaxHeight = 460 }, 560,
            Ui.Button(I18n.T("common.close"), w.CloseDialog));

        void Render(PriceInfo? p, string? error)
        {
            list.Children.Clear();
            if (error is not null) { list.Children.Add(Ui.Text(error, "small", color: Ui.Res("Bad"), wrap: true)); return; }
            if (p is null || p.Deals.Count == 0) { list.Children.Add(Ui.Text(I18n.T("v29.price.unknown"), "muted", wrap: true)); return; }
            var cheapest = p.Cheapest!.Price;
            foreach (var d in p.Deals)
            {
                var url = d.Url;
                var open = Ui.Button(I18n.T("v29.price.open"), () => Ui.OpenUrl(url), d.Price == cheapest ? "primary" : "", Icons.External);
                var right = Ui.Row(10);
                if (d.Savings > 0)
                {
                    right.Children.Add(ModRow.Tag($"−{d.Savings}%", Ui.Soft("#16351F", "#3FB950"), Ui.Ink("#3FB950")));
                    right.Children.Add(new TextBlock { Text = PriceCompare.Usd(d.Retail), TextDecorations = TextDecorations.Strikethrough, Foreground = Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Center });
                }
                right.Children.Add(new TextBlock { Text = PriceCompare.Usd(d.Price), FontSize = 15, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, MinWidth = 64, TextAlignment = TextAlignment.Right });
                right.Children.Add(open);
                foreach (var c in right.Children) c.VerticalAlignment = VerticalAlignment.Center;
                var row = new DockPanel();
                DockPanel.SetDock(right, Dock.Right);
                row.Children.Add(right);
                var title = Ui.Col(1, Ui.Text(d.Store, "h3"));
                if (d.Price == cheapest && p.Deals.Count > 1) title.Children.Add(Ui.Text(I18n.T("v29.price.best"), "tiny", color: Ui.Res("Good")));
                title.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(title);
                list.Children.Add(new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8), Child = row });
            }
            if (p.LowestEver is decimal low)
                list.Children.Add(Ui.Text(p.LowestEverAt is DateTime at
                    ? I18n.T("v29.price.lowestEver", ("price", PriceCompare.Usd(low)), ("when", at.ToLocalTime().ToString("MMMM yyyy", I18n.Culture)))
                    : I18n.T("v29.price.lowestEverNoDate", ("price", PriceCompare.Usd(low))), "small muted", wrap: true));
        }

        if (PriceCompare.Known(appId) is { } known) Render(known, null);
        _ = Task.Run(async () =>
        {
            try
            {
                var p = await PriceCompare.For(appId);
                Dispatcher.UIThread.Post(() => Render(p, null));
            }
            catch (Exception e)
            {
                var text = I18n.T("v29.price.failed", ("error", Jobs.Explain(e)));
                Dispatcher.UIThread.Post(() => { if (PriceCompare.Known(appId) is null) Render(null, text); });
            }
        });
    }
}
