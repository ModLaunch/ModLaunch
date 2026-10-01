using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Games;

namespace ModLaunch.Views;

/// <summary>Раздел «Проекты C#»: мастер создаёт готовый проект под загрузчик игры, кнопка «Собрать» кладёт мод в игру.</summary>
public sealed partial class CreatorPage
{
    CodeProject? _codeOpen;
    bool _wizard;
    string _wName = "", _wGame = "", _wAuthor = Settings.Data.Str("crAuthor") ?? "";
    readonly StringBuilder _log = new();
    SelectableTextBlock? _logBox;
    bool _building;

    // ---------------------------------------------------------------- общие кусочки форм

    static TextBox Box(string value, string hint, bool multi = false, int max = 200) => new()
    {
        Text = value, Watermark = hint, AcceptsReturn = multi, TextWrapping = multi ? TextWrapping.Wrap : TextWrapping.NoWrap,
        MinHeight = multi ? 90 : 0, MaxLength = max, VerticalContentAlignment = multi ? VerticalAlignment.Top : VerticalAlignment.Center,
    };

    static Control Field(string label, Control input) => Ui.Col(5, Ui.Text(label, "small muted"), input);

    static FontFamily Mono => new("Cascadia Mono, Consolas, monospace");

    /// <summary>Игры плиткой-чипами: с обложкой и названием; выбранная подсвечена.</summary>
    static Control GamePicker(string selected, Action<string> pick, Func<GameDef, bool>? filter = null)
    {
        var wrap = new WrapPanel();
        foreach (var g in AppState.Games.Where(g => filter?.Invoke(g.Def) ?? true))
        {
            var id = g.Def.Id;
            var chip = new Button
            {
                Classes = { "chip" }, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(10, 6),
                Content = Ui.Row(8, Ui.Thumb(g.Def.ArtUrl, g.Def.Name, 22, 6), new TextBlock { Text = g.Def.ShortName, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 }),
            };
            if (selected == id) chip.Classes.Add("active");
            chip.Click += (_, _) => pick(id);
            wrap.Children.Add(chip);
        }
        return wrap;
    }

    /// <summary>Строка проверки: цветная точка и текст, который переносится, а не уходит за край карточки.</summary>
    static Control Dotted(bool ok, string text, bool warn = false)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
        var dot = Ui.Dot(ok ? Ui.Res("Good") : warn ? Ui.Res("Warn") : Ui.Res("Bad"), 9);
        dot.VerticalAlignment = VerticalAlignment.Top;
        dot.Margin = new Thickness(0, 5, 0, 0);
        row.Children.Add(dot);
        var label = Ui.Text(text, "small", wrap: true);
        Grid.SetColumn(label, 1);
        row.Children.Add(label);
        return row;
    }

    async void Copy(string text, string toast)
    {
        try { if (TopLevel.GetTopLevel(this)?.Clipboard is { } clip) await clip.SetTextAsync(text); MainWindow.Current?.Toast(toast); }
        catch { }
    }

    // ---------------------------------------------------------------- список и мастер

    Control CodeView()
    {
        if (_codeOpen is not null && Directory.Exists(_codeOpen.Dir)) return CodeDetail(_codeOpen);
        _codeOpen = null;
        var col = new StackPanel { Spacing = 16 };
        var projects = CodeProjects.List().Where(p => _filter == "" || p.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase)).ToList();

        var bar = new DockPanel();
        var right = Ui.Row(8, Ui.Button(I18n.T("cr.folder"), () => Actions.OpenFolder(CodeProjects.Root), "ghost", Icons.Folder),
            Ui.Button(I18n.T("cr.code.new"), () => { _wizard = !_wizard; Build(); }, "primary", Icons.Plus));
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(right);
        bar.Children.Add(Ui.Text(I18n.T("cr.code.count", ("n", projects.Count)), "muted"));
        col.Children.Add(bar);

        if (_wizard) col.Children.Add(Wizard());

        if (projects.Count == 0 && !_wizard)
        {
            col.Children.Add(Ui.Card(Ui.Col(10,
                Ui.Text(I18n.T("cr.code.empty"), "h3"),
                Ui.Text(I18n.T("cr.code.empty.text"), "muted", wrap: true),
                Ui.Row(8, Ui.Button(I18n.T("cr.code.new"), () => { _wizard = true; Build(); }, "primary", Icons.Plus), Ui.Button(I18n.T("cr.nav.guides"), () => Go("guides"), "", Icons.Book))), 28));
            return col;
        }
        var grid = new UniformGrid { Columns = 3 };
        foreach (var p in projects)
        {
            var game = GameCatalog.ById(p.Game);
            var pp = p;
            var card = new Button
            {
                Classes = { "tile" }, Margin = new Thickness(0, 0, 12, 12), Padding = new Thickness(16), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = Ui.Col(6,
                    Ui.Row(10, Ui.Thumb(game?.ArtUrl, game?.Name ?? p.Game, 36, 9), Ui.Col(2, Ui.Text(p.Name, "h3"), Ui.Text(game?.Name ?? p.Game, "small muted"))),
                    Ui.Text(CodeProjects.Label(p.Kind), "small", color: Ui.Res("Brand2")),
                    Ui.Text(I18n.T("cr.edited", ("when", Ui.Ago(p.Updated))), "small muted")),
            };
            card.Click += (_, _) => { _codeOpen = pp; _log.Clear(); Build(); };
            grid.Children.Add(card);
        }
        col.Children.Add(grid);
        return col;
    }

    Control Wizard()
    {
        var name = Box(_wName, I18n.T("cr.code.w.name"), max: 60);
        name.TextChanged += (_, _) => _wName = name.Text ?? "";
        var author = Box(_wAuthor, I18n.T("cr.code.w.author"), max: 40);
        author.TextChanged += (_, _) => _wAuthor = author.Text ?? "";

        var def = GameCatalog.ById(_wGame);
        var state = def is null ? null : AppState.Game(def.Id);
        var kind = def is null ? null : CodeProjects.KindFor(def, state?.Path);

        var info = Ui.Col(6);
        if (def is null) info.Children.Add(Ui.Text(I18n.T("cr.code.w.pick"), "small muted"));
        else if (kind is null) info.Children.Add(Dotted(false, I18n.T("cr.code.noLoader", ("game", def.Name))));
        else
        {
            info.Children.Add(Dotted(true, I18n.T("cr.code.w.kind", ("kind", CodeProjects.Label(kind.Value)))));
            info.Children.Add(Dotted(state?.Path is not null, state?.Path is { } path ? I18n.T("cr.code.w.found", ("path", path)) : I18n.T("cr.code.w.notFound"), warn: true));
        }

        var create = Ui.Button(I18n.T("cr.code.w.create"), () =>
        {
            if (def is null || kind is null) { MainWindow.Current?.Toast(I18n.T("cr.code.w.pick"), bad: true); return; }
            if (_wName.Trim() == "") { MainWindow.Current?.Toast(I18n.T("cr.code.w.needName"), bad: true); return; }
            try
            {
                Settings.Data["crAuthor"] = _wAuthor.Trim();
                Settings.Save();
                _codeOpen = CodeProjects.Create(_wName, def, state?.Path, _wAuthor);
                _wizard = false;
                _wName = "";
                _log.Clear();
                Build();
            }
            catch (Exception e) { MainWindow.Current?.Toast(e.Message, bad: true); }
        }, "primary", Icons.Wand);

        return Ui.Card(Ui.Col(14,
            Ui.Text(I18n.T("cr.code.w.title"), "h3"),
            Ui.Text(I18n.T("cr.code.w.lead"), "muted", wrap: true),
            Ui.Row(14, Field(I18n.T("cr.code.w.nameLabel"), name), Field(I18n.T("cr.code.w.authorLabel"), author)),
            Field(I18n.T("cr.code.w.game"), GamePicker(_wGame, id => { _wGame = id; Build(); }, g => g.Loader != LoaderKind.None)),
            info,
            Ui.Row(8, create, Ui.Button(I18n.T("common.cancel"), () => { _wizard = false; Build(); }, "ghost"))), 24);
    }

    // ---------------------------------------------------------------- проект

    Control CodeDetail(CodeProject p)
    {
        var game = GameCatalog.ById(p.Game);
        var back = Ui.Button(I18n.T("cr.code.back"), () => { _codeOpen = null; Build(); }, "ghost", Icons.Back);
        var title = Ui.Row(12, Ui.Thumb(game?.ArtUrl, game?.Name ?? p.Game, 40, 10), Ui.Col(1, Ui.Text(p.Name, "h2"), Ui.Text($"{game?.Name ?? p.Game} · {CodeProjects.Label(p.Kind)}", "small muted")));
        title.VerticalAlignment = VerticalAlignment.Center;

        var build = Ui.Button(_building ? I18n.T("cr.code.building") : I18n.T("cr.code.build"), () => _ = BuildCode(p), "primary", Icons.Play);
        build.IsEnabled = !_building;
        var actions = Ui.Row(8,
            Ui.Button("", () => Actions.OpenFolder(p.Dir), "icon", Icons.Folder, I18n.T("cr.files")),
            Ui.Button("", () => DeleteCode(p), "icon ghost", Icons.Trash, I18n.T("cr.delete")),
            Ui.Button(I18n.T("cr.code.editor"), () => OpenEditor(p), "", Icons.Code),
            Ui.Button(I18n.T("cr.code.pack"), () => PackFrom(p), "", Icons.Package),
            build);
        var bar = new DockPanel();
        DockPanel.SetDock(actions, Dock.Right);
        bar.Children.Add(actions);
        bar.Children.Add(Ui.Row(10, back, title));

        // Готовность: что нужно, чтобы кнопка «Собрать» сработала.
        var dotnet = _dotnet ??= CodeProjects.FindDotnet();
        var checks = Ui.Col(8, Ui.Text(I18n.T("cr.code.ready"), "h3"));
        foreach (var c in CodeProjects.Preflight(p, dotnet))
        {
            var row = Ui.Col(2, Dotted(c.Ok, I18n.T(c.Key + (c.Ok ? ".ok" : ".no"), ("detail", c.Detail)), warn: c.Key.EndsWith("game")));
            checks.Children.Add(row);
        }
        if (dotnet is null) checks.Children.Add(Ui.Button(I18n.T("cr.code.getDotnet"), () => Ui.OpenUrl("https://dotnet.microsoft.com/download"), "", Icons.External));
        checks.Children.Add(Ui.Button(I18n.T("cr.code.recheck"), () => { _dotnet = null; Build(); }, "ghost", Icons.Refresh));

        // Файлы проекта и следующие шаги.
        var files = Ui.Col(6, Ui.Text(I18n.T("cr.code.files"), "h3"));
        foreach (var f in Directory.EnumerateFileSystemEntries(p.Dir).Select(Path.GetFileName).Where(n => n is not (null or "bin" or "obj" or ".vs") && !n.StartsWith('.')).OrderBy(n => n).Take(14))
        {
            var full = Path.Combine(p.Dir, f!);
            files.Children.Add(Ui.Row(8, Ui.Icon(Directory.Exists(full) ? Icons.Folder : Icons.Code, 14, Ui.Res("Muted")), new SelectableTextBlock { Text = f, FontFamily = Mono, FontSize = 12.5 }));
        }
        var next = Ui.Col(6, Ui.Text(I18n.T("cr.code.next"), "h3"));
        for (var i = 1; i <= 4; i++) next.Children.Add(Ui.Text($"{i}. {I18n.T("cr.code.next." + i)}", "small muted", wrap: true));
        var more = new WrapPanel();
        foreach (var b in new[] { Ui.Button(I18n.T("cr.nav.snippets"), () => Go("snippets"), "ghost", Icons.Layers), Ui.Button(I18n.T("cr.nav.assets"), () => Go("assets"), "ghost", Icons.Package) }) more.Children.Add(b);
        next.Children.Add(more);

        var side = Ui.Col(14, Ui.Card(checks, 18), Ui.Card(files, 18), Ui.Card(next, 18));
        side.VerticalAlignment = VerticalAlignment.Top;

        // Журнал сборки.
        _logBox = new SelectableTextBlock { Text = _log.Length == 0 ? I18n.T("cr.code.log.empty") : _log.ToString(), FontFamily = Mono, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Res(_log.Length == 0 ? "Muted" : "Text") };
        var log = new Border { Classes = { "card" }, Padding = new Thickness(16), Child = Ui.Col(8, Ui.Text(I18n.T("cr.code.log"), "h3"), new ScrollViewer { Content = _logBox, MinHeight = 260, MaxHeight = 420 }) };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,360"), ColumnSpacing = 16 };
        grid.Children.Add(log);
        Grid.SetColumn(side, 1);
        grid.Children.Add(side);
        return Ui.Col(14, bar, grid);
    }

    static string? _dotnet;

    void AppendLog(string line)
    {
        _log.AppendLine(line);
        if (_logBox is not null && _logBox.IsAttachedToVisualTree()) { _logBox.Text = _log.ToString(); _logBox.Foreground = Ui.Res("Text"); }
    }

    async Task BuildCode(CodeProject p)
    {
        var w = MainWindow.Current;
        _dotnet ??= await Task.Run(CodeProjects.FindDotnet);
        if (_dotnet is null) { w?.Toast(I18n.T("cr.code.chk.dotnet.no"), bad: true); Build(); return; }
        _building = true;
        _log.Clear();
        Build();
        try
        {
            var code = await CodeProjects.Build(p, line => Dispatcher.UIThread.Post(() => AppendLog(line)));
            _building = false;
            AppendLog("");
            if (code == 0)
            {
                AppendLog(I18n.T("cr.code.built", ("dir", CodeProjects.OutputDir(p) ?? p.Dir)));
                w?.Toast(I18n.T("cr.code.built.toast", ("name", p.Name)));
            }
            else
            {
                AppendLog(I18n.T("cr.code.failed", ("code", code)));
                w?.Toast(I18n.T("cr.code.failed.toast"), bad: true);
            }
        }
        catch (Exception e)
        {
            _building = false;
            AppendLog(e.Message);
            w?.Toast(e.Message, bad: true);
        }
        if (_tab == "code" && _codeOpen?.Id == p.Id) Build();
    }

    static void OpenEditor(CodeProject p)
    {
        try { Process.Start(new ProcessStartInfo("code", $"\"{p.Dir}\"") { UseShellExecute = true, CreateNoWindow = true }); return; }
        catch { }
        try { Process.Start(new ProcessStartInfo(p.Csproj) { UseShellExecute = true }); return; }
        catch { }
        Actions.OpenFolder(p.Dir);
    }

    /// <summary>В «Упаковку» с заполненными полями: имя, игра и папка сборки проекта.</summary>
    void PackFrom(CodeProject p)
    {
        _pack = new PackSpec { Name = CodeProjects.Identifier(p.Name), Game = p.Game, Description = p.Name, Source = CodeProjects.OutputDir(p) ?? Path.Combine(p.Dir, "bin", "Release") };
        Go("pack");
    }

    void DeleteCode(CodeProject p)
    {
        var w = MainWindow.Current!;
        w.Dialog(I18n.T("cr.delete"), Ui.Text(I18n.T("cr.code.delete.text", ("name", p.Name)), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("cr.delete"), () => { w.CloseDialog(); CodeProjects.Delete(p); _codeOpen = null; Build(); }, "primary", Icons.Trash));
    }
}
