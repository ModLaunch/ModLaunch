using System.Text.Json.Nodes;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Social;

namespace ModLaunch.Creator;

/// <summary>
/// 26.0: дневник счётчиков автора на этом ПК и «что нового» для колокольчика. Всё хранится в настройках под uid
/// аккаунта (creatorStats.&lt;uid&gt;): days — снимки по дням (CreatorStats), market — продажи по дням, names — названия
/// модов для строк уведомлений, seen — что автор уже видел (с этим сравниваются новые счётчики).
/// Пишется только на потоке интерфейса: настройки — общий JsonObject.
/// </summary>
public static class CreatorWatch
{
    static List<CreatorNote> _notes = [];
    static string? _notesUid;

    /// <summary>Новое с прошлого просмотра статистики — для колокольчика (пусто, если не вошли).</summary>
    public static IReadOnlyList<CreatorNote> Notes => Account.SignedIn && _notesUid == Account.Uid || Program.Demo && _notesUid == "demo" ? _notes : [];

    /// <summary>Когда уведомления пересчитаны в последний раз (UTC) — статистика перечитывается, если они новее неё.</summary>
    public static DateTime NotesAt { get; private set; }

    /// <summary>Уведомления автора изменились — перерисовать колокольчик.</summary>
    public static event Action? Changed;

    static JsonObject Root => Settings.Data.Obj("creatorStats");

    /// <summary>Записи этого аккаунта; null — автор ещё ничего не выкладывал (настройки не трогаем).</summary>
    static JsonObject? Find(string uid) => Root[uid] as JsonObject;

    /// <summary>Снимки по дням для графика (пустой объект — истории нет).</summary>
    public static JsonObject Days(string uid) => Find(uid)?["days"] as JsonObject ?? new JsonObject();

    /// <summary>Продажи по дням (sales, revenue) — последний известный итог.</summary>
    public static (long Sales, long Revenue)? LastMarket(string uid)
    {
        if (Find(uid)?["market"] is not JsonObject market || market.Count == 0) return null;
        var last = market.OrderBy(kv => kv.Key, StringComparer.Ordinal).Last().Value;
        return (last.Long("sales"), last.Long("revenue"));
    }

    /// <summary>Есть ли у автора опубликованные моды (по последнему снимку этого аккаунта) — шаг «Опубликовать».</summary>
    public static bool Published => Account.Uid is { } uid && Find(uid)?["days"] is JsonObject days && days.Any(kv => kv.Value is JsonObject d && d.Count > 0);

    /// <summary>Автор копировал ссылку на свой мод — шаг «Поделиться».</summary>
    public static bool Shared => Settings.Data.Bool("creatorShared");

    public static void MarkShared()
    {
        if (Shared) return;
        Settings.Data["creatorShared"] = true;
        Settings.Save();
    }

    /// <summary>«Путь автора» скрыт крестиком.</summary>
    public static bool JourneyHidden
    {
        get => Settings.Data.Bool("creatorJourneyHidden");
        set { Settings.Data["creatorJourneyHidden"] = value; Settings.Save(); }
    }

    static JsonObject Counts(ModCounts c) => new() { ["downloads"] = c.Downloads, ["likes"] = c.Likes, ["comments"] = c.Comments };

    /// <summary>
    /// Данные Hub загрузились: записать снимок дня и пересчитать «что нового» против seen. Первый раз seen — сами
    /// счётчики (иначе первым уведомлением пришли бы все скачивания за всё время). sales — сколько продано на рынке
    /// (null — не узнали, остаётся прежнее), revenue — выручка (известна только из статистики). Возвращает новое.
    /// </summary>
    public static IReadOnlyList<CreatorNote> Observe(string uid, IEnumerable<HubMod> mine, long? sales = null, long? revenue = null, DateTime? now = null)
    {
        var counts = mine.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => ModCounts.Of(g.First()), StringComparer.Ordinal);
        // Не автор (ничего не выложено и не продаётся) — в настройки ничего не пишем.
        if (counts.Count == 0 && (sales ?? 0) == 0 && Find(uid) is null) return [];
        var store = Root.Obj(uid);
        var today = (now ?? DateTime.Now).Date;
        CreatorStats.Record(store.Obj("days"), today, counts);
        var names = store.Obj("names");
        foreach (var m in mine) names[m.Id] = m.Name;
        if (sales is not null)
        {
            var market = store.Obj("market");
            var day = new JsonObject { ["sales"] = sales.Value };
            // Выручку знает только статистика (продажи по лотам); без неё — прежняя за сегодня, если была.
            var old = market[CreatorStats.Day(today)] as JsonObject;
            if (revenue is not null) day["revenue"] = revenue.Value;
            else if (old?["revenue"] is JsonValue r) day["revenue"] = r.DeepClone();
            market[CreatorStats.Day(today)] = day;
            CreatorStats.Prune(market, today);
        }
        if (store["seen"] is not JsonObject seen)
        {
            store["seen"] = Seen(counts, sales ?? 0);
            _notes = [];
        }
        else
        {
            var nameMap = names.Select(kv => kv.Key).ToDictionary(k => k, k => names.Str(k) ?? k, StringComparer.Ordinal);
            // Продажи не узнавали (данные пришли с рынка или Hub без лотов) — последнее известное число, а не «уже видел»:
            // иначе уведомление о продажах с запуска пропадало бы при следующей загрузке Hub.
            var nowSales = sales ?? LastMarket(uid)?.Sales ?? seen.Long("sales");
            _notes = CreatorStats.Notes(CreatorStats.ReadMods(seen["mods"] as JsonObject), counts, nameMap, seen.Long("sales"), nowSales);
        }
        _notesUid = uid;
        NotesAt = DateTime.UtcNow;
        Settings.Save();
        Changed?.Invoke();
        return _notes;
    }

    static JsonObject Seen(Dictionary<string, ModCounts> counts, long sales)
    {
        var mods = new JsonObject();
        foreach (var (id, c) in counts) mods[id] = Counts(c);
        return new JsonObject { ["mods"] = mods, ["sales"] = sales, ["at"] = DateTime.UtcNow.ToString("o") };
    }

    /// <summary>Автор посмотрел статистику (или очистил колокольчик): нынешние счётчики — «уже видел».</summary>
    public static void MarkSeen()
    {
        if (Program.Demo && _notesUid == "demo") { _notes = []; Changed?.Invoke(); return; }
        if (Account.Uid is not { } uid || Find(uid) is not { } store) { _notes = []; Changed?.Invoke(); return; }
        var days = store["days"] as JsonObject;
        var latest = days?.OrderBy(kv => kv.Key, StringComparer.Ordinal).LastOrDefault().Value as JsonObject;
        var sales = LastMarket(uid)?.Sales ?? (store["seen"] as JsonObject).Long("sales");
        store["seen"] = Seen(CreatorStats.ReadMods(latest), sales);
        _notes = [];
        Settings.Save();
        Changed?.Invoke();
    }

    /// <summary>Вышли или сменили аккаунт — чужие уведомления не показываем.</summary>
    public static void Reset()
    {
        if (_notesUid == Account.Uid) return;
        _notes = [];
        _notesUid = null;
        Changed?.Invoke();
    }

    /// <summary>
    /// Проверка в фоне (запуск программы): свои моды из общего списка Hub (кэш на минуту — тот же запрос, что у
    /// отслеживания) и число продаж своих лотов; снимок и уведомления — на потоке интерфейса.
    /// </summary>
    public static async Task Check(bool force = false)
    {
        if (Program.Demo || !Account.SignedIn || Account.Uid is not { } uid) return;
        List<HubMod> mine;
        try
        {
            var (mods, complete) = await Mine(uid, force);
            if (!complete) return;
            mine = mods;
        }
        catch { return; }
        long? sales = null;
        try { sales = (await Market.Mine()).Where(l => !l.Free).Sum(l => l.Sales); }
        catch { }
        Dispatcher.UIThread.Post(() => { if (Account.Uid == uid) Observe(uid, mine, sales); });
    }

    /// <summary>Id модов из последнего снимка этого аккаунта (читать на потоке интерфейса).</summary>
    static List<string> KnownIds(string uid) =>
        (Find(uid)?["days"] as JsonObject)?.OrderBy(kv => kv.Key, StringComparer.Ordinal).LastOrDefault().Value is JsonObject day ? day.Select(kv => kv.Key).ToList() : [];

    /// <summary>
    /// Все ли моды из последнего снимка есть в списке (общий список Hub — до 300 свежих: старый мод автора мог из него
    /// выпасть, и итог снимка «упал» бы). Нет — снимок по этому списку не пишем, его допишет проверка с Mine.
    /// </summary>
    public static bool Covers(string uid, IEnumerable<HubMod> list)
    {
        var have = list.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        return KnownIds(uid).All(have.Contains);
    }

    /// <summary>
    /// Свои моды Hub: из общего списка (кэш на минуту), а те, что были в прошлом снимке, но в список не попали
    /// (он — до 300 свежих), — по одному запросу на мод; удалённые (нет на сервере) из снимка уходят.
    /// Complete = false — какой-то мод не дочитался (сеть): такой список показывать можно, а записывать в снимок нельзя.
    /// </summary>
    public static async Task<(List<HubMod> Mods, bool Complete)> Mine(string uid, bool force = false)
    {
        var mine = (await Hub.All(force)).Where(m => m.Uid == uid).ToList();
        var known = await Dispatcher.UIThread.InvokeAsync(() => KnownIds(uid));
        var complete = true;
        foreach (var id in known.Where(id => mine.All(m => m.Id != id)).Take(30))
        {
            try { if (await Hub.Get(id) is { } m && m.Uid == uid) mine.Add(m); }
            catch { complete = false; }
        }
        return (mine, complete);
    }

    /// <summary>Для снимков: уведомления автора без сети.</summary>
    public static void Demo(IEnumerable<CreatorNote> notes)
    {
        _notes = notes.ToList();
        _notesUid = "demo";
        Changed?.Invoke();
    }
}
