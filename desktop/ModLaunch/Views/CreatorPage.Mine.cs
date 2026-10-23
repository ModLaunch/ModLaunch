using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Features;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Games;
using MX = ModScriptX;

namespace ModLaunch.Views;

public sealed partial class CreatorPage
{
    // ---------------------------------------------------------------- «Мои проекты → Доступные»

    /// <summary>
    /// 18.0: «Доступные» — всё, что уже есть: проекты ModScript X, классические проекты ModScript и приобретённые
    /// решения (купленные и импортированные скрипты, под защитой). Никаких постоянных кнопок на плитках: при наведении
    /// проявляются «Запуск» и «Настройка». Создание нового — пункт «Создать» слева (раньше то же делали
    /// баннер, кнопка в шапке и пунктирная плитка). «Опубликованные» — вкладкой в шапке.
    /// </summary>
    Control Available()
    {
        var col = new StackPanel { Spacing = 16 };
        if (_minePart == "published") { col.Children.Add(PublishedView()); return col; }

        // Проверка в игре закончится, пока смотрим список, — точка статуса обновится сама.
        HookTest();
        bool Match(string name) => _filter == "" || name.Contains(_filter, StringComparison.OrdinalIgnoreCase);
        var xs = XProjects.List().Where(p => Match(p.Name)).ToList();
        var projects = Projects.List().Where(p => Match(p.Name)).OrderByDescending(p => p.Updated).ToList();
        var owned = Features.Inject.All().Where(i => i.Sealed && Match(i.Title)).ToList();

        if (xs.Count + projects.Count + owned.Count == 0)
        {
            var empty = Ui.Col(12,
                Ui.Text(_filter == "" ? I18n.T("cr.empty") : I18n.T("cr.lib.none"), "h3"),
                Ui.Text(I18n.T("v18.av.empty"), "muted", wrap: true));
            if (_filter == "") empty.Children.Add(Ui.Row(10, Ui.Button(I18n.T("v18.hub.create"), () => Go("form"), "primary", Icons.Plus)));
            col.Children.Add(HubLook.Frame(empty, new Thickness(24, 20)));
            return col;
        }
        if (xs.Count > 0)
        {
            col.Children.Add(Section(I18n.T("v16.mine.x", ("n", xs.Count))));
            col.Children.Add(Tiles(xs.Select(XCard)));
        }
        if (projects.Count > 0)
        {
            col.Children.Add(Section(I18n.T("v16.mine.classic", ("n", projects.Count))));
            col.Children.Add(Tiles(projects.Select(ProjectCard)));
        }
        if (owned.Count > 0)
        {
            var all = Ui.Button(I18n.T("v18.av.owned.all"), () => Go("scripts"), "ghost", Icons.Shield);
            col.Children.Add(Section(I18n.T("v18.av.owned", ("n", owned.Count)), all));
            col.Children.Add(Tiles(owned.Select(OwnedCard)));
        }
        return col;
    }

    static UniformGrid Tiles(IEnumerable<Control> cards)
    {
        var grid = new UniformGrid { Columns = 4 };
        foreach (var c in cards) grid.Children.Add(c);
        return grid;
    }

    static Control Section(string title, Control? right = null)
    {
        var bar = new DockPanel { Margin = new Thickness(0, 6, 0, -4) };
        if (right is not null) { DockPanel.SetDock(right, Dock.Right); bar.Children.Add(right); }
        bar.Children.Add(HubLook.Eyebrow(title, HubLook.Violet));
        return bar;
    }

    /// <summary>
    /// Плитка «Доступных»: полоса цвета сверху (если есть), картинка с ярлыками, название, подпись и строка внизу.
    /// Над картинкой при наведении — «Запуск» и «Настройка»; клик по плитке — то же, что главное действие.
    /// </summary>
    static Button Tile(Control art, Control[] tags, string title, string sub, Control footLeft, Control footRight, Action launch, Action configure,
        IBrush? stripe = null, string? launchText = null, Action? click = null)
    {
        var top = new Panel { Children = { art } };
        foreach (var t in tags) top.Children.Add(t);
        var card = new Button
        {
            Classes = { "tile" }, Margin = new Thickness(0, 0, 14, 14), Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        top.Children.Add(HoverActions.OnArt(card, launch, configure, launchText));
        var name = Ui.Text(title, "h3");
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var subText = Ui.Text(sub, "small muted");
        subText.TextTrimming = TextTrimming.CharacterEllipsis;
        footLeft.Margin = new Thickness(0, 0, 8, 0);
        var foot = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(footRight, Dock.Right);
        foot.Children.Add(footRight);
        foot.Children.Add(footLeft);
        var body = Ui.Col(2, name, subText, new Border { Height = 1, Background = HubLook.Edge, Margin = new Thickness(0, 8, 0, 0) }, foot);
        body.Margin = new Thickness(14, 12, 14, 12);
        var content = new StackPanel();
        if (stripe is not null) content.Children.Add(new Border { Height = 3, Background = stripe });
        content.Children.Add(new Border { Height = 96, ClipToBounds = true, CornerRadius = stripe is null ? new CornerRadius(4, 4, 0, 0) : new CornerRadius(0), Child = top });
        content.Children.Add(body);
        card.Content = content;
        card.Click += (_, _) => (click ?? launch)();
        return card;
    }

    /// <summary>Ярлык в углу картинки: тёмный (версия) или цветной (вид проекта).</summary>
    static Border Tag(string text, IBrush? background, HorizontalAlignment h) => new()
    {
        Background = background ?? new SolidColorBrush(Color.FromArgb(215, 10, 12, 18)), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 2),
        Margin = new Thickness(8), HorizontalAlignment = h, VerticalAlignment = VerticalAlignment.Top, Child = HubLook.MonoText(text, 9.5, Brushes.White),
    };

    static Control NoArt(string icon) => new Border { Background = HubLook.Raised, Child = Ui.Icon(icon, 30, Ui.Res("Faint")) };

    /// <summary>
    /// Плитка проекта ModScript X: полоса цвета проекта, игра, путь моста (движок → язык), когда изменён.
    /// «Запуск» — среда ModScript X, «Настройка» — её панель настроек проекта (название, игра, движок, цвет).
    /// </summary>
    Control XCard(XProject p)
    {
        var game = p.Game == "" ? null : GameCatalog.ById(p.Game);
        var target = p.Target is { } t ? MX.Target.Find(t) : null;
        var route = HubLook.MonoText(target is null ? I18n.T("v16.sum.files") : $"{target.Title} → {XStudio.Lang(target.Language)}", 10.5, HubLook.Cyan);
        route.TextTrimming = TextTrimming.CharacterEllipsis;
        var hex = XProjects.Hex(p.Color);
        var sub = game?.Name ?? (p.GamePath is { } gp ? Path.GetFileName(gp.TrimEnd('\\', '/')) : I18n.T("v16.sum.noGame"));
        var card = Tile(game is not null ? Ui.GameImage(game, 400) : NoArt(Icons.Code),
            [Tag("MODSCRIPT X", Ui.Hex(hex), HorizontalAlignment.Left), Tag("v" + p.Version, null, HorizontalAlignment.Right)],
            p.Name, sub, route, HubLook.MonoText(Ui.Ago(p.Updated), 10.5),
            () => MainWindow.Current?.Navigate(() => new XStudio(p.Id)), () => MainWindow.Current?.Navigate(() => new XStudio(p.Id, "settings")), Ui.Hex(hex));
        Ctx.Attach(card, () => Ctx.Menu(
            Ctx.Item(I18n.T("v18.hover.launch"), Icons.Play, () => MainWindow.Current?.Navigate(() => new XStudio(p.Id))),
            Ctx.Item(I18n.T("v18.hover.configure"), Icons.Sliders, () => MainWindow.Current?.Navigate(() => new XStudio(p.Id, "settings"))),
            Ctx.Item(I18n.T("v16.x.side.bridge"), Icons.Layers, () => MainWindow.Current?.Navigate(() => new XStudio(p.Id, "bridge"))),
            Ctx.Folder(I18n.T("games.openFolder"), p.Dir),
            "-",
            Ctx.Item(I18n.T("ctx.delete"), Icons.Trash, () => DeleteX(p))));
        return card;
    }

    /// <summary>
    /// Плитка классического проекта ModScript: точка статуса (собран, проверен, менялся) и когда изменён.
    /// «Запуск» — редактор, «Настройка» — обзор проекта (сборки, выпуски, публикация).
    /// </summary>
    Control ProjectCard(Project p)
    {
        var game = GameCatalog.ById(p.Game);
        // 12.0: точка статуса из project.json — собран ли, проверен ли в игре, менялся ли код после сборки.
        var status = p.Meta().Status(p.Updated);
        var state = Ui.Row(6, Ui.Dot(StatusBrush(status), 8), Ui.Text(I18n.T($"v12.pj.st.{status}.short"), "small"));
        ToolTip.SetTip(state, I18n.T($"v12.pj.st.{status}"));
        var card = Tile(game is not null ? Ui.GameImage(game, 400) : NoArt(Icons.Creator),
            [Tag("MODSCRIPT", null, HorizontalAlignment.Left), Tag(p.Version == "" ? "v—" : "v" + p.Version, null, HorizontalAlignment.Right)],
            p.Name, game?.Name ?? p.Game, state, HubLook.MonoText(Ui.Ago(p.Updated), 10.5),
            () => OpenProject(p), () => OpenProject(p, "overview"));
        Ctx.Attach(card, () => ProjectMenu(p));
        return card;
    }

    /// <summary>
    /// Плитка приобретённого решения (купленный или импортированный скрипт): ярлык «Защищён», продавец, что сделала защита.
    /// «Запуск» — игра с этим скриптом, «Настройка» — игра, вкл/выкл, подробности защиты; клик — настройка.
    /// </summary>
    Control OwnedCard(InjectItem i)
    {
        var game = i.Game == "" ? null : GameCatalog.ById(i.Game);
        var shield = Ui.Row(6, Ui.Icon(Icons.Lock, 12, HubLook.Cyan), HubLook.MonoText(GuardLine(i), 10.5, HubLook.Cyan));
        var sub = (i.Source == "market" ? I18n.T("v18.lib.from", ("seller", i.Seller)) : I18n.T("v18.lib.imported")) + (game is null ? "" : " · " + game.Name);
        var card = Tile(game is not null ? Ui.GameImage(game, 400) : NoArt(Icons.Lock),
            [Tag(I18n.T("v18.lib.sealed").ToUpper(I18n.Culture), HubLook.Signature(), HorizontalAlignment.Left), Tag(I18n.T(i.Source == "market" ? "v18.lib.bought" : "v18.lib.import").ToUpper(I18n.Culture), null, HorizontalAlignment.Right)],
            i.Title, sub, shield, HubLook.MonoText(Ui.Ago(i.Added), 10.5),
            () => LaunchScript(i), () => ScriptSettings(i), launchText: I18n.T("v18.hover.play"), click: () => ScriptSettings(i));
        if (!i.Enabled) card.Opacity = 0.6;
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
