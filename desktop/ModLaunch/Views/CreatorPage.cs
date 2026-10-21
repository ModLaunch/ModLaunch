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

    /// <summary>12.0: подсказка поиска — о том, что ищется на этой вкладке (см. <see cref="SearchTab"/>).</summary>
    public override string SearchHint => _tab switch
    {
        "examples" => I18n.T("v12h2.search.templates"),
        "codes" or "models" or "hub" => I18n.T("cr.search"),
        _ => I18n.T("v12h2.search.projects"),
    };

    string _tab;
    Project? _open;
    string _filter = "";
    /// <summary>Часть «Моих модов»: projects — проекты, published — опубликованные в Hub.</summary>
    string _minePart = "projects";
    readonly ScriptEditor _code = new() { MinHeight = 480 };
    readonly StackPanel _check = new() { Spacing = 8 };
    DispatcherTimer? _debounce;
    bool _dirty;
    /// <summary>Страница сейчас строится: загрузки, начатые из Build, не строят её второй раз изнутри.</summary>
    bool _building;
    bool _hadWallet;
    static bool _walletAsked, _rebuildQueued;

    public CreatorPage(string tab = "home", string? project = null)
    {
        // 12.0: витрина и студия продаж живут на странице рынка (Go туда и ведёт); старые ссылки — на обзор проектов.
        _tab = tab is "market" or "studio" ? "home" : tab;
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
        DetachedFromVisualTree += (_, _) => { if (_dirty) Save(quiet: true); MarketData.Changed -= OnMarketData; };
        AttachedToVisualTree += (_, _) => { MarketData.Changed -= OnMarketData; MarketData.Changed += OnMarketData; };
    }

    /// <summary>
    /// 12.0: где показать результат поиска. Вкладки со своим списком (Мои, Коды, Модели, Шаблоны, Hub)
    /// фильтруют его; Обзор, редактор, справка и Lang ведут к своим проектам. Раньше любой поиск
    /// с Обзора и «Моих» уводил в старый вид Hub, и фильтр проектов был недостижим.
    /// </summary>
    internal static string SearchTab(string tab, bool editing, string query) =>
        query == "" || !editing && tab is "mine" or "codes" or "models" or "examples" or "hub" ? tab : "mine";

    public override void Search(string text)
    {
        _filter = text.Trim();
        var to = SearchTab(_tab, _open is not null, _filter);
        if (to != _tab || _open is not null && _filter != "")
        {
            if (_dirty) Save(quiet: true);
            _open = null;
            _tab = to;
            _minePart = "projects";
        }
        Build();
        MainWindow.Current?.RenderCrumbs();
    }

    /// <summary>12.0: рынок и мастерская — один экран Creator Hub: переход между ними без въезда всей страницы.</summary>
    public override bool SameScreenAs(Page? previous) => HubNav.IsHub(previous);

    /// <summary>Баланс для шапки догрузился — показать его (если на экране список, а не редактор).</summary>
    void OnMarketData()
    {
        if (!_hadWallet && MarketData.Wallet is not null && _open is null && MainWindow.Current?.CurrentPage == this) RebuildCurrent();
    }

    /// <summary>
    /// Данные Hub или рынка догрузились: перестроить ту мастерскую, что сейчас на экране, — одну, сколько бы
    /// загрузок ни закончилось разом. Раньше перестраивалась страница, начавшая загрузку, даже если с неё уже ушли,
    /// а открытая осталась бы с «загрузкой».
    /// </summary>
    static void RebuildCurrent()
    {
        if (_rebuildQueued) return;
        _rebuildQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _rebuildQueued = false;
            if (MainWindow.Current?.CurrentPage is CreatorPage page) page.Build();
        }, DispatcherPriority.Background);
    }

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
        ("hub", "hub.tab", Icons.Globe), ("docs", "cr.tab.docs", Icons.Book),
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

    /// <summary>9.5: та же шапка и навигация, что у рынка (HubNav): мастерская — это раздел «Автору».</summary>
    internal static string HubTabOf(string tab) => tab switch
    {
        "examples" => "templates",
        "docs" => "docs",
        "hub" => "workshop",
        "inject" or "mxdev" => "lang",
        "injects" => "injects",
        _ => "projects",
    };

    public override void Build()
    {
        // 12.0: один проход по проектам на постройку (Обзор, «Студия автора» и «Мои» читали все main.mls каждая).
        _building = true;
        try { using (Projects.Batch()) BuildNow(); }
        finally { _building = false; }
    }

    void BuildNow()
    {
        var content = StoreKit.Fit(new StackPanel { Spacing = 22, Margin = new Thickness(40, 24, 40, 40), MaxWidth = 1640 });
        // Баланс в шапке берётся из общих данных рынка: открыли сразу мастерскую — загрузить их (один раз за сеанс).
        if (Social.Account.SignedIn && MarketData.Wallet is null && !MarketData.Loading && !_walletAsked && !Program.Screenshot) { _walletAsked = true; _ = MarketData.Load(); }
        _hadWallet = MarketData.Wallet is not null;

        var hubTab = HubTabOf(_tab);
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
        content.Children.Add(HubNav.Header(hubTab, sold: () => _ = LoadMarket(force: true), sub: sub, action: create));

        content.Children.Add(_tab switch
        {
            "home" => Home(),
            "examples" => Examples(),
            "codes" => Shelf("codes"),
            "models" => Shelf("models"),
            "hub" => HubView(),
            "docs" => Docs(),
            "inject" => Lang(),
            "mxdev" => Injector(),
            "injects" => Injects(),
            _ => _open is null ? Mine() : Editor(),
        });
        Content = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    /// <summary>
    /// «Создать» (шапка, «путь автора»): 12.0 — мастер нового проекта (имя, игра, с чего начать),
    /// а после него — та же анимация, что и у «Скачать», и сразу редактор.
    /// </summary>
    public static void CreateNew() => NewProjectDialog();
}