using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>
/// 29.0: «Готовые сборки» — популярные сборки модов (Thunderstore) для всех ваших игр на одной странице.
/// «Поставить и играть» делает всё сам: загрузчик, сборку со всеми модами, профиль с её именем и запуск.
/// Перед этим — снимок «как было», так что вернуть прежнее можно одной кнопкой. Сборки Minecraft — в разделе Minecraft.
/// </summary>
public sealed class ModpacksPage : Page
{
    public override string Title => I18n.T("v29.packs.title");

    const int PerGame = 6;
    Dictionary<string, List<ModInfo>>? _packs;
    readonly Dictionary<string, string> _errors = [];
    bool _loading;

    public ModpacksPage()
    {
        AttachedToVisualTree += (_, _) => { if (_packs is null && !_loading) _ = Load(); };
    }

    static IEnumerable<GameState> Games() => AppState.Games.Where(g => g.Status == Detect.Found && g.Path is not null && g.Registry is not null
        && g.Def.Catalog == CatalogKind.Thunderstore && g.Def.Sections.Any(s => s.Id == "modpacks"));

    async Task Load()
    {
        _loading = true;
        _errors.Clear();
        Build();
        var result = new Dictionary<string, List<ModInfo>>();
        var tasks = Games().Select(async g =>
        {
            try
            {
                var q = new Query(Sort: SortBy.Popular, SectionId: "modpacks");
                var mods = Program.Demo ? DemoPacks(g) : (await Catalog.Browse(g.Def, q, source: "thunderstore")).Mods;
                lock (result) result[g.Def.Id] = mods.Where(m => !m.Deprecated).Take(PerGame).ToList();
            }
            catch (Exception e) { lock (_errors) _errors[g.Def.Id] = Jobs.Explain(e); }
        }).ToList();
        await Task.WhenAll(tasks);
        _packs = result;
        _loading = false;
        Dispatcher.UIThread.Post(Build);
    }

    /// <summary>Снимки экрана: правдоподобные сборки (демо-каталог разделов не различает).</summary>
    static List<ModInfo> DemoPacks(GameState g)
    {
        string[] deps = ["BepInEx-BepInExPack-5.4.2100", "Author-ModA-1.0.0", "Author-ModB-2.1.0", "Author-ModC-1.3.0", "Author-ModD-0.9.1", "Author-ModE-1.0.4", "Author-ModF-3.2.0"];
        (string Name, string Author, string Text, int Mods, long Downloads)[] rows =
        [
            ("Friends Night", "Lena", I18n.Lang == "ru" ? "Больше игроков, костюмы и голосовой чат по расстоянию — для вечера с друзьями." : "More players, suits and proximity voice — for a night with friends.", 6, 412_000),
            ("Vanilla Plus", "kirill_d", I18n.Lang == "ru" ? "Игра как была, только удобнее: исправления, интерфейс и мелкие улучшения." : "The game as it was, just nicer: fixes, UI and small improvements.", 4, 198_000),
            ("Hardcore", "Max", I18n.Lang == "ru" ? "Сложнее монстры, меньше лута и новые опасности. Для тех, кому мало." : "Tougher monsters, less loot and new dangers. For those who want more.", 5, 87_000),
        ];
        return rows.Select((r, i) => new ModInfo
        {
            Source = "thunderstore", Id = $"Demo-Pack{i}-{g.Def.Id}", Name = r.Name, Author = r.Author, Description = r.Text, Downloads = r.Downloads,
            Categories = ["Modpacks"], Dependencies = [.. deps.Take(r.Mods + 1)],
        }).ToList();
    }

    public override void Build()
    {
        var head = Ui.Col(4, Ui.Text(I18n.T("v29.packs.title"), "h1"), Ui.Text(I18n.T("v29.packs.lead"), "muted", wrap: true));
        var body = Ui.Col(18, head);
        var games = Games().ToList();
        if (games.Count == 0)
            body.Children.Add(Ui.Card(Ui.Col(8, Ui.Text(I18n.T("v29.packs.noGames"), "h3"), Ui.Text(I18n.T("v29.packs.noGames.text"), "muted", wrap: true),
                Ui.Button(I18n.T("add.title"), () => MainWindow.Current?.Navigate(() => new AddGamePage()), "primary", Icons.Plus)), 22));
        else if (_packs is null)
            body.Children.Add(Ui.Card(Ui.Text(I18n.T("v29.packs.loading"), "muted"), 22));
        else
            foreach (var g in games)
            {
                var gg = g;
                var all = Ui.Button(I18n.T("v29.packs.all"), () => MainWindow.Current?.Navigate(() => new GamePage(gg.Def.Id)), "ghost", Icons.Forward);
                var row = new DockPanel();
                DockPanel.SetDock(all, Dock.Right);
                row.Children.Add(all);
                row.Children.Add(Ui.Row(10, new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(7), ClipToBounds = true, Child = Ui.GameImage(g.Def, 80) }, Ui.Text(g.Def.Name, "h2")));
                body.Children.Add(row);
                if (_errors.TryGetValue(g.Def.Id, out var error)) { body.Children.Add(Ui.Text(I18n.T("v29.packs.failed", ("error", error)), "small muted", wrap: true)); continue; }
                var packs = _packs.GetValueOrDefault(g.Def.Id) ?? [];
                if (packs.Count == 0) { body.Children.Add(Ui.Text(I18n.T("v29.packs.none"), "small muted")); continue; }
                var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
                foreach (var p in packs) wrap.Children.Add(Card(g, p));
                body.Children.Add(wrap);
            }
        if (AppState.Games.Any(g => g.Def.IsMinecraft && g.Status == Detect.Found))
            body.Children.Add(Ui.Card(Ui.Row(12, Ui.Icon(Icons.Cube, 20, Ui.Res("Brand2")), Ui.Col(2, Ui.Text(I18n.T("v29.packs.mc"), "h3"), Ui.Text(I18n.T("v29.packs.mc.text"), "small muted", wrap: true)),
                Ui.Button(I18n.T("v12w.steam.open"), () => MainWindow.Current?.Navigate(() => new MinecraftPage()), "", Icons.Forward)), 16));
        body.Margin = new Thickness(34, 28);
        Content = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    Control Card(GameState g, ModInfo p)
    {
        var installed = Actions.IsInstalled(g, p.Id);
        var mods = p.Dependencies.Count(d => !Mods.Installer.IsLoader(g.Def, Features.Recommend.Bare(d)));
        var facts = Ui.Text(mods > 0 ? I18n.T("v29.packs.facts", ("mods", mods), ("downloads", I18n.Compact(p.Downloads))) : I18n.T("v29.packs.downloads", ("downloads", I18n.Compact(p.Downloads))), "tiny muted");
        var desc = new TextBlock { Text = p.Description, TextWrapping = TextWrapping.Wrap, MaxHeight = 36, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12, Foreground = Ui.Res("Muted") };
        var go = installed
            ? Ui.Button(I18n.T("games.play"), () => Actions.Play(g), "", Icons.Play)
            : Ui.Button(I18n.T("v29.packs.go"), () => Ask(g, p, mods), "primary", Icons.Play);
        var head = Ui.Row(10, Ui.Thumb(p.Icon, p.Name, 48, 10), Ui.Col(1,
            new TextBlock { Text = p.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 190 },
            Ui.Text(p.Author, "tiny muted"), facts));
        var col = Ui.Col(10, head, desc, go);
        if (installed) col.Children.Insert(2, Ui.Text(I18n.T("v29.packs.have"), "tiny", color: Ui.Res("Good")));
        return new Border { Classes = { "card" }, Width = 290, Margin = new Thickness(0, 0, 14, 14), Padding = new Thickness(14), Child = col };
    }

    internal static void Ask(GameState g, ModInfo p, int mods)
    {
        if (MainWindow.Current is not { } w) return;
        var only = new CheckBox { IsChecked = true, Content = I18n.T("v29.packs.only") };
        var lines = Ui.Col(10, Ui.Text(I18n.T("v29.packs.ask.text", ("mods", mods), ("game", g.Def.Name)), "muted", wrap: true));
        if (!g.LoaderInstalled && g.Def.Loader != LoaderKind.None) lines.Children.Add(Ui.Text(I18n.T("v29.packs.ask.loader", ("loader", g.Def.LoaderName)), "small", wrap: true));
        lines.Children.Add(only);
        lines.Children.Add(Ui.Text(I18n.T("v29.packs.ask.undo"), "tiny muted", wrap: true));
        w.Dialog(I18n.T("v29.packs.ask.title", ("name", p.Name)), lines,
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("v29.packs.installOnly"), () => { w.CloseDialog(); _ = Actions.InstallModpack(g, p, only.IsChecked == true, play: false); }, "ghost", Icons.Download),
            Ui.Button(I18n.T("v29.packs.go"), () => { w.CloseDialog(); _ = Actions.InstallModpack(g, p, only.IsChecked == true, play: true); }, "primary", Icons.Play));
    }
}
