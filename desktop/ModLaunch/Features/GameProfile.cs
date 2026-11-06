using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using SkiaSharp;

namespace ModLaunch.Features;

/// <summary>
/// 23.7: у каждой игры — своё меню конструктора. По меткам Steam (Horror, Racing, Survival…) выбирается набор:
/// какие разделы деталей показать первыми и какие быстрые действия дать сразу (в хорроре — полночь, туман, мигающий
/// свет; в гонках — дороги и перекрёсток; в выживании — костёр, снег, смена дня и ночи). Цвет меню берётся с обложки игры.
/// Профиль пишется в profile.txt рядом с ModScript Studio (и в плашки других движков); без сети — набор «конструктор».
/// </summary>
public static partial class GameProfile
{
    public sealed record Profile(string Name, string Preset, string Accent, string[] Tags, string[] Cats, string[] Quick);

    /// <summary>Набор: метки Steam, цвет по умолчанию, разделы библиотеки (по порядку) и быстрые действия (команды и детали kit:).</summary>
    public sealed record Preset(string Id, string[] Tags, string Accent, string[] Cats, string[] Quick);

    public static readonly Preset[] Presets =
    [
        new("horror", ["Horror", "Psychological Horror", "Survival Horror", "Lovecraftian", "Gore", "Zombies", "Dark", "Thriller", "Supernatural"], "#E5484D",
            ["lights", "effects", "build", "furniture", "fromGame", "terrain"], ["time.0", "fog.d0.06", "weather.storm", "kit:fx:mist", "beh.flicker"]),
        new("racing", ["Racing", "Driving", "Automobile Sim", "Offroad", "Arcade", "Combat Racing", "Motocross"], "#3DD9C5",
            ["terrain", "vehicles", "build", "shapes", "lights", "effects"], ["kit:road:16", "kit:crossroad", "kit:hill:20", "time.18", "weather.rain"]),
        new("platformer", ["Platformer", "2D Platformer", "3D Platformer", "Precision Platformer", "Metroidvania", "Side Scroller", "Parkour", "Puzzle Platformer"], "#7C6CF2",
            ["shapes", "build", "game", "nature", "effects", "lights"], ["beh.bob1", "beh.swing45", "beh.orbit3", "beh.blink1", "kit:fx:sparks"]),
        new("survival", ["Survival", "Open World Survival Craft", "Crafting", "Base Building", "Sandbox", "Open World", "Exploration", "Hunting", "Climbing", "Nature"], "#5BD68F",
            ["nature", "terrain", "build", "furniture", "lights", "effects"], ["kit:fx:fire", "weather.snow", "cycle.10", "kit:hill:20", "time.6"]),
        new("shooter", ["FPS", "Shooter", "Tactical", "Military", "Looter Shooter", "Third-Person Shooter", "Hero Shooter", "Action"], "#F2994A",
            ["build", "game", "shapes", "vehicles", "lights", "effects"], ["kit:fx:smoke", "kit:fx:sparks", "weather.dust", "time.18", "fog.d0.02"]),
        new("city", ["City Builder", "Building", "Management", "Colony Sim", "Automation", "Strategy", "Simulation", "Economy", "Trains", "Transportation"], "#5BB8F5",
            ["build", "furniture", "vehicles", "terrain", "lights", "nature"], ["kit:road:16", "kit:crossroad", "kit:water:30", "cycle.10", "time.20"]),
        new("fantasy", ["RPG", "Fantasy", "Action RPG", "JRPG", "Souls-like", "Magic", "Dungeon Crawler", "Medieval", "Mythology", "Dragons"], "#B57BFF",
            ["nature", "build", "effects", "lights", "terrain", "furniture"], ["kit:fx:magic", "kit:fx:fireflies", "weather.petals", "cycle.20", "kit:mountain"]),
        new("space", ["Space", "Sci-fi", "Space Sim", "Cyberpunk", "Futuristic", "Robots", "Aliens", "Mechs"], "#4CC3FF",
            ["shapes", "lights", "effects", "build", "vehicles", "text"], ["time.0", "kit:fx:magic", "beh.orbit3", "beh.rainbowSlow", "kit:textGlow"]),
        new("cozy", ["Cute", "Cozy", "Relaxing", "Casual", "Family Friendly", "Farming Sim", "Life Sim", "Wholesome", "Colorful", "Funny"], "#FF8FB1",
            ["furniture", "nature", "build", "lights", "effects", "text"], ["weather.petals", "kit:fx:bubbles", "kit:fx:fireflies", "time.17", "cycle.20"]),
        new("story", ["Visual Novel", "Story Rich", "Dating Sim", "Interactive Fiction", "Anime", "Choices Matter", "Point & Click", "Walking Simulator"], "#F0B429",
            ["build", "furniture", "lights", "text", "nature", "effects"], ["kit:textGlow", "time.18", "kit:fx:confetti", "weather.petals", "kit:fx:fireflies"]),
        new("builder", [], "#F0B429",
            ["build", "shapes", "nature", "furniture", "lights", "effects"], ["kit:fx:fire", "weather.snow", "cycle.5", "kit:text", "kit:water:10"]),
    ];

    static Preset Builder => Presets[^1];

    /// <summary>Набор по меткам: каждая метка весит тем больше, чем она выше в списке Steam (там они по числу голосов).</summary>
    public static Preset Pick(IReadOnlyList<string> tags)
    {
        Preset? best = null;
        var bestScore = 0;
        foreach (var p in Presets)
        {
            var score = 0;
            for (var i = 0; i < tags.Count; i++)
                if (p.Tags.Contains(tags[i], StringComparer.OrdinalIgnoreCase)) score += Math.Max(1, 20 - i);
            if (score > bestScore) { best = p; bestScore = score; }
        }
        return best ?? Builder;
    }

    // Кэш, а не данные игрока: в cache/ (не в profiles/ — там профили модов, они идут в бэкап).
    static string CacheFile(int appId) => Path.Combine(Paths.CacheDir, "game-menus", appId + ".json");

    static readonly ConcurrentDictionary<string, Profile> ByPath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Профиль, собранный для этой папки при последнем запуске (для плашек движков).</summary>
    public static Profile? Of(string gamePath) => ByPath.TryGetValue(Path.GetFullPath(gamePath).TrimEnd('\\', '/'), out var p) ? p : null;

    static readonly ConcurrentDictionary<int, Profile?> CacheMemo = new();

    /// <summary>Профиль из кэша (без сети) — для карточек; null — метки игры ещё не загружались.</summary>
    public static Profile? Cached(int appId)
    {
        if (appId <= 0) return null;
        if (CacheMemo.TryGetValue(appId, out var known) && known is not null) return known;
        try
        {
            var file = CacheFile(appId);
            if (!File.Exists(file) || JsonNode.Parse(File.ReadAllText(file)) is not JsonObject o) return null;
            var tags = (o["tags"] as JsonArray)?.Select(t => (string?)t).OfType<string>().ToArray() ?? [];
            var preset = Pick(tags);
            var p = new Profile("", preset.Id, ((string?)o["accent"] is { } a && HexRe().IsMatch(a) ? a : preset.Accent), tags, preset.Cats, preset.Quick);
            CacheMemo[appId] = p;
            return p;
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException) { return null; }
    }

    /// <summary>Метки игры в Steam (по голосам игроков) и цвет её обложки; кэш — на месяц. Без сети — что есть в кэше.</summary>
    public static async Task<Profile> For(string name, int appId, CancellationToken ct = default)
    {
        string[] tags = [];
        string? accent = null;
        if (appId > 0)
        {
            var file = CacheFile(appId);
            try
            {
                if (File.Exists(file) && JsonNode.Parse(File.ReadAllText(file)) is JsonObject o && File.GetLastWriteTimeUtc(file) > DateTime.UtcNow.AddDays(-30))
                {
                    tags = (o["tags"] as JsonArray)?.Select(t => (string?)t).OfType<string>().ToArray() ?? [];
                    accent = (string?)o["accent"] is { } a && HexRe().IsMatch(a) ? a : null;
                }
            }
            catch (Exception e) when (e is IOException or System.Text.Json.JsonException) { }
            if (tags.Length == 0 || accent is null)
            {
                try
                {
                    var html = await Http.GetString($"https://store.steampowered.com/apphover/{appId}?l=english", ct, 8);
                    tags = TagRe().Matches(html).Select(m => Clean(System.Net.WebUtility.HtmlDecode(m.Groups[1].Value))).Where(t => t != "" && t != "+").Distinct().Take(20).ToArray();
                }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException) { }
                try { accent ??= AccentOf(await Cover(appId, ct)); }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException) { }
                if (tags.Length > 0)
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                        File.WriteAllText(file, new JsonObject { ["tags"] = new JsonArray(tags.Select(t => (JsonNode)t).ToArray()), ["accent"] = accent }.ToJsonString());
                    }
                    catch (IOException) { }
            }
        }
        var preset = Pick(tags);
        return new Profile(name, preset.Id, accent ?? preset.Accent, tags, preset.Cats, preset.Quick);
    }

    /// <summary>
    /// Цвет меню по обложке: самый «весомый» насыщенный оттенок (12 корзин по кругу оттенков), приведённый к яркому
    /// и читаемому на тёмной панели. Серые и чёрные обложки — null (берётся цвет набора).
    /// </summary>
    public static string? AccentOf(byte[]? image)
    {
        if (image is null || image.Length == 0) return null;
        using var bmp = SKBitmap.Decode(image);
        if (bmp is null) return null;
        var weight = new double[12];
        var sum = new (double R, double G, double B)[12];
        for (var y = 0; y < 24; y++)
            for (var x = 0; x < 48; x++)
            {
                var c = bmp.GetPixel(x * bmp.Width / 48, y * bmp.Height / 24);
                c.ToHsv(out var h, out var s, out var v);
                var wgt = s / 100.0 * (v / 100.0) * (s > 35 && v > 30 ? 1 : 0.05);
                var bin = (int)(h / 30) % 12;
                weight[bin] += wgt;
                sum[bin].R += c.Red * wgt; sum[bin].G += c.Green * wgt; sum[bin].B += c.Blue * wgt;
            }
        var best = Array.IndexOf(weight, weight.Max());
        if (weight[best] < 8) return null;
        var avg = new SKColor((byte)(sum[best].R / weight[best]), (byte)(sum[best].G / weight[best]), (byte)(sum[best].B / weight[best]));
        avg.ToHsv(out var hh, out var ss, out _);
        var fixedColor = SKColor.FromHsv(hh, Math.Clamp(ss, 55, 80), 95);
        return $"#{fixedColor.Red:X2}{fixedColor.Green:X2}{fixedColor.Blue:X2}";
    }

    /// <summary>Обложка игры: старый адрес Steam, а у новых игр (у них он 404) — адрес из описания игры в магазине.</summary>
    static async Task<byte[]?> Cover(int appId, CancellationToken ct)
    {
        try { return await Http.GetBytes(Games.GameDef.SteamArt(appId), ct, 8); }
        catch (HttpRequestException) { }
        var info = await Http.GetJson($"https://store.steampowered.com/api/appdetails?appids={appId}&filters=basic", ct, 8);
        return (string?)info?[appId.ToString(CultureInfo.InvariantCulture)]?["data"]?["header_image"] is { } url ? await Http.GetBytes(url, ct, 8) : null;
    }

    /// <summary>Собрать профиль и положить его туда, где его прочтёт конструктор этой игры.</summary>
    public static async Task<Profile> Apply(string gamePath, string name, int appId, CancellationToken ct = default)
    {
        var p = await For(name, appId, ct);
        ByPath[Path.GetFullPath(gamePath).TrimEnd('\\', '/')] = p;
        if (DevMode.KindOf(gamePath) == "unity")
            try
            {
                var dir = Directory.CreateDirectory(DevMode.ConfigDir(gamePath)).FullName;
                File.WriteAllText(Path.Combine(dir, "profile.txt"), Text(p), new UTF8Encoding(false));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return p;
    }

    /// <summary>profile.txt: name, preset, accent, tags, cats, quick — по строке «ключ|значение».</summary>
    public static string Text(Profile p) => new StringBuilder()
        .Append("name|").Append(Clean(p.Name).Replace('|', '/')).Append('\n')
        .Append("preset|").Append(p.Preset).Append('\n')
        .Append("accent|").Append(p.Accent.TrimStart('#')).Append('\n')
        .Append("tags|").Append(string.Join(",", p.Tags.Select(t => Clean(t).Replace(',', ' ').Replace('|', ' ')))).Append('\n')
        .Append("cats|").Append(string.Join(",", p.Cats)).Append('\n')
        .Append("quick|").Append(string.Join(",", p.Quick)).Append('\n')
        .ToString();

    [GeneratedRegex("app_tag\">([^<]*)<")]
    private static partial Regex TagRe();

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexRe();

    /// <summary>Имя игры и метки идут в файлы, которые читает сама игра (profile.txt, сценарий Ren'Py): без управляющих символов и «\».</summary>
    public static string Clean(string s) => Regex.Replace(s, @"[\p{C}\\]", " ").Trim();

    [SelfTest]
    static string ProfilesPickByTags()
    {
        if (Pick(["Horror", "Online Co-Op", "Multiplayer", "Comedy"]).Id != "horror") throw new Exception("horror");
        if (Pick(["Racing", "Driving", "Simulation"]).Id != "racing") throw new Exception("racing");
        if (Pick(["Survival", "Open World Survival Craft", "Crafting", "Horror"]).Id != "survival") throw new Exception("survival first");
        // PEAK (метки Steam, 2026): «Adventure» слишком общая — горы и выживание важнее.
        if (Pick(["Multiplayer", "Online Co-Op", "Co-op", "Adventure", "Physics", "Exploration", "First-Person", "Comedy", "3D", "Procedural Generation", "Atmospheric", "Action Roguelike", "Character Customization", "Action", "Time Management", "Platformer", "Horror"]).Id != "survival") throw new Exception("PEAK");
        if (Pick(["Metroidvania", "Platformer", "2D"]).Id != "platformer") throw new Exception("platformer");
        if (Pick([]).Id != "builder" || Pick(["Indie", "Singleplayer"]).Id != "builder") throw new Exception("default");
        // Все наборы ссылаются только на существующие разделы и команды/детали конструктора.
        var source = DevMode.Source();
        string[] cats = ["build", "shapes", "nature", "terrain", "furniture", "vehicles", "lights", "effects", "text", "game", "fromGame", "models", "materials"];
        foreach (var p in Presets)
        {
            if (p.Cats.Length < 5 || p.Quick.Length < 4) throw new Exception(p.Id + ": too few");
            foreach (var c in p.Cats) if (!cats.Contains(c)) throw new Exception(p.Id + ": cat " + c);
            foreach (var q in p.Quick) if (!Known(q, source)) throw new Exception(p.Id + ": " + q);
            // Строки загружены (в --selfcheck без окна их нет) — у каждого набора есть подпись.
            if (I18n.T("v23.dev.route") != "v23.dev.route" && I18n.T("v23.gp." + p.Id) == "v23.gp." + p.Id) throw new Exception("no text v23.gp." + p.Id);
        }
        // Цвет с обложки: красная картинка → красный акцент, серая → нет.
        using (var red = new SKBitmap(64, 32))
        {
            red.Erase(new SKColor(200, 30, 40));
            using var data = SKImage.FromBitmap(red).Encode(SKEncodedImageFormat.Png, 90);
            var a = AccentOf(data.ToArray());
            if (a is null || !a.StartsWith("#F", StringComparison.Ordinal) || int.Parse(a[3..5], NumberStyles.HexNumber) > 0x60) throw new Exception("accent red: " + a);
        }
        using (var grey = new SKBitmap(64, 32))
        {
            grey.Erase(new SKColor(120, 120, 120));
            using var data = SKImage.FromBitmap(grey).Encode(SKEncodedImageFormat.Png, 90);
            if (AccentOf(data.ToArray()) is { } g) throw new Exception("grey gave " + g);
        }
        var text = Text(new Profile("A|B", "horror", "#E5484D", ["Horror", "Co,op"], ["lights"], ["time.0"]));
        // Имя и метки не могут добавить строку в profile.txt или выйти из строки сценария Ren'Py.
        if (Clean("Evil\nquick|x\\") != "Evil quick|x" || Clean("Name\u0007") != "Name") throw new Exception("clean");
        if (Text(new Profile("A\nquick|kit:x", "builder", "#F0B429", ["T\nag"], [], [])).Split('\n').Count(l => l.StartsWith("quick|", StringComparison.Ordinal)) != 1) throw new Exception("line injection");
        if (!text.Contains("name|A/B\n") || !text.Contains("accent|E5484D\n") || !text.Contains("tags|Horror,Co op\n")) throw new Exception("profile text");
        return Presets.Length + " presets, every quick action exists in the studio";
    }

    /// <summary>Есть ли такая команда или деталь в исходнике конструктора (с учётом команд, собранных в циклах).</summary>
    static bool Known(string q, string source)
    {
        if (q.StartsWith("kit:", StringComparison.Ordinal))
        {
            var id = q[4..];
            var colon = id.IndexOf(':');
            return source.Contains($"add(\"{id}\"") || colon > 0 && source.Contains($"add(\"{id[..(colon + 1)]}\" +");
        }
        var m = Regex.Match(q, @"^([a-z]+\.[a-zA-Z]*?)(\d[\d.]*)?$");
        if (source.Contains($"A(\"{q}\"")) return true;
        if (q.StartsWith("weather.", StringComparison.Ordinal)) return source.Contains($"\"{q[8..]}\"") && source.Contains("A(\"weather.\" + kind");
        return m.Success && m.Groups[2].Success && source.Contains($"A(\"{m.Groups[1].Value}\" +");
    }
}
