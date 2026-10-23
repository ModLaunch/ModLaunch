using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Games;

namespace ModLaunch.Views;

/// <summary>
/// Кнопка «+»: все игры с диска, движок каждой и добавление в один клик. 12.0: справочник
/// поддерживаемых игр — установленные из него добавляются сразу (каталоги, загрузчик и папка модов
/// известны заранее), остальные можно найти, поставить через Steam или указать их папку.
/// </summary>
public sealed class AddGamePage : Page
{
    /// <summary>Сколько строк справочника показывать за раз: тысяча строк сразу — это секунды вёрстки.</summary>
    const int PageSize = 60;

    public override string Title => I18n.T("add.title");
    public override string SearchHint => I18n.T("v12g.add.search");

    static List<FoundGame>? _found;
    static bool _scanning, _refreshStarted;
    string _filter;
    int _shown = PageSize;
    bool _addingAll;
    readonly HashSet<string> _adding = new(StringComparer.OrdinalIgnoreCase);
    // Поле поиска и список справочника живут дольше одной перестройки: набор текста не теряет фокус,
    // а каждая буква перерисовывает только список, а не всю страницу.
    readonly TextBox _box;
    readonly StackPanel _list = new();
    readonly ScrollViewer _scroll = new();
    IDisposable? _debounce;

    static AddGamePage()
    {
        // Справочник обновился (живые списки Thunderstore и Nexus) — сопоставить игры с диска заново.
        GameDirectory.Changed += () => Dispatcher.UIThread.Post(() =>
        {
            if (_scanning) return;
            if (MainWindow.Current?.CurrentPage is AddGamePage page) _ = page.Rescan();
            else _found = null;
        });
    }

    public AddGamePage() : this("") { }

    /// <summary>Сразу с поиском: из библиотеки («+ N поддерживаемых игр») с набранным там текстом.</summary>
    public AddGamePage(string query)
    {
        _filter = query.Trim();
        _box = new TextBox { Text = _filter, Watermark = I18n.T("v12g.all.search"), Height = 38, Width = 360, VerticalAlignment = VerticalAlignment.Center };
        _box.InnerLeftContent = new Border { Padding = new Thickness(12, 0, 0, 0), Child = Ui.Icon(Icons.Search, 15, Ui.Res("Muted")) };
        _box.TextChanged += (_, _) => Typed(_box.Text ?? "");
        if (_found is null && !_scanning && !Program.Screenshot) _ = Rescan();
        // Свежий справочник — в фоне, один раз за запуск; пока он грузится, работает встроенный снимок.
        if (!_refreshStarted && !Program.Screenshot)
        {
            _refreshStarted = true;
            _ = Task.Run(() => GameDirectory.RefreshAsync());
        }
    }

    /// <summary>Enter в верхнем поиске — то же, что набор в поле над списком, только сразу.</summary>
    public override void Search(string text)
    {
        _debounce?.Dispose();
        if (_box.Text != text.Trim()) _box.Text = text.Trim();
        Apply(text);
    }

    /// <summary>Набирают текст — список перестраивается, когда пальцы замрут на 200 мс.</summary>
    void Typed(string text)
    {
        // Старый таймер гасим всегда: набрали «skyr» и стёрли «r» — искать «skyr» уже не надо.
        _debounce?.Dispose();
        if (text.Trim() == _filter) return;
        _debounce = DispatcherTimer.RunOnce(() => Apply(text), TimeSpan.FromMilliseconds(200));
    }

    void Apply(string text)
    {
        _filter = text.Trim();
        _shown = PageSize;
        RenderList();
    }

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

    /// <summary>Вынуть долгоживущий элемент из прежней разметки, чтобы вставить в новую.</summary>
    static T Detach<T>(T c) where T : Control
    {
        if (c.Parent is Panel p) p.Children.Remove(c);
        else if (c.Parent is Decorator d) d.Child = null;
        return c;
    }

    public override void Build()
    {
        var content = StoreKit.Fit(new StackPanel { Spacing = 18, Margin = new Thickness(34, 26, 34, 34), MaxWidth = 1100 }, 1700);
        var head = new DockPanel();
        var actions = Ui.Row(8,
            Ui.Button(I18n.T("add.exe"), PickExe, "", Icons.FilePlus),
            Ui.Button(I18n.T("add.rescan"), () => _ = Rescan(), "ghost", Icons.Refresh));
        actions.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(actions, Dock.Right);
        head.Children.Add(actions);
        var supported = CustomGames.SupportedCount;
        var count = ModRow.Tag(I18n.T("v12g.supported." + I18n.Plural(supported, "one", "few", "many"), ("n", supported.ToString("N0", I18n.Culture))), Ui.Soft("#1E1A3A", "#A99BFF"), Ui.Ink("#A99BFF"));
        count.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(Ui.Col(6, Ui.Row(12, Ui.Text(I18n.T("add.title"), "h2"), count), Ui.Text(I18n.T("v12g.add.text"), "muted small", wrap: true)));
        content.Children.Add(head);

        if (_scanning)
            content.Children.Add(Ui.Card(Ui.Row(12, new ProgressBar { IsIndeterminate = true, Width = 120 }, Ui.Text(I18n.T("add.scanning"), "muted"))));

        var found = _found ?? [];
        var ready = found.Where(f => f.Dir is not null).ToList();
        var other = found.Where(f => f.Dir is null).ToList();
        if (!_scanning && found.Count == 0)
            content.Children.Add(Ui.Card(Ui.Col(6, Ui.Text(I18n.T("add.empty"), "h3"), Ui.Text(I18n.T("add.empty.text"), "muted small", wrap: true))));

        // Установленные игры из справочника: добавить одну или все сразу.
        if (ready.Count > 0)
        {
            var all = Ui.Button(_addingAll ? I18n.T("add.adding") : I18n.T("v12g.addAll"), () => _ = AddAll(), "primary", Icons.Plus);
            all.IsEnabled = !_addingAll && _adding.Count == 0;
            content.Children.Add(Ui.Col(6, StoreKit.Header(I18n.T("v12g.ready"), right: all, count: ready.Count.ToString()), Ui.Text(I18n.T("v12g.ready.hint"), "small muted", wrap: true)));
            var existing = CustomGames.ExistingLookup();
            foreach (var f in ready) content.Children.Add(Ui.Card(DirRow(f.Dir!, f, existing(f.Dir!)), 14));
        }
        // Остальные установленные — как раньше: движок по файлам, каталог по имени.
        if (other.Count > 0)
        {
            content.Children.Add(Ui.Col(6, StoreKit.Header(I18n.T(ready.Count > 0 ? "v12g.other" : "v12g.installed"), count: other.Count.ToString()), Ui.Text(I18n.T("v12g.other.hint"), "small muted", wrap: true)));
            foreach (var f in other) content.Children.Add(Row(f));
        }

        var mine = AppState.Games.Where(g => g.Def.Custom).ToList();
        if (mine.Count > 0)
        {
            content.Children.Add(StoreKit.Header(I18n.T("add.mine"), count: mine.Count.ToString()));
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

        // Весь справочник: поиск над списком, показ по 60 строк.
        var searchHead = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var box = Detach(_box);
        DockPanel.SetDock(box, Dock.Right);
        searchHead.Children.Add(box);
        searchHead.Children.Add(Ui.Col(2,
            Ui.Row(10, Ui.Text(I18n.T("v12g.all"), "h2"), Ui.Text(Math.Max(GameDirectory.Count, CustomGames.SupportedCount).ToString("N0", I18n.Culture), "h2", color: Ui.Res("Faint"))),
            Ui.Text(I18n.T(SteamCatalog.Ready && SteamCatalog.Count > 0 ? "v15.all.hint" : "v12g.all.hint"), "small muted", wrap: true)));
        content.Children.Add(searchHead);
        content.Children.Add(Detach(_list));
        RenderList();

        // Один и тот же ScrollViewer: перестройка (нашлась игра, добавилась другая) не сбрасывает прокрутку.
        _scroll.Content = content;
        if (Content != _scroll) Content = _scroll;
    }

    sealed record Sorted(IReadOnlyList<DirGame> Source, List<DirGame> List);
    static Sorted? _sorted;

    /// <summary>Справочник без поиска: игры с наибольшим числом модов сверху. Сортировка — раз на версию справочника.</summary>
    internal static List<DirGame> PopularDirectory() => Popular();

    static List<DirGame> Popular()
    {
        var all = GameDirectory.All;
        var s = _sorted;
        if (s is null || !ReferenceEquals(s.Source, all))
            _sorted = s = new Sorted(all, all.OrderByDescending(g => g.Mods).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
        return s.List;
    }

    sealed record SteamIds(IReadOnlyList<DirGame> Source, HashSet<int> Ids);
    static SteamIds? _dirSteam;

    /// <summary>Номера Steam справочника: без поиска игры Steam идут после него и не повторяют его.</summary>
    static HashSet<int> DirectorySteamIds()
    {
        var all = GameDirectory.All;
        var s = _dirSteam;
        if (s is null || !ReferenceEquals(s.Source, all)) _dirSteam = s = new SteamIds(all, all.Where(g => g.SteamAppId > 0).Select(g => g.SteamAppId).ToHashSet());
        return s.Ids;
    }

    /// <summary>Перестроить только список справочника (поиск, «Показать ещё»), не трогая остальную страницу.</summary>
    void RenderList()
    {
        _list.Children.Clear();
        var rows = _filter == "" ? Popular().Take(_shown + 1).ToList() : GameDirectory.Search(_filter, _shown + 1);
        // 15.0: справочник моддируемых кончился — дальше все игры Steam. Совпала игра справочника — берём её запись (с каталогами).
        var need = _shown + 1 - rows.Count;
        if (need > 0 && SteamCatalog.Ready)
        {
            var shown = rows.Where(r => r.SteamAppId > 0).Select(r => r.SteamAppId).ToHashSet();
            var more = _filter == "" ? SteamCatalog.Popular(0, need, DirectorySteamIds()) : SteamCatalog.Search(_filter, need, shown);
            rows.AddRange(more.Select(g => GameDirectory.BySteam(g.SteamAppId) ?? g).Where(g => g.SteamAppId <= 0 || shown.Add(g.SteamAppId)));
        }
        if (rows.Count == 0)
        {
            _list.Children.Add(Ui.Card(Ui.Text(I18n.T("v12g.all.none", ("query", _filter)), "muted", wrap: true), 18));
            return;
        }
        var existing = CustomGames.ExistingLookup();
        var installed = new Dictionary<string, FoundGame>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in _found ?? []) if (f.Dir is not null) installed.TryAdd(f.Dir.Key, f);
        var lines = new StackPanel();
        // 17.0: строки справочника — порциями (видимые сразу, остальные между кадрами).
        var make = rows.Take(_shown).Select((d, i) => (Func<Control>)(() =>
        {
            var row = new Border { Padding = new Thickness(2, 10), Child = DirRow(d, installed.GetValueOrDefault(d.Key), existing(d)) };
            if (i == 0) return row;
            return new StackPanel { Children = { new Border { Height = 1, Background = Ui.Res("Line"), Margin = new Thickness(62, 0, 0, 0) }, row } };
        })).ToList();
        Ui.Fill(lines, make);
        _list.Children.Add(Ui.Card(lines, 12));
        if (rows.Count > _shown)
        {
            var more = Ui.Button(I18n.T("v12g.all.more"), () => { _shown += PageSize; RenderList(); }, "ghost", Icons.ChevronDown);
            more.HorizontalAlignment = HorizontalAlignment.Center;
            more.Margin = new Thickness(0, 12, 0, 0);
            _list.Children.Add(more);
        }
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

    /// <summary>Метки игры справочника: где она уже есть, каталоги, загрузчик и число модов.</summary>
    static Control Badges(DirGame d, bool installed, bool inLibrary)
    {
        var wrap = new WrapPanel();
        void Add(Control c) { c.Margin = new Thickness(0, 5, 6, 0); wrap.Children.Add(c); }
        if (inLibrary) Add(ModRow.Tag(I18n.T("v12g.badge.inLibrary"), Ui.Soft("#123A26", "#5BD68F"), Ui.Ink("#5BD68F")));
        else if (installed) Add(ModRow.Tag(I18n.T("v12g.badge.installed"), Ui.Soft("#123A26", "#5BD68F"), Ui.Ink("#5BD68F")));
        if (d.Community is not null) Add(ModRow.Tag(Sources.Catalog.Title("thunderstore"), Ui.Soft("#0F2E33", "#3FC6D4"), Ui.Ink("#3FC6D4")));
        if (d.NexusDomain is not null) Add(ModRow.Tag(Sources.Catalog.Title("nexus"), Ui.Soft("#3A2A12", "#F2A93B"), Ui.Ink("#F2A93B")));
        // Названия загрузчиков — имена программ, их не переводят.
        var loader = d.Loader switch { "bepinex" => "BepInEx", "bepinex-il2cpp" => "BepInEx IL2CPP", "melonloader" => "MelonLoader", _ => null };
        if (loader is not null) Add(ModRow.Tag(loader, Ui.Soft("#1E1A3A", "#A99BFF"), Ui.Ink("#A99BFF")));
        if (d.Mods > 0) Add(ModRow.Tag(I18n.T("v12g.mods." + I18n.Plural(d.Mods, "one", "few", "many"), ("n", I18n.Compact(d.Mods))), Ui.Res("Surface3"), Ui.Res("Muted")));
        return wrap;
    }

    /// <summary>Строка игры справочника: обложка, имя, метки и то, что с ней можно сделать сейчас.</summary>
    Control DirRow(DirGame d, FoundGame? found, GameState? existing)
    {
        var buttons = Ui.Row(6);
        buttons.VerticalAlignment = VerticalAlignment.Center;
        buttons.Margin = new Thickness(12, 0, 0, 0);
        if (existing is not null)
            buttons.Children.Add(Ui.Button(I18n.T("v12g.open"), () => MainWindow.Current?.Navigate(() => new GamePage(existing.Def.Id)), "ghost", Icons.ChevronRight));
        else if (found is not null)
        {
            var busy = _adding.Contains(found.Path) || _addingAll;
            var add = Ui.Button(busy ? I18n.T("add.adding") : I18n.T("add.add"), () => _ = Add(found), "primary", Icons.Plus);
            add.IsEnabled = !busy;
            buttons.Children.Add(add);
        }
        else
        {
            if (d.SteamAppId > 0) buttons.Children.Add(Ui.Button(I18n.T("v12g.steam"), () => InstallViaSteam(d), "ghost", Icons.Download));
            buttons.Children.Add(Ui.Button("", () => PickFolder(d), "icon ghost", Icons.Folder, I18n.T("v12g.pickFolder")));
        }
        var row = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        row.Children.Add(buttons);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Ui.Text(d.Name, "h3"));
        text.Children.Add(Badges(d, found is not null, existing is not null));
        if (found is not null) text.Children.Add(new TextBlock { Text = $"{I18n.T("store." + found.Store)} · {found.Path}", Classes = { "small", "muted" }, Margin = new Thickness(0, 5, 0, 0), TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis });
        if (d.Loader == "melonloader" && existing is null) text.Children.Add(new TextBlock { Text = I18n.T("v12g.melon.hint"), Classes = { "small" }, Foreground = Ui.Res("Faint"), Margin = new Thickness(0, 3, 0, 0) });
        row.Children.Add(Ui.Row(14, Ui.Thumb(d.Art, d.Name, 48, 10), text));
        return row;
    }

    Control Row(FoundGame f)
    {
        var busy = _adding.Contains(f.Path) || _addingAll;
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
            Ui.Thumb(f.AppId > 0 ? GameDef.SteamArt(f.AppId) : f.Dir?.Art, f.Name, 48, 10),
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

    /// <summary>«Добавить все»: по очереди, без перехода на страницу каждой игры.</summary>
    async Task AddAll()
    {
        var ready = (_found ?? []).Where(f => f.Dir is not null).ToList();
        if (ready.Count == 0 || _addingAll) return;
        _addingAll = true;
        Build();
        var added = 0;
        foreach (var f in ready)
        {
            try { await CustomGames.Add(f); _found?.Remove(f); added++; }
            catch (Exception e) { MainWindow.Current?.Toast($"{f.Name}: {Jobs.Explain(e)}", bad: true); }
        }
        _addingAll = false;
        MainWindow.Current?.Toast(I18n.T("v12g.addedAll", ("n", added)));
        AppState.Notify();
        Build();
    }

    /// <summary>Игры нет на компьютере — её ставит сам Steam; потом она найдётся кнопкой «Искать снова».</summary>
    static void InstallViaSteam(DirGame d)
    {
        if (!Program.Demo)
        {
            // Нет Steam — страница игры в магазине (там же ссылка на сам Steam).
            if (!Features.GetGame.SteamInstalled) { Ui.OpenUrl($"https://store.steampowered.com/app/{d.SteamAppId}/"); return; }
            try { Process.Start(new ProcessStartInfo($"steam://install/{d.SteamAppId}") { UseShellExecute = true }); } catch { }
        }
        MainWindow.Current?.Toast(I18n.T("v12g.steam.started", ("game", d.Name)));
    }

    /// <summary>Игра стоит, но не нашлась (репак, своя папка): указать папку — и добавить по справочнику.</summary>
    async void PickFolder(DirGame d)
    {
        var dir = await MainWindow.Current!.PickFolder(I18n.T("v12g.pickFolder.title", ("game", d.Name)));
        if (dir is null) return;
        if (CustomGames.FromFolder(d, dir) is not { } found) { MainWindow.Current?.Toast(I18n.T("v12g.noExe", ("game", d.Name)), bad: true); return; }
        await Add(found);
    }

    async void PickExe()
    {
        var exe = await MainWindow.Current!.PickExe(I18n.T("add.exe"));
        if (exe is null) return;
        await Add(CustomGames.FromExe(exe));
    }
}
