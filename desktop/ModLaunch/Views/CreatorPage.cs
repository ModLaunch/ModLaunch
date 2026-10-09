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
    /// <summary>Часть «Моих модов»: projects — проекты, published — опубликованные в Hub.</summary>
    string _minePart = "projects";
    readonly ScriptEditor _code = new() { MinHeight = 480 };
    readonly StackPanel _check = new() { Spacing = 8 };
    DispatcherTimer? _debounce;
    bool _dirty;

    public CreatorPage(string tab = "home", string? project = null)
    {
        _tab = tab;
        if (tab == "published") { _tab = "mine"; _minePart = "published"; }
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

    public override void Search(string text) { _filter = text.Trim(); if (_open is null && _tab is "mine" or "home" or "studio") _tab = "hub"; Build(); }

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

    /// <summary>Перейти на вкладку; несохранённый код сохраняется.</summary>
    void Go(string tab)
    {
        if (_dirty) Save(quiet: true);
        _tab = tab;
        if (tab != "mine") _open = null;
        Build();
    }

    /// <summary>
    /// 9.1 (по эскизу): главные вкладки — «Главная · Мои · Коды · Модели · Шаблоны».
    /// Hub, маркет, студия продавца и справка — справа «Ещё» и в выдвижной полоске у левого края.
    /// </summary>
    public static readonly (string Id, string Key, string Icon)[] MainTabs =
    [
        ("home", "cr.tab.home", Icons.Home), ("mine", "v91.cr.tab.my", Icons.Edit), ("codes", "v91.cr.tab.codes", Icons.Code),
        ("models", "v91.cr.tab.models", Icons.Cube), ("examples", "v91.cr.tab.templates", Icons.Wand),
    ];

    public static readonly (string Id, string Key, string Icon)[] MoreTabs =
    [
        ("hub", "hub.tab", Icons.Globe), ("market", "mk.tab", Icons.Bag), ("studio", "st.tab", Icons.Chart), ("docs", "cr.tab.docs", Icons.Book),
    ];

    public string Tab => _tab;

    static string TabTitle(string tab) => MainTabs.Concat(MoreTabs).Where(t => t.Id == tab).Select(t => t.Key).FirstOrDefault() is string key ? I18n.T(key) : I18n.T("cr.tab.home");

    public override IEnumerable<(string Text, Action? Open)> Crumbs
    {
        get
        {
            if (_tab == "home") return [("Creator Hub", (Action?)null)];
            return [("Creator Hub", () => MainWindow.Current?.Navigate(() => new CreatorPage())), (TabTitle(_tab), null)];
        }
    }

    public override void Build()
    {
        var content = new StackPanel { Spacing = 22, Margin = new Thickness(40, 24, 40, 40), MaxWidth = 1640 };

        // Шапка: логотип и название; на «Главной» крупно, на вкладках — компактно (эскиз: «Creator Hub» сверху).
        var home = _tab == "home";
        var words = home
            ? Ui.Col(4, Ui.Text("Creator Hub", "h1"), Ui.Text(I18n.T("cr.home.hello"), "muted"))
            : Ui.Col(2, Ui.Text("Creator Hub", "h2"), Ui.Text(I18n.T("cr.subtitle"), "small muted"));
        words.VerticalAlignment = VerticalAlignment.Center;
        content.Children.Add(Ui.Row(home ? 18 : 14, CreatorLogo.Tile(home ? 64 : 44), words));

        var tabs = new List<Control>();
        foreach (var (id, key, icon) in MainTabs)
        {
            var tab = id;
            var b = Ui.Button(I18n.T(key), () => Go(tab), "tab", icon);
            if (_tab == id) b.Classes.Add("active");
            tabs.Add(b);
        }
        // Открыт раздел из «Ещё» — он тоже виден вкладкой, чтобы было понятно, где мы.
        var extra = Array.FindIndex(MoreTabs, t => t.Id == _tab);
        if (extra >= 0)
        {
            var (extraId, extraKey, extraIcon) = MoreTabs[extra];
            tabs.Add(Ui.Button(I18n.T(extraKey), () => Go(extraId), "tab active", extraIcon));
        }
        var bar = new DockPanel();
        var more = Ui.Row(2);
        foreach (var (id, key, icon) in MoreTabs)
        {
            if (id == _tab) continue;
            var tab = id;
            var link = Ui.Button(I18n.T(key), () => Go(tab), "ghost", icon);
            link.Foreground = Ui.Res("Muted");
            link.Padding = new Thickness(10, 6);
            link.FontSize = 13;
            more.Children.Add(link);
        }
        more.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(more, Dock.Right);
        bar.Children.Add(more);
        bar.Children.Add(Ui.TabBar(tabs.ToArray()));
        content.Children.Add(bar);

        content.Children.Add(_tab switch
        {
            "home" => Home(),
            "examples" => Examples(),
            "codes" => Shelf("codes"),
            "models" => Shelf("models"),
            "hub" => HubView(),
            "market" => MarketView(),
            "studio" => Studio(),
            "docs" => Docs(),
            _ => _open is null ? Mine() : Editor(),
        });
        Content = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    /// <summary>
    /// «Создать» (эскиз Creator Hub, «с теми же анимациями»): новый проект, та же анимация,
    /// что и у «Скачать», — и сразу редактор.
    /// </summary>
    public static void CreateNew()
    {
        var p = Projects.Create(I18n.Lang == "ru" ? "Мой мод" : "My mod", Templates.ById("blank")!.Code);
        var game = GameCatalog.ById(p.Game);
        InstallFx.Play(new FxCard(p.Name, I18n.T("v91.fx.project") + (game is null ? "" : " · " + game.Name), null, game, Icons.Plus), null,
            () => MainWindow.Current?.Navigate(() => new CreatorPage("mine", p.Id)));
    }
}