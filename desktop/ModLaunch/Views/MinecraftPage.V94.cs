using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Minecraft;

namespace ModLaunch.Views;

/// <summary>
/// 9.4 — Minecraft как в Modrinth App: картинка у каждой сборки, экспорт в .mrpack, серверы с онлайном,
/// «Поделиться логом», обновление модпака, размер окна и своя Java.
/// </summary>
public sealed partial class MinecraftPage
{
    // ---------------------------------------------------------------- картинка сборки

    /// <summary>Картинка сборки: встроенная пиксельная, значок модпака или своя.</summary>
    public static Control InstanceIcon(McInstance i, double size, double? radius = null)
    {
        var icon = McIcons.Effective(i);
        var r = radius ?? size * 0.24;
        Control inner;
        if (icon.StartsWith("builtin:", StringComparison.Ordinal))
            inner = new Image { Source = Images.Asset(McIcons.AssetName(icon[8..]), (int)Math.Min(256, size * 2)), Stretch = Stretch.UniformToFill };
        else if (icon.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return Ui.Thumb(icon, i.Name, size, r, (int)Math.Min(256, size * 2));
        else
            return LocalImage(icon, size, size, r, (int)Math.Min(256, size * 2));
        RenderOptions.SetBitmapInterpolationMode(inner, BitmapInterpolationMode.MediumQuality);
        return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(r), ClipToBounds = true, Child = inner, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>Выбрать картинку: встроенные пиксельные, своя с диска или «как было».</summary>
    static void IconDialog(McInstance i)
    {
        var grid = new WrapPanel { ItemWidth = 74, ItemHeight = 74 };
        var current = McIcons.Effective(i);
        foreach (var name in McIcons.Builtin)
        {
            var n = name;
            var b = new Button
            {
                Classes = { "icon-pick" }, Padding = new Thickness(3), Margin = new Thickness(0, 0, 6, 6), Background = Brushes.Transparent,
                BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(16),
                BorderBrush = current == "builtin:" + n ? Ui.Res("Brand") : Brushes.Transparent,
                Content = new Border { Width = 58, Height = 58, CornerRadius = new CornerRadius(13), ClipToBounds = true, Child = new Image { Source = Images.Asset(McIcons.AssetName(n), 128), Stretch = Stretch.UniformToFill } },
            };
            b.Click += (_, _) => { McIcons.SetBuiltin(i, n); W.CloseDialog(); };
            grid.Children.Add(b);
        }
        var body = Ui.Col(14, Ui.Text(I18n.T("mc94.icon.text"), "small muted", wrap: true), grid);
        W.Dialog(I18n.T("mc94.icon.title", ("name", i.Name)), body, 560,
            Ui.Button(I18n.T("mc94.icon.reset"), () => { McIcons.Reset(i); W.CloseDialog(); }, "ghost", Icons.Undo),
            Ui.Button(I18n.T("mc94.icon.own"), async () =>
            {
                var file = await Pickers.Typed(W, I18n.T("mc94.icon.own"), ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif"], I18n.T("mc94.icon.files"));
                if (file is null) return;
                try { McIcons.SetCustom(i, file); W.CloseDialog(); }
                catch (Exception e) { W.Toast(Jobs.Explain(e), bad: true); }
            }, "primary", Icons.Image));
    }

    // ---------------------------------------------------------------- экспорт .mrpack

    static void ExportDialog(McInstance i)
    {
        var name = new TextBox { Text = i.Name };
        var version = new TextBox { Text = "1.0.0", Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
        var summary = new TextBox { Watermark = I18n.T("mc94.export.summary.hint") };
        CheckBox Box(string key, bool on) => new() { Content = I18n.T(key), IsChecked = on };
        var config = Box("mc94.export.config", true);
        var packs = Box("mc94.export.packs", true);
        var options = Box("mc94.export.options", true);
        var servers = Box("mc94.export.servers", true);
        var worlds = Box("mc94.export.worlds", false);
        Control Labeled(string key, Control c) => Ui.Col(6, Ui.Text(I18n.T(key), "small muted"), c);
        var body = Ui.Col(14,
            Ui.Text(I18n.T("mc94.export.text"), "small muted", wrap: true),
            Labeled("mc94.export.name", name),
            Labeled("mc94.export.version", version),
            Labeled("mc94.export.summary", summary),
            Ui.Col(6, Ui.Text(I18n.T("mc94.export.include"), "small muted"), config, packs, options, servers, worlds));
        W.Dialog(I18n.T("mc94.export.title"), body, 520,
            Ui.Button(I18n.T("common.cancel"), W.CloseDialog),
            Ui.Button(I18n.T("mc94.export.go"), async () =>
            {
                var title = (name.Text ?? "").Trim() == "" ? i.Name : name.Text!.Trim();
                var safe = string.Concat(title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                var target = await Pickers.SaveAny(W, I18n.T("mc94.export.title"), $"{safe} {version.Text}.mrpack", "mrpack", "Modrinth (.mrpack)");
                if (target is null) return;
                W.CloseDialog();
                var o = new McExport.Options(title, (version.Text ?? "").Trim() == "" ? "1.0.0" : version.Text!.Trim(), summary.Text ?? "",
                    config.IsChecked == true, packs.IsChecked == true, worlds.IsChecked == true, options.IsChecked == true, servers.IsChecked == true);
                Jobs.Run(title + ".mrpack", "Minecraft", async (_, progress, ct) =>
                {
                    var r = await McExport.Export(i, o, target, progress, ct);
                    Dispatcher.UIThread.Post(() => W.Toast(I18n.T("mc94.export.done", ("linked", r.Linked), ("bundled", r.Bundled), ("size", GamePage.Size(r.Bytes)))));
                });
                W.OpenDownloads();
            }, "primary", Icons.Upload));
    }

    // ---------------------------------------------------------------- обновление модпака

    /// <summary>Плашка «Вышла новая версия модпака» над модами сборки.</summary>
    Control? PackUpdateBanner(McInstance a)
    {
        if (McModrinth.PackUpdate(a) is not { } v) return null;
        var text = Ui.Col(3, Ui.Text(I18n.T("mc94.pack.title", ("version", v.Number)), "h3"), Ui.Text(I18n.T("mc94.pack.text"), "small muted", wrap: true));
        text.VerticalAlignment = VerticalAlignment.Center;
        var go = Ui.Button(I18n.T("mc94.pack.go"), () => UpdatePack(a, v), "primary", Icons.ArrowUp);
        go.VerticalAlignment = VerticalAlignment.Center;
        var dock = new DockPanel();
        DockPanel.SetDock(go, Dock.Right);
        dock.Children.Add(go);
        dock.Children.Add(Ui.Row(14, InstanceIcon(a, 40, 12), text));
        return new Border { Classes = { "card" }, Padding = new Thickness(16, 14), BorderBrush = Ui.Res("Brand"), BorderThickness = new Thickness(1), Child = dock };
    }

    void UpdatePack(McInstance a, Sources.MrVersion v)
    {
        if (Features.Launcher.IsRunning(Mc.Id)) { W.Toast(I18n.T("mine.worlds.closeGame"), bad: true); return; }
        Jobs.Run(I18n.T("mc94.pack.job", ("name", a.Name), ("version", v.Number)), "Minecraft", async (_, progress, ct) =>
        {
            await McModrinth.UpdatePack(a, v, progress, ct);
            Dispatcher.UIThread.Post(() => { W.Toast(I18n.T("mc94.pack.done", ("name", a.Name), ("version", v.Number))); Mc.Notify(); });
        });
        W.OpenDownloads();
    }

    // ---------------------------------------------------------------- серверы

    static readonly Dictionary<string, (McServerStatus Status, DateTime At)> Pinged = [];
    static readonly HashSet<string> Pinging = [];

    Control ServersView()
    {
        var a = Mc.Active;
        if (a is null) return NoBuildView();
        var col = Ui.Col(14);
        var head = new DockPanel();
        var buttons = Ui.Row(8,
            Ui.Button(I18n.T("mc94.servers.add"), () => AddServerDialog(a), "primary", Icons.Plus),
            Ui.Button("", () => { lock (Pinged) Pinged.Clear(); Build(); }, "icon ghost", Icons.Refresh, I18n.T("cr.refresh")));
        DockPanel.SetDock(buttons, Dock.Right);
        head.Children.Add(buttons);
        head.Children.Add(Ui.Col(3, Ui.Text(I18n.T("mc94.servers.title"), "h2"), Ui.Text(I18n.T("mc94.servers.text"), "small muted", wrap: true)));
        col.Children.Add(head);

        var servers = Program.Demo ? DemoServers() : McServers.List(a);
        if (servers.Count == 0)
        {
            col.Children.Add(Ui.Card(Ui.Col(8, Ui.Text(I18n.T("mc94.servers.none"), "h3"), Ui.Text(I18n.T("mc94.servers.none.text"), "muted", wrap: true)), 24));
            return col;
        }
        var list = Ui.Col(8);
        foreach (var s in servers) list.Children.Add(ServerRow(a, s));
        col.Children.Add(list);
        return col;
    }

    static List<McServer> DemoServers() =>
    [
        new("Выживание с друзьями", "mc.friends.example:25565", null),
        new("Hypixel", "mc.hypixel.net", null),
        new("Локальный сервер", "localhost", null),
    ];

    Control ServerRow(McInstance a, McServer s)
    {
        McServerStatus? status;
        lock (Pinged) status = Pinged.TryGetValue(s.Address, out var p) && DateTime.UtcNow - p.At < TimeSpan.FromMinutes(2) ? p.Status : null;
        if (Program.Demo)
            status = s.Address.Contains("hypixel") ? new McServerStatus(true, 31_204, 200_000, "Hypixel Network  [1.8-1.21]", "Requires MC 1.8 / 1.21", 64, null)
                : s.Address.StartsWith("mc.") ? new McServerStatus(true, 3, 20, "Добро пожаловать домой", "Fabric 1.21.1", 18, null)
                : new McServerStatus(false, 0, 0, "", "", -1, null);
        else if (status is null) _ = PingLater(s.Address);

        Control icon;
        var favicon = s.Icon ?? status?.Favicon;
        if (favicon is { Length: > 0 } && Decode(favicon) is { } bmp)
            icon = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = new Image { Source = bmp, Stretch = Stretch.UniformToFill } };
        else
            icon = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(10), Background = Ui.Res("Surface3"), Child = Ui.Icon(Icons.Globe, 22, Ui.Res("Muted")) };
        if (icon is Border ib) RenderOptions.SetBitmapInterpolationMode(ib, BitmapInterpolationMode.None);

        var words = Ui.Col(3, Ui.Text(s.Name == "" ? s.Address : s.Name, "h3"));
        string line;
        IBrush lineColor = Ui.Res("Muted");
        if (status is null) line = I18n.T("mc94.servers.pinging");
        else if (!status.Online) { line = I18n.T("mc94.servers.offline"); lineColor = Ui.Res("Faint"); }
        else line = status.Motd == "" ? status.Version : status.Motd;
        words.Children.Add(new TextBlock { Text = line, FontSize = 12.5, Foreground = lineColor, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
        words.Children.Add(Ui.Text(s.Address, "tiny muted"));
        words.VerticalAlignment = VerticalAlignment.Center;

        var right = Ui.Row(14);
        right.VerticalAlignment = VerticalAlignment.Center;
        if (status is { Online: true })
        {
            right.Children.Add(Ui.Col(1, Ui.Text(I18n.T("mc94.servers.players", ("n", I18n.Compact(status.Players)), ("max", I18n.Compact(status.Max))), "small"), Ui.Text(status.Version, "tiny muted")));
            var color = status.Ms < 0 ? Ui.Res("Muted") : status.Ms < 80 ? Ui.Res("Good") : status.Ms < 200 ? Ui.Res("Warn") : Ui.Res("Bad");
            right.Children.Add(Ui.Row(5, new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = color, VerticalAlignment = VerticalAlignment.Center }, Ui.Text(status.Ms < 0 ? "—" : $"{status.Ms} ms", "small")));
        }
        right.Children.Add(Ui.Button("", () => { _ = TopLevel.GetTopLevel(W)?.Clipboard?.SetTextAsync(s.Address); W.Toast(I18n.T("mc94.servers.copied", ("address", s.Address))); }, "icon ghost", Icons.Copy, I18n.T("mc94.servers.copy")));
        right.Children.Add(Ui.Button("", () =>
        {
            try { McServers.Remove(a, s); Build(); } catch (Exception e) { W.Toast(Jobs.Explain(e), bad: true); }
        }, "icon ghost", Icons.Trash, I18n.T("mc94.servers.remove")));

        var dock = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(right);
        dock.Children.Add(Ui.Row(14, icon, words));
        return Ctx.Attach(Ui.Card(dock, 12), () => Ctx.Menu(
            Ctx.Copy(I18n.T("mc94.servers.copy"), s.Address),
            Ctx.Item(I18n.T("mc94.servers.remove"), Icons.Trash, () => { try { McServers.Remove(a, s); Build(); } catch { } })));
    }

    static Bitmap? Decode(string dataUrl)
    {
        try
        {
            var comma = dataUrl.IndexOf(',');
            var bytes = Convert.FromBase64String(comma >= 0 ? dataUrl[(comma + 1)..] : dataUrl);
            using var ms = new MemoryStream(bytes);
            return new Bitmap(ms);
        }
        catch { return null; }
    }

    async Task PingLater(string address)
    {
        lock (Pinging) if (!Pinging.Add(address)) return;
        try
        {
            var status = await McServers.Ping(address);
            lock (Pinged) Pinged[address] = (status, DateTime.UtcNow);
        }
        finally { lock (Pinging) Pinging.Remove(address); }
        Dispatcher.UIThread.Post(() => { if (MainWindow.Current?.CurrentPage == this && _tab == "servers") Build(); });
    }

    void AddServerDialog(McInstance a)
    {
        var name = new TextBox { Watermark = I18n.T("mc94.servers.name.hint") };
        var address = new TextBox { Watermark = "play.example.net" };
        var body = Ui.Col(12,
            Ui.Col(6, Ui.Text(I18n.T("mc94.servers.name"), "small muted"), name),
            Ui.Col(6, Ui.Text(I18n.T("mc94.servers.address"), "small muted"), address),
            Ui.Text(I18n.T("mc94.servers.add.hint"), "small muted", wrap: true));
        W.Dialog(I18n.T("mc94.servers.add"), body,
            Ui.Button(I18n.T("common.cancel"), W.CloseDialog),
            Ui.Button(I18n.T("mc94.servers.add"), () =>
            {
                var addr = (address.Text ?? "").Trim();
                if (addr == "") return;
                try { McServers.Add(a, name.Text ?? "", addr); W.CloseDialog(); Build(); }
                catch (Exception e) { W.Toast(Jobs.Explain(e), bad: true); }
            }, "primary", Icons.Plus));
    }

    // ---------------------------------------------------------------- лог

    /// <summary>«Поделиться» — лог или отчёт о вылете на mclo.gs, ссылка в буфер обмена.</summary>
    static async void ShareLog(string file, Button? button = null)
    {
        if (button is not null) button.IsEnabled = false;
        try
        {
            var url = Program.Demo ? "https://mclo.gs/demo123" : await Features.LogShare.Upload(file);
            await (TopLevel.GetTopLevel(W)?.Clipboard?.SetTextAsync(url) ?? Task.CompletedTask);
            W.Toast(I18n.T("share.done", ("url", url)));
        }
        catch (Exception e) { W.Toast(I18n.T("share.failed", ("reason", Jobs.Explain(e))), bad: true); }
        finally { if (button is not null) button.IsEnabled = true; }
    }

    // ---------------------------------------------------------------- окно игры и Java

    Control WindowJavaCard(McInstance a)
    {
        int[][] sizes = [[0, 0], [1280, 720], [1600, 900], [1920, 1080], [2560, 1440]];
        var size = new ComboBox { Width = 220 };
        foreach (var s in sizes) size.Items.Add(s[0] == 0 ? I18n.T("mc94.window.auto") : $"{s[0]} × {s[1]}");
        size.SelectedIndex = Math.Max(0, Array.FindIndex(sizes, s => s[0] == a.Width && s[1] == a.Height));
        if (size.SelectedIndex == 0 && a.Width > 0) { size.Items.Add($"{a.Width} × {a.Height}"); size.SelectedIndex = size.Items.Count - 1; }
        size.SelectionChanged += (_, _) =>
        {
            if (size.SelectedIndex < 0 || size.SelectedIndex >= sizes.Length) return;
            (a.Width, a.Height) = (sizes[size.SelectedIndex][0], sizes[size.SelectedIndex][1]);
            Mc.Save(a);
            try { McProfiles.Sync(a); } catch { }
        };

        var java = new TextBox { Text = a.JavaPath, Watermark = I18n.T("mc94.java.auto"), IsReadOnly = true, Width = 420 };
        var pick = Ui.Button(I18n.T("mc94.java.pick"), async () =>
        {
            var file = await Pickers.Typed(W, I18n.T("mc94.java.pick"), ["javaw.exe", "java.exe", "*.exe"], "Java");
            if (file is null) return;
            var major = McInstall.JavaMajor(file);
            a.JavaPath = file;
            Mc.Save(a);
            try { McProfiles.Sync(a); } catch { }
            java.Text = file;
            var need = McInstall.JavaFor(a.GameVersion);
            W.Toast(major > 0 && major < need ? I18n.T("mc94.java.old", ("have", major), ("need", need)) : I18n.T("mc94.java.set", ("version", major > 0 ? major.ToString() : "?")), bad: major > 0 && major < need);
        }, "", Icons.Folder);
        var reset = Ui.Button("", () => { a.JavaPath = ""; Mc.Save(a); try { McProfiles.Sync(a); } catch { } java.Text = ""; }, "icon ghost", Icons.Undo, I18n.T("mc94.java.reset"));
        var need = McInstall.JavaFor(a.GameVersion);
        return Section(I18n.T("mc94.window.title"), I18n.T("mc94.window.text"),
            Field(I18n.T("mc94.window.size"), size),
            Field(I18n.T("mc94.java"), Ui.Row(8, java, pick, reset)),
            Ui.Text(I18n.T("mc94.java.hint", ("need", need), ("version", a.GameVersion)), "small muted", wrap: true));
    }

    /// <summary>Картинка сборки в настройках: показать, сменить.</summary>
    Control IconCard(McInstance a)
    {
        var row = Ui.Row(16, InstanceIcon(a, 72, 18),
            Ui.Col(8, Ui.Text(I18n.T("mc94.icon.card"), "small muted", wrap: true),
                Ui.Row(8, Ui.Button(I18n.T("mc94.icon.change"), () => IconDialog(a), "", Icons.Image),
                    Ui.Button(I18n.T("mc94.export.title"), () => ExportDialog(a), "ghost", Icons.Upload))));
        return Section(I18n.T("mc94.icon.section"), I18n.T("mc94.icon.section.text"), row);
    }

    // Для снимков экрана.
    internal static void ShotIconDialog(McInstance i) => IconDialog(i);
    internal static void ShotExportDialog(McInstance i) => ExportDialog(i);
}
