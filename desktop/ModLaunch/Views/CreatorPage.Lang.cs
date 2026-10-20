using System.IO.Compression;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Features;
using MX = ModScriptX;

namespace ModLaunch.Views;

/// <summary>
/// 11.0: свой язык Creator Hub — ModScript X 2. Редактор слева; справа — песочница (запустить мод без игры:
/// хуки по порядку, свои переменные игры, вывод say/log), код движка (во что переведётся мод) и справка.
/// Внизу — проверка автомодерацией, подключение к игре инжектом и продажа инжекта в Creator Hub.
/// </summary>
public sealed partial class CreatorPage
{
    static string? _lxPath, _lxText;
    static string _lxVars = "gold = 100\nstreak = 0";
    static int _lxTicks = 3;
    static string _lxPane = "run", _lxEngine = "renpy";
    static MX.Sandbox.Result? _lxResult;
    static Verdict? _lxVerdict;
    ScriptEditor? _lxEditor;

    static string Example(string name)
    {
        using var s = AssetLoader.Open(new Uri($"avares://ModLaunch/Assets/modscriptx/{name}.modx"));
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    /// <summary>Пример «Арена» — для нового файла и снимков экрана.</summary>
    internal static string ArenaExample() => Example("arena");

    /// <summary>Для снимков экрана: пример, запуск в песочнице и проверка автомодерацией.</summary>
    internal static void DemoLang(string pane = "run")
    {
        _lxPath = null;
        _lxText = Example("arena");
        _lxVars = "gold = 100\nstreak = 0";
        _lxTicks = 3;
        _lxPane = pane;
        _lxEngine = "renpy";
        _lxResult = MX.Sandbox.Run(MX.ModxDoc.Parse(_lxText), MX.Sandbox.ParseVars(_lxVars), _lxTicks);
        _lxVerdict = Moderation.CheckModx(_lxText);
    }

    static string ScriptsDir()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Directory.CreateDirectory(Path.Combine(docs != "" ? docs : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ModScript X")).FullName;
    }

    Control Lang()
    {
        _lxPath ??= Settings.Data.Str("lxFile");
        if (_lxText is null)
        {
            try { _lxText = _lxPath is not null && File.Exists(_lxPath) ? File.ReadAllText(_lxPath) : Example("arena"); }
            catch (IOException) { _lxText = Example("arena"); }
        }
        var doc = MX.ModxDoc.Parse(_lxText);

        // ---- шапка: язык, файл, кнопки файла
        var fileName = _lxPath is null ? I18n.T("v11.lx.unsaved") : Path.GetFileName(_lxPath);
        var title = Ui.Col(2, Ui.Row(10, Ui.Text("ModScript X 2", "h2"), StoreKit.Pill(I18n.T("v11.lx.badge"), onArt: false)),
            Ui.Text(I18n.T("v11.lx.lead"), "muted", wrap: true));
        var files = Ui.Row(8,
            Ui.Button(I18n.T("v11.lx.new"), () => { _lxPath = null; _lxText = Example("arena"); _lxResult = null; _lxVerdict = null; Build(); }, "", Icons.FilePlus),
            Ui.Button(I18n.T("v97.mx.pick"), () => _ = OpenModx(), "", Icons.Folder),
            Ui.Button(I18n.T("common.save"), () => SaveModx(false), "", Icons.Save));
        files.VerticalAlignment = VerticalAlignment.Top;
        var head = new DockPanel();
        DockPanel.SetDock(files, Dock.Right);
        head.Children.Add(files);
        head.Children.Add(title);

        // ---- редактор
        _lxEditor = new ScriptEditor { Text = _lxText, Height = 560 };
        _lxEditor.TextChanged += () => _lxText = _lxEditor.Text;
        _lxEditor.SaveRequested += () => SaveModx(false);
        _lxEditor.MarkErrors(doc.Diags.Where(d => !d.Warning && d.Line > 0).Select(d => d.Line));
        var problems = doc.Diags.Count == 0
            ? Ui.Row(6, Ui.Icon(Icons.Check, 14, Ui.Res("Good")), Ui.Text(I18n.T("v11.lx.ok", ("hooks", doc.Hooks.Count), ("fns", doc.Functions.Count)), "small muted"))
            : Ui.Col(4, doc.Diags.Take(4).Select(d => (Control)Ui.Row(6, Ui.Icon(Icons.Alert, 14, d.Warning ? Ui.Res("Warn") : Ui.Res("Bad")),
                Ui.Text((d.Line > 0 ? I18n.T("v11.lx.line", ("n", d.Line)) + " · " : "") + d.Message, "small", color: d.Warning ? Ui.Res("Warn") : Ui.Res("Bad")))).ToArray());
        var editorCard = Ui.Card(Ui.Col(10, Ui.Row(8, Ui.Icon(Icons.Code, 15, Ui.Res("Muted")), Ui.Text(fileName, "small muted")), _lxEditor, problems), 14);

        // ---- правая панель: песочница, код движка, справка
        var paneTabs = new List<Control>();
        foreach (var (id, key) in new[] { ("run", "v11.lx.pane.run"), ("code", "v11.lx.pane.code"), ("docs", "v11.lx.pane.docs") })
        {
            var pane = id;
            var b = Ui.Button(I18n.T(key), () => { _lxPane = pane; Build(); }, "tab");
            if (_lxPane == id) b.Classes.Add("active");
            paneTabs.Add(b);
        }
        Control body = _lxPane switch { "code" => EnginePane(), "docs" => DocsPane(), _ => RunPane() };
        var side = Ui.Card(Ui.Col(12, Ui.TabBar(paneTabs.ToArray()), body), 16);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,2*"), ColumnSpacing = 16 };
        grid.Children.Add(editorCard);
        Grid.SetColumn(side, 1);
        grid.Children.Add(side);

        return Ui.Col(16, head, grid, ShipCard());
    }

    /// <summary>Песочница: переменные игры, сколько тиков, «Запустить» и вывод.</summary>
    Control RunPane()
    {
        var vars = new TextBox { Text = _lxVars, AcceptsReturn = true, FontFamily = Mono, FontSize = 12.5, MinHeight = 84, Watermark = "gold = 100", VerticalContentAlignment = VerticalAlignment.Top };
        vars.TextChanged += (_, _) => _lxVars = vars.Text ?? "";
        var ticks = new NumericUpDown { Value = _lxTicks, Minimum = 0, Maximum = 1000, Increment = 1, Width = 120, FormatString = "0" };
        ticks.ValueChanged += (_, _) => _lxTicks = (int)(ticks.Value ?? 3);
        var run = Ui.Button(I18n.T("v11.lx.run"), RunSandbox, "primary", Icons.Play);
        var col = Ui.Col(10,
            Ui.Text(I18n.T("v11.lx.vars"), "small muted"), vars,
            Ui.Row(10, Ui.Text(I18n.T("v11.lx.ticks"), "small muted"), ticks, run));
        ((StackPanel)col.Children[2]).Children[0].VerticalAlignment = VerticalAlignment.Center;
        if (_lxResult is { } r)
        {
            var output = new SelectableTextBlock
            {
                FontFamily = Mono, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, LineHeight = 19,
                Text = r.Output.Count == 0 ? I18n.T("v11.lx.silent") : string.Join('\n', r.Output.Take(300)),
            };
            col.Children.Add(Ui.Row(8, Ui.Icon(r.Ok ? Icons.Check : Icons.Alert, 14, r.Ok ? Ui.Res("Good") : Ui.Res("Bad")),
                Ui.Text(I18n.T(r.Ok ? "v11.lx.ran" : "v11.lx.failed", ("steps", r.Steps)), "small", color: r.Ok ? Ui.Res("Good") : Ui.Res("Bad"))));
            col.Children.Add(new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10), Child = new ScrollViewer { MaxHeight = 200, Content = output } });
            foreach (var e in r.Errors.Take(5))
                col.Children.Add(Ui.Text(I18n.T("v11.lx.line", ("n", e.Line)) + " · " + e.Message, "small", color: e.Warning ? Ui.Res("Warn") : Ui.Res("Bad"), wrap: true));
            var table = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 14, RowSpacing = 4 };
            var row = 0;
            foreach (var (name, value) in r.Vars.Take(24))
            {
                table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var n = Ui.Text(name, "small muted");
                n.FontFamily = Mono;
                var v = Ui.Text(MX.Sandbox.Show(value), "small");
                v.FontFamily = Mono;
                v.TextTrimming = TextTrimming.CharacterEllipsis;
                Grid.SetRow(n, row);
                Grid.SetRow(v, row);
                Grid.SetColumn(v, 1);
                table.Children.Add(n);
                table.Children.Add(v);
                row++;
            }
            col.Children.Add(Ui.Col(6, Ui.Text(I18n.T("v11.lx.after"), "small muted"), table));
        }
        else col.Children.Add(Ui.Text(I18n.T("v11.lx.runHint"), "small muted", wrap: true));
        return col;
    }

    void RunSandbox()
    {
        var doc = MX.ModxDoc.Parse(_lxText ?? "");
        if (!doc.Ok) { MainWindow.Current?.Toast(I18n.T("v11.lx.fix"), bad: true); Build(); return; }
        var errors = new List<Diag>();
        var vars = MX.Sandbox.ParseVars(_lxVars, errors);
        _lxResult = MX.Sandbox.Run(doc, vars, _lxTicks);
        foreach (var e in errors) _lxResult.Errors.Add(new Diag(e.Line, I18n.T("v11.lx.varsBad") + ": " + e.Message, true));
        Build();
    }

    /// <summary>Во что переведётся мод: код выбранного движка.</summary>
    Control EnginePane()
    {
        var engines = new ComboBox { MinWidth = 220 };
        foreach (var t in MX.Target.All) engines.Items.Add(new ComboBoxItem { Content = $"{t.Title} · {t.Language}", Tag = t.Id });
        engines.SelectedItem = engines.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag! == _lxEngine) ?? engines.Items[0];
        engines.SelectionChanged += (_, _) => { if (engines.SelectedItem is ComboBoxItem { Tag: string id }) { _lxEngine = id; Build(); } };
        string code;
        try
        {
            var doc = MX.ModxDoc.Parse(_lxText ?? "");
            if (!doc.Ok) code = I18n.T("v11.lx.fix");
            else
            {
                var sb = new StringBuilder();
                foreach (var f in MX.Target.Find(_lxEngine)!.Emit(doc, null))
                {
                    sb.Append("// ").Append(f.Path).Append('\n');
                    if (f.Bytes is null) { sb.Append(f.Text); continue; }
                    // Godot: архив мода — показываем скрипт из него.
                    using var zip = new ZipArchive(new MemoryStream(f.Bytes), ZipArchiveMode.Read);
                    foreach (var e in zip.Entries.Where(e => e.FullName.EndsWith(".gd")))
                    {
                        using var r = new StreamReader(e.Open());
                        sb.Append(r.ReadToEnd());
                    }
                }
                code = sb.ToString();
            }
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException) { code = e.Message; }
        var box = new Border
        {
            Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10),
            Child = new ScrollViewer { MaxHeight = 470, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new SelectableTextBlock { Text = code, FontFamily = Mono, FontSize = 12, LineHeight = 18 } },
        };
        return Ui.Col(10, Ui.Text(I18n.T("v11.lx.code.hint"), "small muted", wrap: true), engines, box);
    }

    /// <summary>Справка по языку: синтаксис строкой и пояснение.</summary>
    static Control DocsPane()
    {
        var rows = new (string Code, string Key)[]
        {
            ("mod \"Name\"  ·  version 1.0.0  ·  engine renpy", "v11.lx.d.head"),
            ("let bonus = 250", "v11.lx.d.let"),
            ("fn reward(wave) { return wave * 50 }", "v11.lx.d.fn"),
            ("hook ready | start | load | tick { … }", "v11.lx.d.hook"),
            ("gold = 10  ·  gold += 5  ·  set / add", "v11.lx.d.set"),
            ("if a > 1 { } else if a == 1 { } else { }", "v11.lx.d.if"),
            ("while n < 3 { }  ·  for i in 1..10 { }  ·  for x in list { }", "v11.lx.d.loop"),
            ("break  ·  continue  ·  return value", "v11.lx.d.flow"),
            ("say \"Gold: {gold}\"  ·  log \"…\"", "v11.lx.d.say"),
            ("len str int num rand chance min max abs round clamp range contains upper lower", "v11.lx.d.lib"),
            ("native python { … }", "v11.lx.d.native"),
        };
        var col = Ui.Col(12);
        foreach (var (code, key) in rows)
        {
            var c = new SelectableTextBlock { Text = code, FontFamily = Mono, FontSize = 12.5, Foreground = Ui.Res("Brand2"), TextWrapping = TextWrapping.Wrap };
            col.Children.Add(Ui.Col(3, c, Ui.Text(I18n.T(key), "small muted", wrap: true)));
        }
        return new ScrollViewer { MaxHeight = 560, Content = col };
    }

    /// <summary>Нижняя карточка: автомодерация, подключить к игре, продать как инжект.</summary>
    Control ShipCard()
    {
        var games = AppState.Games.Where(g => g.Status == Detect.Found && !g.Def.IsMinecraft).OrderBy(g => g.Def.Name).ToList();
        var pick = new ComboBox { MinWidth = 220, PlaceholderText = I18n.T("v11.lx.pickGame") };
        foreach (var g in games) pick.Items.Add(new ComboBoxItem { Content = g.Def.Name, Tag = g.Def.Id });
        if (Settings.Data.Str("lxGame") is { } last) pick.SelectedItem = pick.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag! == last);
        pick.SelectionChanged += (_, _) => { if (pick.SelectedItem is ComboBoxItem { Tag: string id }) { Settings.Data["lxGame"] = id; Settings.Save(); } };

        var check = Ui.Button(I18n.T("v11.lx.check"), () => { _lxVerdict = Moderation.CheckModx(_lxText ?? ""); Build(); }, "", Icons.Shield);
        var attach = Ui.Button(I18n.T("v11.lx.attach"), () =>
        {
            if (pick.SelectedItem is not ComboBoxItem { Tag: string gameId }) { MainWindow.Current?.Toast(I18n.T("v11.lx.pickGame"), bad: true); return; }
            if (!SaveModx(false)) return;
            var (item, v) = Features.Inject.AddLocal(_lxPath!, gameId);
            _lxVerdict = v;
            if (item is not null) MainWindow.Current?.Toast(I18n.T("v11.lx.attached", ("title", item.Title), ("game", AppState.Game(gameId).Def.Name)));
            Build();
        }, "primary", Icons.Zap);
        var sell = Ui.Button(I18n.T("v11.lx.sell"), () =>
        {
            var v = Moderation.CheckModx(_lxText ?? "");
            _lxVerdict = v;
            if (!v.Ok) { Build(); return; }
            if (!SaveModx(false)) return;
            ListingWizard.Show(null, MarketData.Reload, _lxPath, "script");
        }, "", Icons.Bag);

        var col = Ui.Col(12,
            Ui.Row(10, Ui.Icon(Icons.Shield, 18, Ui.Res("Brand2")), Ui.Text(I18n.T("v11.lx.ship"), "h3")),
            Ui.Text(I18n.T("v11.lx.ship.text"), "small muted", wrap: true),
            Ui.Row(10, pick, attach, check, sell, Ui.Button(I18n.T("v11.lx.dev"), () => Go("mxdev"), "ghost", Icons.Folder, I18n.T("v11.lx.dev.tip"))));
        if (_lxVerdict is { } verdict) col.Children.Add(VerdictView(verdict));
        return Ui.Card(col, 18);
    }

    /// <summary>Итог автомодерации: пропущен или нет, что найдено и что меняет мод.</summary>
    internal static Control VerdictView(Verdict v)
    {
        var col = Ui.Col(6, Ui.Row(8, Ui.Icon(v.Ok ? Icons.Check : Icons.Alert, 15, v.Ok ? Ui.Res("Good") : Ui.Res("Bad")),
            Ui.Text(I18n.T(v.Ok ? (v.Clean ? "v11.mod.pass" : "v11.mod.passWarn") : "v11.mod.fail"), "h3", color: v.Ok ? Ui.Res("Good") : Ui.Res("Bad"))));
        if (v.Changes != "") col.Children.Add(Ui.Text(v.Changes, "small muted", wrap: true));
        foreach (var f in v.Findings)
            col.Children.Add(Ui.Row(6, Ui.Icon(f.Level == "block" ? Icons.Close : f.Level == "warn" ? Icons.Alert : Icons.Info, 13,
                f.Level == "block" ? Ui.Res("Bad") : f.Level == "warn" ? Ui.Res("Warn") : Ui.Res("Muted")), Ui.Text(f.Text, "small", wrap: true)));
        return new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 12), Child = col };
    }

    async Task OpenModx()
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return;
        var type = new FilePickerFileType("ModScript X (.modx)") { Patterns = ["*" + MX.ModxDoc.Extension] };
        var result = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = I18n.T("v97.mx.pick"), AllowMultiple = false, FileTypeFilter = [type] });
        if (result.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        try { _lxText = await File.ReadAllTextAsync(path); }
        catch (IOException e) { MainWindow.Current?.Toast(e.Message, bad: true); return; }
        _lxPath = path;
        _lxResult = null;
        _lxVerdict = null;
        Settings.Data["lxFile"] = path;
        Settings.Save();
        Build();
    }

    /// <summary>Сохранить .modx (новый — в «Документы\ModScript X» под именем мода).</summary>
    bool SaveModx(bool rebuild = true)
    {
        var text = _lxEditor?.Text ?? _lxText ?? "";
        _lxText = text;
        try
        {
            _lxPath ??= Path.Combine(ScriptsDir(), MX.ModxDoc.Parse(text).Id.ToLowerInvariant() + MX.ModxDoc.Extension);
            File.WriteAllText(_lxPath, text);
            Settings.Data["lxFile"] = _lxPath;
            Settings.Save();
            MainWindow.Current?.Toast(I18n.T("v11.lx.saved", ("file", Path.GetFileName(_lxPath))));
            if (rebuild) Build();
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MainWindow.Current?.Toast(e.Message, bad: true);
            return false;
        }
    }

    // ---------------------------------------------------------------- «Инжекты»: библиотека и журнал автомодерации

    Control Injects()
    {
        var items = Features.Inject.All();
        var games = AppState.Games.Where(g => g.Status == Detect.Found && !g.Def.IsMinecraft).OrderBy(g => g.Def.Name).ToList();
        var mode = new ToggleSwitch { IsChecked = Features.Inject.On, OnContent = "", OffContent = "", MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        mode.IsCheckedChanged += (_, _) => { Features.Inject.SetOn(mode.IsChecked == true); Build(); };
        var modeRow = new DockPanel();
        DockPanel.SetDock(mode, Dock.Right);
        modeRow.Children.Add(mode);
        modeRow.Children.Add(Ui.Col(3, Ui.Text(I18n.T("v11.inj.mode"), "h3"), Ui.Text(I18n.T("v11.inj.mode.hint"), "small muted", wrap: true)));

        var list = Ui.Col(10);
        if (items.Count == 0) list.Children.Add(Ui.Text(I18n.T("v11.inj.none"), "muted", wrap: true));
        foreach (var i in items)
        {
            var item = i;
            var on = new ToggleSwitch { IsChecked = i.Enabled, OnContent = "", OffContent = "", MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
            on.IsCheckedChanged += (_, _) => Features.Inject.SetEnabled(item.Id, on.IsChecked == true);
            var game = new ComboBox { MinWidth = 180, PlaceholderText = I18n.T("v11.lx.pickGame") };
            foreach (var g in games) game.Items.Add(new ComboBoxItem { Content = g.Def.Name, Tag = g.Def.Id });
            game.SelectedItem = game.Items.OfType<ComboBoxItem>().FirstOrDefault(x => (string)x.Tag! == i.Game);
            game.SelectionChanged += (_, _) => { if (game.SelectedItem is ComboBoxItem { Tag: string id }) Features.Inject.SetGame(item.Id, id); };
            var strikes = Moderation.Strikes(i.Id);
            var tags = Ui.Row(6,
                StoreKit.Pill(I18n.T(i.Sealed ? "v11.inj.bought" : "v11.inj.own"), onArt: false),
                StoreKit.Pill(I18n.T("v11.inj.kind." + i.Kind), onArt: false));
            if (strikes > 0) tags.Children.Add(Gx.Tag(I18n.T("v11.inj.strikes", ("n", strikes)), Ui.Res("Warn"), Ui.Res("OnBrand"), 11));
            var words = Ui.Col(4, Ui.Row(8, Ui.Text(i.Title, "h3"), tags),
                Ui.Text(i.Changes == "" ? I18n.T("v11.inj.noSource") : i.Changes, "small muted", wrap: true),
                Ui.Text(i.Sealed ? I18n.T("v11.inj.sealed", ("seller", i.Seller)) : i.LocalPath ?? "", "small muted"));
            var right = Ui.Row(10, game, on, Ui.Button("", () => { Features.Inject.Remove(item.Id); Build(); }, "icon ghost", Icons.Trash, I18n.T("common.remove")));
            right.VerticalAlignment = VerticalAlignment.Center;
            var row = new DockPanel();
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);
            row.Children.Add(Ui.Row(12, new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(10), Background = Ui.Res("Surface2"), Child = Ui.Icon(i.Kind == "modx" ? Icons.Code : Icons.Package, 18, Ui.Res("Brand2")) }, words));
            list.Children.Add(new Border { Classes = { "card" }, Padding = new Thickness(16, 12), Child = row });
        }

        var log = Ui.Col(8);
        foreach (var e in Moderation.Log().Take(20))
        {
            var line = Ui.Row(8, Ui.Icon(e.Ok ? Icons.Check : Icons.Alert, 13, e.Ok ? Ui.Res("Good") : Ui.Res("Bad")),
                Ui.Text($"{e.Title} · {I18n.T("v11.mod.stage." + e.Stage)} · {Ui.Ago(e.At)}", "small"));
            var col = Ui.Col(3, line);
            foreach (var f in e.Findings.Where(f => f.Level != "info").Take(3)) col.Children.Add(Ui.Text("   " + f.Text, "small muted", wrap: true));
            log.Children.Add(col);
        }
        if (log.Children.Count == 0) log.Children.Add(Ui.Text(I18n.T("v11.mod.log.empty"), "small muted"));

        return Ui.Col(16,
            Ui.Card(Ui.Col(12, Ui.Row(10, Ui.Icon(Icons.Zap, 20, Ui.Res("Brand2")), Ui.Text(I18n.T("v11.inj.title"), "h2")),
                Ui.Text(I18n.T("v11.inj.lead"), "muted", wrap: true), modeRow), 20),
            list,
            Ui.Card(Ui.Col(10, Ui.Row(10, Ui.Icon(Icons.Shield, 18, Ui.Res("Brand2")), Ui.Text(I18n.T("v11.mod.title"), "h3")),
                Ui.Text(I18n.T("v11.mod.lead"), "small muted", wrap: true), log), 18));
    }
}
