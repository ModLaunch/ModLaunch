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
    // ---------------------------------------------------------------- «Мои моды»

    /// <summary>
    /// 22.0: «Мои моды» — одна полка для всего, что написано на ModScript: и скрипты (.modx), и правки файлов
    /// (.mls, раньше «классические проекты»), от последнего изменённого. Ниже — купленные и импортированные
    /// скрипты (под защитой). Плитка — обложка игры с ярлыком мода цвета проекта, как подписанный картридж;
    /// при наведении — «Открыть» и «Настройка».
    /// </summary>
    Control Available()
    {
        var col = new StackPanel { Spacing = 18 };
        if (_minePart == "published") { col.Children.Add(PublishedView()); return col; }
        if (_minePart == "stats") { col.Children.Add(StatsView()); return col; }

        // Проверка в игре закончится, пока смотрим список, — точка статуса обновится сама.
        HookTest();
        bool Match(string name) => _filter == "" || name.Contains(_filter, StringComparison.OrdinalIgnoreCase);
        var xs = XProjects.List().Where(p => Match(p.Name)).ToList();
        var entries = xs.Select(p => (p.Game, p.Name, When: p.Updated, Make: (Func<Control>)(() => XCard(p))))
            .Concat(Projects.List().Where(p => Match(p.Name) && xs.All(x => !string.Equals(x.Dir, p.Dir, StringComparison.OrdinalIgnoreCase)))
                .Select(p => (p.Game, p.Name, When: p.Updated, Make: (Func<Control>)(() => ProjectCard(p)))))
            .ToList();
        var owned = Features.Inject.All().Where(i => i.Sealed && Match(i.Title)).ToList();

        // 23.0: над полкой — игры (с числом модов), порядок и «Новый мод».
        if (entries.Count > 0)
        {
            var chips = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            void Chip(string text, bool on, Action pick)
            {
                var b = Ui.Button(text, () => { pick(); Build(); }, on ? "chip active" : "chip");
                b.Margin = new Thickness(0, 0, 6, 6);
                chips.Children.Add(b);
            }
            var games = entries.Where(e => e.Game != "").GroupBy(e => e.Game).OrderByDescending(g => g.Count()).ToList();
            if (_mineGame is not null && games.All(g => g.Key != _mineGame)) _mineGame = null;
            if (games.Count > 1)
            {
                Chip($"{I18n.T("hub.allGames")}  {entries.Count}", _mineGame is null, () => _mineGame = null);
                foreach (var g in games)
                {
                    var id = g.Key;
                    Chip($"{GameCatalog.ById(id)?.ShortName ?? id}  {g.Count()}", _mineGame == id, () => _mineGame = _mineGame == id ? null : id);
                }
            }
            var sorts = new[] { "recent", "name", "game" };
            var sort = new ComboBox { Width = 190, ItemsSource = sorts.Select(s => I18n.T("v23.mine.sort." + s)).ToList(), SelectedIndex = Math.Max(0, Array.IndexOf(sorts, _mineSort)), VerticalAlignment = VerticalAlignment.Center };
            sort.SelectionChanged += (_, _) => { var v = sorts[Math.Max(0, sort.SelectedIndex)]; if (v != _mineSort) { _mineSort = v; Build(); } };
            // 26.0: «Новый мод» — в шапке раздела (как «Импортировать скрипт» у «Скриптов»): у вкладок одно место главной кнопки.
            var tools = Ui.Row(10, sort);
            tools.VerticalAlignment = VerticalAlignment.Top;
            var bar = new DockPanel();
            DockPanel.SetDock(tools, Dock.Right);
            bar.Children.Add(tools);
            bar.Children.Add(chips);
            col.Children.Add(bar);
        }
        var shown = entries.Where(e => _mineGame is null || e.Game == _mineGame);
        shown = _mineSort switch
        {
            "name" => shown.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase),
            "game" => shown.OrderBy(e => GameCatalog.ById(e.Game)?.Name ?? "~", StringComparer.CurrentCultureIgnoreCase).ThenByDescending(e => e.When),
            _ => shown.OrderByDescending(e => e.When),
        };
        var mods = shown.Select(e => e.Make()).ToList();

        if (mods.Count + owned.Count == 0)
        {
            // 26.0: пустая полка — общее пустое состояние хаба, «Путь автора» и примеры для своих игр (раньше — строка и кнопка).
            if (_filter == "") EmptyMine(col);
            else col.Children.Add(HubLook.Empty(Icons.Search, I18n.T("cr.lib.none"), I18n.T("v18.av.empty")));
            return col;
        }
        if (mods.Count > 0) col.Children.Add(Tiles(mods));
        if (owned.Count > 0)
        {
            var all = Ui.Button(I18n.T("v18.av.owned.all"), () => Go("scripts"), "ghost", Icons.Shield);
            col.Children.Add(Section(I18n.T("v22.mods.owned", ("n", owned.Count)), all));
            col.Children.Add(Tiles(owned.Select(OwnedCard)));
        }
        // 23.0: карты, сделанные конструктором прямо в игре (режим разработчика).
        var worlds = AppState.Games.Where(g => g.Status == Detect.Found && g.Path is not null && (_mineGame is null || g.Def.Id == _mineGame))
            .SelectMany(g => DevMode.Worlds(g.Path).Where(w => Match(w.Level)).Select(w => (Game: g, w.Level, w.Objects, w.At))).ToList();
        if (worlds.Count > 0)
        {
            col.Children.Add(Section(I18n.T("v23.dev.worlds", ("n", worlds.Count))));
            col.Children.Add(Tiles(worlds.Select(w => WorldCard(w.Game, w.Level, w.Objects, w.At))));
        }
        return col;
    }

    /// <summary>Карта из конструктора в игре: обложка игры, ярлык с уровнем, сколько изменений; «Открыть» — снова в игру разработчиком.</summary>
    Control WorldCard(GameState g, string level, int objects, DateTime at)
    {
        var state = Ui.Row(6, Ui.Icon(Icons.Cube, 12, HubLook.Violet), Ui.Text(I18n.T("v23.dev.changes", ("n", objects)), "small"));
        var card = Tile(Cover(g.Def), [Tag(I18n.T("v23.dev.map"), null, HorizontalAlignment.Right)],
            level, g.Def.Name, state, Ui.Text(Ui.Ago(at), "small muted"),
            () => DevLaunch.Start(g), () => Actions.OpenFolder(DevMode.ConfigDir(g.Path!)), Ui.Hex("#2F7D6B"), launchText: I18n.T("v23.dev.open"));
        Ctx.Attach(card, () => Ctx.Menu(
            Ctx.Item(I18n.T("v23.dev.open"), Icons.Play, () => DevLaunch.Start(g)),
            Ctx.Folder(I18n.T("games.openFolder"), DevMode.ConfigDir(g.Path!))));
        return card;
    }

    /// <summary>
    /// 26.0: сетка плиток — столбцов столько, сколько влезает плиток от 230 px (было всегда 4: на широком окне плитки
    /// раздувались, на узком сжимались). Поле −16 справа съедает отступ последней плитки — край сетки ровно по краю шапки.
    /// </summary>
    static UniformGrid Tiles(IEnumerable<Control> cards)
    {
        const double min = 230, gap = 16;
        var grid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, -gap, 0) };
        foreach (var c in cards) grid.Children.Add(c);
        grid.SizeChanged += (_, e) =>
        {
            var cols = Math.Max(1, (int)(e.NewSize.Width / (min + gap)));
            if (grid.Columns != cols) grid.Columns = cols;
        };
        return grid;
    }

    /// <summary>
    /// 26.0: обложка плитки — фон игры без логотипа (hero). Шапка магазина с вшитым логотипом в низкой плитке
    /// резалась («STARDEV VALLEY», «ETHAL COMPANY»), и ярлык мода ложился прямо на буквы.
    /// </summary>
    static Control Cover(GameDef game) => Ui.GameImage(game, 800, art: Images.Art.Hero);

    /// <summary>Ярлык с названием мода цвета проекта — на обложке плитки и в карточке анкеты.</summary>
    internal static Border Plate(TextBlock text, Color color, double margin = 10)
    {
        text.FontSize = 14;
        text.FontWeight = FontWeight.SemiBold;
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        text.Foreground = IsLightColor(color) ? HubLook.OnAmber : Brushes.White;
        // 26.0: радиус 6 и без тени — затемнение низа обложки и так даёт контраст.
        return new Border
        {
            Background = new SolidColorBrush(color), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 4),
            Margin = new Thickness(margin), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Child = text,
        };
    }

    /// <summary>Затемнение к низу обложки: ярлык читается на любой картинке.</summary>
    internal static Border Scrim() => new()
    {
        IsHitTestVisible = false,
        Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.45, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(0, 0, 0, 0), 0), new GradientStop(Color.FromArgb(150, 0, 0, 0), 1) },
        },
    };

    static Control Section(string title, Control? right = null)
    {
        var bar = new DockPanel { Margin = new Thickness(0, 8, 0, -2) };
        if (right is not null) { DockPanel.SetDock(right, Dock.Right); bar.Children.Add(right); }
        bar.Children.Add(HubLook.Eyebrow(title));
        return bar;
    }

    /// <summary>Светлый ли цвет ярлыка: на светлом — тёмный текст, на тёмном — белый.</summary>
    static bool IsLightColor(Color c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B > 160;

    /// <summary>
    /// Плитка мода — «картридж»: обложка игры, на ней ярлык с названием мода цвета проекта (label), в углу —
    /// версия или пометка (tags); под обложкой — игра и строка состояния. При наведении — «Открыть» и «Настройка»;
    /// клик по плитке — главное действие.
    /// </summary>
    static Button Tile(Control art, Control[] tags, string title, string sub, Control footLeft, Control footRight, Action launch, Action configure,
        IBrush? label = null, string? launchText = null, Action? click = null)
    {
        var plateColor = (label as ISolidColorBrush)?.Color ?? HubLook.AmberColor;
        var plate = Plate(new TextBlock { Text = title }, plateColor);
        var top = new Panel { Children = { art, Scrim() } };
        foreach (var t in tags) top.Children.Add(t);
        top.Children.Add(plate);
        var card = new Button
        {
            Classes = { "tile" }, Margin = new Thickness(0, 0, 16, 16), Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        top.Children.Add(HoverActions.OnArt(card, launch, configure, launchText));
        var subText = Ui.Text(sub, "small muted");
        subText.TextTrimming = TextTrimming.CharacterEllipsis;
        footLeft.Margin = new Thickness(0, 0, 8, 0);
        var foot = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(footRight, Dock.Right);
        foot.Children.Add(footRight);
        foot.Children.Add(footLeft);
        var body = Ui.Col(0, subText, foot);
        body.Margin = new Thickness(14, 10, 14, 12);
        var content = new StackPanel
        {
            Children = { new Border { Height = 150, ClipToBounds = true, CornerRadius = new CornerRadius(10, 10, 0, 0), Child = top }, body },
        };
        card.Content = content;
        ToolTip.SetTip(card, title);
        card.Click += (_, _) => (click ?? launch)();
        return card;
    }

    /// <summary>Пометка в правом верхнем углу обложки: версия, «Куплен», «Импорт». 26.0: общий ярлык хаба (тёмный на картинке).</summary>
    static Border Tag(string text, IBrush? background, HorizontalAlignment h)
    {
        var badge = HubLook.Badge(text, "onArt");
        if (background is not null) badge.Background = background;
        badge.Margin = new Thickness(8);
        badge.HorizontalAlignment = h;
        badge.VerticalAlignment = VerticalAlignment.Top;
        return badge;
    }

    static Control NoArt(string icon) => new Border { Background = HubLook.Raised, Child = Ui.Icon(icon, 30, Ui.Res("Faint")) };

    /// <summary>
    /// Плитка скрипта ModScript (.modx): ярлык цвета проекта, игра, куда переводится мод (движок и язык), когда изменён.
    /// «Открыть» — среда ModScript, «Настройка» — её панель настроек проекта (название, игра, движок, цвет).
    /// </summary>
    Control XCard(XProject p)
    {
        var game = p.Game == "" ? null : GameCatalog.ById(p.Game);
        var target = p.Target is { } t ? MX.Target.Find(t) : null;
        // 23.0: под обложкой — стоит ли мод в игре сейчас (и не изменён ли после установки); путь кода — в подсказке.
        var (st, brush) = XState(p);
        var state = Ui.Row(6, Ui.Dot(brush, 8), Ui.Text(I18n.T("v23.mine.st." + st), "small"));
        ToolTip.SetTip(state, target is null ? I18n.T("v16.sum.files") : I18n.T("v22.mods.route", ("engine", target.Title)));
        var hex = XProjects.Hex(p.Color);
        var sub = game?.Name ?? (p.GamePath is { } gp ? Path.GetFileName(gp.TrimEnd('\\', '/')) : I18n.T("v16.sum.noGame"));
        var when = Ui.Text(Ui.Ago(p.Updated), "small muted");
        var card = Tile(game is not null ? Cover(game) : NoArt(Icons.Code),
            [Tag("v" + p.Version, null, HorizontalAlignment.Right)],
            p.Name, sub, state, when,
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
        var card = Tile(game is not null ? Cover(game) : NoArt(Icons.Creator),
            [Tag(p.Version == "" ? "v—" : "v" + p.Version, null, HorizontalAlignment.Right)],
            p.Name, game?.Name ?? p.Game, state, Ui.Text(Ui.Ago(p.Updated), "small muted"),
            () => OpenInStudio(p), () => OpenInStudio(p, "settings"));
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
        // 26.0: строка состояния — тем же шрифтом, что у своих модов («Новый», «В игре»), а не мелким моноширинным.
        var shield = Ui.Row(6, Ui.Icon(Icons.Lock, 12, HubLook.Cyan), Ui.Text(I18n.T("v18.lib.sealed"), "small"));
        ToolTip.SetTip(shield, GuardLine(i));
        var sub = game?.Name ?? (i.Source == "market" ? I18n.T("v18.lib.from", ("seller", i.Seller)) : I18n.T("v18.lib.imported"));
        var card = Tile(game is not null ? Cover(game) : NoArt(Icons.Lock),
            [Tag(I18n.T(i.Source == "market" ? "v18.lib.bought" : "v18.lib.import"), null, HorizontalAlignment.Right)],
            i.Title, sub, shield, Ui.Text(Ui.Ago(i.Added), "small muted"),
            () => LaunchScript(i), () => ScriptSettings(i), Ui.Hex("#3B4252"), launchText: I18n.T("v18.hover.play"), click: () => ScriptSettings(i));
        if (!i.Enabled) card.Opacity = 0.6;
        return card;
    }

    void DeleteX(XProject p)
    {
        var w = MainWindow.Current;
        if (w is null) return;
        w.Dialog(I18n.T("v16.x.delete.title", ("name", p.Name)), Ui.Text(I18n.T("v16.x.delete.text"), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog, "ghost"),
            Ui.Button(I18n.T("ctx.delete"), () => { w.CloseDialog(); XProjects.Delete(p); Build(); }, "danger", Icons.Trash));
    }

    /// <summary>
    /// Вкладки «Моих проектов» для шапки Hub: проекты мастерской, защищённые скрипты (20.0: бывший пункт «Скрипты» слева)
    /// и опубликованные в Hub.
    /// </summary>
    List<Control> MineTabs()
    {
        var tabs = new List<Control>();
        // 26.0: четвёртая вкладка — «Статистика» автора (CreatorPage.Stats.cs).
        foreach (var (id, key) in new[] { ("projects", "cr.mine.projects"), ("scripts", "v18.hub.scripts"), ("published", "cr.mine.published"), ("stats", "v26.features.stats.tab") })
        {
            var part = id;
            var b = Ui.Button(I18n.T(key), () =>
            {
                if (part == "scripts") { Go("scripts"); return; }
                _minePart = part;
                Go("home");
            }, "hub-tab");
            if (id == "scripts" ? _tab == "scripts" : _tab != "scripts" && _minePart == id) b.Classes.Add("active");
            tabs.Add(b);
        }
        return tabs;
    }

    /// <summary>Меню проекта (плитка «Моих проектов» и строка «Верстака»).</summary>
    MenuFlyout ProjectMenu(Project p) => Ctx.Menu(
        Ctx.Item(I18n.T("ctx.open"), Icons.Edit, () => OpenInStudio(p)),
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
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog, "ghost"),
            // 26.0: удаление — красным тоном «danger», а не янтарной главной кнопкой (она звала бы удалить).
            Ui.Button(I18n.T("ctx.delete"), () => { w.CloseDialog(); Projects.Delete(p); if (_open?.Id == p.Id) _open = null; Build(); }, "danger", Icons.Trash));
    }
}
public sealed partial class CreatorPage
{
    /// <summary>
    /// 22.0: проект правок файлов (main.mls) открывается в той же среде ModScript, что и скрипты: проверка на лету,
    /// F5 — что изменит мод, F6 — собрать и поставить в игру. Папка проекта остаётся на месте.
    /// </summary>
    internal static void OpenInStudio(Project p, string? side = null)
    {
        XProject x;
        try { x = XProjects.Adopt(p); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { MainWindow.Current?.Toast(e.Message, bad: true); return; }
        MainWindow.Current?.Navigate(() => new XStudio(x.Id, side));
    }
}
public sealed partial class CreatorPage
{
    /// <summary>«Мои моды»: выбранная игра (null — все) и порядок (recent, name, game) — на сеанс.</summary>
    static string? _mineGame;
    static string _mineSort = "recent";

    /// <summary>
    /// 23.0: состояние скрипта ModScript для плитки: draft — без игры, notyet — ещё не ставили, ingame — стоит и
    /// не менялся после установки, changed — код новее установленного (поставить заново, F6).
    /// </summary>
    static (string Key, IBrush Brush) XState(XProject p)
    {
        var path = p.GamePath ?? (p.Game != "" ? AppState.Games.FirstOrDefault(g => g.Def.Id == p.Game && g.Status == Detect.Found)?.Path : null);
        if (path is null) return ("draft", Ui.Res("Faint"));
        if (!p.Main.EndsWith(".modx", StringComparison.OrdinalIgnoreCase)) return ("notyet", Ui.Res("Faint"));
        DateTime? at;
        try { at = MX.Injector.InstalledAt(MX.ModxDoc.Parse(File.ReadAllText(p.Main)).Id, path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException) { at = null; }
        if (at is null) return ("notyet", Ui.Res("Faint"));
        return at.Value >= p.Updated.AddSeconds(-2) ? ("ingame", Ui.Res("Good")) : ("changed", Ui.Res("Warn"));
    }
}
