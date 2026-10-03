using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using ModLaunch.Core;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>
/// Полка над каталогом: «В тренде», «Новые» и «Обновлённые», как на Nexus и Modrinth.
/// «В тренде» считаем сами для любого каталога: свежие и недавно обновлённые моды,
/// у которых много скачиваний для их возраста.
/// </summary>
public sealed partial class GamePage
{
    static readonly Dictionary<string, (DateTime At, List<ModInfo> Mods)> FeedCache = [];
    string _feed = "trending";
    bool _feedLoading;

    static bool OnlyWorking => Settings.Data.Bool("onlyWorkingMods", true);

    /// <summary>Полка видна на обычном каталоге: раздел «Все», без поиска, по популярности.</summary>
    Control? FeedShelf()
    {
        if (_section != "all" || _query != "" || _sort != SortBy.Popular || _period != 0 || !Settings.Data.Bool("catalogFeeds", true)) return null;
        var key = FeedKey();
        if (!FeedCache.TryGetValue(key, out var cached) || DateTime.UtcNow - cached.At > TimeSpan.FromMinutes(10))
        {
            if (!_feedLoading) _ = LoadFeed(key);
            cached = (DateTime.MinValue, []);
        }
        // Загрузили, а показать нечего (нет сети или всё отфильтровано) — полку не рисуем совсем.
        else if (cached.Mods.Count == 0) return null;

        var tabs = Ui.Row(6);
        foreach (var id in new[] { "trending", "new", "updated" })
        {
            var feed = id;
            var b = Ui.Button(I18n.T("feed." + id), () => { _feed = feed; RenderList(); }, "chip", id == "trending" ? Icons.Flame : id == "new" ? Icons.Sparkles : Icons.Refresh);
            if (_feed == id) b.Classes.Add("active");
            tabs.Children.Add(b);
        }
        var hide = Ui.Button("", () => { Settings.Data["catalogFeeds"] = false; Settings.Save(); RenderList(); MainWindow.Current?.Toast(I18n.T("feed.hidden")); }, "icon ghost", Icons.Close, I18n.T("feed.hide"));
        var head = new DockPanel();
        DockPanel.SetDock(hide, Dock.Right);
        head.Children.Add(hide);
        head.Children.Add(tabs);

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (cached.Mods.Count == 0)
        {
            for (var i = 0; i < 5; i++) row.Children.Add(new Border { Classes = { "card" }, Width = 238, Height = 150, Margin = new Thickness(0, 0, 14, 0), Opacity = 0.5 });
        }
        foreach (var mod in cached.Mods)
        {
            var m = mod;
            var tile = ModRow.Tile(_g.Def, m, IsInstalled(m), IsInstalling(m), () => _ = Actions.Install(_g, m), () => MainWindow.Current?.Navigate(() => new ModPage(_g.Def.Id, m)), _feed == "new" ? "new" : null, compact: true);
            tile.Margin = new Thickness(0, 0, 14, 4);
            row.Children.Add(tile);
        }
        var scroll = new ScrollViewer { Content = row, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 0, 10) };
        return Ui.Col(12, head, scroll, new Border { Height = 1, Background = Ui.Res("Line"), Margin = new Thickness(0, 4, 0, 6) });
    }

    // «18+» и «Только рабочие» — тоже в ключе: выключили 18+ — полка не должна показывать запомненные моды 18+.
    string FeedKey() => $"{_g.Def.Id}|{_source ?? _g.Def.PrimarySource}|{_feed}|{_adult}|{OnlyWorking}";

    async Task LoadFeed(string key)
    {
        _feedLoading = true;
        await Task.Yield();
        var feed = _feed;
        var source = _source ?? _g.Def.PrimarySource;
        var adult = _adult;
        var onlyWorking = OnlyWorking;
        // Нас зовут из середины RenderList. Каталог может ответить сразу (ModLaunch Hub из кэша, демо) —
        // тогда наш RenderList сработал бы внутри того и список нарисовался бы дважды. Ждём, пока тот закончит.
        await Task.Yield();
        try
        {
            Task<Sources.Page> Get(SortBy sort) => Program.Demo ? Task.FromResult(Demo.Catalog(_g.Def, new Query(Sort: sort))) : Catalog.Browse(_g.Def, new Query(Sort: sort, Adult: adult), source: source);
            List<ModInfo> mods = feed switch
            {
                "new" => (await Get(SortBy.New)).Mods,
                "updated" => (await Get(SortBy.Updated)).Mods,
                _ => Trending((await Get(SortBy.New)).Mods.Concat((await Get(SortBy.Updated)).Mods)),
            };
            if (onlyWorking) mods = mods.Where(m => Features.Compat.Works(_g.Def, m)).ToList();
            FeedCache[key] = (DateTime.UtcNow, mods.Take(12).ToList());
        }
        catch { FeedCache[key] = (DateTime.UtcNow, []); }
        _feedLoading = false;
        if (_tab == "catalog") RenderList();
    }

    /// <summary>Тренд: скачивания, делённые на возраст в днях (в степени 1.3) — свежее и популярное наверху.</summary>
    public static List<ModInfo> Trending(IEnumerable<ModInfo> mods) => mods
        .DistinctBy(m => m.Id)
        .OrderByDescending(m => m.Downloads / Math.Pow(Math.Max(1, (DateTime.UtcNow - (m.UpdatedAt ?? DateTime.UtcNow.AddYears(-1))).TotalDays) + 1, 1.3))
        .ToList();

    [SelfTest]
    static string TrendingPrefersFreshAndPopular()
    {
        ModInfo M(string id, long downloads, int days) => new() { Source = "thunderstore", Id = id, Name = id, Downloads = downloads, UpdatedAt = DateTime.UtcNow.AddDays(-days) };
        var order = Trending([M("old-giant", 900_000, 700), M("fresh-hit", 20_000, 2), M("fresh-small", 300, 1), M("fresh-hit", 20_000, 2)]).Select(m => m.Id).ToList();
        if (string.Join(",", order) != "fresh-hit,old-giant,fresh-small") throw new Exception(string.Join(",", order));
        return "fresh hit beats a 2-year-old giant; duplicates merged";
    }

    /// <summary>Снимок: каталог с полкой «В тренде».</summary>
    [DemoShots]
    static void FeedShots(Shots s)
    {
        var page = new GamePage("lethal-company", "catalog");
        s.Window.Navigate(() => page);
        page.ShowSection("all");
        s.Pump(900);
        s.Save("discovery-3-feeds");
        page._feed = "new";
        page.RenderList();
        s.Pump(900);
        s.Save("discovery-4-feeds-new");
    }
}
