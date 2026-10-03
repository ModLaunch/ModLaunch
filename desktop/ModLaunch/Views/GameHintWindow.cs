using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>
/// Подсказка поверх игры, как у Steam («Shift+Tab — оверлей»): маленькое окно в углу
/// экрана через несколько секунд после запуска. Фокус у игры не забирает и само
/// закрывается. Показывается, только если оверлей включён и сочетание занято.
/// </summary>
public sealed class GameHintWindow : Window
{
    public GameHintWindow(string game, string key)
    {
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = Card(game, key);
        Opened += (_, _) => Place();
    }

    static Control Card(string game, string key)
    {
        var icon = new Border
        {
            Width = 38, Height = 38, CornerRadius = new CornerRadius(10), ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center,
            Child = new Image { Source = Images.Asset("icon.png", 96), Stretch = Stretch.UniformToFill },
        };
        var words = Ui.Col(2,
            new TextBlock { Text = "ModLaunch · " + game, FontSize = 12, Foreground = Ui.Hex("#AAB2C2") },
            new TextBlock { Text = I18n.T("hint.overlay", ("key", key)), FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White });
        words.VerticalAlignment = VerticalAlignment.Center;
        return new Border
        {
            Margin = new Thickness(14),
            Padding = new Thickness(16, 14, 20, 14),
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Color.Parse("#F2171A21")),
            BorderBrush = new SolidColorBrush(Color.Parse("#3A4150")),
            BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse("0 10 30 0 #80000000"),
            Child = Ui.Row(12, icon, words),
        };
    }

    void Place()
    {
        var screen = Screens.Primary;
        if (screen is null) return;
        var area = screen.WorkingArea;
        var s = screen.Scaling;
        Position = new PixelPoint(
            area.Right - (int)(Bounds.Width * s) - (int)(12 * s),
            area.Bottom - (int)(Bounds.Height * s) - (int)(12 * s));
    }

    /// <summary>Показать через несколько секунд после запуска игры и закрыть сама собой.</summary>
    public static void ShowFor(string game)
    {
        if (!OperatingSystem.IsWindows() || Program.Screenshot) return;
        if (!Settings.Data.Bool("overlay", true) || !Settings.Data.Bool("overlayHint", true) || Hotkey.Armed is not string armed) return;
        var key = Hotkey.Display(armed);
        DispatcherTimer.RunOnce(() =>
        {
            try
            {
                var w = new GameHintWindow(game, key);
                w.Show();
                DispatcherTimer.RunOnce(() => { try { w.Close(); } catch { } }, TimeSpan.FromSeconds(6));
            }
            catch { }
        }, TimeSpan.FromSeconds(4));
    }
}
