using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>
/// 20.0: точка состояния в шапке вместо строки внизу окна. Одной фразой — «Всё работает», «Офлайн» или
/// «Бэкап не удался»; по нажатию — подробности (сеть, инжектор, последний бэкап и «Сделать бэкап», версия),
/// каждая строка ведёт в свой раздел.
/// </summary>
public sealed class StatusPill : Button
{
    readonly Border _dot = new() { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _text = new() { FontSize = 12.5, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center };
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(30) };

    public StatusPill()
    {
        Classes.Add("status20");
        Height = 34;
        CornerRadius = new CornerRadius(999);
        Padding = new Thickness(12, 0);
        VerticalAlignment = VerticalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;
        Content = Ui.Row(8, _dot, _text);
        Click += (_, _) => Details().ShowAt(this, true);
        _tick.Tick += (_, _) => Render();
        AttachedToVisualTree += (_, _) =>
        {
            Offline.Changed += Later;
            Inject.Changed += Later;
            OneClickBackup.Changed += Later;
            I18n.Changed += Later;
            _tick.Start();
            Render();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            Offline.Changed -= Later;
            Inject.Changed -= Later;
            OneClickBackup.Changed -= Later;
            I18n.Changed -= Later;
            _tick.Stop();
        };
    }

    void Later() => Dispatcher.UIThread.Post(Render);

    /// <summary>Главное одной фразой: что-то не так — это, иначе «Всё работает».</summary>
    public void Render()
    {
        var last = OneClickBackup.Last;
        var (text, color) = Offline.On ? (I18n.T("v20.status.offline"), Ui.Res("Warn"))
            : last is { Ok: false } && !OneClickBackup.Running ? (I18n.T("v20.status.backupFail"), Ui.Res("Warn"))
            : (I18n.T("v20.status.ok"), Ui.Res("Good"));
        _text.Text = text;
        _text.Foreground = Ui.Res("Muted");
        _dot.Background = color;
        ToolTip.SetTip(this, I18n.T("v20.status.title"));
    }

    /// <summary>Подробности: те же пункты, что были в строке состояния, — столбиком.</summary>
    Flyout Details()
    {
        var w = MainWindow.Current;
        var col = Ui.Col(4);
        Flyout? fly = null;
        void Row(string icon, string text, IBrush tint, Action? open)
        {
            var row = Ui.Row(10, Ui.Icon(icon, 15, tint), new TextBlock { Text = text, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 250 });
            var b = new Button { Classes = { "ghost" }, Padding = new Thickness(10, 8), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Content = row };
            b.Click += (_, _) => { fly?.Hide(); open?.Invoke(); };
            col.Children.Add(b);
        }
        col.Children.Add(new TextBlock { Text = I18n.T("v20.status.title"), FontWeight = FontWeight.Bold, FontSize = 15, Margin = new Thickness(10, 4, 10, 6) });

        if (Offline.On)
        {
            var text = Offline.Manual ? I18n.T("v11.sb.offline.manual")
                : Offline.Since is { } since ? I18n.T("v11.sb.offline.auto", ("ago", Ui.Ago(since))) : I18n.T("v11.sb.offline.auto.now");
            Row(Icons.WifiOff, text, Ui.Res("Warn"), () => w?.Navigate(() => new SettingsPage("general")));
        }
        else Row(Icons.Wifi, I18n.T("v11.sb.online"), Ui.Res("Good"), () => w?.Navigate(() => new SettingsPage("general")));

        var live = AppState.Games.Count(g => Inject.Live(g.Def.Id));
        var count = Inject.All().Count(i => i.Enabled);
        var (injText, injTint) = !Inject.On ? (I18n.T("v11.sb.inj.off"), Ui.Res("Faint"))
            : live > 0 ? (I18n.T("v11.sb.inj.live"), Ui.Res("Brand2"))
            : (count > 0 ? I18n.T("v11.sb.inj.ready.n", ("n", count)) : I18n.T("v11.sb.inj.ready"), Ui.Res("Good"));
        Row(Icons.Zap, injText, injTint, () => w?.Navigate(() => new CreatorPage("injects")));

        var last = OneClickBackup.Last;
        var (bkText, bkTint) = OneClickBackup.Running ? (I18n.T("v11.sb.backup.running"), Ui.Res("Brand2"))
            : last is null ? (I18n.T("v11.sb.backup.never"), Ui.Res("Faint"))
            : last.Ok ? (I18n.T("v11.sb.backup.ago", ("ago", Ui.Ago(last.At))), Ui.Res("Good"))
            : (I18n.T("v11.sb.backup.fail"), Ui.Res("Warn"));
        Row(Icons.Shield, bkText, bkTint, () => w?.Navigate(() => new SettingsPage("backups")));
        if (!OneClickBackup.Running)
        {
            var now = Ui.Button(I18n.T("v11.sb.backup.now"), () => { fly?.Hide(); BackupPanel.Quick(); }, "", Icons.Shield);
            now.HorizontalAlignment = HorizontalAlignment.Stretch;
            now.HorizontalContentAlignment = HorizontalAlignment.Center;
            now.Margin = new Thickness(10, 4, 10, 4);
            col.Children.Add(now);
        }
        col.Children.Add(new Border { Height = 1, Background = Ui.Res("Line"), Margin = new Thickness(10, 6) });
        Row(Icons.Info, "ModLaunch " + Http.Version, Ui.Res("Faint"), () => w?.Navigate(() => new SettingsPage("about")));
        fly = new Flyout { Content = new Border { Width = 300, Padding = new Thickness(6), Child = col }, Placement = PlacementMode.BottomEdgeAlignedRight };
        return fly;
    }
}
