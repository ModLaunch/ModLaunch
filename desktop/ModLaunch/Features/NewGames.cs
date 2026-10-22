using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Games;

namespace ModLaunch.Features;

/// <summary>
/// 15.1: новая игра — сразу везде. Время, когда игра появилась (нашлась на диске, добавлена вручную, установлена
/// в Steam, Epic или GOG), считается как запуск: панель слева, сцена на главной, библиотека и виджеты показывают её
/// первой, три дня у неё метка «Новая». Установленные с прошлой проверки игры добавляются в библиотеку сами.
/// </summary>
public static class NewGames
{
    static readonly TimeSpan Fresh = TimeSpan.FromDays(3);

    static JsonObject Seen => Settings.Data.Obj("gameSeen");

    public static DateTime? SeenAt(string id) =>
        DateTime.TryParse(Seen.Str(id), null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t.ToUniversalTime() : null;

    /// <summary>Запомнить, что игра только что появилась. Второй раз время не меняется.</summary>
    public static void Mark(string id)
    {
        if (Seen.ContainsKey(id)) return;
        Seen[id] = DateTime.UtcNow.ToString("o");
        Settings.Save();
    }

    /// <summary>
    /// Находки на диске считаются «новыми» только после первого полного поиска: при первом запуске
    /// (и при переходе на 15.1) все уже стоящие игры — не новые.
    /// </summary>
    public static bool Tracking => Settings.Data.ContainsKey("gameSeen");

    public static void StartTracking()
    {
        if (Tracking) return;
        _ = Seen;
        Settings.Save();
    }

    /// <summary>Для «недавних» списков: последний запуск или появление игры — что позже.</summary>
    public static DateTime Recent(GameState g)
    {
        var played = PlayTime.Get(g.Def.Id).LastPlayed ?? DateTime.MinValue;
        var seen = SeenAt(g.Def.Id) ?? DateTime.MinValue;
        return played > seen ? played : seen;
    }

    /// <summary>Игра на компьютере появилась меньше трёх дней назад, и в неё ещё не играли.</summary>
    public static bool IsNew(GameState g) =>
        g.Status == Detect.Found && SeenAt(g.Def.Id) is { } t && DateTime.UtcNow - t < Fresh && PlayTime.Get(g.Def.Id).LastPlayed is null;

    public static bool AutoAdd
    {
        get => Settings.Data.Bool("autoAddGames", true);
        set { Settings.Data["autoAddGames"] = value; Settings.Save(); }
    }

    static DateTime _lastCheck = DateTime.MinValue;
    static bool _checking;

    /// <summary>
    /// Игры Steam, Epic и GOG, установленные с прошлой проверки, — в библиотеку. Первая проверка только запоминает,
    /// что уже стоит: старые игры добавляются в «Добавить игру», библиотека не заполняется разом. Убранная вручную
    /// игра уже в списке «видели» — сама не вернётся. Проверка — при запуске и когда окно снова активно (не чаще раза в 3 минуты).
    /// </summary>
    public static async Task Check(bool force = false)
    {
        if (_checking || Program.Screenshot || !AutoAdd) return;
        if (!force && DateTime.UtcNow - _lastCheck < TimeSpan.FromMinutes(3)) return;
        _checking = true;
        _lastCheck = DateTime.UtcNow;
        try
        {
            var found = await CustomGames.Scan();
            var first = !Settings.Data.ContainsKey("pcGames");
            var known = Settings.Data.Arr("pcGames").Select(x => x?.ToString()).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            var fresh = first ? [] : found.Where(f => !known.Contains(Key(f.Path))).ToList();
            foreach (var f in found) known.Add(Key(f.Path));
            Settings.Data["pcGames"] = new JsonArray(known.Order(StringComparer.OrdinalIgnoreCase).Select(p => (JsonNode)JsonValue.Create(p)).ToArray());
            Settings.Save();
            var added = new List<string>();
            foreach (var f in fresh.Take(6))
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    var g = await CustomGames.Add(f, cts.Token);
                    added.Add(g.Def.Name);
                }
                catch { }  // нет сети или игра без exe — добавится вручную
            }
            if (added.Count > 0)
            {
                AppState.Notify();
                Views.MainWindow.Current?.Toast(added.Count == 1
                    ? I18n.T("v151.new.toast", ("game", added[0]))
                    : I18n.T("v151.new.toast.many", ("games", string.Join(", ", added))));
            }
        }
        catch { }
        finally { _checking = false; }
    }

    static string Key(string path) => path.TrimEnd('\\', '/');

    /// <summary>Время появления — одно на игру; «недавняя» — позже запуска; «Новая» — пока в неё не играли.</summary>
    [SelfTest]
    static string NewGameComesFirst()
    {
        var id = "selftest-new-" + Environment.ProcessId;
        try
        {
            if (SeenAt(id) is not null) throw new Exception("seen before mark");
            Mark(id);
            var at = SeenAt(id) ?? throw new Exception("not marked");
            Mark(id);
            if (SeenAt(id) != at) throw new Exception("second mark moved the time");
            var g = new GameState { Def = GameCatalog.Builtin[0], Status = Detect.Found };
            if (Recent(g) < DateTime.MinValue) throw new Exception("recent");
            if (DateTime.UtcNow - at > TimeSpan.FromMinutes(1)) throw new Exception("time is not now: " + at);
            return "marked once, counts as recent, fresh for 3 days";
        }
        finally { Seen.Remove(id); }
    }
}
