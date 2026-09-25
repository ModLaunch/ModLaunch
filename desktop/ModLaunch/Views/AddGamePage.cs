using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using ModLaunch.Core;
using ModLaunch.Games;

namespace ModLaunch.Views;

/// <summary>Кнопка «+»: все игры с диска, движок каждой и добавление в один клик.</summary>
public sealed class AddGamePage : Page
{
    public override string Title => I18n.T("add.title");
    public override string SearchHint => I18n.T("add.search");

    static List<FoundGame>? _found;
    static bool _scanning;
    string _filter = "";
    readonly HashSet<string> _adding = new(StringComparer.OrdinalIgnoreCase);

    public AddGamePage()
    {
        if (_found is null && !_scanning && !Program.Screenshot) _ = Rescan();
    }

    public override void Search(string text) { _filter = text.Trim(); Build(); }

    async Task Rescan()
    {
        _scanning = true;
        Build();
        try { _found = await CustomGames.Scan(); }
        catch { _found = []; }
        _scanning = false;
        Build();
    }

    /// <summary>Для скриншотов: список без обхода диска.</summary>
    public static void Demo(List<FoundGame> list) => _found = list;

    public override void Build()
    {
        var content = new StackPanel { Spacing = 18, Margin = new Thickness(34, 26, 34, 34), MaxWidth = 1100 };
        var head = new DockPanel();
        var actions = Ui.Row(8,
            Ui.Button(I18n.T("add.exe"), PickExe, "", Icons.FilePlus),
            Ui.Button(I18n.T("add.rescan"), () => _ = Rescan(), "ghost", Icons.Refresh));
        DockPanel.SetDock(actions, Dock.Right);
        head.Children.Add(actions);
        head.Children.Add(Ui.Col(4, Ui.Text(I18n.T("add.title"), "h2"), Ui.Text(I18n.T("add.text"), "muted small", wrap: true)));
        content.Children.Add(head);

        if (_scanning)
            content.Children.Add(Ui.Card(Ui.Row(12, new ProgressBar { IsIndeterminate = true, Width = 120 }, Ui.Text(I18n.T("add.scanning"), "muted"))));

        var list = (_found ?? []).Where(f => _filter == "" || f.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!_scanning && list.Count == 0)
            content.Children.Add(Ui.Card(Ui.Col(6, Ui.Text(I18n.T("add.empty"), "h3"), Ui.Text(I18n.T("add.empty.text"), "muted small", wrap: true))));

        foreach (var f in list) content.Children.Add(Row(f));

        var mine = AppState.Games.Where(g => g.Def.Custom).ToList();
        if (mine.Count > 0)
        {
            content.Children.Add(Ui.Text(I18n.T("add.mine"), "h3"));
            foreach (var g in mine)
            {
                var id = g.Def.Id;
                var remove = Ui.Button("", () => { CustomGames.Remove(id); MainWindow.Current?.Toast(I18n.T("add.removed", ("game", g.Def.Name))); Build(); }, "icon ghost", Icons.Trash, I18n.T("add.remove"));
                var row = new DockPanel();
                DockPanel.SetDock(remove, Dock.Right);
                row.Children.Add(remove);
                row.Children.Add(Ui.Row(12, Ui.Thumb(g.Def.ArtUrl, g.Def.Name, 40, 10), new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { Ui.Text(g.Def.Name, "h3"), Ui.Text(Describe(g.Def), "small muted") },
                }));
                content.Children.Add(Ui.Card(row, 12));
            }
        }
        Content = new ScrollViewer { Content = content };
    }

    static string EngineName(string engine) => I18n.Has("engine." + engine) ? I18n.T("engine." + engine) : engine;

    static string Describe(GameDef def)
    {
        var parts = new List<string> { EngineName(def.Engine ?? "other") };
        if (def.Loader == LoaderKind.Bepinex) parts.Add("BepInEx");
        parts.AddRange(def.Sources.Select(Sources.Catalog.Title));
        if (!def.HasCatalog) parts.Add(I18n.T("add.noCatalog"));
        return string.Join(" · ", parts);
    }

    Control Row(FoundGame f)
    {
        var busy = _adding.Contains(f.Path);
        var add = Ui.Button(busy ? I18n.T("add.adding") : I18n.T("add.add"), () => _ = Add(f), "primary", Icons.Plus);
        add.IsEnabled = !busy;
        add.VerticalAlignment = VerticalAlignment.Center;
        var row = new DockPanel();
        DockPanel.SetDock(add, Dock.Right);
        row.Children.Add(add);
        var hint = f.Engine switch
        {
            "unity" => I18n.T("add.hint.unity"),
            "unity-il2cpp" => I18n.T("add.hint.il2cpp"),
            "unreal" => I18n.T("add.hint.unreal"),
            _ => I18n.T("add.hint.other"),
        };
        row.Children.Add(Ui.Row(14,
            Ui.Thumb(f.AppId > 0 ? GameDef.SteamArt(f.AppId) : null, f.Name, 48, 10),
            new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center, Spacing = 2,
                Children =
                {
                    Ui.Text(f.Name, "h3"),
                    Ui.Text($"{EngineName(f.Engine)} · {I18n.T("store." + f.Store)} · {f.Path}", "small muted"),
                    Ui.Text(hint, "small", color: f.Engine == "unity" ? Ui.Res("Good") : Ui.Res("Faint")),
                },
            }));
        return Ui.Card(row, 14);
    }

    async Task Add(FoundGame f)
    {
        _adding.Add(f.Path);
        Build();
        try
        {
            var state = await CustomGames.Add(f);
            _found?.Remove(f);
            MainWindow.Current?.Toast(I18n.T("add.added", ("game", f.Name)));
            MainWindow.Current?.Navigate(() => new GamePage(state.Def.Id));
        }
        catch (Exception e) { MainWindow.Current?.Toast(Jobs.Explain(e), bad: true); }
        finally { _adding.Remove(f.Path); }
    }

    async void PickExe()
    {
        var exe = await MainWindow.Current!.PickExe(I18n.T("add.exe"));
        if (exe is null) return;
        await Add(CustomGames.FromExe(exe));
    }
}
