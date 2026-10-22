using System.Text.Json.Nodes;
using ModLaunch.Core;

namespace ModLaunch.Games;

/// <summary>Игра из библиотеки Steam: установлена (Installed не null) или только есть в аккаунте. Minutes — наигранные минуты.</summary>
public sealed record SteamGame(int AppId, string Name, SteamApp? Installed, long LastPlayed, long Minutes)
{
    public bool IsInstalled => Installed is not null;
    public long Size => Installed?.SizeOnDisk ?? 0;
}

/// <summary>
/// 14.0: библиотека Steam целиком, как в самом клиенте. Установленные — из манифестов (SteamLibrary),
/// остальные и наигранные часы — из userdata/&lt;id&gt;/config/localconfig.vdf последнего вошедшего
/// пользователя (там все игры, которые запускались на этом ПК). Ключ Steam Web API не нужен.
/// Имена неустановленных берутся из справочника игр, а неизвестные — из магазина Steam
/// (по одной, лениво) и хранятся в steam-names.json.
/// </summary>
public static class SteamAccount
{
    static List<SteamGame>? _games;
    static DateTime _at;
    static readonly object Gate = new();
    static JsonObject? _names;

    public static event Action? Changed;

    /// <summary>Демо и снимки: у игр есть часы, часть не установлена.</summary>
    static readonly (int App, string Name, long Minutes, int DaysAgo)[] DemoOwned =
    [
        (1245620, "", 9_420, 0), (1086940, "", 7_310, 2), (105600, "", 21_150, 5), (1145350, "", 2_480, 1), (504230, "", 1_260, 40),
        (570, "", 64_800, 9), (1091500, "", 5_530, 21), (413150, "", 12_780, 3),
        (292030, "The Witcher 3: Wild Hunt", 8_840, 120), (620, "Portal 2", 960, 300), (367520, "Hollow Knight", 3_300, 60),
        (1174180, "Red Dead Redemption 2", 6_120, 90), (814380, "Sekiro: Shadows Die Twice", 2_210, 200), (1794680, "Vampire Survivors", 1_840, 30),
        (646570, "Slay the Spire", 4_420, 75), (892970, "Valheim", 3_960, 14),
    ];

    /// <summary>Вся библиотека: сначала недавние. Кеш на минуту.</summary>
    public static IReadOnlyList<SteamGame> All
    {
        get
        {
            lock (Gate)
            {
                if (_games is null || DateTime.UtcNow - _at > TimeSpan.FromMinutes(1))
                {
                    _games = Program.Demo || Program.Screenshot ? Demo() : Read();
                    _at = DateTime.UtcNow;
                }
                return _games;
            }
        }
    }

    public static SteamGame? ById(int appId) => All.FirstOrDefault(g => g.AppId == appId);

    static List<SteamGame> Demo()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var installed = SteamLibrary.Installed.ToDictionary(a => a.AppId);
        return Sort(DemoOwned.Select(d =>
        {
            installed.TryGetValue(d.App, out var app);
            return new SteamGame(d.App, app?.Name ?? d.Name, app, now - d.DaysAgo * 86_400L - 3_600, d.Minutes);
        }));
    }

    static List<SteamGame> Sort(IEnumerable<SteamGame> list) =>
        [.. list.OrderByDescending(g => g.LastPlayed).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)];

    static List<SteamGame> Read()
    {
        var installed = SteamLibrary.Installed.ToDictionary(a => a.AppId);
        var played = LocalApps();
        var result = new List<SteamGame>();
        foreach (var a in installed.Values)
        {
            played.TryGetValue(a.AppId, out var p);
            result.Add(new SteamGame(a.AppId, a.Name, a, Math.Max(a.LastPlayed, p.LastPlayed), p.Minutes));
        }
        foreach (var (id, p) in played)
        {
            if (installed.ContainsKey(id)) continue;
            // Без минут и без даты запуска — это служебные записи (инструменты, демо, бесплатные выходные).
            if (p.Minutes <= 0 && p.LastPlayed <= 0) continue;
            var name = NameOf(id);
            if (name is null) { Lookup(id); name = ""; }
            if (CustomGames.IsTool(name)) continue;
            result.Add(new SteamGame(id, name, null, p.LastPlayed, p.Minutes));
        }
        return Sort(result);
    }

    /// <summary>appid → (последний запуск, минуты) из localconfig.vdf последнего пользователя.</summary>
    internal static Dictionary<int, (long LastPlayed, long Minutes)> LocalApps()
    {
        var file = Locator.SteamRoots()
            .SelectMany(r => { try { return Directory.EnumerateDirectories(Path.Combine(r, "userdata")); } catch { return []; } })
            .Select(d => Path.Combine(d, "config", "localconfig.vdf"))
            .Where(File.Exists)
            .OrderByDescending(f => { try { return File.GetLastWriteTimeUtc(f); } catch { return DateTime.MinValue; } })
            .FirstOrDefault();
        if (file is null) return [];
        try { return ParseLocal(File.ReadAllText(file)); } catch { return []; }
    }

    internal static Dictionary<int, (long LastPlayed, long Minutes)> ParseLocal(string text)
    {
        var result = new Dictionary<int, (long, long)>();
        if (Vdf.Get(Vdf.Parse(text), "UserLocalConfigStore", "Software", "Valve", "Steam", "apps") is not Dictionary<string, object> apps) return result;
        foreach (var (key, value) in apps)
        {
            if (!int.TryParse(key, out var id) || id <= 0 || value is not Dictionary<string, object> app) continue;
            long.TryParse(Vdf.Get(app, "LastPlayed") as string, out var last);
            long.TryParse(Vdf.Get(app, "Playtime") as string, out var minutes);
            result[id] = (last, minutes);
        }
        return result;
    }

    // ---------------------------------------------------------------- имена неустановленных

    static string NamesFile => Path.Combine(Paths.DataDir, "steam-names.json");

    static string? NameOf(int appId)
    {
        if (GameDirectory.BySteam(appId) is { } dir) return dir.Name;
        lock (Gate)
        {
            if (_names is null)
            {
                try { _names = File.Exists(NamesFile) ? JsonNode.Parse(File.ReadAllText(NamesFile)) as JsonObject : null; } catch { }
                _names ??= [];
            }
            return _names.Str(appId.ToString());
        }
    }

    static readonly HashSet<int> Asked = [];
    static readonly SemaphoreSlim One = new(1, 1);

    /// <summary>Спросить имя у магазина Steam (по одной игре за раз, не чаще раза за сеанс).</summary>
    static void Lookup(int appId)
    {
        lock (Asked) if (!Asked.Add(appId) || Asked.Count > 400) return;
        _ = Task.Run(async () =>
        {
            await One.WaitAsync();
            try
            {
                var json = await Http.GetJson($"https://store.steampowered.com/api/appdetails?appids={appId}&filters=basic", default, 15);
                if (json?[appId.ToString()]?["data"]?["name"]?.ToString() is not { Length: > 0 } name) return;
                lock (Gate)
                {
                    _names ??= [];
                    _names[appId.ToString()] = name;
                    try { File.WriteAllText(NamesFile, _names.ToJsonString()); } catch { }
                    _games = null;
                }
                Changed?.Invoke();
            }
            catch { }
            finally { await Task.Delay(350); One.Release(); }
        });
    }

    [SelfTest]
    static string SteamLocalConfigParses()
    {
        var map = ParseLocal("""
            "UserLocalConfigStore"
            {
              "Software" { "Valve" { "Steam" {
                "apps"
                {
                  "570" { "LastPlayed" "1760000000" "Playtime" "64800" }
                  "620" { "Playtime" "15" }
                  "x" { "Playtime" "9" }
                }
              } } }
            }
            """);
        if (map.Count != 2 || map[570] != (1760000000, 64800) || map[620] != (0, 15)) throw new Exception(string.Join("; ", map));
        if (ParseLocal("\"other\" { }").Count != 0) throw new Exception("empty");
        return "apps with playtime and last launch read, junk keys dropped";
    }
}
