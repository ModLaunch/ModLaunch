using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>
/// 25.0: «ModLaunch Premium» в аккаунте. Сверху — цена (юбилейная 299 ₽ навсегда вместо 499 ₽) и покупка
/// с кошелька, ниже — что даёт Premium, превью наград и расширенная статистика. Обещание крупно:
/// всё остальное в ModLaunch остаётся бесплатным.
/// </summary>
public sealed class PremiumPanel : UserControl
{
    static readonly IBrush Gold = new SolidColorBrush(Color.Parse("#F0B429"));
    long _price = Premium.Price;
    bool _busy;
    /// <summary>Цена лота уже прочитана — до этого «Купить» не нажать (в окне подтверждения была бы не та цена).</summary>
    bool _loaded = Program.Demo || Program.Screenshot;

    public PremiumPanel()
    {
        Render();
        Premium.Changed += OnChanged;
        DetachedFromVisualTree += (_, _) => Premium.Changed -= OnChanged;
        _ = Task.Run(async () =>
        {
            try { await Premium.Refresh(); _price = await Premium.CurrentPrice(); } catch { }
            _loaded = true;
            Dispatcher.UIThread.Post(Render);
        });
    }

    void OnChanged() => Dispatcher.UIThread.Post(Render);

    void Render()
    {
        var col = Ui.Col(18, Hero(), Ui.Text(I18n.T("v25.prem.free"), "muted", wrap: true), Perks(), Preview(), Stats());
        Content = col;
    }

    Control Hero()
    {
        var active = Premium.Active;
        var title = new TextBlock { Text = "ModLaunch Premium", FontSize = 30, FontWeight = FontWeight.Bold, Foreground = Brushes.White };
        var sub = new TextBlock { Text = I18n.T(active ? "v25.prem.thanks" : "v25.prem.sub"), FontSize = 14, Foreground = Ui.Hex("#FFF1D0"), TextWrapping = TextWrapping.Wrap, MaxWidth = 560 };
        var price = Ui.Row(12,
            new TextBlock { Text = Market.Money(_price), FontSize = 34, FontWeight = FontWeight.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Bottom },
            new TextBlock { Text = I18n.T("v25.prem.forever"), FontSize = 15, Foreground = Ui.Hex("#FFF1D0"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6) },
            new TextBlock { Text = Market.Money(Premium.RegularPrice), FontSize = 15, Foreground = Ui.Hex("#D9C79A"), TextDecorations = TextDecorations.Strikethrough, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6) });
        var tag = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)), CornerRadius = new CornerRadius(999), Padding = new Thickness(10, 3), HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = I18n.T("v25.prem.jubilee"), FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White },
        };
        var buttons = Ui.Row(10);
        if (active)
        {
            var since = Premium.Since is { } d ? I18n.T("v25.prem.since", ("date", d.ToLocalTime().ToString("d MMMM yyyy", I18n.Culture))) : I18n.T("v25.prem.on");
            buttons.Children.Add(new Border
            {
                Background = Brushes.White, CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 10),
                Child = Ui.Row(8, Ui.Icon(Icons.Check, 16, Ui.Hex("#8A6400")), new TextBlock { Text = since, FontWeight = FontWeight.SemiBold, Foreground = Ui.Hex("#2A1A00") }),
            });
            buttons.Children.Add(Ui.Button(I18n.T("v25.prem.wear"), () => { Premium.WearAll(); MainWindow.Current?.Toast(I18n.T("v25.prem.worn")); }, "hero-ghost", Icons.Palette));
        }
        else
        {
            var buy = Ui.Button(I18n.T("v25.prem.buy", ("price", Market.Money(_price))), Buy, "hero-light", Icons.Sparkles);
            buy.IsEnabled = !_busy && _loaded;
            buttons.Children.Add(buy);
            buttons.Children.Add(Ui.Button(I18n.T("v25.prem.topup"), () => MainWindow.Current?.Navigate(() => new AccountPage("wallet")), "hero-ghost", Icons.Bag));
        }
        var words = Ui.Col(10, tag, title, sub, price, buttons);
        var crown = new Border
        {
            Width = 120, Height = 120, CornerRadius = new CornerRadius(60), Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Child = Ui.Icon(Icons.Sparkles, 60, Brushes.White),
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(28, 24) };
        grid.Children.Add(words);
        Grid.SetColumn(crown, 1);
        grid.Children.Add(crown);
        return new Border
        {
            CornerRadius = new CornerRadius(20), ClipToBounds = true,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#B8860B"), 0), new GradientStop(Color.Parse("#F0B429"), 0.45), new GradientStop(Color.Parse("#5A2A82"), 1) },
            },
            Child = grid,
        };
    }

    async void Buy()
    {
        var w = MainWindow.Current;
        if (w is null || _busy || !_loaded) return;
        if (!Social.Account.SignedIn) { w.Navigate(() => new AccountPage()); return; }
        var price = _price;
        w.Dialog(I18n.T("v25.prem.confirm"), Ui.Text(I18n.T("v25.prem.confirm.text", ("price", Market.Money(price))), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("v25.prem.buy", ("price", Market.Money(price))), async () =>
            {
                w.CloseDialog();
                _busy = true;
                Render();
                try
                {
                    // Списывается ровно та цена, что была в окне: поменялась — покупка не пройдёт.
                    await Premium.Buy(price);
                    w.Toast(I18n.T("v25.prem.bought"));
                    Premium.WearAll();
                }
                catch (Social.ServiceError e) when (e.Code == "PRICE_CHANGED")
                {
                    if (long.TryParse(e.Message, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var now)) _price = now;
                    w.Toast(I18n.T("v25.prem.priceChanged", ("price", Market.Money(_price))), bad: true);
                }
                catch (Exception e)
                {
                    w.Toast(e is Social.ServiceError { Code: "PREMIUM_OFF" } ? I18n.T("v25.prem.off") : Market.Explain(e), bad: true);
                }
                _busy = false;
                Render();
            }, "primary", Icons.Sparkles));
        await Task.CompletedTask;
    }

    static Control Perks()
    {
        var grid = new UniformGrid { Columns = 3 };
        foreach (var (id, icon) in Premium.Perks)
        {
            var iconData = typeof(Icons).GetField(icon)?.GetValue(null) as string ?? Icons.Star;
            var card = new Border
            {
                Classes = { "card" }, Padding = new Thickness(16, 14), Margin = new Thickness(0, 0, 12, 12),
                Child = Ui.Col(8,
                    new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromArgb(40, 240, 180, 41)), HorizontalAlignment = HorizontalAlignment.Left, Child = Ui.Icon(iconData, 19, Gold) },
                    Ui.Text(I18n.T($"v25.prem.perk.{id}"), "h3", wrap: true),
                    Ui.Text(I18n.T($"v25.prem.perk.{id}.text"), "small muted", wrap: true)),
            };
            grid.Children.Add(card);
        }
        return Ui.Col(10, Ui.Text(I18n.T("v25.prem.perks"), "h2"), grid);
    }

    /// <summary>Как выглядят награды Premium: рамка, титул и баннер.</summary>
    static Control Preview()
    {
        var name = Social.Account.Get().Name ?? I18n.T("v93.feed.player");
        var banner = new Border { Height = 110, CornerRadius = new CornerRadius(16), ClipToBounds = true, Child = new Panel { Children = { ProfileKit.Banner("premium"),
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Margin = new Thickness(18), VerticalAlignment = VerticalAlignment.Center, Children =
            {
                ProfileKit.Avatar(name, 70, "premium", 4),
                Ui.Col(6, new TextBlock { Text = name, FontSize = 20, FontWeight = FontWeight.Bold, Foreground = Brushes.White }, ProfileKit.Title("premium")),
                ProfileKit.Badge("premium", 44),
            } } } } };
        return Ui.Card(Ui.Col(12, Ui.Text(I18n.T("v25.prem.preview"), "h2"), banner,
            Ui.Text(I18n.T("v25.prem.preview.text"), "small muted", wrap: true)), 20);
    }

    /// <summary>Расширенная статистика: с Premium — цифры, без него — что откроется.</summary>
    static Control Stats()
    {
        var active = Premium.Active;
        var games = AppState.Games.Where(g => g.Status == Detect.Found).ToList();
        var hours = games.Select(g => (g, h: PlayTime.Get(g.Def.Id).TotalMs / 3_600_000.0)).OrderByDescending(x => x.h).ToList();
        var top = hours.FirstOrDefault(x => x.h > 0);
        var cells = new (string Label, string Value)[]
        {
            (I18n.T("v25.stat.hours"), ((int)hours.Sum(x => x.h)).ToString(I18n.Culture)),
            (I18n.T("v25.stat.mods"), games.Sum(g => g.ModCount).ToString(I18n.Culture)),
            (I18n.T("v25.stat.crashes"), Premium.Counter("crashFixed").ToString(I18n.Culture)),
            (I18n.T("v25.stat.updated"), Premium.Counter("modsUpdated").ToString(I18n.Culture)),
            (I18n.T("v25.stat.shared"), Premium.Counter("codesShared").ToString(I18n.Culture)),
            (I18n.T("v25.stat.used"), Premium.Counter("codesUsed").ToString(I18n.Culture)),
        };
        var grid = new UniformGrid { Columns = 3 };
        foreach (var (label, value) in cells)
            grid.Children.Add(new Border
            {
                Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 12), Margin = new Thickness(0, 0, 10, 10),
                Child = Ui.Col(4, Ui.Text(active ? value : "•••", "h2"), Ui.Text(label, "small muted", wrap: true)),
            });
        var col = Ui.Col(12, Ui.Row(10, Ui.Text(I18n.T("v25.stat.title"), "h2"), active ? new Control() : ModRow.Tag("Premium", Ui.Soft("#3A2A12", "#F2B84B"), Gold)), grid);
        // Часы по играм — полосками (с Premium).
        if (active && hours.Any(x => x.h > 0))
        {
            var max = hours.Max(x => x.h);
            var bars = Ui.Col(8);
            foreach (var (g, h) in hours.Where(x => x.h > 0).Take(6))
            {
                var bar = new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = Gold, HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(6, 360 * h / max) };
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("160,*,Auto"), ColumnSpacing = 12 };
                var name = Ui.Text(g.Def.ShortName, "small");
                row.Children.Add(name);
                bar.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(bar, 1);
                row.Children.Add(bar);
                var val = Ui.Text(PlayTime.Format((long)(h * 3_600_000)), "small muted");
                Grid.SetColumn(val, 2);
                row.Children.Add(val);
                bars.Children.Add(row);
            }
            col.Children.Add(Ui.Text(I18n.T("v25.stat.byGame") + (top.g is not null ? " · " + I18n.T("v25.stat.top", ("game", top.g.Def.ShortName)) : ""), "h3"));
            col.Children.Add(bars);
        }
        else if (!active) col.Children.Add(Ui.Text(I18n.T("v25.stat.locked"), "small muted", wrap: true));
        return Ui.Card(col, 20);
    }
}
