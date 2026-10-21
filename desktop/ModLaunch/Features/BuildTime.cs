using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using ModLaunch.Core;

namespace ModLaunch.Features;

/// <summary>Время в одной сборке: игра + набор модов (профиль, сборка Minecraft или «без модов»).</summary>
public sealed record BuildPlay(string Key, string Game, string Name, int Mods, long TotalMs, int Sessions, DateTime? First, DateTime? Last, long LongestMs, bool Running)
{
    public double Hours => TotalMs / 3_600_000.0;
}

/// <summary>
/// 11.0: часы по сборкам. Сборка — это то, с чем человек играл: сохранённый профиль модов,
/// сборка Minecraft, «без модов» или безымянный набор (тогда ключ — отпечаток списка модов,
/// а имя — «N модов»). Считается при каждом запуске из ModLaunch, хранится в buildtime.json,
/// попадает в бэкап и в карточку достижений («Сохранить»).
/// </summary>
public static class BuildTime
{
    static readonly JsonFile File = new(Path.Combine(Paths.DataDir, "buildtime.json"), () => new JsonObject { ["builds"] = new JsonObject() });
    static readonly Dictionary<string, (string Key, DateTime Since)> Running = [];
    static readonly Dictionary<string, (string Key, string Name, int Mods)> Pending = [];
    /// <summary>
    /// Сборки трогают окно (запуск, список), поток выхода игры, облако (Adopt, Snapshot) и запись на диск —
    /// всё под одним замком, иначе запись падает с «Collection was modified».
    /// </summary>
    static readonly object Lock = new();
    /// <summary>Сколько сборок уходит в облако с одного ПК (по часам): документ облака не должен расти без конца.</summary>
    const int CloudMax = 200;

    /// <summary>Пороги часов для достижений сборки.</summary>
    public static readonly int[] Milestones = [1, 10, 50, 100, 500];

    static JsonObject Builds => File.Data.Obj("builds");

    /// <summary>Сборку назвал не профиль, а сам запуск (Minecraft: сборка с её именем).</summary>
    public static void Use(string gameId, string key, string name, int mods)
    {
        lock (Pending) Pending[gameId] = (key, name, mods);
    }

    /// <summary>Какая сборка сейчас у игры: профиль, набор модов или «без модов».</summary>
    public static (string Key, string Name, int Mods) Current(GameState g, bool vanilla = false)
    {
        var id = g.Def.Id;
        if (vanilla) return (id + "|vanilla", I18n.T("v11.bt.vanilla"), 0);
        List<string> mods = [];
        try { mods = g.Registry?.List().Where(r => r.Bool("enabled", true) && !r.Bool("missing")).Select(r => r.Str("id") ?? "").Where(s => s != "").OrderBy(s => s, StringComparer.Ordinal).ToList() ?? []; }
        catch { }
        if (Profiles.ActiveName(id) is { } profile) return (id + "|p:" + profile, profile, mods.Count);
        if (mods.Count == 0) return (id + "|vanilla", I18n.T("v11.bt.vanilla"), 0);
        var print = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', mods))))[..10].ToLowerInvariant();
        return (id + "|m:" + print, I18n.T("v11.bt.mods", ("n", mods.Count)), mods.Count);
    }

    public static void Start(GameState g, bool vanilla)
    {
        (string Key, string Name, int Mods) b;
        lock (Pending) b = Pending.Remove(g.Def.Id, out var p) ? p : Current(g, vanilla);
        lock (Lock)
        {
            var o = Builds[b.Key] as JsonObject;
            if (o is null)
            {
                o = new JsonObject { ["game"] = g.Def.Id, ["totalMs"] = 0, ["sessions"] = 0, ["longestMs"] = 0, ["first"] = DateTime.UtcNow.ToString("o") };
                Builds[b.Key] = o;
            }
            o["name"] = b.Name;
            o["mods"] = b.Mods;
            o["last"] = DateTime.UtcNow.ToString("o");
            lock (Running) Running[g.Def.Id] = (b.Key, DateTime.UtcNow);
            File.Save();
        }
    }

    /// <summary>Игра закрылась: те же правила, что у общего времени (меньше 15 секунд не считается).</summary>
    public static void Stop(string gameId)
    {
        (string Key, DateTime Since) r;
        lock (Running) if (!Running.Remove(gameId, out r)) return;
        var ms = (long)(DateTime.UtcNow - r.Since).TotalMilliseconds;
        if (ms < 15_000 || ms > 86_400_000) return;
        lock (Lock)
        {
            if (Builds[r.Key] is not JsonObject o) return;
            o["totalMs"] = o.Long("totalMs") + ms;
            o["sessions"] = o.Long("sessions") + 1;
            o["longestMs"] = Math.Max(o.Long("longestMs"), ms);
            // Сыграно здесь — теперь это и часы этого ПК (уходят в облако).
            o.Remove("adopted");
            File.Save();
        }
        Changed?.Invoke();
    }

    public static event Action? Changed;

    static DateTime? Date(JsonObject o, string key) => DateTime.TryParse(o.Str(key), out var d) ? d.ToUniversalTime() : null;

    /// <summary>Все сборки, где играли: больше часов — выше. Сборки Minecraft — с временем, накопленным до 11.0.</summary>
    public static List<BuildPlay> All()
    {
        var list = new List<BuildPlay>();
        HashSet<string> running;
        lock (Running) running = Running.Values.Select(v => v.Key).ToHashSet();
        lock (Lock)
            foreach (var (key, node) in Builds)
            {
                if (node is not JsonObject o) continue;
                list.Add(new BuildPlay(key, o.Str("game") ?? "", o.Str("name") ?? key, (int)o.Long("mods"), o.Long("totalMs"), (int)o.Long("sessions"),
                    Date(o, "first"), Date(o, "last"), o.Long("longestMs"), running.Contains(key)));
            }
        try
        {
            if (Minecraft.Mc.Def is { } mc)
                foreach (var i in Minecraft.Mc.Instances().Where(i => i.PlayMs > 0))
                {
                    var key = mc.Id + "|mc:" + i.Id;
                    var have = list.FindIndex(b => b.Key == key);
                    if (have < 0) list.Add(new BuildPlay(key, mc.Id, i.Name, 0, i.PlayMs, 0, i.Created, i.LastPlayed, 0, running.Contains(key)));
                    else if (list[have].TotalMs < i.PlayMs) list[have] = list[have] with { TotalMs = i.PlayMs, Name = i.Name };
                }
        }
        catch { }
        return list.Where(b => b.TotalMs > 0 || b.Running).OrderByDescending(b => b.TotalMs).ToList();
    }

    public static List<BuildPlay> ForGame(string gameId) => All().Where(b => b.Game == gameId).ToList();

    /// <summary>Следующий порог часов сборки (и сколько уже открыто).</summary>
    public static (int Done, int Next) Milestone(BuildPlay b)
    {
        var done = Milestones.Count(m => b.Hours >= m);
        return (done, done < Milestones.Length ? Milestones[done] : Milestones[^1]);
    }

    /// <summary>
    /// Для облачного профиля: часы по сборкам этого ПК — без взятых с других ПК (они и так в облаке) и без пустых
    /// заготовок; не больше CloudMax сборок, самые долгие.
    /// </summary>
    public static JsonObject Snapshot()
    {
        var result = new JsonObject();
        lock (Lock)
            foreach (var (key, node) in Builds.Where(b => b.Value.Long("totalMs") > 0 && !b.Value.Bool("adopted")).OrderByDescending(b => b.Value.Long("totalMs")).Take(CloudMax))
                result[key] = node!.DeepClone();
        return result;
    }

    /// <summary>
    /// Сборки из облака, которых здесь нет или где здесь пока ноль (запуск с вылетом за 15 секунд); своё не перезаписывается.
    /// Сборки Minecraft не берём: их часы на этом ПК — в самой сборке (PlayMs, и до 11.0), а id по имени
    /// («fabric-1-21-1») совпадает и у разных сборок на разных ПК.
    /// </summary>
    public static void Adopt(JsonObject builds)
    {
        var added = false;
        lock (Lock)
        {
            foreach (var (key, node) in builds)
            {
                if (node is not JsonObject o || o.Long("totalMs") <= 0 || key.Contains("|mc:")) continue;
                if (Builds[key] is JsonObject mine && mine.Long("totalMs") > 0) continue;
                var copy = (JsonObject)o.DeepClone();
                // С другого ПК: в облако от имени этого не уходит, пока здесь в неё не сыграют.
                copy["adopted"] = true;
                Builds[key] = copy;
                added = true;
            }
            if (!added) return;
            File.Save();
        }
        Changed?.Invoke();
    }

    /// <summary>Для бэкапа и переноса: файл с часами по сборкам.</summary>
    public static string FilePath => Path.Combine(Paths.DataDir, "buildtime.json");

    /// <summary>Для снимков экрана: несколько сборок с часами.</summary>
    public static void Demo()
    {
        var at = DateTime.UtcNow;
        void Add(string game, string name, int mods, double hours, int sessions, int days)
        {
            lock (Lock) Builds[$"{game}|p:{name}"] = new JsonObject
            {
                ["game"] = game, ["name"] = name, ["mods"] = mods, ["totalMs"] = (long)(hours * 3_600_000), ["sessions"] = sessions,
                ["longestMs"] = (long)(hours / Math.Max(1, sessions) * 3 * 3_600_000), ["first"] = at.AddDays(-days).ToString("o"), ["last"] = at.AddHours(-days / 4.0).ToString("o"),
            };
        }
        Add("lethal-company", "Друзья · хоррор", 38, 126.5, 64, 120);
        Add("valheim", "Строитель", 22, 61.2, 31, 200);
        Add("repo", "Ванильный+", 14, 12.4, 9, 30);
        Add("peak", "Без названия", 6, 3.1, 4, 8);
    }

    [SelfTest]
    static string BuildHoursCount()
    {
        var key = "selftest|p:Test";
        try
        {
            Builds[key] = new JsonObject { ["game"] = "selftest", ["name"] = "Test", ["mods"] = 3, ["totalMs"] = 0, ["sessions"] = 0, ["longestMs"] = 0 };
            lock (Running) Running["selftest"] = (key, DateTime.UtcNow.AddHours(-2));
            Stop("selftest");
            var b = All().FirstOrDefault(x => x.Key == key) ?? throw new Exception("build not listed");
            if (b.Sessions != 1 || Math.Abs(b.Hours - 2) > 0.01) throw new Exception($"bad count {b.Sessions} {b.Hours:0.00}");
            if (Milestone(b) != (1, 10)) throw new Exception("milestone");
            lock (Running) Running["selftest"] = (key, DateTime.UtcNow.AddSeconds(-5));
            Stop("selftest");
            if (All().First(x => x.Key == key).Sessions != 1) throw new Exception("5-second session counted");
        }
        finally { Builds.Remove(key); File.Save(); }
        return "hours are counted per build; short sessions are skipped";
    }

    [SelfTest]
    static string CloudBuildsMerge()
    {
        string[] keys = ["selftest|p:Mine", "selftest|p:Zero", "selftest|p:Other", "selftest|mc:fabric-1-21-1", "selftest|p:Empty"];
        long Ms(string key) { lock (Lock) return Builds[key].Long("totalMs"); }
        try
        {
            lock (Lock)
            {
                Builds[keys[0]] = new JsonObject { ["game"] = "selftest", ["name"] = "Mine", ["totalMs"] = 5_000_000 };
                Builds[keys[1]] = new JsonObject { ["game"] = "selftest", ["name"] = "Zero", ["totalMs"] = 0 };
            }
            var cloud = new JsonObject();
            foreach (var k in keys) cloud[k] = new JsonObject { ["game"] = "selftest", ["name"] = "cloud", ["totalMs"] = k == keys[4] ? 0 : 9_000_000 };
            Adopt(cloud);
            if (Ms(keys[0]) != 5_000_000) throw new Exception("own hours overwritten");
            if (Ms(keys[1]) != 9_000_000) throw new Exception("a zero placeholder blocked the cloud hours");
            if (Ms(keys[2]) != 9_000_000) throw new Exception("another PC's build not taken");
            lock (Lock) if (Builds[keys[3]] is not null || Builds[keys[4]] is not null) throw new Exception("Minecraft or empty build taken");
            var snap = Snapshot();
            if (!snap.ContainsKey(keys[0]) || snap.ContainsKey(keys[2])) throw new Exception("snapshot: " + string.Join(", ", snap.Select(b => b.Key)));
            lock (Running) Running["selftest"] = (keys[2], DateTime.UtcNow.AddMinutes(-1));
            Stop("selftest");
            if (!Snapshot().ContainsKey(keys[2])) throw new Exception("a build played here is not sent");
        }
        finally { lock (Lock) { foreach (var k in keys) Builds.Remove(k); File.Save(); } }
        return "cloud adds missing and zero builds, skips Minecraft; only builds played here are sent";
    }
}
