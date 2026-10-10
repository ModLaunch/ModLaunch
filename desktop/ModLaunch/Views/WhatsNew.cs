using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>«Что нового»: один раз после обновления на новую версию — короткий список с кнопками «попробовать».</summary>
public static class WhatsNew
{
    static readonly (string Icon, string Key, Func<Page>? Open)[] Items =
    [
        (Icons.Home, "new.v93.home", () => new HomePage()),
        (Icons.Flame, "new.v93.feed", () => new HomePage(feed: true)),
        (Icons.Bag, "new.v93.market", () => new MarketPage()),
        (Icons.Play, "new.v93.gamefx", () => new LibraryPage()),
        (Icons.Sidebar, "new.v93.rail", null),
        (Icons.Home, "new.v92.store", () => new HomePage()),
        (Icons.Palette, "new.v92.styles", () => new SettingsPage("look")),
        (Icons.Gamepad, "new.v92.game", () => new LibraryPage()),
        (Icons.Sparkles, "new.v92.openfx", null),
        (Icons.Grid, "new.v91.home", () => new HomePage()),
        (Icons.Download, "new.v91.fx", null),
        (Icons.Creator, "new.v91.creator", () => new MarketPage()),
        (Icons.Sparkles, "new.v91.splash", () => new SettingsPage("interface")),
        (Icons.Palette, "new.v9.design", () => new SettingsPage("look")),
        (Icons.Shield, "new.v9.safety", null),
        (Icons.Cube, "new.v85.minecraft", () => new MinecraftPage()),
        (Icons.Layers, "new.v85.builds", () => new MinecraftPage("builds")),
        (Icons.Bag, "new.v85.catalog", () => new MinecraftPage("catalog")),
        (Icons.ArrowUp, "new.v85.updates", () => new MinecraftPage("mods")),
        (Icons.Sidebar, "new.v85.rail", null),
    ];

    /// <summary>«Настройки» первой найденной игры с модами.</summary>
    static Page ConfigPage()
    {
        var g = AppState.Games.FirstOrDefault(g => g.Status == Detect.Found && g.ModCount > 0) ?? AppState.Games.FirstOrDefault(g => g.Status == Detect.Found);
        return g is null ? new LibraryPage() : new GamePage(g.Def.Id, "config");
    }

    /// <summary>Показать, если эту версию ещё не видели (при самом первом запуске — молча запомнить).</summary>
    public static void MaybeShow()
    {
        if (Program.Screenshot) return;
        var seen = Settings.Data.Str("seenVersion");
        Settings.Data["seenVersion"] = Http.Version;
        Settings.Save();
        // 9.2: после обновления — сначала выбор оформления, потом «Что нового».
        var picker = StylePicker.ShouldShow;
        if (seen == Http.Version && !picker) return;
        // 9.1: сначала заставка при запуске, потом «Что нового».
        DispatcherTimer.RunOnce(() =>
        {
            if (picker) StylePicker.Show(seen == Http.Version ? null : Show);
            else Show();
        }, TimeSpan.FromSeconds(Splash.Enabled ? 2.6 : 1.2));
    }

    public static void Show()
    {
        var w = MainWindow.Current;
        if (w is null) return;
        var list = Ui.Col(10);
        foreach (var (icon, key, open) in Items)
        {
            var words = Ui.Col(2, Ui.Text(I18n.T(key), "h3"), Ui.Text(I18n.T(key + ".text"), "small muted", wrap: true));
            words.VerticalAlignment = VerticalAlignment.Center;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 14 };
            var badge = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(12), Background = Ui.Res("BrandSoft"), Child = Ui.Icon(icon, 19, Ui.Res("Brand2")), VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(badge);
            Grid.SetColumn(words, 1);
            row.Children.Add(words);
            if (open is not null)
            {
                var go = Ui.Button(I18n.T("new.try"), () => { w.CloseDialog(); w.Navigate(open); }, "ghost", Icons.Forward);
                go.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(go, 2);
                row.Children.Add(go);
            }
            list.Children.Add(row);
            Animate.From(row, "translateY(14px)", 420, 160 + list.Children.Count * 70, new Avalonia.Animation.Easings.CubicEaseOut());
        }
        var hero = new Border
        {
            CornerRadius = new CornerRadius(16), Padding = new Thickness(20, 16), Margin = new Thickness(0, 0, 0, 6),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse(Look.Accent), 0), new GradientStop(Look.Mix(Color.Parse(Look.Accent), Color.Parse("#111"), 0.55), 1) },
            },
            Child = Ui.Col(2,
                new TextBlock { Text = $"ModLaunch {Http.Version}", FontSize = 24, FontWeight = FontWeight.Bold, Foreground = Brushes.White },
                new TextBlock { Text = I18n.T("new.subtitle"), Foreground = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), TextWrapping = TextWrapping.Wrap }),
        };
        w.Dialog(I18n.T("new.title"), new ScrollViewer { MaxHeight = 560, Content = Ui.Col(12, hero, list) }, 620,
            Ui.Button(I18n.T("new.ok"), w.CloseDialog, "primary", Icons.Check));
    }
}
