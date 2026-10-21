using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>
/// 11.0: раздел «Бэкапы» в настройках — бэкап в один клик. Сверху большая кнопка с прогрессом и строкой
/// о последнем бэкапе; ниже места сохранения карточками (эта папка, найденные облачные сервисы, своя
/// папка, облако ModLaunch), «с сохранениями игр» и сколько хранить, и бэкапы выбранного места:
/// восстановить, открыть папку, удалить. Панель перерисовывает себя сама (без прыжка страницы);
/// rebuild — перестроить весь раздел после восстановления (там же копии сохранений игр).
/// Quick — тот же бэкап с любого экрана, итог — всплывающим сообщением.
/// </summary>
public static class BackupPanel
{
    static double _progress;
    static ProgressBar? _bar;
    /// <summary>Последний показанный список: пока грузится свежий, видно прежний.</summary>
    static (string Target, List<BackupEntry>? Items, string? Error)? _shown;

    /// <summary>Цвет значка сервиса (свой у каждого, без логотипов).</summary>
    static readonly Dictionary<string, string> Tint = new()
    {
        ["local"] = "#2DD4BF", ["onedrive"] = "#0078D4", ["gdrive"] = "#1FA463", ["dropbox"] = "#0061FF",
        ["yandex"] = "#FC3F1D", ["mega"] = "#D9272E", ["icloud"] = "#3693F3", ["custom"] = "#A78BFA", ["cloud"] = "#F59E0B",
    };

    /// <summary>Раздел целиком. rebuild — перестроить страницу настроек (нужно только после восстановления).</summary>
    public static Control Build(Action rebuild)
    {
        var host = new ContentControl();
        void Render() => host.Content = Body(Render, rebuild);
        void OnChanged() => Render();
        host.AttachedToVisualTree += (_, _) => OneClickBackup.Changed += OnChanged;
        host.DetachedFromVisualTree += (_, _) => OneClickBackup.Changed -= OnChanged;
        Render();
        return host;
    }

    static Control Body(Action render, Action rebuild)
    {
        var targets = OneClickBackup.Targets();
        var current = targets.FirstOrDefault(t => t.Id == OneClickBackup.CurrentId) ?? targets[0];
        return Ui.Col(16, Hero(targets, current), Places(targets, current), Options(), History(current, render, rebuild));
    }

    // ---------------------------------------------------------------- кнопка

    static Control Hero(List<BackupTarget> targets, BackupTarget current)
    {
        var badge = new Border
        {
            Width = 56, Height = 56, CornerRadius = new CornerRadius(16), Background = Ui.Res("BrandSoft"), VerticalAlignment = VerticalAlignment.Top,
            Child = Ui.Icon(Icons.Shield, 26, Ui.Res("Brand2")),
        };
        var words = Ui.Col(6, Ui.Text(I18n.T("v11.bk.title"), "h2"), Ui.Text(I18n.T("v11.bk.lead"), "muted", wrap: true), LastLine(targets));
        words.VerticalAlignment = VerticalAlignment.Center;
        var run = Ui.Button(OneClickBackup.Running ? I18n.T("v11.bk.running") : I18n.T("v11.bk.run"), () => _ = Start(), "primary", Icons.Upload);
        run.Padding = new Thickness(24, 13);
        run.FontSize = 15;
        run.IsEnabled = !OneClickBackup.Running && current.Available;
        var where = Ui.Text(I18n.T("v11.bk.run.to", ("name", current.Title)), "small muted");
        var right = Ui.Col(8, run, where);
        right.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 18 };
        grid.Children.Add(badge);
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);
        grid.Children.Add(right);
        // В узкой колонке настроек кнопка уходит под текст, чтобы тот не сжимался в столбик.
        void Fit(bool narrow)
        {
            Grid.SetColumn(right, narrow ? 1 : 2);
            Grid.SetRow(right, narrow ? 1 : 0);
            right.Margin = new Thickness(0, narrow ? 14 : 0, 0, 0);
            right.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            where.HorizontalAlignment = right.HorizontalAlignment;
        }
        Fit(false);
        grid.SizeChanged += (_, e) => Fit(e.NewSize.Width < 640);
        var col = Ui.Col(16, grid);
        _bar = null;
        if (OneClickBackup.Running)
        {
            _bar = new ProgressBar { Minimum = 0, Maximum = 1, Value = _progress, Height = 6, MinHeight = 6 };
            col.Children.Add(_bar);
        }
        return Ui.Card(col, 24);
    }

    static Control LastLine(List<BackupTarget> targets)
    {
        if (OneClickBackup.Last is not { } last) return Ui.Text(I18n.T("v11.bk.last.none"), "small muted", wrap: true);
        if (!last.Ok) return Line(Icons.Alert, Ui.Res("Bad"), I18n.T("v11.bk.last.fail", ("when", When(last.At)), ("error", last.Error ?? "")));
        var where = targets.FirstOrDefault(t => t.Id == last.Target)?.Title ?? last.Target;
        return Line(Icons.Check, Ui.Res("Good"), I18n.T("v11.bk.last.ok", ("when", When(last.At)), ("where", where), ("size", GamePage.Size(last.Size))));
    }

    /// <summary>Значок и текст, который переносится по словам.</summary>
    static Control Line(string icon, IBrush color, string text)
    {
        var mark = Ui.Icon(icon, 14, color);
        mark.VerticalAlignment = VerticalAlignment.Top;
        mark.Margin = new Thickness(0, 2, 8, 0);
        var row = new DockPanel();
        DockPanel.SetDock(mark, Dock.Left);
        row.Children.Add(mark);
        row.Children.Add(Ui.Text(text, "small", wrap: true));
        return row;
    }

    /// <summary>«Сегодня в 14:32», «20 октября, 14:32», «3 марта 2025, 09:10».</summary>
    static string When(DateTime utc)
    {
        var at = utc.ToLocalTime();
        if (at.Date == DateTime.Today) return I18n.T("v11.bk.today", ("time", at.ToString("HH:mm", I18n.Culture)));
        return at.ToString(at.Year == DateTime.Today.Year ? "d MMMM, HH:mm" : "d MMMM yyyy, HH:mm", I18n.Culture);
    }

    /// <summary>С заглавной — для заголовка строки («Сегодня в 14:32»).</summary>
    static string Cap(string s) => s.Length == 0 ? s : char.ToUpper(s[0], I18n.Culture) + s[1..];

    static string TitleOf(string id) => OneClickBackup.Targets().FirstOrDefault(t => t.Id == id)?.Title ?? id;

    static async Task Start()
    {
        _progress = 0;
        Report(await OneClickBackup.Run(progress: new Progress<double>(Show)));
    }

    static void Show(double p)
    {
        _progress = p;
        if (_bar is not null) _bar.Value = p;
    }

    static void Report(BackupResult r)
    {
        var text = r.Ok ? I18n.T("v11.bk.done", ("where", TitleOf(r.Target)), ("size", GamePage.Size(r.Size))) : r.Error ?? "";
        if (r.Ok && r.Skipped > 0) text += " " + I18n.T("v11.bk.done.skipped", ("n", r.Skipped));
        MainWindow.Current?.Toast(text, bad: !r.Ok);
    }

    /// <summary>Бэкап одной кнопкой с любого экрана (кнопка в строке состояния): итог — всплывающим сообщением.</summary>
    public static async void Quick()
    {
        var w = MainWindow.Current;
        if (OneClickBackup.Running) { w?.Toast(I18n.T("v11.bk.err.running"), bad: true); return; }
        w?.Toast(I18n.T("v11.bk.quick", ("name", TitleOf(OneClickBackup.CurrentId))));
        // Прогресс — и в открытом разделе «Бэкапы», если он на экране.
        _progress = 0;
        Report(await OneClickBackup.Run(progress: new Progress<double>(Show)));
    }

    // ---------------------------------------------------------------- куда

    static Control Places(List<BackupTarget> targets, BackupTarget current)
    {
        // Сетка с высотой по строке (а не по самой высокой карточке): 3 колонки, в узкой колонке настроек — меньше.
        var grid = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        var cards = targets.Select(t => Place(t, t.Id == current.Id)).ToList();
        void Arrange(int cols)
        {
            grid.Children.Clear();
            grid.ColumnDefinitions.Clear();
            grid.RowDefinitions.Clear();
            for (var c = 0; c < cols; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            for (var r = 0; r < (cards.Count + cols - 1) / cols; r++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (var i = 0; i < cards.Count; i++)
            {
                Grid.SetColumn(cards[i], i % cols);
                Grid.SetRow(cards[i], i / cols);
                grid.Children.Add(cards[i]);
            }
        }
        Arrange(3);
        grid.SizeChanged += (_, e) =>
        {
            var cols = Math.Clamp((int)(e.NewSize.Width / 230), 1, 3);
            if (cols != grid.ColumnDefinitions.Count) Arrange(cols);
        };
        return Section(I18n.T("v11.bk.where"), I18n.T("v11.bk.where.hint"), null, grid);
    }

    static Control Place(BackupTarget t, bool chosen)
    {
        var hex = Tint.GetValueOrDefault(t.Id, "#94A3B8");
        var tint = Color.Parse(hex);
        var icon = new Border
        {
            Width = 42, Height = 42, CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(Color.FromArgb(48, tint.R, tint.G, tint.B)),
            Child = Ui.Icon(t.Kind switch { "local" => Icons.Save, "custom" => Icons.Folder, "cloud" => Icons.Shield, _ => Icons.Cloud }, 18, Ui.Ink(hex)),
        };
        var name = Ui.Col(4, Ui.Text(t.Title, "h3"), State(t, chosen));
        name.VerticalAlignment = VerticalAlignment.Center;
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
        top.Children.Add(icon);
        Grid.SetColumn(name, 1);
        top.Children.Add(name);

        var hint = Ui.Text(Hint(t), "small muted", wrap: true);
        hint.MaxLines = 2;
        hint.TextTrimming = TextTrimming.CharacterEllipsis;
        if (t.Folder is not null) ToolTip.SetTip(hint, t.Folder);
        var col = Ui.Col(10, top, hint);
        if (t.Kind == "custom")
            col.Children.Add(Ui.Button(I18n.T(t.Available ? "v11.bk.custom.change" : "v11.bk.custom.pick"), () => _ = PickCustom(), "ghost", Icons.Folder));
        else if (t.Kind == "cloud" && !t.Available && Social.Firebase.Configured)
            col.Children.Add(Ui.Button(I18n.T("aside.login"), () => MainWindow.Current?.Navigate(() => new AccountPage()), "ghost", Icons.User));

        var card = new Border
        {
            Padding = new Thickness(14, 12), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1.5),
            Background = chosen ? Ui.Res("BrandSoft") : Ui.Res("Surface2"), BorderBrush = chosen ? Ui.Res("Brand") : Ui.Res("Line"),
            Opacity = t.Available || t.Kind == "custom" ? 1 : 0.6,
            Child = col,
        };
        if (t.Available && !chosen)
        {
            card.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
            var id = t.Id;
            StoreKit.OnClick(card, () => OneClickBackup.Choose(id));
        }
        return card;
    }

    static Control State(BackupTarget t, bool chosen)
    {
        if (chosen) return Gx.Tag(I18n.T("v11.bk.state.chosen"), Ui.Res("Brand"), Ui.Res("OnBrand"), 11);
        var key = (t.Kind, t.Available) switch
        {
            ("local", _) => "v11.bk.state.here",
            ("service", true) => "v11.bk.state.found",
            ("service", false) => "v11.bk.state.missing",
            ("custom", true) => "v11.bk.state.set",
            ("custom", false) => t.Folder is null ? "v11.bk.state.unset" : "v11.bk.state.gone",
            (_, true) => "v11.bk.state.signedIn",
            _ => "v11.bk.state.signIn",
        };
        return StoreKit.Pill(I18n.T(key), t.Available ? Ui.Res("Good") : Ui.Res("Faint"), onArt: false);
    }

    /// <summary>Под названием: папка, если место есть; иначе — как его получить.</summary>
    static string Hint(BackupTarget t)
    {
        if (t.Kind == "cloud")
        {
            if (t.Available) return I18n.T("v11.bk.hint.cloud");
            if (Social.Firebase.Configured) return I18n.T("v11.bk.hint.cloud.signIn");
            // Настройка сервера — дело владельца; остальным — просто «недоступно».
            return I18n.T(Social.Account.Owner ? "v11.bk.hint.cloud.config" : "v11.bk.hint.cloud.off");
        }
        // Своя папка, которой сейчас нет (флешка вынута), — тоже путём: видно, какая.
        if (t.Folder is not null && (t.Available || t.Kind == "custom")) return Ui.ShortPath(t.Folder);
        return I18n.T("v11.bk.hint." + t.Id);
    }

    static async Task PickCustom()
    {
        if (MainWindow.Current is not { } w || await w.PickFolder(I18n.T("v11.bk.custom.pick")) is not { } dir) return;
        OneClickBackup.SetCustom(dir);
    }

    // ---------------------------------------------------------------- настройки

    static Control Options()
    {
        var saves = new ToggleSwitch { IsChecked = OneClickBackup.WithSaves, OnContent = "", OffContent = "", MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        saves.IsCheckedChanged += (_, _) => OneClickBackup.WithSaves = saves.IsChecked == true;
        var keep = new NumericUpDown { Minimum = 1, Maximum = 100, Value = OneClickBackup.Keep, Width = 140, FormatString = "0", Increment = 1, VerticalAlignment = VerticalAlignment.Center };
        keep.ValueChanged += (_, _) => OneClickBackup.Keep = (int)(keep.Value ?? 10);
        var inside = Ui.Text(I18n.T("v11.bk.inside"), "small muted", wrap: true);
        return Section(I18n.T("v11.bk.options"), null, null,
            Setting(I18n.T("v11.bk.saves"), I18n.T("v11.bk.saves.hint"), saves),
            Setting(I18n.T("v11.bk.keep"), I18n.T("v11.bk.keep.hint"), keep),
            Line(Icons.Lock, Ui.Res("Muted"), I18n.T("v11.bk.safe")),
            inside);
    }

    static Control Setting(string title, string hint, Control control)
    {
        var row = new DockPanel();
        DockPanel.SetDock(control, Dock.Right);
        row.Children.Add(control);
        var words = Ui.Col(3, Ui.Text(title, "h3"), Ui.Text(hint, "small muted", wrap: true));
        words.Margin = new Thickness(0, 0, 14, 0);
        row.Children.Add(words);
        return row;
    }

    /// <summary>Карточка раздела, как в настройках: метка, заголовок, пояснение, строки.</summary>
    static Control Section(string title, string? hint, Control? right, params Control[] rows)
    {
        var marker = new Border { Width = Look.Studio ? 0 : 4, Height = 20, CornerRadius = new CornerRadius(2), Background = Ui.Res("Brand"), VerticalAlignment = VerticalAlignment.Center, IsVisible = !Look.Studio };
        var head = new DockPanel();
        if (right is not null)
        {
            DockPanel.SetDock(right, Dock.Right);
            head.Children.Add(right);
        }
        head.Children.Add(Ui.Row(10, marker, Ui.Text(title, "h2")));
        var col = Ui.Col(14, head);
        if (hint is not null) col.Children.Add(Ui.Text(hint, "muted", wrap: true));
        foreach (var row in rows) col.Children.Add(row);
        return Ui.Card(col, 24);
    }

    // ---------------------------------------------------------------- список бэкапов

    static Control History(BackupTarget current, Action render, Action rebuild)
    {
        Control? open = null;
        // Своя папка может быть сетевой — её не проверяем здесь, чтобы не ждать сервер в потоке окна.
        if (current.Available && current.Folder is { } folder && (current.Kind == "custom" || Directory.Exists(folder)))
            open = Ui.Button(I18n.T("v11.bk.openFolder"), () => Actions.OpenFolder(folder), "ghost", Icons.Folder);
        var list = Ui.Col(8);
        if (!current.Available) list.Children.Add(Ui.Text(I18n.T("v11.bk.list.unavailable"), "muted", wrap: true));
        else
        {
            if (_shown is { } shown && shown.Target == current.Id) Fill(list, current, shown.Items, shown.Error, render, rebuild);
            else list.Children.Add(Ui.Row(10, new ProgressBar { IsIndeterminate = true, Width = 120, VerticalAlignment = VerticalAlignment.Center }, Ui.Text(I18n.T("common.loading"), "small muted")));
            _ = Load(current, list, render, rebuild);
        }
        return Section(I18n.T("v11.bk.list", ("name", current.Title)), null, open, list);
    }

    static async Task Load(BackupTarget t, StackPanel list, Action render, Action rebuild)
    {
        List<BackupEntry>? items = null;
        string? error = null;
        try { items = await OneClickBackup.List(t); }
        catch (Exception e) { error = OneClickBackup.Explain(e); }
        _shown = (t.Id, items, error);
        Fill(list, t, items, error, render, rebuild);
    }

    static void Fill(StackPanel list, BackupTarget t, List<BackupEntry>? items, string? error, Action render, Action rebuild)
    {
        list.Children.Clear();
        if (error is not null) list.Children.Add(Line(Icons.Alert, Ui.Res("Warn"), error));
        else if (items is not { Count: > 0 }) list.Children.Add(Ui.Text(I18n.T("v11.bk.list.empty"), "muted", wrap: true));
        else foreach (var e in items) list.Children.Add(Row(t, e, render, rebuild));
    }

    static Control Row(BackupTarget t, BackupEntry e, Action render, Action rebuild)
    {
        var tags = Ui.Row(6);
        if (e.Safety) tags.Children.Add(StoreKit.Pill(I18n.T("v11.bk.tag.safety"), onArt: false));
        if (e.Saves) tags.Children.Add(StoreKit.Pill(I18n.T("v11.bk.tag.saves"), onArt: false));
        var facts = new List<string> { GamePage.Size(e.Size) };
        if (e.Device != "") facts.Add(e.Device);
        if (e.Version != "") facts.Add("ModLaunch " + e.Version);
        var words = Ui.Col(3, Ui.Row(8, Ui.Text(Cap(When(e.At)), "h3"), tags), Ui.Text(string.Join(" · ", facts), "small muted"));
        words.VerticalAlignment = VerticalAlignment.Center;
        var restore = Ui.Button(I18n.T("v11.bk.restore"), () => ConfirmRestore(e, rebuild), "", Icons.Undo);
        restore.IsEnabled = !OneClickBackup.Running;
        var actions = Ui.Row(6, restore);
        if (e.File is not null) actions.Children.Add(Ui.Button("", () => Actions.OpenFolder(e.File), "icon ghost", Icons.Folder, I18n.T("v11.bk.openFolder")));
        actions.Children.Add(Ui.Button("", () => ConfirmDelete(t, e, render), "icon ghost", Icons.Trash, I18n.T("v11.bk.delete")));
        actions.VerticalAlignment = VerticalAlignment.Center;
        var row = new DockPanel();
        DockPanel.SetDock(actions, Dock.Right);
        row.Children.Add(actions);
        row.Children.Add(words);
        return new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10), Child = row };
    }

    // ---------------------------------------------------------------- восстановить, удалить

    static void ConfirmRestore(BackupEntry e, Action rebuild)
    {
        if (MainWindow.Current is not { } w) return;
        w.Dialog(I18n.T("v11.bk.restore.title", ("when", When(e.At))),
            Ui.Col(10, Ui.Text(I18n.T("v11.bk.restore.text"), "muted", wrap: true), Line(Icons.Shield, Ui.Res("Good"), I18n.T("v11.bk.restore.safe"))),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("v11.bk.restore"), () => { w.CloseDialog(); _ = Restore(e, rebuild); }, "primary", Icons.Undo));
    }

    static async Task Restore(BackupEntry e, Action rebuild)
    {
        _progress = 0;
        RestoreResult r;
        try { r = await OneClickBackup.Restore(e, new Progress<double>(Show)); }
        catch (Exception ex)
        {
            MainWindow.Current?.Toast(OneClickBackup.Explain(ex), bad: true);
            return;
        }
        // Копии сохранений из бэкапа видны в соседнем разделе — перестраиваем весь раздел.
        rebuild();
        Restored(r);
    }

    static void Restored(RestoreResult r)
    {
        if (MainWindow.Current is not { } w) return;
        var body = Ui.Col(10, Ui.Text(I18n.T("v11.bk.restored.text", ("n", r.Files)), "muted", wrap: true));
        if (r.ModLists > 0) body.Children.Add(Line(Icons.Package, Ui.Res("Brand2"), I18n.T("v11.bk.restored.mods", ("n", r.ModLists))));
        if (r.Saves > 0) body.Children.Add(Line(Icons.Save, Ui.Res("Brand2"), I18n.T("v11.bk.restored.saves", ("n", r.Saves))));
        if (r.Safety is not null) body.Children.Add(Line(Icons.Shield, Ui.Res("Good"), I18n.T("v11.bk.restored.safety", ("name", r.Safety))));
        if (!r.NeedsRestart)
        {
            w.Dialog(I18n.T("v11.bk.restored.title"), body, Ui.Button(I18n.T("common.close"), w.CloseDialog, "primary"));
            return;
        }
        body.Children.Add(Line(Icons.Refresh, Ui.Res("Warn"), I18n.T("v11.bk.restored.restart")));
        w.Dialog(I18n.T("v11.bk.restored.title"), body,
            Ui.Button(I18n.T("v11.bk.later"), w.CloseDialog),
            Ui.Button(I18n.T("v11.bk.restartNow"), Restart, "primary", Icons.Refresh));
    }

    /// <summary>
    /// Перезапуск: новый экземпляр стартует через пару секунд, когда этот уже закрыт (иначе он отдал бы
    /// себя открытому окну и вышел), и при старте подменяет отложенные файлы.
    /// </summary>
    static void Restart()
    {
        if (OperatingSystem.IsWindows() && Environment.ProcessPath is { } exe)
        {
            try
            {
                Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), $"/d /s /c \"ping 127.0.0.1 -n 4 >nul & start \"\" \"{exe}\"\"")
                { CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetTempPath() });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                MainWindow.Current?.Toast(I18n.T("v11.bk.restart.manual"), bad: true);
                return;
            }
        }
        MainWindow.Current?.Quit();
    }

    static void ConfirmDelete(BackupTarget t, BackupEntry e, Action render)
    {
        if (MainWindow.Current is not { } w) return;
        w.Dialog(I18n.T("v11.bk.delete.title"), Ui.Text(I18n.T("v11.bk.delete.text", ("when", When(e.At)), ("where", t.Title)), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("v11.bk.delete"), () => { w.CloseDialog(); _ = Delete(e, render); }, "primary", Icons.Trash));
    }

    static async Task Delete(BackupEntry e, Action render)
    {
        try { await OneClickBackup.Delete(e); }
        catch (Exception ex)
        {
            MainWindow.Current?.Toast(OneClickBackup.Explain(ex), bad: true);
            render();
            return;
        }
        MainWindow.Current?.Toast(I18n.T("v11.bk.deleted"));
    }

    // ---------------------------------------------------------------- снимки экрана

    /// <summary>Только для снимков: раздел «Бэкапы» отдельной страницей (в программе он — в настройках).</summary>
    sealed class ShotPage(double width = 1080) : Page
    {
        public override string Title => I18n.T("v11.bk.title");

        public override void Build()
        {
            var col = StoreKit.Column(spacing: 16, top: 22);
            col.MaxWidth = width;
            col.Children.Add(BackupPanel.Build(Build));
            Content = new ScrollViewer { Content = col, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        }
    }

    /// <summary>Снимки: панель с найденными сервисами и бэкапами, подтверждение восстановления, итог, английский.</summary>
    [DemoShots]
    static void BackupPanelShots(Shots s)
    {
        var w = s.Window;
        var root = Path.Combine(Path.GetTempPath(), "modlaunch-demo-backups-" + Environment.ProcessId);
        if (Directory.Exists(root)) Directory.Delete(root, true);
        string Dir(string name) => Directory.CreateDirectory(Path.Combine(root, name)).FullName;
        var local = Dir("Documents/ModLaunch/Backups");
        var onedrive = Dir("OneDrive/ModLaunch Backups");
        BackupTarget T(string id, string kind, bool on, string? folder = null) => new(id, kind, I18n.T("v11.bk.dest." + id), on, folder);
        void Demo() => OneClickBackup.DemoTargets =
        [
            T("local", "local", true, local), T("onedrive", "service", true, onedrive), T("gdrive", "service", false),
            T("dropbox", "service", true, Dir("Dropbox/ModLaunch Backups")), T("yandex", "service", false), T("mega", "service", false),
            T("icloud", "service", false), T("custom", "custom", false), T("cloud", "cloud", false),
        ];
        Demo();
        try
        {
            // Настоящие архивы из демо-папки данных: вчерашний, страховочный и с сохранениями.
            var saves = Path.Combine(Dir("saves"), "20261012-190000-manual.zip");
            File.WriteAllText(saves, "demo");
            var now = DateTime.Now;
            foreach (var (days, reason, withSaves) in new[] { (1, "manual", false), (4, "safety", false), (9, "manual", true) })
                OneClickBackup.Write(Paths.DataDir, Path.Combine(onedrive, $"ModLaunch-{now.AddDays(-days).AddHours(-3):yyyyMMdd-HHmmss}.zip"),
                    new Dictionary<string, System.Text.Json.Nodes.JsonObject>(), withSaves ? [("valheim", saves)] : [], reason);
            OneClickBackup.Choose("onedrive");
            var run = OneClickBackup.Run();
            while (!run.IsCompleted) s.Pump(100);
            _shown = null;

            w.Navigate(() => new ShotPage());
            s.Pump(1200);
            s.Save("v11-bk-1-panel");
            if (Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(w).OfType<ShotPage>().FirstOrDefault()?.Content is ScrollViewer sv)
            {
                sv.Offset = new Vector(0, 640);
                s.Pump(500);
                s.Save("v11-bk-2-list");
            }
            var entry = OneClickBackup.ListFolder("onedrive", onedrive)[1];
            ConfirmRestore(entry, () => { });
            s.Pump(500);
            s.Save("v11-bk-3-restore");
            w.CloseDialog();
            Restored(new RestoreResult(14, ["settings.json", "profiles.json", "playtime.json"], 3, 0, "ModLaunch-20261020-143200.zip"));
            s.Pump(500);
            s.Save("v11-bk-4-restored");
            w.CloseDialog();

            I18n.Set("en");
            Demo();
            _shown = null;
            w.Navigate(() => new ShotPage());
            s.Pump(1200);
            s.Save("v11-bk-5-en");
            // Шириной в колонку раздела настроек: карточки мест встают в две колонки.
            w.Navigate(() => new ShotPage(560));
            s.Pump(1200);
            s.Save("v11-bk-6-column");
        }
        finally
        {
            OneClickBackup.DemoTargets = null;
            _shown = null;
            Settings.Data.Remove("bkDest");
            Settings.Data.Remove("bkLast");
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }
}
