using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Games;

namespace ModLaunch.Views;

public sealed partial class CreatorPage
{
    // ---------------------------------------------------------------- мои моды

    /// <summary>Мои моды: проекты мастерской и опубликованные в Hub — на одной вкладке.</summary>
    Control Mine()
    {
        var col = new StackPanel { Spacing = 16 };
        // 12.1: «Проекты · Опубликованные» — вкладками в шапке Hub (MineTabs), здесь — только папка проектов.
        var bar = new DockPanel();
        if (_minePart == "projects")
        {
            var folder = Ui.Button(I18n.T("cr.folder"), () => Actions.OpenFolder(Projects.Root), "ghost", Icons.Folder);
            DockPanel.SetDock(folder, Dock.Right);
            bar.Children.Add(folder);
            bar.Children.Add(HubLook.MonoText("~/" + Path.GetFileName(Projects.Root), 11));
            col.Children.Add(bar);
        }
        if (_minePart == "published") { col.Children.Add(PublishedView()); return col; }

        // Проверка в игре закончится, пока смотрим список, — точка статуса обновится сама.
        HookTest();
        var projects = Projects.List().Where(p => _filter == "" || p.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase)).ToList();
        if (projects.Count == 0)
        {
            col.Children.Add(Ui.Card(Ui.Col(10,
                Ui.Text(I18n.T("cr.empty"), "h3"),
                Ui.Text(I18n.T("cr.empty.text"), "muted", wrap: true),
                Ui.Row(8, Ui.Button(I18n.T("cr.home.create"), NewProjectDialog, "primary", Icons.Plus), Ui.Button(I18n.T("cr.tab.examples"), () => Go("examples"), "", Icons.Wand))), 28));
            return col;
        }
        var grid = new UniformGrid { Columns = 4 };
        var plus = Ui.Col(10, new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(3), BorderBrush = HubLook.Edge, BorderThickness = new Thickness(1), Child = Ui.Icon(Icons.Plus, 18, HubLook.Violet), HorizontalAlignment = HorizontalAlignment.Center }, Ui.Text(I18n.T("cr.new"), "h3"));
        plus.HorizontalAlignment = HorizontalAlignment.Center;
        var add = new Button
        {
            Classes = { "card-btn", "dashed" }, Margin = new Thickness(0, 0, 14, 14), MinHeight = 180,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            Content = plus,
        };
        // 12.0: новый проект — через мастер (имя, игра, шаблон или импорт), см. CreatorPage.NewProject.cs.
        add.Click += (_, _) => NewProjectDialog();
        grid.Children.Add(add);
        foreach (var p in projects) grid.Children.Add(ProjectCard(p));
        col.Children.Add(grid);
        return col;
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
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(215, 10, 12, 18)), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 2),
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
}
