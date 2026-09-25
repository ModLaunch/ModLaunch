using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ModLaunch.Core;

/// <summary>Картинки: из ресурсов программы и из сети (с кэшем на диске и в памяти).</summary>
public static class Images
{
    static readonly ConcurrentDictionary<string, Bitmap?> Memory = new();
    static readonly ConcurrentDictionary<string, Task<Bitmap?>> Pending = new();
    static readonly SemaphoreSlim Parallel = new(6);

    public static Bitmap? Asset(string name, int decodeWidth = 0)
    {
        var key = $"asset:{name}:{decodeWidth}";
        return Memory.GetOrAdd(key, _ =>
        {
            try
            {
                using var stream = AssetLoader.Open(new Uri($"avares://ModLaunch/Assets/{name}"));
                return decodeWidth > 0 ? Bitmap.DecodeToWidth(stream, decodeWidth) : new Bitmap(stream);
            }
            catch { return null; }
        });
    }

    /// <summary>Вид обложки: вертикальная (как в библиотеке Steam), широкая шапка, фон или логотип.</summary>
    public enum Art { Cover, Header, Hero, Logo }

    static string Kind(Art art) => art switch { Art.Cover => "cover", Art.Header => "header", Art.Hero => "hero", _ => "logo" };

    /// <summary>Встроенная картинка игры (Assets/art/&lt;appid&gt;-&lt;вид&gt;), если есть.</summary>
    /// <summary>Ширины декодирования округляются до нескольких ступеней: меньше разных копий в памяти и меньше работы.</summary>
    static readonly int[] Buckets = [64, 128, 256, 384, 512, 768, 1024, 1600, 1920];
    static int Bucket(int width) => Buckets.FirstOrDefault(b => b >= width, Buckets[^1]);

    /// <summary>Заранее, в фоне, декодировать обложки и фоны — чтобы страницы открывались без подтормаживаний.</summary>
    public static void Prewarm(IEnumerable<Games.GameDef> games)
    {
        var list = games.ToList();
        _ = Task.Run(() =>
        {
            foreach (var g in list)
            {
                GameAsset(g, Art.Cover, 256); GameAsset(g, Art.Cover, 384); GameAsset(g, Art.Header, 256);
            }
            foreach (var g in list.Take(8)) { GameAsset(g, Art.Hero, 1600); GameAsset(g, Art.Logo, 768); }
        });
    }

    public static Bitmap? GameAsset(Games.GameDef def, Art art, int width)
    {
        width = Bucket(width);
        if (def.SteamAppId <= 0) return null;
        var name = $"art/{def.SteamAppId}-{Kind(art)}.{(art == Art.Logo ? "png" : "jpg")}";
        return AssetExists(name) ? Asset(name, width) : null;
    }

    static readonly ConcurrentDictionary<string, bool> Exists = new();
    static bool AssetExists(string name) => Exists.GetOrAdd(name, n =>
    {
        try { return AssetLoader.Exists(new Uri($"avares://ModLaunch/Assets/{n}")); } catch { return false; }
    });

    /// <summary>Ссылки Steam на картинку игры — если встроенной нет (свои игры, новые игры).</summary>
    public static string[] SteamUrls(int appId, Art art)
    {
        if (appId <= 0) return [];
        var file = art switch { Art.Cover => "library_600x900.jpg", Art.Hero => "library_hero.jpg", Art.Logo => "logo.png", _ => "header.jpg" };
        return
        [
            $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/{file}",
            $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/{file}",
        ];
    }

    /// <summary>Обложка игры, если она уже под рукой (встроенная или в кэше); из сети — через Ui.GameImage.</summary>
    public static Bitmap? Game(Games.GameDef def, int width)
    {
        if (GameAsset(def, Art.Header, width) is { } header) return header;
        if (def.Art is not null) return Asset(def.Art, width);
        if (def.ArtUrl is null) return null;
        var task = FromUrl(def.ArtUrl, width);
        return task.IsCompleted ? task.Result : null;
    }

    /// <summary>Картинка игры любого вида: встроенная, иначе из Steam по очереди ссылок.</summary>
    public static async Task<Bitmap?> GameAsync(Games.GameDef def, Art art, int width)
    {
        if (GameAsset(def, art, width) is { } local) return local;
        foreach (var url in SteamUrls(def.SteamAppId, art))
            if (await FromUrl(url, width) is { } bmp) return bmp;
        if (art == Art.Cover && GameAsset(def, Art.Hero, width * 3) is { } hero) return hero;
        if (art is Art.Header or Art.Cover && def.Art is not null) return Asset(def.Art, width);
        return def.ArtUrl is null ? null : await FromUrl(def.ArtUrl, width);
    }

    public static Task<Bitmap?> FromUrl(string? url, int decodeWidth = 160)
    {
        if (string.IsNullOrEmpty(url)) return Task.FromResult<Bitmap?>(null);
        var key = $"{url}:{decodeWidth}";
        if (Memory.TryGetValue(key, out var hit)) return Task.FromResult(hit);
        return Pending.GetOrAdd(key, _ => Task.Run(async () =>
        {
            var file = Path.Combine(Paths.CacheDir, "img", Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url))) + ".bin");
            Bitmap? bmp = null;
            try
            {
                if (!File.Exists(file))
                {
                    await Parallel.WaitAsync();
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                        var bytes = await Http.Client.GetByteArrayAsync(url);
                        await File.WriteAllBytesAsync(file, bytes);
                    }
                    finally { Parallel.Release(); }
                }
                await using var stream = File.OpenRead(file);
                bmp = Bitmap.DecodeToWidth(stream, decodeWidth);
            }
            catch { try { File.Delete(file); } catch { } }
            Memory[key] = bmp;
            Pending.TryRemove(key, out Task<Bitmap?>? _);
            return bmp;
        }));
    }
}
