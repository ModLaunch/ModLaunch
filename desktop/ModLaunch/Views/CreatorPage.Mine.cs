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
        var parts = Ui.Row(6);
        foreach (var (id, key) in new[] { ("projects", "cr.mine.projects"), ("published", "cr.mine.published") })
        {
            var part = id;
            var chip = Ui.Button(I18n.T(key), () => { _minePart = part; Build(); }, "chip");
            if (_minePart == id) chip.Classes.Add("active");
            parts.Children.Add(chip);
        }
        var bar = new DockPanel();
        if (_minePart == "projects")
        {
            var folder = Ui.Button(I18n.T("cr.folder"), () => Actions.OpenFolder(Projects.Root), "ghost", Icons.Folder);
            DockPanel.SetDock(folder, Dock.Right);
            bar.Children.Add(folder);
        }
        bar.Children.Add(parts);
        col.Children.Add(bar);
        if (_minePart == "published") { col.Children.Add(PublishedView()); return col; }

        var projects = Projects.List().Where(p => _filter == "" || p.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase)).ToList();
        if (projects.Count == 0)
        {
            col.Children.Add(Ui.Card(Ui.Col(10,
                Ui.Text(I18n.T("cr.empty"), "h3"),
                Ui.Text(I18n.T("cr.empty.text"), "muted", wrap: true),
                Ui.Row(8, Ui.Button(I18n.T("cr.home.create"), NewProject, "primary", Icons.Plus), Ui.Button(I18n.T("cr.tab.examples"), () => Go("examples"), "", Icons.Wand))), 28));
            return col;
        }
        var grid = new UniformGrid { Columns = 4 };
        var plus = Ui.Col(8, new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(22), Background = Ui.Res("Surface3"), Child = Ui.Icon(Icons.Plus, 20) }, Ui.Text(I18n.T("cr.new"), "h3"));
        plus.HorizontalAlignment = HorizontalAlignment.Center;
        var add = new Button
        {
            Classes = { "card-btn", "dashed" }, Margin = new Thickness(0, 0, 14, 14), MinHeight = 180,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            Content = plus,
        };
        add.Click += (_, _) => NewProject();
        grid.Children.Add(add);
        foreach (var p in projects) grid.Children.Add(ProjectCard(p));
        col.Children.Add(grid);
        return col;
    }

    void NewProject()
    {
        var p = Projects.Create("Мой мод", Templates.ById("blank")!.Code);
        _tab = "mine";
        Open(p);
        Build();
    }

    /// <summary>Карточка проекта: картинка игры, название, игра, когда изменён.</summary>
    Control ProjectCard(Project p)
    {
        var game = GameCatalog.ById(p.Game);
        Control art = game is not null
            ? Ui.GameImage(game, 400)
            : new Border { Background = Ui.Res("Surface3"), Child = Ui.Icon(Icons.Creator, 30, Ui.Res("Faint")) };
        var top = new Border { Height = 96, ClipToBounds = true, CornerRadius = new CornerRadius(16, 16, 0, 0), Child = art };
        var body = Ui.Col(3,
            Ui.Text(p.Name, "h3"),
            Ui.Text(game?.Name ?? p.Game, "small muted"),
            Ui.Text(I18n.T("cr.edited", ("when", Ui.Ago(p.Updated))), "small muted"));
        body.Margin = new Thickness(14, 12, 14, 14);
        var card = new Button
        {
            Classes = { "tile" }, Margin = new Thickness(0, 0, 14, 14), Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel { Children = { top, body } },
        };
        card.Click += (_, _) => { _tab = "mine"; Open(p); Build(); };
        Ctx.Attach(card, () => Ctx.Menu(
            Ctx.Item(I18n.T("ctx.open"), Icons.Edit, () => { _tab = "mine"; Open(p); Build(); }),
            Ctx.Item(I18n.T("ctx.duplicate"), Icons.Layers, () => { Projects.Create(p.Name + " (2)", File.ReadAllText(p.Script)); Build(); }),
            Ctx.Folder(I18n.T("games.openFolder"), p.Dir),
            Ctx.Copy(I18n.T("ctx.copyCode"), File.Exists(p.Script) ? File.ReadAllText(p.Script) : ""),
            "-",
            Ctx.Item(I18n.T("ctx.delete"), Icons.Trash, () => DeleteProject(p))));
        return card;
    }
}
public sealed partial class CreatorPage
{
    void DeleteProject(Project p)
    {
        var w = MainWindow.Current!;
        w.Dialog(I18n.T("ctx.delete"), Ui.Text(I18n.T("cr.deleteProject", ("name", p.Name)), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("ctx.delete"), () => { w.CloseDialog(); Projects.Delete(p); if (_open?.Id == p.Id) _open = null; Build(); }, "primary", Icons.Trash));
    }
}
