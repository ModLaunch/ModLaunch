using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>
/// 11.0: строка состояния внизу окна, как у взрослых лаунчеров: сеть (и сам включившийся офлайн),
/// инжектор (готов, инжекты в игре или выключен), последний бэкап с кнопкой «в один клик» и версия.
/// Каждая часть — ссылка на свой раздел.
/// </summary>
public sealed class StatusBar : Border
{
    readonly StackPanel _left = Ui.Row(4);
    readonly StackPanel _right = Ui.Row(4);
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(30) };

    public StatusBar()
    {
        Height = 28;
        Background = Ui.Res("Rail");
        BorderBrush = Ui.Res("Line");
        BorderThickness = new Thickness(0, 1, 0, 0);
        Padding = new Thickness(8, 0);
        _left.VerticalAlignment = _right.VerticalAlignment = VerticalAlignment.Center;
        var dock = new DockPanel();
        DockPanel.SetDock(_right, Dock.Right);
        dock.Children.Add(_right);
        dock.Children.Add(_left);
        Child = dock;
        _tick.Tick += (_, _) => Render();
        AttachedToVisualTree += (_, _) =>
        {
            Offline.Changed += Later;
            Inject.Changed += Later;
            OneClickBackup.Changed += Later;
            AppState.Changed += Later;
            I18n.Changed += Later;
            _tick.Start();
            Render();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            Offline.Changed -= Later;
            Inject.Changed -= Later;
            OneClickBackup.Changed -= Later;
            AppState.Changed -= Later;
            I18n.Changed -= Later;
            _tick.Stop();
        };
        IsVisible = Settings.Data.Bool("statusBar", true);
    }

    void Later() => Dispatcher.UIThread.Post(Render);

    /// <summary>Пункт строки: значок и текст; с действием — кнопка-ссылка.</summary>
    static Control Item(string icon, string text, IBrush tint, Action? open, string? tip = null)
    {
        var row = Ui.Row(6, Ui.Icon(icon, 12, tint), new TextBlock { Text = text, FontSize = 11.5, Foreground = Ui.Res("Muted"), VerticalAlignment = VerticalAlignment.Center });
        Control c = row;
        if (open is not null)
        {
            var b = new Button { Classes = { "ghost" }, Padding = new Thickness(8, 2), MinHeight = 0, Height = 24, Content = row, VerticalAlignment = VerticalAlignment.Center };
            b.Click += (_, _) => open();
            c = b;
        }
        else row.Margin = new Thickness(8, 0);
        if (tip is not null) ToolTip.SetTip(c, tip);
        return c;
    }

    static Control Dot() => new Border { Width = 1, Height = 12, Background = Ui.Res("Line"), Margin = new Thickness(4, 0), VerticalAlignment = VerticalAlignment.Center };

    void Render()
    {
        IsVisible = Settings.Data.Bool("statusBar", true);
        if (!IsVisible) return;
        var w = MainWindow.Current;
        _left.Children.Clear();
        _right.Children.Clear();

        // Сеть.
        if (Offline.On)
        {
            var text = Offline.Manual ? I18n.T("v11.sb.offline.manual")
                : Offline.Since is { } since ? I18n.T("v11.sb.offline.auto", ("ago", Ui.Ago(since))) : I18n.T("v11.sb.offline.auto.now");
            _left.Children.Add(Item(Icons.WifiOff, text, Ui.Res("Warn"), () => w?.Navigate(() => new SettingsPage("general")), I18n.T("v11.sb.offline.tip")));
        }
        else _left.Children.Add(Item(Icons.Wifi, I18n.T("v11.sb.online"), Ui.Res("Good"), () => w?.Navigate(() => new SettingsPage("general"))));
        _left.Children.Add(Dot());

        // Инжектор.
        var live = AppState.Games.Count(g => Inject.Live(g.Def.Id));
        var count = Inject.All().Count(i => i.Enabled);
        var (injText, injTint) = !Inject.On ? (I18n.T("v11.sb.inj.off"), Ui.Res("Faint"))
            : live > 0 ? (I18n.T("v11.sb.inj.live"), Ui.Res("Brand2"))
            : (count > 0 ? I18n.T("v11.sb.inj.ready.n", ("n", count)) : I18n.T("v11.sb.inj.ready"), Ui.Res("Good"));
        _left.Children.Add(Item(Icons.Zap, injText, injTint, () => w?.Navigate(() => new CreatorPage("injects")), I18n.T("v11.sb.inj.tip")));

        // Бэкап в один клик.
        _left.Children.Add(Dot());
        var last = OneClickBackup.Last;
        var (bkText, bkTint) = OneClickBackup.Running ? (I18n.T("v11.sb.backup.running"), Ui.Res("Brand2"))
            : last is null ? (I18n.T("v11.sb.backup.never"), Ui.Res("Faint"))
            : last.Ok ? (I18n.T("v11.sb.backup.ago", ("ago", Ui.Ago(last.At))), Ui.Res("Good"))
            : (I18n.T("v11.sb.backup.fail"), Ui.Res("Warn"));
        _left.Children.Add(Item(Icons.Shield, bkText, bkTint, () => w?.Navigate(() => new SettingsPage("backups"))));
        if (!OneClickBackup.Running)
        {
            var b = new Button { Classes = { "ghost" }, Padding = new Thickness(8, 2), Height = 24, MinHeight = 0, VerticalAlignment = VerticalAlignment.Center,
                Content = new TextBlock { Text = I18n.T("v11.sb.backup.now"), FontSize = 11.5, FontWeight = FontWeight.SemiBold, Foreground = Ui.Res("Brand2") } };
            b.Click += (_, _) => BackupPanel.Quick();
            _left.Children.Add(b);
        }

        // 2.0: номер версии уже стоит у логотипа — здесь вместо него «Что нового».
        _right.Children.Add(Item(Icons.Sparkles, I18n.T("v2.sb.news", ("v", Http.Display)), Ui.Res("Faint"), WhatsNew.Show));
    }
}
