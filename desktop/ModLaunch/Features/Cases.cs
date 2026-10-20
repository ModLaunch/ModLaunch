using System.Text.Json.Nodes;
using ModLaunch.Core;

namespace ModLaunch.Features;

/// <summary>Кейс: за какое достижение (или неделю игры) дан и какой редкости.</summary>
public sealed record GameCase(string Id, string Tier, string From);

/// <summary>Что выпало: подсказка или небольшая функция (конфиг, виджет, акцент, косметика).</summary>
public sealed record Loot(string Kind, string Id, string Tier)
{
    /// <summary>Ключ открытия: «cfg:terminal», «widget:streak», «frame:holo»…</summary>
    public string Key => Kind + ":" + Id;
}

/// <summary>
/// 10.0: кейсы ModLaunch. Даются только за игру: каждое открытое достижение — кейс его редкости,
/// плюс кейс за неделю, в которую вы хоть раз играли. Купить их нельзя, ключей нет.
/// Внутри — подсказка или маленькая функция, которой ещё нет: конфиг «Терминал» или «Синтвейв»,
/// виджеты «Серия» и «Случайный мод», золотой акцент, рамка «Голограмма», баннер «Хранилище»,
/// титул «Открыватель», значок «Кейс». Повторов нет: когда функции кончаются — только подсказки.
/// Всё хранится в settings.profile.cases.
/// </summary>
public static class Cases
{
    public static readonly string[] Tiers = ["common", "rare", "legend"];

    /// <summary>Функции из кейсов и с какой редкости они могут выпасть.</summary>
    public static readonly Loot[] Features =
    [
        new("widget", "streak", "common"),
        new("badge", "case", "common"),
        new("title", "opener", "common"),
        new("widget", "random", "rare"),
        new("accent", "gold", "rare"),
        new("banner", "vault", "rare"),
        new("cfg", "terminal", "rare"),
        new("frame", "holo", "legend"),
        new("cfg", "synthwave", "legend"),
    ];

    public const int HintCount = 18;
    public const string GoldAccent = "#E8B84A";

    static JsonObject Store => Settings.Data.Obj("profile").Obj("cases");

    /// <summary>Все заработанные кейсы (и открытые, и нет).</summary>
    public static List<GameCase> Earned(List<Achievement>? all = null)
    {
        all ??= Achievements.All();
        var list = all.Where(a => a.Done).Select(a => new GameCase("a:" + a.Id, a.Tier switch { "legend" or "epic" => "legend", "rare" => "rare", _ => "common" }, a.Id)).ToList();
        foreach (var week in Store.Arr("weeks").Select(n => n?.ToString()).OfType<string>())
            list.Add(new GameCase("w:" + week, "common", "week"));
        return list;
    }

    /// <summary>Сколько дней подряд вы играли (сегодня или вчера — последний).</summary>
    public static int Streak()
    {
        var days = Store.Arr("days").Select(n => DateTime.TryParse(n?.ToString(), out var x) ? x.Date : DateTime.MinValue).ToHashSet();
        var day = DateTime.Today;
        if (!days.Contains(day)) day = day.AddDays(-1);
        var n = 0;
        while (days.Contains(day)) { n++; day = day.AddDays(-1); }
        return n;
    }

    public static HashSet<string> Opened() => Store.Arr("opened").Select(n => n?.ToString()).OfType<string>().ToHashSet();

    public static List<GameCase> Ready(List<Achievement>? all = null)
    {
        var opened = Opened();
        return Earned(all).Where(c => !opened.Contains(c.Id))
            .OrderByDescending(c => Array.IndexOf(Tiers, c.Tier)).ToList();
    }

    public static HashSet<string> Unlocked() => Store.Arr("unlocked").Select(n => n?.ToString()).OfType<string>().ToHashSet();

    public static bool Has(string key) => Unlocked().Contains(key);

    /// <summary>Отметить неделю игры (вызывается после каждого запуска игры): одна неделя — один кейс.</summary>
    public static void NoteWeek(DateTime? when = null)
    {
        var d = (when ?? DateTime.Now).Date;
        var week = $"{System.Globalization.ISOWeek.GetYear(d)}-{System.Globalization.ISOWeek.GetWeekOfYear(d):00}";
        var day = d.ToString("yyyy-MM-dd");
        var days = Store.Arr("days");
        if (!days.Any(n => n?.ToString() == day))
        {
            days.Add(JsonValue.Create(day));
            while (days.Count > 400) days.RemoveAt(0);
            Store["days"] = days.DeepClone();
            Settings.Save();
        }
        var weeks = Store.Arr("weeks");
        if (weeks.Any(n => n?.ToString() == week)) return;
        weeks.Add(JsonValue.Create(week));
        Store["weeks"] = weeks.DeepClone();
        Settings.Save();
    }

    /// <summary>
    /// Открыть кейс: что выпадет, решено заранее по id кейса (одинаково на любом ПК), но без повторов —
    /// из функций, которых ещё нет и которые подходят по редкости. Иначе — подсказка.
    /// </summary>
    public static Loot Open(GameCase c)
    {
        var have = Unlocked();
        var rank = Array.IndexOf(Tiers, c.Tier);
        var seed = Seed(c.Id);
        var pool = Features.Where(f => !have.Contains(f.Key) && Array.IndexOf(Tiers, f.Tier) <= rank).ToList();
        // Обычный кейс — функция примерно в половине случаев, редкий и легендарный — всегда, пока они есть.
        Loot loot = pool.Count > 0 && (rank > 0 || seed % 2 == 0)
            ? pool.OrderByDescending(f => Array.IndexOf(Tiers, f.Tier)).ThenBy(f => Seed(c.Id + f.Key)).First()
            : new Loot("hint", (1 + seed % HintCount).ToString(), "common");
        var opened = Store.Arr("opened");
        opened.Add(JsonValue.Create(c.Id));
        Store["opened"] = opened.DeepClone();
        if (loot.Kind != "hint")
        {
            var unlocked = Store.Arr("unlocked");
            unlocked.Add(JsonValue.Create(loot.Key));
            Store["unlocked"] = unlocked.DeepClone();
        }
        var log = Store.Arr("log");
        log.Insert(0, new JsonObject { ["case"] = c.Id, ["loot"] = loot.Key, ["tier"] = c.Tier, ["at"] = DateTime.UtcNow.ToString("o") });
        while (log.Count > 60) log.RemoveAt(log.Count - 1);
        Store["log"] = log.DeepClone();
        Settings.Save();
        Changed?.Invoke();
        return loot;
    }

    public static event Action? Changed;

    /// <summary>Что выпадало раньше (новое сверху).</summary>
    public static List<(Loot Loot, DateTime At)> History() => Store.Arr("log").OfType<JsonObject>()
        .Select(o =>
        {
            var key = o.Str("loot") ?? "hint:1";
            var i = key.IndexOf(':');
            var kind = i < 0 ? "hint" : key[..i];
            var id = i < 0 ? "1" : key[(i + 1)..];
            var tier = Features.FirstOrDefault(f => f.Key == key)?.Tier ?? o.Str("tier") ?? "common";
            return (new Loot(kind, id, tier), DateTime.TryParse(o.Str("at"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var at) ? at : DateTime.MinValue);
        }).ToList();

    /// <summary>Награды для шкафчика профиля из кейсов: рамка, баннер, титул, значок.</summary>
    public static IEnumerable<Reward> Cosmetic() => Features.Where(f => f.Kind is "frame" or "banner" or "title" or "badge").Select(f => new Reward(f.Kind, f.Id));

    static int Seed(string s)
    {
        unchecked
        {
            var h = 2166136261u;
            foreach (var ch in s) h = (h ^ ch) * 16777619u;
            return (int)(h & 0x7FFFFFFF);
        }
    }

    [SelfTest]
    static string CasesNeverRepeatFeatures()
    {
        var profile = Settings.Data.Obj("profile");
        var was = profile["cases"]?.DeepClone();
        try
        {
            profile.Remove("cases");
            var got = new List<Loot>();
            for (var i = 0; i < 40; i++) got.Add(Open(new GameCase("test:" + i, i % 3 == 0 ? "legend" : "rare", "test")));
            var features = got.Where(l => l.Kind != "hint").Select(l => l.Key).ToList();
            if (features.Count != features.Distinct().Count()) throw new Exception("a feature dropped twice");
            if (features.Count != Features.Length) throw new Exception($"only {features.Count} of {Features.Length} features dropped");
            if (Ready().Any(c => c.Id.StartsWith("test:"))) throw new Exception("opened case still ready");
            return $"{features.Count} features without repeats, then hints";
        }
        finally
        {
            if (was is null) profile.Remove("cases"); else profile["cases"] = was;
            Settings.Save();
        }
    }
}
