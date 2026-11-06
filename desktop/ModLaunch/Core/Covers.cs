using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using ModLaunch.Games;
using ModLaunch.Sources;
using SkiaSharp;

namespace ModLaunch.Core;

/// <summary>
/// 23.0: обложка мода для каталога — лучшая картинка со страницы мода, а не первая попавшаяся и не значок.
/// Кандидаты — картинки описания (README, галерея); значки-«шилды», кнопки доната, svg и сам значок мода
/// отбрасываются сразу. Остальные (до шести) скачиваются и оцениваются: широкая (около 16:9), крупная,
/// с деталями и цветом — хорошо; квадрат или портрет (логотип), полоса-баннер, мелочь, плоская заливка
/// и скриншот текста на белом — плохо. Ниже порога — обложки нет, остаётся значок. Выбор помнится на диске
/// две недели, так что каждый мод проверяется один раз.
/// </summary>
public static class Covers
{
    sealed record Entry(string? Url, DateTime At);

    static readonly ConcurrentDictionary<string, Entry> Known = new();
    static readonly ConcurrentDictionary<string, Task<string?>> Pending = new();
    static readonly SemaphoreSlim Gate = new(3);
    static bool _loaded;
    static readonly object Sync = new();
    static readonly TimeSpan Keep = TimeSpan.FromDays(14);

    static string FilePath => Path.Combine(Paths.CacheDir, "covers.json");

    // Номер правил в ключе: поменялись правила отбора — старые ответы «нет обложки» не мешают.
    static string Key(GameDef game, ModInfo mod) => $"2|{game.Id}|{mod.Source}|{mod.Id}";

    /// <summary>Уже известная обложка (или null — нет или ещё не проверяли). known — проверяли ли.</summary>
    public static string? Cached(GameDef game, ModInfo mod, out bool known)
    {
        LoadOnce();
        if (Known.TryGetValue(Key(game, mod), out var e) && DateTime.UtcNow - e.At < Keep) { known = true; return e.Url; }
        known = false;
        return null;
    }

    /// <summary>Подобрать обложку (один раз на мод; параллельно — не больше трёх модов).</summary>
    public static Task<string?> Pick(GameDef game, ModInfo mod)
    {
        var cached = Cached(game, mod, out var known);
        if (known) return Task.FromResult(cached);
        var key = Key(game, mod);
        return Pending.GetOrAdd(key, _ => Task.Run(async () =>
        {
            try
            {
                await Gate.WaitAsync();
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));
                    // Кандидаты: картинки страницы мода и его главная картинка (у Nexus это настоящий скриншот,
                    // у Thunderstore — квадратный значок 256², его отсеет оценка по форме и размеру).
                    var images = new List<string>();
                    var pageOk = true;
                    try { images.AddRange((await Details.Load(game, mod.Id, cts.Token, mod.Source)).Images); }
                    catch (Exception e) when (e is not OperationCanceledException || !cts.IsCancellationRequested) { pageOk = false; }
                    if (mod.Icon is { Length: > 0 } icon) images.Add(icon);
                    var url = await Best(images, cts.Token);
                    // Страница не открылась и ничего не нашли — не запоминаем «нет», попробуем в другой раз.
                    if (url is not null || pageOk)
                    {
                        Known[key] = new Entry(url, DateTime.UtcNow);
                        Save();
                    }
                    return url;
                }
                finally { Gate.Release(); }
            }
            // Нет сети или страница не открылась — «нет обложки» не запоминаем, попробуем в другой раз.
            catch (Exception) { return null; }
            finally { Pending.TryRemove(key, out Task<string?>? _); }
        }));
    }

    /// <summary>Ссылки, которые точно не обложки: значки сборки, кнопки, счётчики, svg.</summary>
    internal static bool Junk(string url)
    {
        var u = url.ToLowerInvariant();
        if (!u.StartsWith("https://") && !u.StartsWith("http://")) return true;
        if (u.EndsWith(".svg") || u.Contains(".svg?")) return true;
        string[] hosts = ["shields.io", "badgen.net", "badge.fury.io", "travis-ci", "github.com/sponsors", "ko-fi.com", "buymeacoffee", "patreon.com", "paypal", "discord.com/api/guilds", "discordapp.com/api/guilds", "img.youtube.com/vi", "visitor-badge", "hits.seeyoufarm", "gstatic.com/images/badges", "forthebadge", "liberapay", "codecov", "/actions/workflows/", "/workflows/", "/repository/icons/"];
        if (hosts.Any(h => u.Contains(h))) return true;
        string[] words = ["badge", "button", "donate", "logo", "icon", "banner", "divider", "separator", "spacer", "emoji", "avatar"];
        var name = u.Split('?')[0];
        name = name[(name.LastIndexOf('/') + 1)..];
        return words.Any(w => name.Contains(w));
    }

    static async Task<string?> Best(IEnumerable<string> images, CancellationToken ct)
    {
        var candidates = images.Where(i => !Junk(i)).Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToList();
        if (candidates.Count == 0) return null;
        var scored = await Task.WhenAll(candidates.Select(async (url, i) =>
        {
            try
            {
                var bytes = await Download(url, ct);
                return (Url: url, Score: bytes is null ? -1 : Score(bytes, i));
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException) { return (Url: url, Score: -1.0); }
        }));
        var best = scored.OrderByDescending(s => s.Score).First();
        return best.Score >= Threshold ? best.Url : null;
    }

    internal const double Threshold = 0.45;

    static readonly HttpClient Client = new(new Offline.Gate(new SocketsHttpHandler { AutomaticDecompression = System.Net.DecompressionMethods.All, ConnectTimeout = TimeSpan.FromSeconds(8) })) { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>Скачать картинку для оценки (не больше 6 МБ — больше обложке не нужно).</summary>
    static async Task<byte[]?> Download(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.ParseAdd($"Mozilla/5.0 (Windows NT 10.0; Win64; x64) ModLaunch/{Http.Version}");
        using var res = await Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!res.IsSuccessStatusCode) return null;
        if (res.Content.Headers.ContentLength is > 6_000_000) return null;
        await using var s = await res.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        int n;
        while ((n = await s.ReadAsync(buf, ct)) > 0)
        {
            ms.Write(buf, 0, n);
            if (ms.Length > 6_000_000) return null;
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Оценка картинки 0…1 (−1 — не картинка). Форма (около 16:9), размер, детали и цвет, без засветки текстом;
    /// первым картинкам описания — небольшая прибавка (авторы обычно ставят главный скриншот вверх).
    /// </summary>
    internal static double Score(byte[] bytes, int position)
    {
        using var codec = SKCodec.Create(new MemoryStream(bytes));
        if (codec is null) return -1;
        var (w, h) = (codec.Info.Width, codec.Info.Height);
        if (w < 360 || h < 180) return 0;
        var aspect = (double)w / h;
        if (aspect < 1.15 || aspect > 3.2) return 0.1;
        var shape = 1 - Math.Min(1, Math.Abs(Math.Log(aspect / (16.0 / 9))) / 0.55);
        var size = Math.Min(1, w / 1280.0);

        // Маленькая копия: насколько картинка «живая» (разброс яркости, цвет) и не белый ли это лист с текстом.
        var scale = 64.0 / w;
        using var full = SKBitmap.Decode(bytes);
        if (full is null) return -1;
        using var small = full.Resize(new SKImageInfo(64, Math.Max(8, (int)(h * scale))), SKFilterQuality.Low);
        if (small is null) return -1;
        double sum = 0, sum2 = 0, chroma = 0, white = 0, n = 0;
        for (var y = 0; y < small.Height; y++)
            for (var x = 0; x < small.Width; x++)
            {
                var c = small.GetPixel(x, y);
                var l = 0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue;
                sum += l; sum2 += l * l; n++;
                chroma += Math.Max(c.Red, Math.Max(c.Green, c.Blue)) - Math.Min(c.Red, Math.Min(c.Green, c.Blue));
                if (l > 232 && Math.Max(c.Red, Math.Max(c.Green, c.Blue)) - Math.Min(c.Red, Math.Min(c.Green, c.Blue)) < 14) white++;
            }
        var mean = sum / n;
        var spread = Math.Sqrt(Math.Max(0, sum2 / n - mean * mean));
        // Сплошная заливка или почти без деталей (заглушка, фон) — не обложка.
        if (spread < 12) return 0.15;
        var detail = Math.Min(1, spread / 60);
        var colour = Math.Min(1, chroma / n / 70);
        var paper = white / n;
        var dark = mean < 18 ? 0.3 : 0;
        var score = 0.35 * shape + 0.2 * size + 0.25 * detail + 0.2 * colour - (paper > 0.45 ? 0.35 : 0) - dark;
        return score + Math.Max(0, 0.05 - position * 0.01);
    }

    static void LoadOnce()
    {
        if (_loaded) return;
        lock (Sync)
        {
            if (_loaded) return;
            try
            {
                if (File.Exists(FilePath) && JsonNode.Parse(File.ReadAllText(FilePath)) is JsonObject root)
                    foreach (var (k, v) in root)
                        if (v is JsonObject o && o["at"]?.GetValue<string>() is { } at && DateTime.TryParse(at, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t))
                            Known[k] = new Entry(o["url"]?.GetValue<string>(), t.ToUniversalTime());
            }
            catch (Exception e) when (e is IOException or System.Text.Json.JsonException or InvalidOperationException) { }
            _loaded = true;
        }
    }

    static void Save()
    {
        lock (Sync)
        {
            try
            {
                var root = new JsonObject();
                foreach (var (k, e) in Known.Where(kv => DateTime.UtcNow - kv.Value.At < Keep).Take(5000))
                    root[k] = new JsonObject { ["url"] = e.Url, ["at"] = e.At.ToString("o") };
                var tmp = FilePath + ".part";
                File.WriteAllText(tmp, root.ToJsonString());
                File.Move(tmp, FilePath, true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Для снимков: обложка известна заранее.</summary>
    internal static void Demo(GameDef game, ModInfo mod, string? url) => Known[Key(game, mod)] = new Entry(url, DateTime.UtcNow);

    [SelfTest]
    static string CoversPickPicturesNotIcons()
    {
        foreach (var junk in new[] { "https://img.shields.io/badge/x-y-green.svg", "https://ko-fi.com/img/githubbutton_sm.png", "https://x.org/logo.png", "https://x.org/a.svg", "ftp://x/a.png", "https://cdn.x/icon.png", "https://gcdn.thunderstore.io/live/repository/icons/A-B-1.0.0.png" })
            if (!Junk(junk)) throw new Exception("not junk: " + junk);
        foreach (var ok in new[] { "https://i.imgur.com/AbC123.png", "https://staticdelivery.nexusmods.com/mods/1/images/2/2-1.jpg", "https://raw.githubusercontent.com/a/b/main/screenshots/1.jpg" })
            if (Junk(ok)) throw new Exception("junk: " + ok);

        static byte[] Png(int w, int h, Func<int, int, SKColor> paint)
        {
            using var bmp = new SKBitmap(w, h);
            for (var y = 0; y < h; y++) for (var x = 0; x < w; x++) bmp.SetPixel(x, y, paint(x, y));
            using var img = SKImage.FromBitmap(bmp);
            return img.Encode(SKEncodedImageFormat.Png, 90).ToArray();
        }
        var rnd = new Random(7);
        var scene = Png(1280, 720, (x, y) => new SKColor((byte)(x * 255 / 1280), (byte)(y * 255 / 720), (byte)rnd.Next(60, 200)));
        var square = Png(512, 512, (x, y) => new SKColor((byte)(x / 2), (byte)(y / 2), 120));
        var paper = Png(1280, 720, (x, y) => y % 40 < 3 && x % 9 < 6 ? SKColors.Black : SKColors.White);
        var tiny = Png(200, 110, (x, y) => new SKColor((byte)x, (byte)y, 90));
        var flat = Png(1280, 720, (_, _) => new SKColor(30, 30, 30));
        var (s, q, p, t, f) = (Score(scene, 2), Score(square, 0), Score(paper, 0), Score(tiny, 0), Score(flat, 0));
        if (s < Threshold) throw new Exception($"good screenshot scored {s:0.00}");
        if (q >= Threshold || p >= Threshold || t >= Threshold || f >= Threshold) throw new Exception($"bad pictures pass: square {q:0.00}, text {p:0.00}, tiny {t:0.00}, flat {f:0.00}");
        return $"badges/logos skipped; screenshot {s:0.00} beats logo {q:0.00}, text page {p:0.00}, tiny {t:0.00}, flat {f:0.00}";
    }
}
