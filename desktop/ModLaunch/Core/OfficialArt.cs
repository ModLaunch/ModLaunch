using System.Text.Json.Nodes;

namespace ModLaunch.Core;

/// <summary>
/// 9.4: официальные картинки игр не из Steam — из каталога Microsoft Store (тот же источник, что у
/// страницы игры в Store и в приложении Xbox). У Minecraft это постер (обложка 2:3) и широкий арт
/// без надписей (фон страницы). Картинки качаются в кэш раз в две недели; пока их нет или нет
/// сети — показываются встроенные.
/// </summary>
public static class OfficialArt
{
    /// <summary>Игра → товар в Microsoft Store («Minecraft: Java &amp; Bedrock Edition for PC»).</summary>
    static readonly Dictionary<string, string> Products = new() { ["minecraft"] = "9NXP44L49SHJ" };

    static string Dir => Path.Combine(Paths.CacheDir, "official");
    static string FileFor(string key, Images.Art art) => Path.Combine(Dir, $"{key}-{art switch { Images.Art.Cover => "cover", _ => "hero" }}.img");

    /// <summary>Включено ли (Настройки → Внешний вид): по умолчанию — да.</summary>
    public static bool Enabled => Settings.Data.Bool("officialArt", true);

    /// <summary>Готовая официальная картинка на диске (или null — тогда встроенная).</summary>
    public static string? File(Games.GameDef def, Images.Art art)
    {
        if (!Enabled || Program.Screenshot || def.ArtKey is not { } key || !Products.ContainsKey(def.Id)) return null;
        if (art == Images.Art.Logo) return null;
        var file = FileFor(key, art);
        return System.IO.File.Exists(file) && new FileInfo(file).Length > 1024 ? file : null;
    }

    static bool _started;

    /// <summary>При запуске, в фоне: обновить картинки, если их нет или им больше двух недель.</summary>
    public static void Refresh(Action? changed = null)
    {
        if (_started || Program.Screenshot || Program.Demo || !Enabled) return;
        _started = true;
        _ = Task.Run(async () =>
        {
            var got = false;
            foreach (var (gameId, product) in Products)
            {
                var def = Games.GameCatalog.Builtin.FirstOrDefault(d => d.Id == gameId);
                if (def?.ArtKey is not { } key) continue;
                var cover = FileFor(key, Images.Art.Cover);
                if (System.IO.File.Exists(cover) && DateTime.UtcNow - System.IO.File.GetLastWriteTimeUtc(cover) < TimeSpan.FromDays(14)) continue;
                try { got |= await Fetch(key, product); } catch { }
            }
            if (got) changed?.Invoke();
        });
    }

    /// <summary>Достать из каталога Store ссылки на постер и широкий арт и скачать их.</summary>
    static async Task<bool> Fetch(string key, string product)
    {
        var json = await Http.GetJson($"https://displaycatalog.mp.microsoft.com/v7.0/products?bigIds={product}&market=US&languages=en-US", timeoutSec: 20);
        var images = json?["Products"]?[0]?["LocalizedProperties"]?[0]?["Images"] as JsonArray;
        if (images is null) return false;
        var (poster, hero) = Pick(images);
        var any = false;
        Directory.CreateDirectory(Dir);
        foreach (var (url, art) in new[] { (poster, Images.Art.Cover), (hero, Images.Art.Hero) })
        {
            if (url is null) continue;
            var bytes = await Http.Client.GetByteArrayAsync(url);
            if (bytes.Length < 1024) continue;
            var target = FileFor(key, art);
            await System.IO.File.WriteAllBytesAsync(target + ".tmp", bytes);
            System.IO.File.Move(target + ".tmp", target, true);
            any = true;
        }
        return any;
    }

    /// <summary>Постер — вертикальный «Poster» (иначе BoxArt), фон — «SuperHeroArt» (иначе широкий «TitledHeroArt»).</summary>
    public static (string? Poster, string? Hero) Pick(JsonArray images)
    {
        string? Url(string purpose) => images.OfType<JsonNode>()
            .Where(i => string.Equals(i.Str("ImagePurpose"), purpose, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(i => i.Long("Width"))
            .Select(i => i.Str("Uri")).FirstOrDefault(u => !string.IsNullOrEmpty(u)) is { } u ? (u.StartsWith("//") ? "https:" + u : u) : null;
        return (Url("Poster") ?? Url("BoxArt"), Url("SuperHeroArt") ?? Url("TitledHeroArt"));
    }

    [SelfTest]
    static string PicksPosterAndHero()
    {
        var images = JsonNode.Parse("""
            [{"ImagePurpose":"Logo","Width":300,"Height":300,"Uri":"//store-images.s-microsoft.com/image/logo"},
             {"ImagePurpose":"Poster","Width":720,"Height":1080,"Uri":"//store-images.s-microsoft.com/image/poster"},
             {"ImagePurpose":"SuperHeroArt","Width":1920,"Height":1080,"Uri":"//store-images.s-microsoft.com/image/hero"},
             {"ImagePurpose":"Screenshot","Width":3840,"Height":2160,"Uri":"//store-images.s-microsoft.com/image/shot"}]
            """) as JsonArray;
        var (poster, hero) = Pick(images!);
        if (poster != "https://store-images.s-microsoft.com/image/poster" || hero != "https://store-images.s-microsoft.com/image/hero")
            throw new Exception($"{poster} / {hero}");
        return "poster + super hero art picked, https added";
    }
}
