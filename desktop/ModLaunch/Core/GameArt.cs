using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Avalonia.Media.Imaging;

namespace ModLaunch.Core;

/// <summary>
/// 16.0: откуда брать картинки игр Steam, если встроенной нет. Раньше были только старые адреса CDN:
/// у игр, вышедших или обновивших картинки после 2024 года, Steam хранит их по адресам с хэшем, и старый
/// адрес вертикальной обложки отвечает 404 — вместо обложки в узкую рамку вставала широкая шапка и
/// обрезалась посередине (у названия «съезжала» половина). Теперь по очереди:
/// 1) кэш библиотеки самого Steam на этом ПК (appcache/librarycache — и старые имена «appid_вид.jpg»,
///    и папки по играм с 2024 года), без сети;
/// 2) старые адреса CDN;
/// 3) настоящие адреса из магазина Steam (IStoreBrowseService/GetItems, до 40 игр одним запросом),
///    запоминаются в cache/steam-art.json.
/// Если нужной формы нет ни там, ни там — вернётся другая картинка игры, а Ui.GameImage сложит из неё постер.
/// </summary>
public static class GameArt
{
    static string FileOf(Images.Art art) => art switch
    {
        Images.Art.Cover => "library_600x900.jpg", Images.Art.Hero => "library_hero.jpg", Images.Art.Logo => "logo.png", _ => "header.jpg",
    };

    /// <summary>Имена в кэше Steam: сначала крупнее (2x), потом обычные.</summary>
    static string[] LocalNames(Images.Art art) => art switch
    {
        Images.Art.Cover => ["library_600x900_2x.jpg", "library_600x900.jpg", "library_capsule.jpg"],
        Images.Art.Hero => ["library_hero_2x.jpg", "library_hero.jpg"],
        Images.Art.Logo => ["logo_2x.png", "logo.png"],
        _ => ["header_2x.jpg", "header.jpg", "library_header.jpg"],
    };

    // ---------------------------------------------------------------- кэш Steam на компьютере

    static List<string>? _caches;

    /// <summary>Папки appcache/librarycache всех найденных Steam (ищутся один раз за запуск).</summary>
    static List<string> Caches()
    {
        if (_caches is not null) return _caches;
        var list = new List<string>();
        try
        {
            foreach (var root in Games.Locator.SteamRoots())
            {
                var dir = Path.Combine(root, "appcache", "librarycache");
                if (Directory.Exists(dir)) list.Add(dir);
            }
        }
        catch { }
        return _caches = list;
    }

    /// <summary>Только для самотеста: свой кэш вместо найденного.</summary>
    internal static void UseCaches(List<string>? dirs) { _caches = dirs; Found.Clear(); }

    static readonly ConcurrentDictionary<string, string?> Found = new();

    /// <summary>Файл картинки из кэша Steam: «librarycache/730_header.jpg» или «librarycache/730/…/header.jpg».</summary>
    public static string? LocalFile(int appId, Images.Art art)
    {
        if (appId <= 0) return null;
        return Found.GetOrAdd($"{appId}:{art}", _ =>
        {
            foreach (var cache in Caches())
            {
                foreach (var name in LocalNames(art))
                {
                    var flat = Path.Combine(cache, $"{appId}_{name}");
                    if (File.Exists(flat)) return flat;
                }
                var folder = Path.Combine(cache, appId.ToString());
                if (!Directory.Exists(folder)) continue;
                foreach (var name in LocalNames(art))
                {
                    var direct = Path.Combine(folder, name);
                    if (File.Exists(direct)) return direct;
                    try
                    {
                        var nested = Directory.EnumerateFiles(folder, name, new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true }).FirstOrDefault();
                        if (nested is not null) return nested;
                    }
                    catch { }
                }
            }
            return null;
        });
    }

    static Bitmap? Decode(string file, int width)
    {
        try { using var s = File.OpenRead(file); return Bitmap.DecodeToWidth(s, width); }
        catch { return null; }
    }

    // ---------------------------------------------------------------- адреса магазина Steam

    static readonly JsonFile Known = new(Path.Combine(Paths.CacheDir, "steam-art.json"), () => new JsonObject());
    static readonly ConcurrentDictionary<int, TaskCompletionSource<JsonObject?>> Waiting = new();
    static readonly object Gate = new();
    static bool _flushing;

    /// <summary>Сведения о картинках игры из магазина: из файла или одним общим запросом на несколько игр.</summary>
    static Task<JsonObject?> Assets(int appId)
    {
        lock (Gate)
        {
            if (Known.Data[appId.ToString()] is JsonObject hit && DateTime.TryParse(hit.Str("at"), out var at) && DateTime.UtcNow - at < TimeSpan.FromDays(hit.Str("format") is null ? 3 : 30))
                return Task.FromResult<JsonObject?>(hit.Str("format") is null ? null : hit);
            var tcs = Waiting.GetOrAdd(appId, _ => new TaskCompletionSource<JsonObject?>(TaskCreationOptions.RunContinuationsAsynchronously));
            if (!_flushing)
            {
                _flushing = true;
                _ = Task.Run(Flush);
            }
            return tcs.Task;
        }
    }

    /// <summary>Подождать 150 мс, собрать все запрошенные игры и спросить магазин пачками по 40.</summary>
    static async Task Flush()
    {
        await Task.Delay(150);
        while (true)
        {
            List<int> ids;
            lock (Gate)
            {
                ids = Waiting.Keys.Take(40).ToList();
                if (ids.Count == 0) { _flushing = false; return; }
            }
            Dictionary<int, JsonObject> got = [];
            var ok = false;
            try { got = await Ask(ids); ok = true; }
            catch { }
            lock (Gate)
            {
                foreach (var id in ids)
                {
                    got.TryGetValue(id, out var a);
                    // Сеть не ответила — не запоминаем «нет», спросим в следующий раз.
                    if (ok) Known.Data[id.ToString()] = a?.DeepClone() ?? new JsonObject { ["at"] = DateTime.UtcNow.ToString("o") };
                    if (Waiting.TryRemove(id, out var tcs)) tcs.TrySetResult(a);
                }
                if (ok) Known.Save();
            }
        }
    }

    static async Task<Dictionary<int, JsonObject>> Ask(List<int> ids)
    {
        var input = new JsonObject
        {
            ["ids"] = new JsonArray(ids.Select(id => (JsonNode)new JsonObject { ["appid"] = id }).ToArray()),
            ["context"] = new JsonObject { ["language"] = "english", ["country_code"] = "US", ["steam_realm"] = 1 },
            ["data_request"] = new JsonObject { ["include_assets"] = true },
        };
        var url = "https://api.steampowered.com/IStoreBrowseService/GetItems/v1/?input_json=" + Uri.EscapeDataString(input.ToJsonString());
        var answer = await Http.GetJson(url, timeoutSec: 15);
        return Parse(answer);
    }

    /// <summary>Ответ GetItems → appid → {format, cover, header, hero}.</summary>
    internal static Dictionary<int, JsonObject> Parse(JsonNode? answer)
    {
        var map = new Dictionary<int, JsonObject>();
        foreach (var item in answer?["response"]?["store_items"] as JsonArray ?? [])
        {
            if (item is not JsonObject o || o["assets"] is not JsonObject a) continue;
            var id = (int)(o.Long("appid") is > 0 and var x ? x : o.Long("id"));
            if (id <= 0 || a.Str("asset_url_format") is not { Length: > 0 } format) continue;
            map[id] = new JsonObject
            {
                ["format"] = format,
                ["cover"] = a.Str("library_capsule_2x") ?? a.Str("library_capsule"),
                ["header"] = a.Str("header"),
                ["hero"] = a.Str("library_hero_2x") ?? a.Str("library_hero"),
                ["at"] = DateTime.UtcNow.ToString("o"),
            };
        }
        return map;
    }

    /// <summary>Адрес картинки по сведениям магазина: «steam/apps/730/хэш/${FILENAME}?t=…» + имя файла.</summary>
    internal static string? UrlOf(JsonObject assets, Images.Art art)
    {
        var file = assets.Str(art switch { Images.Art.Cover => "cover", Images.Art.Hero => "hero", Images.Art.Header => "header", _ => "-" });
        if (file is null || assets.Str("format") is not { } format) return null;
        return "https://shared.akamai.steamstatic.com/store_item_assets/" + format.Replace("${FILENAME}", file);
    }

    // ---------------------------------------------------------------- всё вместе

    /// <summary>Картинка Steam нужного вида: кэш Steam на ПК → старые адреса CDN → адреса из магазина. null — такой нет.</summary>
    public static async Task<Bitmap?> Steam(int appId, Images.Art art, int width)
    {
        if (appId <= 0) return null;
        if (LocalFile(appId, art) is { } file && await Task.Run(() => Decode(file, width)) is { } local) return local;
        foreach (var url in Images.SteamUrls(appId, art))
            if (await Images.FromUrl(url, width) is { } bmp) return bmp;
        if (art == Images.Art.Logo || Program.Demo) return null;
        if (await Assets(appId) is { } assets && UrlOf(assets, art) is { } real) return await Images.FromUrl(real, width);
        return null;
    }

    /// <summary>Номер игры Steam в адресе картинки («…/apps/730/header.jpg») и её вид; не Steam — (0, Header).</summary>
    public static (int AppId, Images.Art Art) FromUrl(string? url)
    {
        if (url is null) return (0, Images.Art.Header);
        var m = System.Text.RegularExpressions.Regex.Match(url, @"/apps/(\d+)/(?:[0-9a-f]{20,}/)?(library_600x900|library_hero|header|logo)");
        if (!m.Success || !int.TryParse(m.Groups[1].Value, out var id)) return (0, Images.Art.Header);
        return (id, m.Groups[2].Value switch { "library_600x900" => Images.Art.Cover, "library_hero" => Images.Art.Hero, "logo" => Images.Art.Logo, _ => Images.Art.Header });
    }

    /// <summary>Подходит ли картинка под рамку: обложка — вертикальная, шапка и фон — широкие.</summary>
    public static bool Fits(Bitmap bmp, Images.Art art)
    {
        var (w, h) = (bmp.PixelSize.Width, bmp.PixelSize.Height);
        if (w <= 0 || h <= 0) return true;
        return art switch
        {
            Images.Art.Cover => h >= w * 1.15,
            Images.Art.Header or Images.Art.Hero => w >= h * 1.25,
            _ => true,
        };
    }

    [SelfTest]
    static string SteamArtSources()
    {
        // Адрес из магазина: формат с хэшем и имя файла.
        var answer = JsonNode.Parse("""
            {"response":{"store_items":[{"item_type":0,"id":3527290,"appid":3527290,"assets":{"asset_url_format":"steam/apps/3527290/0a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9b/${FILENAME}?t=1750000000",
            "header":"header.jpg","library_capsule":"library_600x900.jpg","library_capsule_2x":"library_600x900_2x.jpg","library_hero":"library_hero.jpg"}},{"id":1,"assets":{}}]}}
            """);
        var map = Parse(answer);
        if (map.Count != 1 || !map.ContainsKey(3527290)) throw new Exception("parse: " + string.Join(",", map.Keys));
        var cover = UrlOf(map[3527290], Images.Art.Cover);
        if (cover != "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/3527290/0a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9b/library_600x900_2x.jpg?t=1750000000")
            throw new Exception("cover url: " + cover);
        if (UrlOf(map[3527290], Images.Art.Logo) is not null) throw new Exception("logo is not in the store answer");
        if (FromUrl(Games.GameDef.SteamArt(730)) != (730, Images.Art.Header) || FromUrl(Games.SteamLibrary.Cover(570)) != (570, Images.Art.Cover)) throw new Exception("url → app");
        if (FromUrl("https://example.com/a.jpg").AppId != 0) throw new Exception("not steam");

        // Кэш Steam: старое плоское имя и папка игры с подпапкой-хэшем.
        var dir = Path.Combine(Path.GetTempPath(), "modlaunch-art-" + Environment.ProcessId);
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "620", "ab12"));
            File.WriteAllText(Path.Combine(dir, "400_library_600x900.jpg"), "x");
            File.WriteAllText(Path.Combine(dir, "620", "ab12", "library_600x900.jpg"), "x");
            File.WriteAllText(Path.Combine(dir, "620", "header.jpg"), "x");
            UseCaches([dir]);
            if (LocalFile(400, Images.Art.Cover) != Path.Combine(dir, "400_library_600x900.jpg")) throw new Exception("flat cache");
            if (LocalFile(620, Images.Art.Cover) != Path.Combine(dir, "620", "ab12", "library_600x900.jpg")) throw new Exception("nested cache");
            if (LocalFile(620, Images.Art.Header) != Path.Combine(dir, "620", "header.jpg")) throw new Exception("folder cache");
            if (LocalFile(620, Images.Art.Hero) is not null || LocalFile(999, Images.Art.Cover) is not null) throw new Exception("missing art found");
        }
        finally
        {
            UseCaches(null);
            try { Directory.Delete(dir, true); } catch { }
        }
        return "store asset urls with hash, url → app id, Steam library cache (flat and per-app folders)";
    }
}
