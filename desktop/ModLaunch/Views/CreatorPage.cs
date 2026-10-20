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
        // 9.3: Мастерская, рынок и студия продавца — разделы рынка креаторов (MarketPage).
        if (tab is "hub" or "market" or "studio")
        {
            var to = tab switch { "hub" => "workshop", "studio" => "studio", _ => "store" };
            MainWindow.Current?.Navigate(() => new MarketPage(to));
            return;
        }
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
        ("models", "v91.cr.tab.models", Icons.Cube), ("inject", "v11.cr.tab.lang", Icons.Code), ("examples", "v91.cr.tab.templates", Icons.Wand),
    ];

    public static readonly (string Id, string Key, string Icon)[] MoreTabs =
    [
        ("hub", "hub.tab", Icons.Globe), ("market", "mk.tab", Icons.Bag), ("studio", "st.tab", Icons.Chart), ("docs", "cr.tab.docs", Icons.Book),
        ("injects", "v11.hub.injects", Icons.Zap), ("mxdev", "v97.cr.tab.inject", Icons.Zap),
    ];

    public string Tab => _tab;

    static string TabTitle(string tab) => MainTabs.Concat(MoreTabs).Where(t => t.Id == tab).Select(t => t.Key).FirstOrDefault() is string key ? I18n.T(key) : I18n.T("cr.tab.home");

    public override IEnumerable<(string Text, Action? Open)> Crumbs
    {
        get
        {
            // 9.3: Creator Hub — это рынок; здесь — «Создать» (свои моды, код, модели, шаблоны).
            Action market = () => MainWindow.Current?.Navigate(() => new MarketPage());
            if (_tab == "home") return [("Creator Hub", market), (I18n.T("v93.mk.create"), null)];
            return [("Creator Hub", market), (I18n.T("v93.mk.create"), () => MainWindow.Current?.Navigate(() => new CreatorPage())), (TabTitle(_tab), null)];
        }
    }

    public override void Build()
    {
        var content = StoreKit.Fit(new StackPanel { Spacing = 22, Margin = new Thickness(40, 24, 40, 40), MaxWidth = 1640 });

        // 9.5: та же шапка и навигация, что у рынка (HubNav): мастерская — это раздел «Автору».
        var hubTab = _tab switch
        {
            "examples" => "templates",
            "docs" => "docs",
            "hub" => "workshop",
            "market" => "store",
            "studio" => "studio",
            "inject" or "mxdev" => "lang",
            "injects" => "injects",
            _ => "projects",
        };
        // Внутри «Проектов» — свои вкладки (обзор, все мои, коды, модели, ModScript X) в той же линии.
        List<Control>? sub = null;
        Control? create = null;
        if (hubTab == "projects")
        {
            sub = [];
            foreach (var (id, key, _) in MainTabs.Where(t => t.Id is not ("examples" or "inject")))
            {
                var tab = id;
                var b = Ui.Button(I18n.T(key), () => Go(tab), "pivot");
                b.Padding = new Thickness(10, 6);
                b.FontSize = 13;
                b.VerticalAlignment = VerticalAlignment.Center;
                if (_tab == id) { b.Classes.Add("active"); b.Background = Ui.Res("Surface2"); }
                sub.Add(b);
            }
            create = Ui.Button(I18n.T("v95.hub.newProject"), CreateNew, "primary", Icons.Plus);
        }
        content.Children.Add(HubNav.Header(hubTab, sold: () => { _ = LoadMarket(force: true); _studio = null; }, sub: sub, action: create));

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
            "inject" => Lang(),
            "mxdev" => Injector(),
            "injects" => Injects(),
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