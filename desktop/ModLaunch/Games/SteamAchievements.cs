using System.Text;
using ModLaunch.Core;

namespace ModLaunch.Games;

/// <summary>Достижение Steam: название и описание на языке программы, скрытое ли, значок, получено ли и когда.</summary>
public sealed record SteamAchievement(string Id, string Name, string Description, bool Hidden, string? Icon, bool Unlocked, DateTime? At);

/// <summary>
/// 30.0: достижения Steam без сети и ключей — из того, что Steam сам хранит на диске: appcache/stats/
/// UserGameStatsSchema_&lt;app&gt;.bin (список достижений с названиями на всех языках) и
/// UserGameStats_&lt;аккаунт&gt;_&lt;app&gt;.bin (битовые маски полученных и время). Оба файла — двоичные KeyValues.
/// Файлы появляются, когда в игру играли на этом ПК со Steam; нет файлов — null.
/// </summary>
public static class SteamAchievements
{
    static readonly Dictionary<string, (DateTime Stamp, List<SteamAchievement> List)> Cache = [];

    // ---------------------------------------------------------------- двоичный KeyValues

    /// <summary>Разобрать двоичный KeyValues: 0 — вложенный, 1 — строка, 2 — int32, 3 — float, 7/10 — 64 бита, 8 — конец.</summary>
    internal static Dictionary<string, object> Parse(byte[] data)
    {
        var pos = 0;
        string Str()
        {
            var end = Array.IndexOf(data, (byte)0, pos);
            if (end < 0) throw new InvalidDataException("unterminated string");
            var s = Encoding.UTF8.GetString(data, pos, end - pos);
            pos = end + 1;
            return s;
        }
        Dictionary<string, object> Obj(int depth)
        {
            if (depth > 32) throw new InvalidDataException("too deep");
            var o = new Dictionary<string, object>(StringComparer.Ordinal);
            while (pos < data.Length)
            {
                var type = data[pos++];
                if (type == 0x08) return o;
                var key = Str();
                switch (type)
                {
                    case 0x00: o[key] = Obj(depth + 1); break;
                    case 0x01: o[key] = Str(); break;
                    case 0x02: o[key] = (long)BitConverter.ToInt32(data, pos); pos += 4; break;
                    case 0x03: o[key] = (double)BitConverter.ToSingle(data, pos); pos += 4; break;
                    case 0x07: case 0x0A: o[key] = BitConverter.ToInt64(data, pos); pos += 8; break;
                    default: throw new InvalidDataException($"type {type} at {pos - 1}");
                }
            }
            return o;
        }
        return Obj(0);
    }

    static Dictionary<string, object>? Map(object? o, string key) => o is Dictionary<string, object> d && d.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;

    static string? Text(object? o, string key) => o is Dictionary<string, object> d && d.TryGetValue(key, out var v) ? v as string : null;

    static long Num(object? o, string key) => o is Dictionary<string, object> d && d.TryGetValue(key, out var v) ? v switch { long l => l, double x => (long)x, string s when long.TryParse(s, out var p) => p, _ => 0 } : 0;

    /// <summary>Язык программы → язык Steam в схеме.</summary>
    static string SteamLang(string lang) => lang switch
    {
        "ru" => "russian", "uk" => "ukrainian", "es" => "spanish", "de" => "german", "fr" => "french", "pt-BR" => "brazilian", "zh-CN" => "schinese", _ => "english",
    };

    /// <summary>Название: строкой или словарём по языкам (служебный token пропускаем).</summary>
    static string Localized(object? display, string key, string lang)
    {
        if (display is not Dictionary<string, object> d || !d.TryGetValue(key, out var v)) return "";
        if (v is string s) return s;
        if (v is Dictionary<string, object> byLang)
            return (byLang.GetValueOrDefault(SteamLang(lang)) ?? byLang.GetValueOrDefault("english") ?? byLang.Where(kv => kv.Key != "token").Select(kv => kv.Value).FirstOrDefault()) as string ?? "";
        return "";
    }

    /// <summary>Собрать достижения из схемы и статистики игрока (любая может быть пустой).</summary>
    internal static List<SteamAchievement> Build(int appId, Dictionary<string, object> schema, Dictionary<string, object>? user, string lang)
    {
        var result = new List<SteamAchievement>();
        var stats = Map(Map(schema, appId.ToString()) ?? schema.Values.OfType<Dictionary<string, object>>().FirstOrDefault(), "stats");
        var cache = Map(user, "cache");
        foreach (var (statId, statNode) in stats ?? [])
        {
            if (Map(statNode, "bits") is not { Count: > 0 } bits) continue;
            var stat = Map(cache, statId);
            var mask = Num(stat, "data");
            var times = Map(stat, "AchievementTimes");
            foreach (var (bitKey, bitNode) in bits.OrderBy(b => int.TryParse(b.Key, out var n) ? n : 0))
            {
                if (!int.TryParse(bitKey, out var bit) || bit is < 0 or > 31) continue;
                var display = Map(bitNode, "display");
                var unlocked = ((mask >> bit) & 1) == 1;
                var at = Num(times, bitKey) is > 0 and var t ? DateTimeOffset.FromUnixTimeSeconds(t).UtcDateTime : (DateTime?)null;
                var icon = Text(display, unlocked ? "icon" : "icon_gray") ?? Text(display, "icon");
                result.Add(new SteamAchievement(Text(bitNode, "name") ?? $"{statId}.{bit}", Localized(display, "name", lang), Localized(display, "desc", lang),
                    Num(display, "hidden") == 1, icon is { Length: > 0 } ? $"https://cdn.cloudflare.steamstatic.com/steamcommunity/public/images/apps/{appId}/{icon}" : null,
                    unlocked, unlocked ? at : null));
            }
        }
        return result;
    }

    // ---------------------------------------------------------------- с диска

    /// <summary>Достижения игры по файлам Steam этого ПК. null — схемы нет (в игру здесь не играли или в ней нет достижений).</summary>
    public static List<SteamAchievement>? For(int appId)
    {
        if (appId <= 0) return null;
        if (Program.Demo || Program.Screenshot) return Demo(appId);
        foreach (var root in Locator.SteamRoots())
        {
            try
            {
                var dir = Path.Combine(root, "appcache", "stats");
                var schemaFile = Path.Combine(dir, $"UserGameStatsSchema_{appId}.bin");
                if (!File.Exists(schemaFile)) continue;
                // Статистика последнего, кто играл (на ПК бывает несколько аккаунтов Steam).
                var userFile = Directory.EnumerateFiles(dir, $"UserGameStats_*_{appId}.bin").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                var stamp = new[] { File.GetLastWriteTimeUtc(schemaFile), userFile is null ? DateTime.MinValue : File.GetLastWriteTimeUtc(userFile) }.Max();
                var key = $"{appId}|{I18n.Lang}|{userFile}";
                lock (Cache) if (Cache.TryGetValue(key, out var c) && c.Stamp == stamp) return c.List;
                var list = Build(appId, Parse(File.ReadAllBytes(schemaFile)), userFile is null ? null : Parse(File.ReadAllBytes(userFile)), I18n.Lang);
                lock (Cache) Cache[key] = (stamp, list);
                return list.Count == 0 ? null : list;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException) { }
        }
        return null;
    }

    static List<SteamAchievement>? Demo(int appId)
    {
        if (appId != 1245620) return null;
        var now = DateTime.UtcNow;
        string[] names = I18n.Lang == "ru"
            ? ["Элден Лорд", "Наследник Великой Руны", "Порог Эрдтри", "Убийца драконов", "Друг Ранни", "Великий Рыцарь", "Исследователь", "Мастер оружия"]
            : ["Elden Lord", "Great Rune Heir", "Erdtree Threshold", "Dragonslayer", "Ranni's Friend", "Great Knight", "Explorer", "Weapon Master"];
        return names.Select((n, i) => new SteamAchievement("A" + i, n, I18n.Lang == "ru" ? "Пройти важную часть игры" : "Complete an important part of the game", i == 4, null, i < 5, i < 5 ? now.AddDays(-i * 9) : null)).ToList();
    }

    [SelfTest]
    static string SteamAchievementsFromBinaryKv()
    {
        // Схема: 1 стат-число и 1 стат с тремя достижениями (биты 1, 2, 5); игрок получил биты 1 и 5.
        static void Str(List<byte> b, string s) { b.AddRange(Encoding.UTF8.GetBytes(s)); b.Add(0); }
        static void Open(List<byte> b, string k) { b.Add(0); Str(b, k); }
        static void Close(List<byte> b) => b.Add(8);
        static void S(List<byte> b, string k, string v) { b.Add(1); Str(b, k); Str(b, v); }
        static void I(List<byte> b, string k, int v) { b.Add(2); Str(b, k); b.AddRange(BitConverter.GetBytes(v)); }
        var schema = new List<byte>();
        Open(schema, "10"); Open(schema, "stats");
        Open(schema, "1"); S(schema, "type", "INT"); S(schema, "name", "kills"); Close(schema);
        Open(schema, "2"); S(schema, "type", "ACHIEVEMENTS"); Open(schema, "bits");
        foreach (var (bit, id, en, ru, hidden) in new[] { (1, "FIRST", "First steps", "Первые шаги", 0), (2, "DEEP", "Deep dive", "Глубокое погружение", 1), (5, "END", "The end", "Финал", 0) })
        {
            Open(schema, bit.ToString()); S(schema, "name", id); Open(schema, "display");
            Open(schema, "name"); S(schema, "token", "T"); S(schema, "english", en); S(schema, "russian", ru); Close(schema);
            S(schema, "desc", "d"); I(schema, "hidden", hidden); S(schema, "icon", "a.jpg"); S(schema, "icon_gray", "g.jpg");
            Close(schema); Close(schema);
        }
        Close(schema); Close(schema); Close(schema); Close(schema); Close(schema);
        var user = new List<byte>();
        Open(user, "cache"); Open(user, "2"); I(user, "data", (1 << 1) | (1 << 5)); Open(user, "AchievementTimes"); I(user, "1", 1_700_000_000); I(user, "5", 1_710_000_000); Close(user); Close(user);
        I(user, "crc", 5); Close(user); Close(user);

        var list = Build(10, Parse([.. schema]), Parse([.. user]), "ru");
        if (list.Count != 3) throw new Exception("count " + list.Count);
        if (list.Count(a => a.Unlocked) != 2 || list.Single(a => a.Id == "DEEP").Unlocked) throw new Exception("unlocked bits");
        if (list[0].Name != "Первые шаги" || Build(10, Parse([.. schema]), null, "de")[0].Name != "First steps") throw new Exception("language: " + list[0].Name);
        if (!list.Single(a => a.Id == "DEEP").Hidden || list.Single(a => a.Id == "END").At?.Year != 2024) throw new Exception("hidden/time");
        if (list[0].Icon != "https://cdn.cloudflare.steamstatic.com/steamcommunity/public/images/apps/10/a.jpg" || !list[1].Icon!.EndsWith("/g.jpg")) throw new Exception("icons");
        if (Build(10, Parse([.. schema]), null, "ru").Any(a => a.Unlocked)) throw new Exception("no user file means nothing unlocked");
        return "binary KeyValues parsed; bits → unlocked, localized names (token skipped), hidden, unlock time, colour/grey icons";
    }
}
