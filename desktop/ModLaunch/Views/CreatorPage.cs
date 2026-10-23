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
    public override bool Workspace => true;

    /// <summary>12.0: подсказка поиска — о том, что ищется на этой вкладке (см. <see cref="SearchTab"/>).</summary>
    public override string SearchHint => _tab switch
    {
        "examples" => I18n.T("v12h2.search.templates"),
        "scripts" => _ms == "library" ? I18n.T("v18.lib.search") : I18n.T("v13.ms.search"),
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
        // 18.0: «Мои проекты» → «Создать» (form) и «Доступные» (home); «Скрипты» — библиотека, консоль и справка.
        // Старая среда ModScript X (x, inject, mxdev, ide) ведёт в «Доступные»: среда теперь только у проектов.
        _tab = tab switch
        {
            "market" or "studio" or "mine" or "projects" or "available" => "home",
            "inject" or "lang" or "x" or "mxdev" or "ide" => "home",
            "create" => "form",
            "modscript" or "docs" or "commands" or "console" or "functions" or "injects" or "library" => "scripts",
            _ => tab,
        };
        _ms = tab switch { "docs" or "commands" => "commands", "functions" => "functions", "console" => "console", _ => _ms };
        if (tab == "published") { _tab = "home"; _minePart = "published"; }
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
        query == "" ? tab : !editing && tab is "codes" or "models" or "examples" or "hub" or "scripts" ? tab : "home";

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
    /// <summary>Названия вкладок мастерской (для крошек и шапки). 18.0: «Создать», «Доступные», «Скрипты», «Команда» и редкие страницы.</summary>
    public static readonly (string Id, string Key, string Icon)[] MainTabs =
    [
        ("form", "v18.hub.create", Icons.Plus), ("home", "v18.hub.available", Icons.Grid), ("scripts", "v18.hub.scripts", Icons.Shield), ("team", "v13.hub.team", Icons.Users),
        ("codes", "v91.cr.tab.codes", Icons.Code), ("models", "v91.cr.tab.models", Icons.Cube), ("examples", "v91.cr.tab.templates", Icons.Wand),
    ];

    public static readonly (string Id, string Key, string Icon)[] MoreTabs =
    [
        ("hub", "hub.tab", Icons.Globe),
    ];

    public string Tab => _tab;

    static string TabTitle(string tab) => MainTabs.Concat(MoreTabs).Where(t => t.Id == tab).Select(t => t.Key).FirstOrDefault() is string key ? I18n.T(key) : I18n.T("cr.tab.home");

    public override IEnumerable<(string Text, Action? Open)> Crumbs
    {
        get
        {
            // 18.0: Creator Hub › Мои проекты › Создать / Доступные (› проект); остальные разделы — Creator Hub › раздел.
            Action projects = () => MainWindow.Current?.Navigate(() => new CreatorPage());
            var market = projects;
            if (_tab is "home" or "mine" && _open is not null) return [("Creator Hub", market), (I18n.T("v18.hub.projects"), projects), (_open.Name, null)];
            if (_tab is "home" or "mine" or "form" or "examples") return [("Creator Hub", market), (I18n.T("v18.hub.projects"), projects), (TabTitle(_tab), null)];
            return [("Creator Hub", market), (TabTitle(_tab), null)];
        }
    }

    /// <summary>9.5: та же шапка и навигация, что у рынка (HubNav): мастерская — это раздел «Автору».</summary>
    internal static string HubTabOf(string tab) => tab switch
    {
        "hub" => "workshop",
        "codes" or "models" or "scripts" or "team" => tab,
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
        // Баланс в шапке берётся из общих данных рынка: открыли сразу мастерскую — загрузить их (один раз за сеанс).
        if (Social.Account.SignedIn && MarketData.Wallet is null && !MarketData.Loading && !_walletAsked && !Program.Screenshot) { _walletAsked = true; _ = MarketData.Load(); }
        _hadWallet = MarketData.Wallet is not null;

        // 13.0: открытый проект — только у «Проектов» (_tab mine); остальные вкладки показывают своё, даже если проект запомнен.
        var editing = _open is not null && _tab == "mine";
        var body = _tab switch
        {
            "scripts" => ScriptsHub(),
            "team" => Team(),
            "examples" => Examples(),
            "codes" => Shelf("codes"),
            "models" => Shelf("models"),
            "hub" => HubView(),
            "form" => CreateView(),
            _ => editing ? Editor() : Available(),
        };
        // Своё пространство Hub: «Мои проекты» и разделы слева, шапка с одной главной кнопкой, сетка чертежа.
        // Открытый проект: в шапке — его имя и вкладки «Код · Обзор»; в «Доступных» — «Проекты · Опубликованные»; в «Скриптах» — его вкладки.
        var sub = editing ? PaneTabs() : _tab is "home" or "mine" ? MineTabs() : _tab == "scripts" ? MsTabs() : null;
        Content = HubNav.Frame(HubTabOf(_tab), HubNav.RailOf(_tab, creator: true), editing ? _open!.Name : HubTitle(), body, sold: () => _ = LoadMarket(force: true), local: Rail, sub: sub,
            action: _tab == "team" ? TeamAction() : _tab == "scripts" && _ms == "library" ? ImportAction() : null, lead: Lead());
    }

    /// <summary>Строка под названием раздела — там, где без неё непонятно, что делать.</summary>
    string? Lead() => _tab switch
    {
        "form" => I18n.T("v18.create.lead"),
        "scripts" when _ms == "library" => I18n.T("v18.lib.lead"),
        "scripts" => I18n.T("v13.ms.lead", ("n", ModScriptCommands.All.Length), ("new", ModScriptCommands.NewCount)),
        _ => null,
    };

    /// <summary>Название раздела в шапке Hub — так же, как пункт боковой панели.</summary>
    string HubTitle() => _tab switch
    {
        "home" or "mine" => I18n.T("v18.hub.available"),
        "scripts" => I18n.T("v18.hub.scripts"),
        "form" => I18n.T("v18.hub.create"),
        _ => TabTitle(_tab),
    };

    /// <summary>«Создать», «Доступные», «Скрипты» и «Команда» открываются здесь же, без новой страницы.</summary>
    bool Rail(string rail)
    {
        var tab = HubNav.CreatorTab(rail);
        if (tab is not ("home" or "form" or "scripts" or "team")) return false;
        if (_dirty) Save(quiet: true);
        // «Мои проекты» из редактора — к списку (как «Назад»).
        _open = null;
        _pane = "code";
        Go(tab);
        MainWindow.Current?.RenderCrumbs();
        return true;
    }

    /// <summary>
    /// «Создать» (палитра команд, пустые «Доступные»). 16.0: анкета нового проекта (<see cref="OpenForm"/>),
    /// после неё — среда ModScript X. 18.0: анкета — раздел «Мои проекты → Создать».
    /// </summary>
    public static void CreateNew() => OpenForm();
}