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

    public override void Build()
    {
        var content = new StackPanel { Spacing = 24, Margin = new Thickness(40, 26, 40, 40), MaxWidth = 1640 };

        // Шапка: логотип, название и вкладки.
        // На «Главной» шапка крупная, на остальных вкладках — компактная.
        var home = _tab == "home";
        var words = home
            ? Ui.Col(4, Ui.Text("Creator Hub", "h1"), Ui.Text(I18n.T("cr.home.hello"), "muted"))
            : Ui.Col(2, Ui.Text("Creator Hub", "h2"), Ui.Text(I18n.T("cr.subtitle"), "small muted"));
        words.VerticalAlignment = VerticalAlignment.Center;
        content.Children.Add(Ui.Row(home ? 20 : 14, CreatorLogo.Tile(home ? 72 : 44), words));
        var tabs = new List<Control>();
        foreach (var (id, key, icon) in new[] { ("home", "cr.tab.home", Icons.Home), ("hub", "hub.tab", Icons.Globe), ("market", "mk.tab", Icons.Bag), ("studio", "st.tab", Icons.Chart), ("mine", "cr.tab.mine", Icons.Edit), ("examples", "cr.tab.examples", Icons.Wand), ("docs", "cr.tab.docs", Icons.Book) })
        {
            var tab = id;
            var b = Ui.Button(I18n.T(key), () => Go(tab), "tab", icon);
            if (_tab == id) b.Classes.Add("active");
            tabs.Add(b);
        }
        content.Children.Add(Ui.TabBar(tabs.ToArray()));

        content.Children.Add(_tab switch
        {
            "home" => Home(),
            "examples" => Examples(),
            "hub" => HubView(),
            "market" => MarketView(),
            "studio" => Studio(),
            "docs" => Docs(),
            _ => _open is null ? Mine() : Editor(),
        });
        Content = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }
}