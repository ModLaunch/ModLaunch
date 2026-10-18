using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ModLaunch.Core;
using MX = ModScriptX;

namespace ModLaunch.Views;

/// <summary>
/// 9.7: ModScript X в Creator Hub — один файл .modx переводится в язык игры
/// (Python, JavaScript, C#, GDScript, Lua) и ставится через её загрузчик модов.
/// «Следить» ставит заново на каждое сохранение файла.
/// </summary>
public sealed partial class CreatorPage
{
    static string? _mxFile, _mxGame;
    static readonly List<string> _mxLog = [];
    static FileSystemWatcher? _mxWatcher;
    static DispatcherTimer? _mxDebounce;
    SelectableTextBlock? _mxOut;

    static readonly FontFamily Mono = new("Cascadia Mono, Consolas, monospace");

    Control Injector()
    {
        _mxFile ??= Settings.Data.Str("mxFile") ?? "";
        _mxGame ??= Settings.Data.Str("mxGame") ?? "";
        var file = new TextBox { Text = _mxFile, Watermark = I18n.T("v97.mx.file.hint"), FontFamily = Mono, MinWidth = 420 };
        file.LostFocus += (_, _) => Remember(file.Text ?? "", _mxGame);
        var fileRow = new DockPanel();
        var fileButtons = Ui.Row(8,
            Ui.Button(I18n.T("v97.mx.pick"), () => _ = PickModx(file), "", Icons.Folder),
            Ui.Button(I18n.T("v97.mx.example"), () => { file.Text = NewExample(); Remember(file.Text, _mxGame); OpenInEditor(file.Text); }, "", Icons.FilePlus),
            Ui.Button(I18n.T("v97.mx.edit"), () => OpenInEditor(file.Text ?? ""), "", Icons.Edit));
        fileButtons.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(fileButtons, Dock.Right);
        fileRow.Children.Add(fileButtons);
        fileRow.Children.Add(file);

        // Игра: найденные ModLaunch игры или любая папка.
        var games = new ComboBox { MinWidth = 420, PlaceholderText = I18n.T("v97.mx.game.hint") };
        foreach (var g in AppState.Games.Where(g => g.Path is not null && !g.Def.IsMinecraft).OrderBy(g => g.Def.Name))
            games.Items.Add(new ComboBoxItem { Content = g.Def.Name, Tag = g.Path });
        if (_mxGame != "" && !games.Items.OfType<ComboBoxItem>().Any(i => Same(i.Tag as string, _mxGame)))
            games.Items.Add(new ComboBoxItem { Content = _mxGame, Tag = _mxGame });
        games.SelectedItem = games.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Same(i.Tag as string, _mxGame));
        var engine = Ui.Text(EngineLine(_mxGame), "small muted");
        games.SelectionChanged += (_, _) =>
        {
            if (games.SelectedItem is ComboBoxItem { Tag: string dir }) { Remember(file.Text ?? "", dir); engine.Text = EngineLine(dir); }
        };
        var gameRow = Ui.Row(8, games, Ui.Button(I18n.T("v97.mx.folder"), () => _ = PickGame(games), "", Icons.Folder));

        var watch = Ui.Button(I18n.T(_mxWatcher is null ? "v97.mx.watch" : "v97.mx.unwatch"), () => { }, "", _mxWatcher is null ? Icons.Eye : Icons.Stop);
        watch.Click += (_, _) => { ToggleWatch(); Build(); };
        var actions = Ui.Row(8,
            Ui.Button(I18n.T("v97.mx.inject"), () => Run(Inject), "primary", Icons.Zap),
            watch,
            Ui.Button(I18n.T("v97.mx.inspect"), () => Run(Inspect), "", Icons.Search),
            Ui.Button(I18n.T("v97.mx.eject"), () => Run(Eject), "", Icons.Trash));

        _mxOut = new SelectableTextBlock { FontFamily = Mono, FontSize = 13, TextWrapping = TextWrapping.Wrap, Text = _mxLog.Count == 0 ? I18n.T("v97.mx.log.empty") : string.Join('\n', _mxLog) };
        var log = Ui.Card(new ScrollViewer { MaxHeight = 380, Content = _mxOut }, 16);

        return Ui.Col(16,
            Ui.Card(Ui.Col(6,
                Ui.Text("ModScript X", "h2"),
                Ui.Text(I18n.T("v97.mx.about"), "muted", wrap: true),
                Ui.Text(I18n.T("v97.mx.limits"), "small muted", wrap: true))),
            Ui.Card(Ui.Col(12,
                Ui.Text(I18n.T("v97.mx.step.file"), "h3"), fileRow,
                Ui.Text(I18n.T("v97.mx.step.game"), "h3"), gameRow, engine,
                actions)),
            log);
    }

    static bool Same(string? a, string? b) => a is not null && b is not null && string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    static string EngineLine(string dir)
    {
        if (dir == "" || !Directory.Exists(dir)) return I18n.T("v97.mx.engine.none");
        try
        {
            var t = MX.Engines.Detect(dir);
            return t is null ? I18n.T("v97.mx.engine.unknown") : I18n.T("v97.mx.engine", ("engine", t.Title), ("lang", t.Language));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return e.Message; }
    }

    static void Remember(string file, string game)
    {
        _mxFile = file.Trim().Trim('"');
        _mxGame = game;
        Settings.Data["mxFile"] = _mxFile;
        Settings.Data["mxGame"] = _mxGame;
        Settings.Save();
    }

    async Task PickModx(TextBox box)
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return;
        var type = new FilePickerFileType("ModScript X (.modx)") { Patterns = ["*" + MX.ModxDoc.Extension] };
        var result = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = I18n.T("v97.mx.pick"), AllowMultiple = false, FileTypeFilter = [type] });
        if (result.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        box.Text = path;
        Remember(path, _mxGame);
    }

    async Task PickGame(ComboBox games)
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return;
        if (await Pickers.Folder(top, I18n.T("v97.mx.folder")) is not { } dir) return;
        var item = games.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Same(i.Tag as string, dir));
        if (item is null) games.Items.Add(item = new ComboBoxItem { Content = dir, Tag = dir });
        games.SelectedItem = item;
    }

    /// <summary>Пример из комплекта — в «Документы\ModScript X», чтобы было с чего начать.</summary>
    static string NewExample()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var dir = Path.Combine(docs != "" ? docs : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ModScript X");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "gold-boost" + MX.ModxDoc.Extension);
        if (!File.Exists(path))
        {
            using var src = AssetLoader.Open(new Uri("avares://ModLaunch/Assets/modscriptx/gold-boost.modx"));
            using var dst = File.Create(path);
            src.CopyTo(dst);
        }
        return path;
    }

    static void OpenInEditor(string path)
    {
        if (!File.Exists(path)) { MainWindow.Current?.Toast(I18n.T("v97.mx.noFile"), bad: true); return; }
        try
        {
            // .modx — простой текст: Блокнот есть всегда, а редактор по умолчанию у .modx может быть не назначен.
            var psi = OperatingSystem.IsWindows()
                ? new System.Diagnostics.ProcessStartInfo("notepad.exe") { ArgumentList = { path } }
                : new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception e) { MainWindow.Current?.Toast(e.Message, bad: true); }
    }

    /// <summary>Действие с выбранным файлом и игрой — в фоне (сборка C# для Unity идёт долго), вывод — в журнал.</summary>
    void Run(Func<string, string, List<string>> action)
    {
        var (file, game) = (_mxFile ?? "", _mxGame ?? "");
        if (game == "" || !Directory.Exists(game)) { Log([I18n.T("v97.mx.noGame")]); return; }
        Log([$"[{DateTime.Now:HH:mm:ss}] …"]);
        Task.Run(() =>
        {
            List<string> lines;
            try { lines = action(file, game); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException) { lines = ["error: " + e.Message]; }
            Dispatcher.UIThread.Post(() => Log(lines));
        });
    }

    void Log(List<string> lines)
    {
        _mxLog.AddRange(lines);
        if (_mxLog.Count > 400) _mxLog.RemoveRange(0, _mxLog.Count - 400);
        if (_mxOut is not null) _mxOut.Text = string.Join('\n', _mxLog);
    }

    static List<string> Inject(string file, string game)
    {
        if (!File.Exists(file)) return [I18n.T("v97.mx.noFile")];
        var r = MX.Injector.Inject(File.ReadAllText(file), game, null);
        var lines = r.Doc.Diags.Select(d => $"{(d.Warning ? "warning" : "error")}{(d.Line > 0 ? " line " + d.Line : "")}: {d.Message}").ToList();
        if (r.Doc.Ok && r.Target is not null)
        {
            lines.Add($"{r.Doc.Name} → {r.Target.Title} ({r.Target.Language})");
            lines.AddRange(r.Files.Select(f => "  " + f));
            if (r.Note is not null) lines.Add(r.Note);
            lines.Add(I18n.T("v97.mx.done"));
        }
        return lines;
    }

    static List<string> Inspect(string file, string game)
    {
        var t = MX.Engines.Detect(game);
        if (t is null) return [I18n.T("v97.mx.engine.unknown")];
        var lines = MX.Engines.Inspect(game, t);
        if (MX.Engines.FindAntiCheat(game) is { } ac) lines.Add($"warning: {ac} — inject is disabled for this game");
        return lines;
    }

    static List<string> Eject(string file, string game)
    {
        var id = File.Exists(file) ? MX.ModxDoc.Parse(File.ReadAllText(file)).Id : "";
        var removed = id == "" ? [] : MX.Injector.Eject(id, game);
        return removed.Count == 0 ? [I18n.T("v97.mx.notInstalled")] : ["removed: " + string.Join(", ", removed)];
    }

    /// <summary>Живая правка: сохранили .modx — через 300 мс он уже стоит в игре заново.</summary>
    void ToggleWatch()
    {
        if (_mxWatcher is not null) { StopWatch(); Log([I18n.T("v97.mx.unwatched")]); return; }
        if (_mxFile is null || !File.Exists(_mxFile)) { Log([I18n.T("v97.mx.noFile")]); return; }
        var full = Path.GetFullPath(_mxFile);
        _mxWatcher = new FileSystemWatcher(Path.GetDirectoryName(full)!, Path.GetFileName(full)) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size, EnableRaisingEvents = true };
        _mxWatcher.Changed += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            _mxDebounce?.Stop();
            _mxDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _mxDebounce.Tick += (_, _) => { _mxDebounce!.Stop(); Run(Inject); };
            _mxDebounce.Start();
        });
        Log([I18n.T("v97.mx.watching")]);
        Run(Inject);
    }

    static void StopWatch()
    {
        _mxWatcher?.Dispose();
        _mxWatcher = null;
        _mxDebounce?.Stop();
    }
}
