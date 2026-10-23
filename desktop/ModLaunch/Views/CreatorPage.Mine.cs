using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Games;
using MX = ModScriptX;

namespace ModLaunch.Views;

public sealed partial class CreatorPage
{
    // ---------------------------------------------------------------- мои моды

    /// <summary>
    /// 13.0: «Проекты» — бывшие «Обзор» и «Мои проекты» одним экраном. 16.0: сверху — «Создать проект»
    /// (анкета → среда ModScript X → игра), дальше проекты ModScript X плитками в цвете проекта,
    /// ниже — классические проекты ModScript (main.mls) с «Новым проектом» для импорта. «Опубликованные» — вкладкой в шапке.
    /// </summary>
    Control Mine()
    {
        var col = new StackPanel { Spacing = 16 };
        if (_minePart == "published") { col.Children.Add(PublishedView()); return col; }

        // Проверка в игре закончится, пока смотрим список, — точка статуса обновится сама.
        HookTest();
        col.Children.Add(CreateHero());
        var xs = XProjects.List().Where(p => _filter == "" || p.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase)).ToList();
        var projects = Projects.List().Where(p => _filter == "" || p.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase)).ToList();

        if (xs.Count > 0)
        {
            var folder = Ui.Button(I18n.T("cr.folder"), () => Actions.OpenFolder(XProjects.Root), "ghost", Icons.Folder);
            col.Children.Add(Section(I18n.T("v16.mine.x", ("n", xs.Count)), folder));
            var xgrid = new UniformGrid { Columns = 4 };
            foreach (var p in xs) xgrid.Children.Add(XCard(p));
            col.Children.Add(xgrid);
        }

        if (projects.Count == 0 && xs.Count == 0)
        {
            col.Children.Add(HubLook.Frame(Ui.Col(10,
                Ui.Text(_filter == "" ? I18n.T("cr.empty") : I18n.T("cr.lib.none"), "h3"),
                Ui.Text(I18n.T("v16.mine.empty"), "muted", wrap: true)), new Thickness(24, 20)));
            if (_filter == "") col.Children.Add(StartFromTemplate());
            return col;
        }
        if (projects.Count > 0 || _filter == "")
        {
            var folder = Ui.Button(I18n.T("cr.folder"), () => Actions.OpenFolder(Projects.Root), "ghost", Icons.Folder);
            col.Children.Add(Section(I18n.T("v16.mine.classic", ("n", projects.Count)), folder));
            var grid = new UniformGrid { Columns = 4 };
            var plus = Ui.Col(10, new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(3), BorderBrush = HubLook.Edge, BorderThickness = new Thickness(1), Child = Ui.Icon(Icons.Plus, 18, HubLook.Violet), HorizontalAlignment = HorizontalAlignment.Center },
                Ui.Text(I18n.T("v16.mine.classic.new"), "h3"), Ui.Text(I18n.T("v16.mine.classic.text"), "small muted", wrap: true));
            plus.HorizontalAlignment = HorizontalAlignment.Center;
            plus.MaxWidth = 220;
            var add = new Button
            {
                Classes = { "card-btn", "dashed" }, Margin = new Thickness(0, 0, 14, 14), MinHeight = 180,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                Content = plus,
            };
            // Классический мастер (имя, игра, шаблон или импорт папки/архива), см. CreatorPage.NewProject.cs.
            add.Click += (_, _) => NewProjectDialog();
            grid.Children.Add(add);
            foreach (var p in projects.OrderByDescending(p => p.Updated)) grid.Children.Add(ProjectCard(p));
            col.Children.Add(grid);
        }
        if (projects.Count + xs.Count < 4 && _filter == "") col.Children.Add(StartFromTemplate());
        return col;
    }

    static Control Section(string title, Control? right = null)
    {
        var bar = new DockPanel { Margin = new Thickness(0, 6, 0, -4) };
        if (right is not null) { DockPanel.SetDock(right, Dock.Right); bar.Children.Add(right); }
        bar.Children.Add(HubLook.Eyebrow(title, HubLook.Violet));
        return bar;
    }

    /// <summary>
    /// 16.0: «Создать проект» — путь в три шага (анкета, среда ModScript X, игра) и сколько движков знает мост.
    /// Последний проект — второй кнопкой «Продолжить».
    /// </summary>
    static Control CreateHero()
    {
        var steps = new UniformGrid { Columns = 3, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var (n, icon, key) in new[] { (1, Icons.Edit, "v16.mine.step.1"), (2, Icons.Code, "v16.mine.step.2"), (3, Icons.Gamepad, "v16.mine.step.3") })
        {
            var box = new Border
            {
                BorderBrush = HubLook.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(12, 10), Margin = new Thickness(0, 0, 10, 0), Background = HubLook.Raised,
                Child = Ui.Col(6, Ui.Row(8, HubLook.MonoText($"0{n}", 11, HubLook.Cyan), Ui.Icon(icon, 15, HubLook.Violet), Ui.Text(I18n.T(key), "strong")),
                    Ui.Text(I18n.T(key + ".text"), "small muted", wrap: true)),
            };
            steps.Children.Add(box);
        }
        var known = MX.Bridge.Known.ToList();
        var create = Ui.Button(I18n.T("v16.mine.create"), () => OpenForm(), "primary", Icons.Plus);
        create.MinHeight = 42;
        var buttons = Ui.Row(10, create);
        if (Settings.Data.Str("xLast") is { } last && XProjects.Get(last) is { } lp)
            buttons.Children.Add(Ui.Button(I18n.T("v16.mine.continue", ("name", lp.Name)), () => MainWindow.Current?.Navigate(() => new XStudio(lp.Id)), "", Icons.Code));
        buttons.Children.Add(Ui.Button(I18n.T("v16.form.import"), NewProjectDialog, "ghost", Icons.Folder));
        var left = Ui.Col(12, HubLook.Eyebrow(I18n.T("v16.mine.eyebrow"), HubLook.Violet),
            new TextBlock { Text = I18n.T("v16.mine.title"), FontFamily = Look.Display, FontSize = 24, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap },
            Ui.Text(I18n.T("v16.mine.text"), "muted", wrap: true), steps, buttons);
        var stats = Ui.Col(14,
            Gx.Stat(known.Count.ToString(), I18n.T("v16.mine.engines"), HubLook.Cyan),
            Gx.Stat(known.Count(k => k.Code).ToString(), I18n.T("v16.mine.code"), HubLook.Violet),
            Gx.Stat(MX.Target.All.Length.ToString(), I18n.T("v16.mine.targets"), null));
        stats.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 36 };
        grid.Children.Add(left);
        Grid.SetColumn(stats, 1);
        grid.Children.Add(stats);
        return HubLook.Frame(grid, new Thickness(26, 22));
    }

    /// <summary>Плитка проекта ModScript X: полоса цвета проекта, игра, путь моста (движок → язык), когда изменён.</summary>
    Control XCard(XProject p)
    {
        var game = p.Game == "" ? null : GameCatalog.ById(p.Game);
        Control art = game is not null
            ? Ui.GameImage(game, 400)
            : new Border { Background = HubLook.Raised, Child = Ui.Icon(Icons.Code, 30, Ui.Res("Faint")) };
        var hex = XProjects.Hex(p.Color);
        var tag = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(215, 10, 12, 18)), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 2),
            Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Child = HubLook.MonoText("v" + p.Version, 10.5, Brushes.White),
        };
        var badge = new Border
        {
            Background = Ui.Hex(hex), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 2), Margin = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = HubLook.MonoText("MODSCRIPT X", 9.5, Brushes.White),
        };
        var top = new Border { Height = 84, ClipToBounds = true, CornerRadius = new CornerRadius(4, 4, 0, 0), Child = new Panel { Children = { art, tag, badge } } };
        var target = p.Target is { } t ? MX.Target.Find(t) : null;
        var route = HubLook.MonoText(target is null ? I18n.T("v16.sum.files") : $"{target.Title} → {XStudio.Lang(target.Language)}", 10.5, HubLook.Cyan);
        route.TextTrimming = TextTrimming.CharacterEllipsis;
        route.Margin = new Thickness(0, 0, 8, 0);
        var when = HubLook.MonoText(Ui.Ago(p.Updated), 10.5);
        var foot = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(when, Dock.Right);
        foot.Children.Add(when);
        foot.Children.Add(route);
        var name = Ui.Text(p.Name, "h3");
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var sub = Ui.Text(game?.Name ?? (p.GamePath is { } gp ? Path.GetFileName(gp.TrimEnd('\\', '/')) : I18n.T("v16.sum.noGame")), "small muted");
        var body = Ui.Col(2, name, sub, new Border { Height = 1, Background = HubLook.Edge, Margin = new Thickness(0, 8, 0, 0) }, foot);
        body.Margin = new Thickness(14, 12, 14, 12);
        var card = new Button
        {
            Classes = { "tile" }, Margin = new Thickness(0, 0, 14, 14), Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel { Children = { new Border { Height = 3, Background = Ui.Hex(hex) }, top, body } },
        };
        card.Click += (_, _) => MainWindow.Current?.Navigate(() => new XStudio(p.Id));
        Ctx.Attach(card, () => Ctx.Menu(
            Ctx.Item(I18n.T("ctx.open"), Icons.Code, () => MainWindow.Current?.Navigate(() => new XStudio(p.Id))),
            Ctx.Item(I18n.T("v16.x.side.bridge"), Icons.Layers, () => MainWindow.Current?.Navigate(() => new XStudio(p.Id, "bridge"))),
            Ctx.Folder(I18n.T("games.openFolder"), p.Dir),
            "-",
            Ctx.Item(I18n.T("ctx.delete"), Icons.Trash, () => DeleteX(p))));
        return card;
    }

    void DeleteX(XProject p)
    {
        var w = MainWindow.Current;
        if (w is null) return;
        w.Dialog(I18n.T("v16.x.delete.title", ("name", p.Name)), Ui.Text(I18n.T("v16.x.delete.text"), "muted", wrap: true),
            Ui.Button(I18n.T("cr.cancel"), w.CloseDialog, "ghost"),
            Ui.Button(I18n.T("ctx.delete"), () => { w.CloseDialog(); XProjects.Delete(p); Build(); }, "danger", Icons.Trash));
    }

    /// <summary>
    /// Вкладки «Моих проектов» для шапки Hub: проекты мастерской и опубликованные в Hub.
    /// </summary>
    List<Control> MineTabs()
    {
        var tabs = new List<Control>();
        foreach (var (id, key) in new[] { ("projects", "cr.mine.projects"), ("published", "cr.mine.published") })
        {
            var part = id;
            var b = Ui.Button(I18n.T(key), () => { _minePart = part; Build(); }, "hub-tab");
            if (_minePart == id) b.Classes.Add("active");
            tabs.Add(b);
        }
        return tabs;
    }

    /// <summary>
    /// Карточка проекта — плитка верстака (12.1): картинка игры с версией на ярлыке, название и игра,
    /// внизу — точка статуса (собран, проверен, менялся) и когда изменён, моноширинно.
    /// </summary>
    Control ProjectCard(Project p)
    {
        var game = GameCatalog.ById(p.Game);
        Control art = game is not null
            ? Ui.GameImage(game, 400)
            : new Border { Background = HubLook.Raised, Child = Ui.Icon(Icons.Creator, 30, Ui.Res("Faint")) };
        var tag = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(215, 10, 12, 18)), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 2),
            Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Child = HubLook.MonoText(p.Version == "" ? "v—" : "v" + p.Version, 10.5, Avalonia.Media.Brushes.White),
        };
        var top = new Border { Height = 84, ClipToBounds = true, CornerRadius = new CornerRadius(4, 4, 0, 0), Child = new Panel { Children = { art, tag } } };
        // 12.0: точка статуса из project.json — собран ли, проверен ли в игре, менялся ли код после сборки.
        var status = p.Meta().Status(p.Updated);
        var state = Ui.Row(6, Ui.Dot(StatusBrush(status), 8), Ui.Text(I18n.T($"v12.pj.st.{status}.short"), "small"));
        ToolTip.SetTip(state, I18n.T($"v12.pj.st.{status}"));
        var when = HubLook.MonoText(Ui.Ago(p.Updated), 10.5);
        var foot = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(when, Dock.Right);
        foot.Children.Add(when);
        foot.Children.Add(state);
        var name = Ui.Text(p.Name, "h3");
        name.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
        var body = Ui.Col(2, name, Ui.Text(game?.Name ?? p.Game, "small muted"), new Border { Height = 1, Background = HubLook.Edge, Margin = new Thickness(0, 8, 0, 0) }, foot);
        body.Margin = new Thickness(14, 12, 14, 12);
        var card = new Button
        {
            Classes = { "tile" }, Margin = new Thickness(0, 0, 14, 14), Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel { Children = { top, body } },
        };
        card.Click += (_, _) => OpenProject(p);
        Ctx.Attach(card, () => ProjectMenu(p));
        return card;
    }

    /// <summary>Меню проекта (плитка «Моих проектов» и строка «Верстака»).</summary>
    MenuFlyout ProjectMenu(Project p) => Ctx.Menu(
        Ctx.Item(I18n.T("ctx.open"), Icons.Edit, () => OpenProject(p)),
        Ctx.Item(I18n.T("v12.pj.pane.overview"), Icons.Chart, () => OpenProject(p, "overview")),
        Ctx.Item(I18n.T("v12.pj.run"), Icons.Play, () => { OpenProject(p); BuildAndTest(); }, gesture: "F5"),
        Ctx.Item(I18n.T("ctx.duplicate"), Icons.Layers, () => { Projects.Create(p.Name + " (2)", File.ReadAllText(p.Script)); Build(); }),
        Ctx.Folder(I18n.T("games.openFolder"), p.Dir),
        Ctx.Copy(I18n.T("ctx.copyCode"), File.Exists(p.Script) ? File.ReadAllText(p.Script) : ""),
        "-",
        Ctx.Item(I18n.T("ctx.delete"), Icons.Trash, () => DeleteProject(p)));
}
public sealed partial class CreatorPage
{
    /// <summary>Открыть проект: редактор (code) или обзор (overview).</summary>
    void OpenProject(Project p, string pane = "code")
    {
        _tab = "mine";
        _pane = pane;
        if (pane == "overview") ForgetStats(p.Id);
        Open(p);
        Build();
    }

    void DeleteProject(Project p)
    {
        var w = MainWindow.Current!;
        w.Dialog(I18n.T("ctx.delete"), Ui.Text(I18n.T("cr.deleteProject", ("name", p.Name)), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("ctx.delete"), () => { w.CloseDialog(); Projects.Delete(p); if (_open?.Id == p.Id) _open = null; Build(); }, "primary", Icons.Trash));
    }

    /// <summary>«Начни с шаблона»: три шаблона и «Все шаблоны».</summary>
    Control StartFromTemplate()
    {
        var templates = new UniformGrid { Columns = 3 };
        foreach (var t in Templates.All.Take(3)) templates.Children.Add(TemplateCard(t));
        return Section(I18n.T("cr.home.templates"), () => Go("examples"), templates, eyebrow: I18n.T("v91.cr.tab.templates"));
    }
}
