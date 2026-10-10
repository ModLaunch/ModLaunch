using ModLaunch.Games;
using ModLaunch.Loaders;
using ModLaunch.Mods;

namespace ModLaunch.Core;

public enum Detect { Unknown, Searching, Found, NotFound }

/// <summary>Состояние одной игры: где лежит, стоит ли загрузчик, какие моды.</summary>
public sealed class GameState
{
    public required GameDef Def { get; init; }
    public string? Path { get; set; }
    public Detect Status { get; set; } = Detect.Unknown;
    public string? SearchingWhere { get; set; }
    public bool LoaderInstalled { get; set; }
    ModRegistry? _registry;

    public ModRegistry? Registry
    {
        get
        {
            if (Path is null) return null;
            if (_registry is null || _registry.GamePath != Path) _registry = new ModRegistry(Def, Path);
            return _registry;
        }
    }

    /// <summary>Сколько модов: у Minecraft — в выбранной сборке, у остальных — в списке установленных.</summary>
    public int ModCount => Def.IsMinecraft
        ? (Minecraft.Mc.Active is { } a ? Minecraft.McContent.Count(a, "mod") : 0)
        : Registry?.List().Count ?? 0;

    public void Refresh()
    {
        // Отложенный «без модов» загрузчик считается установленным: его вернут сами.
        LoaderInstalled = Path is not null && (Loader.IsInstalled(Def, Path) || Features.Vanilla.IsParked(Def.Id));
        Registry?.Reconcile();
    }
}

/// <summary>Всё состояние программы и событие «что-то поменялось — перерисуй».</summary>
public static class AppState
{
    public static readonly List<GameState> Games = GameCatalog.Builtin.Concat(CustomGames.Defs).Select(d => new GameState { Def = d }).ToList();
    public static event Action? Changed;

    public static GameState Game(string id) => Games.First(g => g.Def.Id == id);

    static int _pending;

    /// <summary>
    /// «Что-то поменялось — перерисуй». Частые сигналы (поиск игр, загрузки)
    /// склеиваются: окно перерисовывается не чаще раза в 150 мс.
    /// </summary>
    public static void Notify()
    {
        if (Interlocked.Exchange(ref _pending, 1) == 1) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Avalonia.Threading.DispatcherTimer.RunOnce(() =>
        {
            Interlocked.Exchange(ref _pending, 0);
            Changed?.Invoke();
        }, TimeSpan.FromMilliseconds(150)));
    }

    /// <summary>
    /// Сохранённые пути проверяем сразу (игра должна быть на месте, а не только папка),
    /// остальные игры ищем в фоне — 9.2: одним общим обходом дисков на все игры.
    /// </summary>
    public static async Task DetectAll(bool force = false)
    {
        foreach (var g in Games)
        {
            var saved = Settings.GamePath(g.Def.Id);
            if (!force && saved is not null && Locator.StillThere(g.Def, saved))
            {
                g.Path = saved;
                g.Status = Detect.Found;
                g.Refresh();
            }
            else if (g.Status == Detect.Found && force) g.Status = Detect.Unknown;
        }
        Notify();
        await DetectMany(Games.Where(g => g.Status != Detect.Found).ToList());
    }

    /// <summary>Найти несколько игр за один обход дисков; каждая показывается, как только нашлась.</summary>
    public static async Task DetectMany(List<GameState> games, bool deep = false)
    {
        games = games.Where(g => g.Status != Detect.Searching).ToList();
        if (games.Count == 0) return;
        foreach (var g in games) { g.Status = Detect.Searching; g.SearchingWhere = null; }
        Notify();
        void Found(GameDef def, Located where) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var g = games.FirstOrDefault(x => x.Def == def);
            if (g is null || g.Status == Detect.Found) return;
            g.Path = where.Path;
            g.Status = Detect.Found;
            g.SearchingWhere = null;
            Settings.SetGamePath(def.Id, where.Path);
            g.Refresh();
            Notify();
        });
        try
        {
            await Locator.LocateAll(games.Select(g => g.Def).ToList(), deep, new Progress<string>(where =>
            {
                foreach (var g in games.Where(x => x.Status == Detect.Searching)) g.SearchingWhere = where;
                Notify();
            }), Found);
        }
        catch { }
        // Дать дойти последним «нашлась» из фона, потом остальных пометить «не найдена».
        await Task.Delay(50);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            foreach (var g in games.Where(x => x.Status == Detect.Searching)) { g.Status = Detect.NotFound; g.SearchingWhere = null; }
            Notify();
        });
    }

    public static async Task DetectOne(GameState g, bool deep = false)
    {
        g.Status = Detect.Searching;
        Notify();
        try
        {
            var found = await Locator.Locate(g.Def, deep, new Progress<string>(where => { g.SearchingWhere = where; Notify(); }));
            if (found is not null)
            {
                g.Path = found.Path;
                g.Status = Detect.Found;
                Settings.SetGamePath(g.Def.Id, found.Path);
                g.Refresh();
            }
            else g.Status = Detect.NotFound;
        }
        catch { g.Status = Detect.NotFound; }
        g.SearchingWhere = null;
        Notify();
    }

    public static void SetPath(GameState g, string? path)
    {
        g.Path = path;
        g.Status = path is null ? Detect.NotFound : Detect.Found;
        Settings.SetGamePath(g.Def.Id, path);
        g.Refresh();
        Notify();
    }
}
