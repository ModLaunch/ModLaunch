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
/// 20.0: это нижняя полоса платформы, как в Steam: слева «Добавить игру» и состояние, посередине — загрузки,
/// справа — версия и «Друзья и чат». Настройка «Строка состояния» прячет только пункты состояния.
/// </summary>
public sealed class StatusBar : Border
{
    readonly StackPanel _left = Ui.Row(4);
    readonly StackPanel _right = Ui.Row(4);
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(30) };

    public StatusBar(Control? lead = null, Control? center = null, Control? trail = null)
    {
        Height = 42;
        Background = Ui.Res("Rail");
        BorderBrush = Ui.Res("Line");
        BorderThickness = new Thickness(0, 1, 0, 0);
        Padding = new Thickness(8, 0, 10, 0);
        _left.VerticalAlignment = _right.VerticalAlignment = VerticalAlignment.Center;
        var leftSide = Ui.Row(4);
        leftSide.VerticalAlignment = VerticalAlignment.Center;
        if (lead is not null) { leftSide.Children.Add(lead); leftSide.Children.Add(Dot()); }
        leftSide.Children.Add(_left);
        var rightSide = Ui.Row(8, _right);
        rightSide.VerticalAlignment = VerticalAlignment.Center;
        if (trail is not null) { trail.VerticalAlignment = VerticalAlignment.Center; rightSide.Children.Add(trail); }
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*") };
        // Узкое окно: пункты состояния не налезают на загрузки посередине — обрезаются, а второстепенные прячутся (Render).
        grid.Children.Add(new Border { Child = leftSide, ClipToBounds = true });
        if (center is not null)
        {
            center.HorizontalAlignment = HorizontalAlignment.Center;
            center.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(center, 1);
            grid.Children.Add(center);
        }
        rightSide.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(rightSide, 2);
        grid.Children.Add(rightSide);
        Child = grid;
        _tick.Tick += (_, _) => Render();
        SizeChanged += (_, e) => { if ((e.NewSize.Width < 1500) != (e.PreviousSize.Width < 1500) || (e.NewSize.Width < 1250) != (e.PreviousSize.Width < 1250)) Render(); };
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
        var on = Settings.Data.Bool("statusBar", true);
        _left.IsVisible = _right.IsVisible = on;
        if (!on) return;
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

        // Инжектор (в узком окне — только когда он что-то делает).
        var narrow = Bounds.Width > 0 && Bounds.Width < 1250;
        var live = AppState.Games.Count(g => Inject.Live(g.Def.Id));
        var count = Inject.All().Count(i => i.Enabled);
        var (injText, injTint) = !Inject.On ? (I18n.T("v11.sb.inj.off"), Ui.Res("Faint"))
            : live > 0 ? (I18n.T("v11.sb.inj.live"), Ui.Res("Brand2"))
            : (count > 0 ? I18n.T("v11.sb.inj.ready.n", ("n", count)) : I18n.T("v11.sb.inj.ready"), Ui.Res("Good"));
        if (!narrow || live > 0) _left.Children.Add(Item(Icons.Zap, injText, injTint, () => w?.Navigate(() => new CreatorPage("injects")), I18n.T("v11.sb.inj.tip")));

        // Бэкап в один клик.
        _left.Children.Add(Dot());
        var last = OneClickBackup.Last;
        var (bkText, bkTint) = OneClickBackup.Running ? (I18n.T("v11.sb.backup.running"), Ui.Res("Brand2"))
            : last is null ? (I18n.T("v11.sb.backup.never"), Ui.Res("Faint"))
            : last.Ok ? (I18n.T("v11.sb.backup.ago", ("ago", Ui.Ago(last.At))), Ui.Res("Good"))
            : (I18n.T("v11.sb.backup.fail"), Ui.Res("Warn"));
        _left.Children.Add(Item(Icons.Shield, bkText, bkTint, () => w?.Navigate(() => new SettingsPage("backups"))));
        if (!OneClickBackup.Running && !(Bounds.Width > 0 && Bounds.Width < 1500))
        {
            var b = new Button { Classes = { "ghost" }, Padding = new Thickness(8, 2), Height = 24, MinHeight = 0, VerticalAlignment = VerticalAlignment.Center,
                Content = new TextBlock { Text = I18n.T("v11.sb.backup.now"), FontSize = 11.5, FontWeight = FontWeight.SemiBold, Foreground = Ui.Res("Brand2") } };
            b.Click += (_, _) => BackupPanel.Quick();
            _left.Children.Add(b);
        }

        _right.Children.Add(Item(Icons.Info, "ModLaunch " + Http.Version, Ui.Res("Faint"), () => w?.Navigate(() => new SettingsPage("about"))));
    }
}
