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

public sealed partial class CreatorPage
{
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
            var chip = Ui.Button("+ " + label, () => Insert(t), "chip snippet");
            chip.FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace");
            snippets.Children.Add(chip);
        }

        var editor = new Border { Classes = { "card" }, Padding = new Thickness(4), Child = _code };
        var side = Ui.Card(new ScrollViewer { Content = _check, MaxHeight = 560 }, 18);
        side.VerticalAlignment = VerticalAlignment.Top;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,340"), ColumnSpacing = 16 };
        grid.Children.Add(Ui.Col(10, new ScrollViewer { Content = snippets, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }, editor));
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
        }, fromProject: true, pack: async () => (await Projects.Pack(b, dir)).Zip, done: () => { _tab = "mine"; _minePart = "published"; Build(); });
    }
}
