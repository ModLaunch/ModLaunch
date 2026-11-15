using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Games;

namespace ModLaunch.Views;

/// <summary>
/// 30.2: «Место на дисках» — сколько занимают игры из всех лаунчеров, на каких дисках, что давно не запускали
/// и где это удалить. В Steam такое есть только для игр Steam.
/// </summary>
public sealed class StoragePage : Page
{
    public override string Title => I18n.T("v30.disk.title");

    string _drive = "all";
    CancellationTokenSource? _measure;
    DispatcherTimer? _redraw;

    public StoragePage()
    {
        void Changed() => Dispatcher.UIThread.Post(() =>
        {
            // Размеры приходят по одному — перерисовываем не чаще раза в секунду.
            if (_redraw is not null) return;
            _redraw = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _redraw.Tick += (_, _) => { _redraw?.Stop(); _redraw = null; if (MainWindow.Current?.CurrentPage == this) Build(); };
            _redraw.Start();
        });
        void Library() => Dispatcher.UIThread.Post(() => { if (MainWindow.Current?.CurrentPage == this) Build(); });
        AttachedToVisualTree += (_, _) =>
        {
            DiskUsage.Changed += Changed;
            AllGames.Changed += Library;
            _measure = new CancellationTokenSource();
            var token = _measure.Token;
            _ = Task.Run(() => DiskUsage.MeasureMissing(AllGames.Current ?? [], token));
        };
        DetachedFromVisualTree += (_, _) =>
        {
            DiskUsage.Changed -= Changed;
            AllGames.Changed -= Library;
            _measure?.Cancel();
        };
    }

    public override void Build()
    {
        if (AllGames.Current is null) _ = AllGames.Refresh();
        var spaces = DiskUsage.Games(AllGames.Current ?? []);
        var drives = DiskUsage.Drives(spaces);
        if (_drive != "all" && drives.All(d => d.Root != _drive)) _drive = "all";
        var shown = _drive == "all" ? spaces : spaces.Where(s => s.Drive == _drive).ToList();
        var counting = spaces.Count(s => s.Bytes < 0);
        var total = spaces.Where(s => s.Bytes > 0).Sum(s => s.Bytes);

        var body = Ui.Col(16,
            Ui.Col(4,
                Ui.Text(I18n.T("v30.disk.title"), "h1"),
                Ui.Text(I18n.T("v30.disk.lead", ("n", spaces.Count), ("size", Size(total))), "muted", wrap: true)));
        if (counting > 0) body.Children.Add(Ui.Text(I18n.T("v30.disk.counting", ("n", counting)), "small muted"));

        var grid = new UniformGrid { Columns = Math.Clamp(drives.Count, 1, 3) };
        foreach (var d in drives) grid.Children.Add(DriveCard(d));
        body.Children.Add(grid);

        // Давно не запускали — удалить их освободит больше всего.
        var idle = DiskUsage.Idle(shown, g => AllGames.Played(g).Last, DateTimeOffset.UtcNow);
        if (idle.Count > 0)
        {
            var list = Ui.Col(6);
            foreach (var s in idle.Take(5)) list.Children.Add(GameRow(s, compact: true));
            body.Children.Add(Ui.Card(Ui.Col(10,
                Ui.Row(10, Ui.Icon(Icons.Sparkles, 18, Ui.Res("Brand2")), Ui.Text(I18n.T("v30.disk.idle", ("size", Size(idle.Sum(s => s.Bytes)))), "h3")),
                Ui.Text(I18n.T("v30.disk.idle.text", ("n", idle.Count)), "small muted", wrap: true),
                list), 18));
        }

        if (drives.Count > 1)
        {
            var chips = new WrapPanel();
            void Chip(string id, string text)
            {
                var b = Ui.Button(text, () => { _drive = id; Build(); }, id == _drive ? "chip active" : "chip");
                b.Margin = new Thickness(0, 0, 4, 4);
                chips.Children.Add(b);
            }
            Chip("all", I18n.T("v27.lib.f.all"));
            foreach (var d in drives) Chip(d.Root, d.Root.TrimEnd('\\'));
            body.Children.Add(chips);
        }

        var rows = Ui.Col(6);
        foreach (var s in shown) rows.Children.Add(GameRow(s));
        if (shown.Count == 0) rows.Children.Add(Ui.Text(I18n.T("v30.disk.none"), "muted", wrap: true));
        body.Children.Add(rows);
        body.Children.Add(Ui.Text(I18n.T("v30.disk.note"), "tiny muted", wrap: true));

        body.Margin = new Thickness(34, 28);
        body.MaxWidth = 1040;
        body.HorizontalAlignment = HorizontalAlignment.Left;
        Content = new ScrollViewer { Content = body };
    }

    static string Size(long bytes) => bytes < 0 ? "…" : AllGamesPage.SizeText(bytes);

    static Control DriveCard(DriveSpace d)
    {
        var used = Math.Max(0, d.Total - d.Free);
        double Part(long v) => d.Total > 0 ? Math.Clamp((double)v / d.Total, 0, 1) : 0;
        var bar = new Grid { Height = 10, ClipToBounds = true };
        bar.Children.Add(new Border { Background = Ui.Res("Surface3"), CornerRadius = new CornerRadius(5), Opacity = 0.6 });
        var fill = new Grid { ColumnDefinitions = new ColumnDefinitions($"{Part(d.Games):0.####}*,{Part(used - d.Games):0.####}*,{Part(d.Free):0.####}*") };
        var games = new Border { Background = Ui.Res("Brand"), CornerRadius = new CornerRadius(5, 0, 0, 5) };
        var other = new Border { Background = Ui.Res("Muted"), Opacity = 0.85, CornerRadius = d.Games > 0 ? default : new CornerRadius(5, 0, 0, 5) };
        Grid.SetColumn(other, 1);
        fill.Children.Add(games);
        fill.Children.Add(other);
        bar.Children.Add(fill);
        var low = d.Total > 0 && d.Free < d.Total / 10;
        var title = d.Root.TrimEnd('\\') + (d.Label != "" ? " · " + d.Label : "");
        var card = Ui.Card(Ui.Col(8,
            Ui.Row(8, Ui.Icon(Icons.Folder, 16, Ui.Res("Muted")), Ui.Text(title, "h3")),
            bar,
            Ui.Text(I18n.T("v30.disk.free", ("free", Size(d.Free)), ("total", Size(d.Total))), "small", color: low ? Ui.Res("Warn") : null),
            Ui.Text(I18n.T("v30.disk.games", ("size", Size(d.Games))), "small muted")), 16);
        card.Margin = new Thickness(0, 0, 12, 0);
        return card;
    }

    Control GameRow(GameSpace s, bool compact = false)
    {
        var g = s.Game;
        var last = AllGames.Played(g).Last;
        var when = last > 0 ? I18n.T("v30.disk.last", ("when", Ui.Ago(DateTimeOffset.FromUnixTimeSeconds(last).UtcDateTime))) : I18n.T("v30.disk.never");
        var sub = string.Join(" · ", new[] { Stores.Title(g.Store), s.Drive.TrimEnd('\\'), when }.Where(x => x != ""));
        var buttons = Ui.Row(4);
        var folder = g.Source?.AltDir ?? g.Dir;
        if (folder is not null && Directory.Exists(folder)) buttons.Children.Add(Ui.Button("", () => Actions.OpenFolder(folder), "icon ghost", Icons.Folder, I18n.T("v14.steam.folder")));
        if (DiskUsage.UninstallUri(g) is { } uri)
            buttons.Children.Add(Ui.Button("", () => Uninstall(g, uri), "icon ghost", Icons.Trash, I18n.T("v30.disk.uninstall", ("store", Stores.Title(g.Store)))));
        buttons.Children.Add(Ui.Button("", () => MainWindow.Current?.Navigate(() => new AllGamesPage(g.Key)), "icon ghost", Icons.Forward, I18n.T("v30.disk.open")));
        buttons.VerticalAlignment = VerticalAlignment.Center;
        var size = Ui.Text(Size(s.Bytes), "h3");
        size.VerticalAlignment = VerticalAlignment.Center;
        size.Margin = new Thickness(12, 0, 8, 0);
        var line = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        line.Children.Add(buttons);
        DockPanel.SetDock(size, Dock.Right);
        line.Children.Add(size);
        line.Children.Add(Ui.Col(2, Ui.Text(g.Name, compact ? "" : "h3"), Ui.Text(sub, "small muted")));
        return compact ? line : Ui.Card(line, 12);
    }

    static void Uninstall(LibGame g, string uri)
    {
        if (MainWindow.Current is not { } w) return;
        w.Dialog(I18n.T("v30.disk.uninstall.title", ("name", g.Name)),
            Ui.Text(I18n.T(g.Store == "steam" ? "v30.disk.uninstall.steam" : "v30.disk.uninstall.other", ("store", Stores.Title(g.Store))), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("v30.disk.uninstall.go"), () =>
            {
                w.CloseDialog();
                if (Program.Screenshot) return;
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true }); }
                catch { w.Toast(I18n.T("v27.lib.noLauncher", ("store", Stores.Title(g.Store))), bad: true); }
            }, "primary", Icons.Trash));
    }
}
