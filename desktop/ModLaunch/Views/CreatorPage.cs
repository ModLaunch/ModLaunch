using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Games;

namespace ModLaunch.Views;

/// <summary>
/// Creator Hub: свои моды на языке ModScript — редактор с проверкой на лету,
/// «примочки» (примеры), сборка в пакет, установка в игру и галерея сообщества.
/// </summary>
public sealed partial class CreatorPage : Page
{
    public override string Title => "Creator Hub";
    public override string SearchHint => I18n.T("cr.search");

    string _tab;
    Project? _open;
    string _filter = "";
    readonly ScriptEditor _code = new() { MinHeight = 480 };
    readonly StackPanel _check = new() { Spacing = 8 };
    DispatcherTimer? _debounce;
    bool _dirty;

    public CreatorPage(string tab = "hub", string? project = null)
    {
        _tab = tab;
        if (project is not null) { _tab = "mine"; Open(Projects.Get(project)); }
        _code.TextChanged += () =>
        {
            _dirty = true;
            _debounce?.Stop();
            _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _debounce.Tick += (_, _) => { _debounce!.Stop(); RenderCheck(); };
            _debounce.Start();
        };
        _code.SaveRequested += () => Save();
        DetachedFromVisualTree += (_, _) => { if (_dirty) Save(quiet: true); };
    }

    public override void Search(string text) { _filter = text.Trim(); if (_open is null && _tab is "mine" or "published") _tab = "hub"; Build(); }

    void Open(Project? p)
    {
        _open = p;
        if (p is null) return;
        _code.Text = File.ReadAllText(p.Script);
        _dirty = false;
    }

    void Save(bool quiet = false)
    {
        if (_open is null) return;
        Projects.Save(_open, _code.Text ?? "");
        _dirty = false;
        if (!quiet) MainWindow.Current?.Toast(I18n.T("cr.saved"));
    }

    public override void Build()
    {
        var content = new StackPanel { Spacing = 18, Margin = new Thickness(34, 26, 34, 34), MaxWidth = 1640 };

        // Шапка: название и вкладки.
        var tabs = Ui.Row(4);
        foreach (var (id, key, icon) in new[] { ("hub", "hub.tab", Icons.Globe), ("mine", "cr.tab.mine", Icons.Edit), ("published", "hub.tab.mine", Icons.Upload), ("examples", "cr.tab.examples", Icons.Wand), ("docs", "cr.tab.docs", Icons.Book) })
        {
            var b = Ui.Button(I18n.T(key), () => { if (_dirty) Save(quiet: true); _tab = id; _open = id == "mine" ? _open : null; Build(); }, "tab", icon);
            if (_tab == id) b.Classes.Add("active");
            tabs.Children.Add(b);
        }
        var title = Ui.Row(12,
            new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(12), Background = Ui.Res("BrandSoft"), Child = Ui.Icon(Icons.Code, 22, Ui.Res("Brand2")) },
            Ui.Col(2, Ui.Text("Creator Hub", "h2"), Ui.Text(I18n.T("cr.subtitle"), "small muted")));
        var head = new DockPanel();
        tabs.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(tabs, Dock.Right);
        head.Children.Add(tabs);
        head.Children.Add(title);
        content.Children.Add(head);

        content.Children.Add(_tab switch
        {
            "examples" => Examples(),
            "hub" => HubView(),
            "published" => PublishedView(),
            "docs" => Docs(),
            _ => _open is null ? Mine() : Editor(),
        });
        Content = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    // ---------------------------------------------------------------- мои моды

    Control Mine()
    {
        var col = new StackPanel { Spacing = 12 };
        var projects = Projects.List().Where(p => _filter == "" || p.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase)).ToList();
        var newButton = Ui.Button(I18n.T("cr.new"), () => { var p = Projects.Create("Мой мод", Templates.ById("blank")!.Code); Open(p); Build(); }, "primary", Icons.Plus);
        var bar = new DockPanel();
        var right = Ui.Row(8, Ui.Button(I18n.T("cr.folder"), () => Actions.OpenFolder(Projects.Root), "ghost", Icons.Folder), newButton);
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(right);
        bar.Children.Add(Ui.Text(I18n.T("cr.mine.count", ("n", projects.Count)), "muted"));
        col.Children.Add(bar);

        if (projects.Count == 0)
        {
            col.Children.Add(Ui.Card(Ui.Col(10,
                Ui.Text(I18n.T("cr.empty"), "h3"),
                Ui.Text(I18n.T("cr.empty.text"), "muted", wrap: true),
                Ui.Row(8, Ui.Button(I18n.T("cr.tab.examples"), () => { _tab = "examples"; Build(); }, "primary", Icons.Wand), Ui.Button(I18n.T("cr.tab.docs"), () => { _tab = "docs"; Build(); }, "", Icons.Book))), 28));
            return col;
        }
        var grid = new UniformGrid { Columns = 3 };
        foreach (var p in projects)
        {
            var game = GameCatalog.ById(p.Game);
            var pp = p;
            var card = new Button
            {
                Classes = { "tile" }, Margin = new Thickness(0, 0, 12, 12), Padding = new Thickness(16),
                Content = Ui.Col(6,
                    Ui.Row(10, Ui.Thumb(game?.ArtUrl, game?.Name ?? p.Game, 36, 9), Ui.Col(2, Ui.Text(p.Name, "h3"), Ui.Text(game?.Name ?? p.Game, "small muted"))),
                    Ui.Text(I18n.T("cr.edited", ("when", Ui.Ago(p.Updated))), "small muted")),
            };
            card.Click += (_, _) => { Open(pp); Build(); };
            grid.Children.Add(card);
        }
        col.Children.Add(grid);
        return col;
    }

    // ---------------------------------------------------------------- редактор

    Control Editor()
    {
        var p = _open!;
        var back = Ui.Button(I18n.T("cr.back"), () => { if (_dirty) Save(quiet: true); _open = null; Build(); }, "ghost", Icons.Back);
        var name = Ui.Text(p.Name, "h3");
        name.VerticalAlignment = VerticalAlignment.Center;
        var actions = Ui.Row(8,
            Ui.Button("", () => Actions.OpenFolder(Directory.CreateDirectory(Path.Combine(p.Dir, "files")).FullName), "icon", Icons.Folder, I18n.T("cr.files")),
            Ui.Button("", Delete, "icon ghost", Icons.Trash, I18n.T("cr.delete")),
            Ui.Button(I18n.T("cr.save"), () => Save(), "", Icons.Save),
            Ui.Button(I18n.T("cr.export"), () => _ = Export(), "", Icons.Package),
            Ui.Button(I18n.T("cr.publish"), Publish, "", Icons.Upload),
            Ui.Button(I18n.T("cr.install"), () => _ = InstallOpen(), "primary", Icons.Play));
        var bar = new DockPanel();
        DockPanel.SetDock(actions, Dock.Right);
        bar.Children.Add(actions);
        bar.Children.Add(Ui.Row(8, back, name));

        // Быстрые вставки: команда с образцом прямо в место курсора.
        var snippets = Ui.Row(6);
        foreach (var (label, text) in new[]
        {
            ("needs", "needs Author-ModName\n"),
            ("config", "config \"BepInEx.cfg\" [Section] Key = value\n"),
            ("edit", "edit \"Data/Objects\" \"472\" Price = 10\n"),
            ("dialogue", "dialogue \"Abigail\" \"Mon\" = \"Привет!\"\n"),
            ("let", "let name = 1\n"),
            ("for", "for x in 1 2 3 {\n  print \"$x\"\n}\n"),
            ("when", "when Season = spring {\n  \n}\n"),
            ("if", "if $x > 1 {\n  \n}\nelse {\n  \n}\n"),
            ("write", "write \"MyMod/readme.txt\" = \"текст\"\n"),
            ("copy", "copy \"file.txt\" to \"plugins/MyMod/file.txt\"\n"),
        })
        {
            var t = text;
            var chip = Ui.Button("+ " + label, () => Insert(t), "chip");
            chip.FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace");
            snippets.Children.Add(chip);
        }

        var editor = new Border { Classes = { "card" }, Padding = new Thickness(4), Child = _code };
        var side = Ui.Card(new ScrollViewer { Content = _check, MaxHeight = 560 }, 18);
        side.VerticalAlignment = VerticalAlignment.Top;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,340"), ColumnSpacing = 16 };
        grid.Children.Add(Ui.Col(10, new ScrollViewer { Content = snippets, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }, editor));
        Grid.SetColumn(side, 1);
        grid.Children.Add(side);
        RenderCheck();
        return Ui.Col(14, bar, grid);
    }

    void Insert(string text) => _code.Insert(text);

    void GoToLine(int line) => _code.GoToLine(line);

    static string Msg(Diag d) => ModScript.Explain(d, (k, a) => I18n.T(k, a));

    /// <summary>Правая панель: ошибки со ссылкой на строку и что получится после сборки.</summary>
    void RenderCheck()
    {
        _check.Children.Clear();
        var b = ModScript.Compile(_code.Text ?? "");
        var game = GameCatalog.ById(b.Game);
        var errors = b.Diags.Where(d => !d.Warning).ToList();
        _code.MarkErrors(errors.Select(d => d.Line));
        _check.Children.Add(Ui.Row(8,
            Ui.Dot(errors.Count == 0 ? Ui.Res("Good") : Ui.Res("Bad"), 10),
            Ui.Text(errors.Count == 0 ? I18n.T("cr.ok") : I18n.T("cr.errors", ("n", errors.Count)), "h3")));
        foreach (var d in errors.Take(12))
        {
            var line = d.Line;
            var row = Ui.Button($"{I18n.T("cr.line", ("n", line))}: {Msg(d)}", () => GoToLine(line), "ghost");
            row.HorizontalAlignment = HorizontalAlignment.Stretch;
            row.HorizontalContentAlignment = HorizontalAlignment.Left;
            row.Foreground = Ui.Res("Bad");
            if (row.Content is string text) row.Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12.5 };
            _check.Children.Add(row);
        }
        if (errors.Count > 0) return;

        var format = game is null ? "?" : game.Loader switch { LoaderKind.Smapi => "Content Patcher", LoaderKind.Bepinex => "Thunderstore (BepInEx)", _ => "zip" };
        _check.Children.Add(new Border { Height = 1, Background = Ui.Res("Line"), Margin = new Thickness(0, 4) });
        _check.Children.Add(Ui.Text(I18n.T("cr.result"), "small muted"));
        void Line(string k, string v) { var r = new DockPanel(); var val = Ui.Text(v, "small"); DockPanel.SetDock(val, Dock.Right); r.Children.Add(val); r.Children.Add(Ui.Text(k, "small muted")); _check.Children.Add(r); }
        Line(I18n.T("cr.r.name"), $"{b.Name} {b.Version}");
        Line(I18n.T("cr.r.game"), game?.Name ?? b.Game);
        Line(I18n.T("cr.r.format"), format);
        if (b.Needs.Count > 0) Line(I18n.T("cr.r.needs"), b.Needs.Count.ToString());
        if (b.Changes.Count > 0) Line(I18n.T("cr.r.changes"), b.Changes.Count.ToString());
        if (b.Configs.Count > 0) Line(I18n.T("cr.r.configs"), b.Configs.Count.ToString());
        if (b.Inis.Count > 0) Line(I18n.T("cr.r.inis"), b.Inis.Count.ToString());
        if (b.Copies.Count > 0) Line(I18n.T("cr.r.files"), b.Copies.Count.ToString());
        if (game is null) _check.Children.Add(Ui.Text(I18n.T("cr.build.noGame", ("game", b.Game)), "small", color: Ui.Res("Warn"), wrap: true));
        foreach (var log in b.Log.Take(8)) _check.Children.Add(Ui.Text("› " + log, "small", color: Ui.Res("Brand2"), wrap: true));
    }

    void Delete()
    {
        var p = _open!;
        var w = MainWindow.Current!;
        if (!Settings.Data.Bool("confirmRemove", true)) { Projects.Delete(p); _open = null; _dirty = false; Build(); return; }
        w.Dialog(I18n.T("cr.delete"), Ui.Text(I18n.T("cr.delete.text", ("name", p.Name)), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("cr.delete"), () => { w.CloseDialog(); Projects.Delete(p); _open = null; _dirty = false; Build(); }, "primary", Icons.Trash));
    }

    // ---------------------------------------------------------------- сборка, установка, публикация

    async Task<(ModBuild Build, Packed Packed)?> BuildOpen()
    {
        Save(quiet: true);
        var b = ModScript.Compile(_code.Text ?? "");
        if (!b.Ok) { MainWindow.Current?.Toast(I18n.T("cr.build.hasErrors"), bad: true); return null; }
        try { return (b, await Projects.Pack(b, _open!.Dir)); }
        catch (Exception e) { MainWindow.Current?.Toast(Jobs.Explain(e), bad: true); return null; }
    }

    async Task InstallOpen()
    {
        if (await BuildOpen() is not { } r) return;
        await InstallBuilt(r.Build, r.Packed);
    }

    static async Task InstallBuilt(ModBuild b, Packed packed)
    {
        var w = MainWindow.Current!;
        var g = AppState.Games.FirstOrDefault(x => x.Def.Id == b.Game);
        if (g is null || g.Path is null) { w.Toast(I18n.T("cr.install.noGame"), bad: true); return; }
        if (!g.LoaderInstalled) { w.Toast(I18n.T("games.loaderMissing", ("loader", g.Def.LoaderName)), bad: true); return; }
        try
        {
            Projects.Install(g, b, packed);
            AppState.Notify();
            foreach (var w2 in packed.Warnings) w.Toast(w2);
            // Моды из needs: ставим из каталога игры (для Stardew — подсказываем, где взять).
            var missing = Projects.MissingNeeds(g, b);
            var fromCatalog = new List<Sources.ModInfo>();
            foreach (var need in missing)
            {
                try { if (await Sources.Catalog.Get(g.Def, need) is { } mod) fromCatalog.Add(mod); } catch { }
            }
            if (fromCatalog.Count > 0) _ = Actions.InstallQueue(g, b.Name, fromCatalog.Select(m => (m, (Pin?)null)).ToList());
            var notFound = missing.Where(m => fromCatalog.All(c => !c.Id.Equals(m, StringComparison.OrdinalIgnoreCase))).ToList();
            if (notFound.Count > 0) w.Toast(I18n.T("cr.install.needs", ("mods", string.Join(", ", notFound))));
            w.Toast(I18n.T("cr.install.done", ("name", b.Name), ("game", g.Def.Name)));
        }
        catch (Exception e) { w.Toast(Jobs.Explain(e), bad: true); }
    }

    async Task Export()
    {
        if (await BuildOpen() is not { } r) return;
        var w = MainWindow.Current!;
        var game = GameCatalog.ById(r.Build.Game);
        var list = Ui.Col(4, r.Packed.Files.Take(14).Select(f => (Control)Ui.Text("• " + f, "small muted")).ToArray());
        var body = Ui.Col(10,
            Ui.Text(I18n.T("cr.export.text", ("format", r.Packed.Format)), "muted", wrap: true),
            new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Child = list });
        foreach (var warn in r.Packed.Warnings) body.Children.Add(Ui.Text("⚠ " + warn, "small", color: Ui.Res("Warn"), wrap: true));
        var buttons = new List<Control> { Ui.Button(I18n.T("common.close"), w.CloseDialog), Ui.Button(I18n.T("cr.export.folder"), () => Actions.OpenFolder(Projects.OutDir), "", Icons.Folder) };
        if (game?.ThunderstoreCommunity is not null && game.Loader == LoaderKind.Bepinex)
            buttons.Add(Ui.Button("Thunderstore", () => Ui.OpenUrl($"https://thunderstore.io/c/{game.ThunderstoreCommunity}/create/"), "primary", Icons.External));
        else if (game?.NexusDomain is not null)
            buttons.Add(Ui.Button("Nexus Mods", () => Ui.OpenUrl($"https://www.nexusmods.com/{game.NexusDomain}/mods/add"), "primary", Icons.External));
        w.Dialog(I18n.T("cr.export.title", ("name", Path.GetFileName(r.Packed.Zip))), body, buttons.ToArray());
    }

    void Publish()
    {
        Save(quiet: true);
        var code = _code.Text;
        var b = ModScript.Compile(code);
        if (!b.Ok) { MainWindow.Current?.Toast(I18n.T("cr.build.hasErrors"), bad: true); return; }
        var dir = _open!.Dir;
        HubPublish.Show(new HubDraft
        {
            Name = b.Name, Summary = b.About, Game = b.Game, Version = b.Version, Code = code,
            Description = b.About,
        }, fromProject: true, pack: async () => (await Projects.Pack(b, dir)).Zip, done: () => { _tab = "published"; Build(); });
    }

    // ---------------------------------------------------------------- примочки

    string _exCategory = "all";

    Control Examples()
    {
        var chips = Ui.Row(6);
        foreach (var c in Templates.Categories)
        {
            var cc = c;
            var n = c == "all" ? Templates.All.Length : Templates.All.Count(t => Templates.Category(t) == c);
            var chip = Ui.Button($"{I18n.T("cr.cat." + c)} · {n}", () => { _exCategory = cc; Build(); }, "chip");
            if (_exCategory == c) chip.Classes.Add("active");
            chips.Children.Add(chip);
        }
        var grid = new UniformGrid { Columns = 2 };
        foreach (var t in Templates.All.Where(t => _exCategory == "all" || Templates.Category(t) == _exCategory))
        {
            var title = I18n.T($"cr.ex.{t.Id}");
            if (_filter != "" && !title.Contains(_filter, StringComparison.OrdinalIgnoreCase) && !t.Code.Contains(_filter, StringComparison.OrdinalIgnoreCase)) continue;
            var game = GameCatalog.ById(t.Game);
            var tt = t;
            var preview = new TextBlock
            {
                Text = Preview(t.Code),
                FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"), FontSize = 12, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var card = Ui.Card(Ui.Col(10,
                Ui.Row(10, Ui.Thumb(game?.ArtUrl, game?.Name ?? t.Game, 36, 9), Ui.Col(2, Ui.Text(title, "h3"), Ui.Text(game?.Name ?? t.Game, "small muted"))),
                Ui.Text(I18n.T($"cr.ex.{t.Id}.text"), "small muted", wrap: true),
                new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 10), Child = preview },
                Ui.Button(I18n.T("cr.ex.try"), () =>
                {
                    var p = Projects.Create(title, tt.Code);
                    _tab = "mine";
                    Open(p);
                    Build();
                }, "primary", Icons.Wand)), 18);
            card.Margin = new Thickness(0, 0, 14, 14);
            grid.Children.Add(card);
        }
        return Ui.Col(14, chips, grid);
    }

    /// <summary>Суть примера: команды после описания мода (или подсказки, если команд нет).</summary>
    static string Preview(string code)
    {
        var meta = new[] { "mod ", "version ", "author ", "about ", "game ", "icon " };
        var lines = code.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Trim() != "").ToList();
        var body = lines.Where(l => !l.TrimStart().StartsWith('#') && !meta.Any(m => l.StartsWith(m))).Take(4).ToList();
        if (body.Count == 0) body = lines.Where(l => l.TrimStart().StartsWith("# ") && l.Contains(' ') && !meta.Any(m => l.TrimStart('#', ' ').StartsWith(m)) && (l.Contains("needs") || l.Contains("config"))).Take(4).ToList();
        return string.Join('\n', body);
    }

    // ---------------------------------------------------------------- справка

    static Control Docs()
    {
        var col = new StackPanel { Spacing = 12 };
        col.Children.Add(Ui.Card(Ui.Col(8, Ui.Text(I18n.T("cr.docs.title"), "h3"), Ui.Text(I18n.T("cr.docs.intro"), "muted", wrap: true)), 20));
        foreach (var group in new[]
        {
            ("cr.docs.basics", new[] { "mod", "version", "author", "about", "game", "icon", "needs" }),
            ("cr.docs.logic", new[] { "let", "if", "else", "for", "when", "print" }),
            ("cr.docs.stardew", new[] { "edit", "entry", "dialogue", "mail", "image" }),
            ("cr.docs.bepinex", new[] { "config", "ini", "copy", "write", "json" }),
        })
        {
            var rows = new StackPanel { Spacing = 10 };
            foreach (var cmd in group.Item2)
            {
                rows.Children.Add(Ui.Col(3,
                    new SelectableTextBlock { Text = I18n.T($"cr.doc.{cmd}.syntax"), FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"), FontSize = 13, Foreground = Ui.Res("Brand2") },
                    Ui.Text(I18n.T($"cr.doc.{cmd}"), "small muted", wrap: true)));
            }
            col.Children.Add(Ui.Card(Ui.Col(12, Ui.Text(I18n.T(group.Item1), "h3"), rows), 20));
        }
        return col;
    }
}
