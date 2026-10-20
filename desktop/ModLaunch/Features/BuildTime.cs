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

    /// <summary>Игра закрылась: те же правила, что у общего времени (меньше 15 секунд не считается).</summary>
    public static void Stop(string gameId)
    {
        (string Key, DateTime Since) r;
        lock (Running) if (!Running.Remove(gameId, out r)) return;
        var ms = (long)(DateTime.UtcNow - r.Since).TotalMilliseconds;
        if (ms < 15_000 || ms > 86_400_000 || Builds[r.Key] is not JsonObject o) return;
        o["totalMs"] = o.Long("totalMs") + ms;
        o["sessions"] = o.Long("sessions") + 1;
        o["longestMs"] = Math.Max(o.Long("longestMs"), ms);
        File.Save();
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

    /// <summary>Для облачного профиля: часы по сборкам этого ПК.</summary>
    public static JsonObject Snapshot() => (JsonObject)Builds.DeepClone();

    /// <summary>Сборки из облака, которых здесь нет (своё не перезаписывается).</summary>
    public static void Adopt(JsonObject builds)
    {
        var added = false;
        foreach (var (key, node) in builds)
            if (node is JsonObject o && Builds[key] is null && o.Long("totalMs") > 0) { Builds[key] = o.DeepClone(); added = true; }
        if (!added) return;
        File.Save();
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
            Builds[$"{game}|p:{name}"] = new JsonObject
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
}
