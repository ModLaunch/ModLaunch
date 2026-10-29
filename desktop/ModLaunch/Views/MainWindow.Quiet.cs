using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// 2.0: «Не беспокоить в игре». Пока идёт игра, обычные всплывашки (скачалось, обновилось, друг в сети) не
/// мелькают за окном игры и не пропадают зря — они копятся, а после выхода из игры приходит одна карточка
/// «Пока вы играли» со всем списком. Ошибки показываются сразу. Выключается в «Настройки → Запуск».
/// </summary>
public sealed partial class MainWindow
{
    readonly List<string> _whilePlaying = [];

    internal static bool QuietInGame => Settings.Data.Bool("quietInGame", true);

    bool HoldWhilePlaying(string text, bool bad)
    {
        if (bad || Program.Screenshot || !QuietInGame || !Features.Launcher.AnyRunning()) return false;
        if (!_whilePlaying.Contains(text)) _whilePlaying.Add(text);
        return true;
    }

    void ShowWhilePlaying()
    {
        if (_whilePlaying.Count == 0 || Features.Launcher.AnyRunning()) return;
        var items = _whilePlaying.ToList();
        _whilePlaying.Clear();
        ShowDigest(items);
    }

    /// <summary>Карточка «Пока вы играли»: до пяти строк и «ещё N»; держится дольше обычной всплывашки.</summary>
    internal void ShowDigest(IReadOnlyList<string> items)
    {
        var lines = Ui.Col(Phi.S5);
        foreach (var line in items.Take(5))
            lines.Children.Add(Ui.Row(Phi.S8, new Border { Width = 5, Height = 5, CornerRadius = new CornerRadius(3), Background = Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = line, FontSize = 12.5, Foreground = Ui.Res("Muted"), TextWrapping = TextWrapping.Wrap, MaxWidth = 340 }));
        if (items.Count > 5) lines.Children.Add(Ui.Text(I18n.T("v2.quiet.more", ("n", items.Count - 5)), "caption"));
        var head = Ui.Row(Phi.S8, Ui.Icon(Icons.Bell, 15, Ui.Res("Brand2")), new TextBlock { Text = I18n.T("v2.quiet.title", ("n", items.Count)), FontSize = 14, FontWeight = FontWeight.SemiBold });
        var card = new Border
        {
            Background = Ui.Res("Surface3"), BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(Phi.R13),
            Padding = new Thickness(Phi.S21 - 5, Phi.S13), MaxWidth = 420, BoxShadow = BoxShadows.Parse("0 10 30 0 #66000000"),
            Child = Ui.Col(Phi.S8 + 2, head, lines),
        };
        _toasts.Children.Add(card);
        Animate.ToastIn(card);
        DispatcherTimer.RunOnce(() => Animate.ToastOut(card, () => _toasts.Children.Remove(card)), TimeSpan.FromSeconds(Math.Clamp(4 + items.Count * 2, 8, 16)));
    }
}
