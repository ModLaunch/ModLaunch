using System.IO.Compression;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Minecraft;

namespace ModLaunch.Views;

/// <summary>«Ещё»: миры сборки (с резервными копиями), скриншоты и лог игры с отчётами о вылетах.</summary>
public sealed partial class MinecraftPage
{
    bool _logRaw;

    /// <summary>Картинка с диска (значок мира, скриншот): декодируем в фоне, пока — заглушка.</summary>
    static Control LocalImage(string? path, double width, double height, double radius, int decode)
    {
        var image = new Image { Stretch = Stretch.UniformToFill };
        var host = new Border
        {
            Width = width, Height = height, CornerRadius = new CornerRadius(radius), ClipToBounds = true, Background = Ui.Res("Surface3"),
            Child = new Panel { Children = { Ui.Icon(Icons.Image, Math.Max(14, Math.Min(width, height) * 0.3), Ui.Res("Faint")), image } },
        };
        if (path is not null && File.Exists(path))
        {
            _ = Task.Run(() =>
            {
                try
                {
                    using var s = File.OpenRead(path);
                    var bmp = Bitmap.DecodeToWidth(s, decode);
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => image.Source = bmp);
                }
                catch { }
            });
        }
        return host;
    }

    // ---------------------------------------------------------------- миры

    Control WorldsView()
    {
        var a = Mc.Active;
        if (a is null) return NoBuildView();
        var saves = Path.Combine(a.Dir, "saves");
        var col = new StackPanel { Spacing = 12 };
        col.Children.Add(Ui.Row(10,
            Ui.Button(I18n.T("games.openFolder"), () => Actions.OpenFolder(Directory.CreateDirectory(saves).FullName), "", Icons.Folder),
            Ui.Button(I18n.T("mine.worlds.backups"), () => Actions.OpenFolder(Directory.CreateDirectory(BackupDir).FullName), "ghost", Icons.Save)));
        List<DirectoryInfo> worlds;
        try { worlds = Directory.Exists(saves) ? new DirectoryInfo(saves).EnumerateDirectories().Where(d => File.Exists(Path.Combine(d.FullName, "level.dat"))).OrderByDescending(d => d.LastWriteTimeUtc).ToList() : []; }
        catch { worlds = []; }
        if (worlds.Count == 0)
        {
            col.Children.Add(Ui.Card(Ui.Col(8, Ui.Text(I18n.T("mine.worlds.none"), "h3"), Ui.Text(I18n.T("mine.worlds.none.text"), "muted", wrap: true)), 24));
            return col;
        }
        var grid = new UniformGrid { Columns = 2 };
        foreach (var w in worlds) grid.Children.Add(WorldCard(a, w));
        col.Children.Add(grid);
        return col;
    }

    static string BackupDir => Path.Combine(Mc.Root, "ModLaunch", "backups");

    Control WorldCard(McInstance a, DirectoryInfo world)
    {
        var played = File.GetLastWriteTimeUtc(Path.Combine(world.FullName, "level.dat"));
        var datapacks = Directory.Exists(Path.Combine(world.FullName, "datapacks")) ? Directory.EnumerateFileSystemEntries(Path.Combine(world.FullName, "datapacks")).Count() : 0;
        var facts = I18n.T("mine.aside.played", ("ago", Ui.Ago(played)));
        if (datapacks > 0) facts += " · " + I18n.T("mine.worlds.datapacks", ("n", datapacks));
        var words = Ui.Col(3, new TextBlock { Text = world.Name, Classes = { "h3" }, TextTrimming = TextTrimming.CharacterEllipsis }, Ui.Text(facts, "small muted"));
        words.VerticalAlignment = VerticalAlignment.Center;
        var backup = Ui.Button("", () => Backup(world), "icon ghost", Icons.Save, I18n.T("mine.worlds.backup"));
        var open = Ui.Button("", () => Actions.OpenFolder(world.FullName), "icon ghost", Icons.Folder, I18n.T("games.openFolder"));
        var right = Ui.Row(4, backup, open);
        right.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 14 };
        grid.Children.Add(LocalImage(Path.Combine(world.FullName, "icon.png"), 56, 56, 12, 112));
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        var card = new Border { Classes = { "card" }, Padding = new Thickness(12), Margin = new Thickness(0, 0, 12, 12), Child = grid };
        return Ctx.Attach(card, () => Ctx.Menu(
            Ctx.Item(I18n.T("mine.worlds.backup"), Icons.Save, () => Backup(world)),
            Ctx.Folder(I18n.T("games.openFolder"), world.FullName),
            Ctx.Copy(I18n.T("ctx.copyName"), world.Name)));
    }

    /// <summary>Резервная копия мира: zip в .minecraft\ModLaunch\backups (игра должна быть закрыта — иначе файлы заняты).</summary>
    void Backup(DirectoryInfo world)
    {
        if (Launcher.IsRunning(Mc.Id)) { W.Toast(I18n.T("mine.worlds.closeGame"), bad: true); return; }
        var target = Path.Combine(BackupDir, $"{world.Name}-{DateTime.Now:yyyy-MM-dd_HH-mm}.zip");
        Jobs.Run(world.Name, "Minecraft", async (_, progress, ct) =>
        {
            progress.Report(new Mods.InstallStep("install.extract", world.Name));
            await Task.Run(() =>
            {
                Directory.CreateDirectory(BackupDir);
                if (File.Exists(target)) File.Delete(target);
                ZipFile.CreateFromDirectory(world.FullName, target, CompressionLevel.Fastest, includeBaseDirectory: true);
            }, ct);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => W.Toast(I18n.T("mine.worlds.backedUp", ("name", world.Name))));
        });
    }

    // ---------------------------------------------------------------- скриншоты

    Control ShotsView()
    {
        var a = Mc.Active;
        if (a is null) return NoBuildView();
        var dir = Path.Combine(a.Dir, "screenshots");
        var col = new StackPanel { Spacing = 12 };
        var hint = Ui.Text(I18n.T("mine.shots.hint"), "small muted");
        hint.VerticalAlignment = VerticalAlignment.Center;
        col.Children.Add(Ui.Row(14, Ui.Button(I18n.T("games.openFolder"), () => Actions.OpenFolder(Directory.CreateDirectory(dir).FullName), "", Icons.Folder), hint));
        List<FileInfo> shots;
        try { shots = Directory.Exists(dir) ? new DirectoryInfo(dir).EnumerateFiles("*.png").OrderByDescending(f => f.LastWriteTimeUtc).Take(60).ToList() : []; }
        catch { shots = []; }
        if (shots.Count == 0)
        {
            col.Children.Add(Ui.Card(Ui.Col(8, Ui.Text(I18n.T("mine.shots.none"), "h3"), Ui.Text(I18n.T("mine.shots.none.text"), "muted", wrap: true)), 24));
            return col;
        }
        var grid = new WrapPanel();
        foreach (var f in shots)
        {
            var file = f.FullName;
            var tile = new Button
            {
                Classes = { "tile" }, Padding = new Thickness(0), Margin = new Thickness(0, 0, 12, 12),
                Content = Ui.Col(0, LocalImage(file, 300, 169, 12, 600), Ui.Text(Ui.Ago(f.LastWriteTimeUtc), "tiny muted")),
            };
            ((StackPanel)tile.Content).Children[1].Margin = new Thickness(10, 6, 10, 8);
            tile.Click += (_, _) => Ui.OpenUrl(file);
            grid.Children.Add(Ctx.Attach(tile, () => Ctx.Menu(
                Ctx.Item(I18n.T("ctx.open"), Icons.Eye, () => Ui.OpenUrl(file)),
                Ctx.Folder(I18n.T("games.openFolder"), dir),
                Ctx.Copy(I18n.T("ctx.copyName"), Path.GetFileName(file)))));
        }
        col.Children.Add(grid);
        return col;
    }

    // ---------------------------------------------------------------- лог

    Control LogView()
    {
        var a = Mc.Active;
        if (a is null) return NoBuildView();
        var path = Path.Combine(a.Dir, "logs", "latest.log");
        var col = Ui.Col(14);
        var head = new DockPanel();
        var buttons = Ui.Row(8, Ui.Button(I18n.T("common.retry"), Build, "", Icons.Refresh));
        if (File.Exists(path)) buttons.Children.Add(Ui.Button(I18n.T("log.open"), () => Ui.OpenUrl(path), "", Icons.External));
        buttons.Children.Add(Ui.Button("", () => Actions.OpenFolder(Directory.CreateDirectory(Path.Combine(a.Dir, "logs")).FullName), "icon ghost", Icons.Folder, I18n.T("games.openFolder")));
        DockPanel.SetDock(buttons, Dock.Right);
        head.Children.Add(buttons);
        head.Children.Add(Ui.Text(I18n.T("log.title"), "h2"));
        col.Children.Add(head);

        // Отчёты о вылетах (crash-reports) — самые свежие сверху.
        var crashes = new List<FileInfo>();
        try
        {
            var dir = Path.Combine(a.Dir, "crash-reports");
            if (Directory.Exists(dir)) crashes = new DirectoryInfo(dir).EnumerateFiles("*.txt").OrderByDescending(f => f.LastWriteTimeUtc).Take(5).ToList();
        }
        catch { }
        if (crashes.Count > 0)
        {
            var list = Ui.Col(6, Ui.Text(I18n.T("mine.log.crashes"), "h3"));
            foreach (var c in crashes)
            {
                var file = c.FullName;
                var cause = CrashCause(file);
                var row = new DockPanel();
                var open = Ui.Button(I18n.T("ctx.open"), () => Ui.OpenUrl(file), "ghost", Icons.External);
                DockPanel.SetDock(open, Dock.Right);
                row.Children.Add(open);
                row.Children.Add(Ui.Col(2, Ui.Text(Ui.Ago(c.LastWriteTimeUtc) + " · " + c.Name, "small"), Ui.Text(cause, "small", color: Ui.Res("Bad"))));
                list.Children.Add(new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10), Child = row });
            }
            col.Children.Add(list);
        }

        if (!File.Exists(path))
        {
            col.Children.Add(Ui.Text(I18n.T("log.none"), "muted", wrap: true));
            return Ui.Card(col, 22);
        }
        col.Children.Add(Ui.Text(I18n.T("log.updated", ("time", File.GetLastWriteTime(path).ToString("g", I18n.Culture))) + " · " + path, "small muted", wrap: true));
        col.Children.Add(Ui.Row(6,
            Ui.Button(I18n.T("log.problems"), () => { _logRaw = false; Build(); }, _logRaw ? "chip" : "chip active"),
            Ui.Button(I18n.T("v4.log.raw"), () => { _logRaw = true; Build(); }, _logRaw ? "chip active" : "chip")));

        if (_logRaw)
        {
            var lines = Logs.Tail(path, 800);
            var text = new SelectableTextBlock
            {
                Text = string.Join("\n", lines), FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"), FontSize = 12,
                TextWrapping = TextWrapping.NoWrap, Foreground = Ui.Res("Muted"),
            };
            var viewer = new ScrollViewer { Content = text, Height = 460, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
            viewer.AttachedToVisualTree += (_, _) => viewer.ScrollToEnd();
            col.Children.Add(new Border { Background = Ui.Hex("#0B0D12"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Child = viewer });
            return Ui.Card(col, 22);
        }

        var issues = Logs.Parse(G.Def, string.Join("\n", Logs.Tail(path, 6000)));
        if (issues.Count == 0)
        {
            col.Children.Add(Ui.Row(10, Ui.Icon(Icons.Check, 18, Ui.Res("Good")), Ui.Text(I18n.T("log.clean"))));
            return Ui.Card(col, 22);
        }
        var mods = McContent.List(a, "mod");
        foreach (var issue in issues.Take(30))
        {
            var match = mods.FirstOrDefault(m => m.Enabled && (Same(m.Name, issue.Mod) || Same(Path.GetFileNameWithoutExtension(m.Key), issue.Mod)));
            var row = new DockPanel();
            if (match is not null)
            {
                var off = Ui.Button(I18n.T("mod.disable"), () => Toggle(a, match, false));
                DockPanel.SetDock(off, Dock.Right);
                row.Children.Add(off);
            }
            row.Children.Add(Ui.Col(4, Ui.Text(issue.Mod, "h3", color: Ui.Res("Bad")), new SelectableTextBlock { Text = issue.Message, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Res("Muted"), FontSize = 12, MaxHeight = 60 }));
            col.Children.Add(new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10), Child = row });
        }
        return Ui.Card(col, 22);
    }

    static bool Same(string a, string b)
    {
        static string N(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        var x = N(a);
        var y = N(b);
        return x.Length > 2 && y.Length > 2 && (x == y || x.StartsWith(y) || y.StartsWith(x));
    }

    /// <summary>«Description: …» из отчёта о вылете — первая понятная строка.</summary>
    static string CrashCause(string file)
    {
        try
        {
            foreach (var line in File.ReadLines(file).Take(40))
                if (line.StartsWith("Description:", StringComparison.OrdinalIgnoreCase)) return line[12..].Trim();
        }
        catch { }
        return I18n.T("mine.log.crash");
    }
}
