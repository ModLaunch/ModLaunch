using Avalonia.Media.Imaging;
using ModLaunch.Core;

namespace ModLaunch.Minecraft;

/// <summary>
/// 9.4: картинка сборки, как в Modrinth App. У каждой сборки она есть сразу — одна из встроенных
/// пиксельных картинок (блоки и предметы, нарисованы для ModLaunch), выбранная по сборке; можно
/// взять другую встроенную или свою картинку. Модпак приходит со своим значком.
/// Значение McInstance.Icon: пусто — встроенная по умолчанию, «builtin:имя», ссылка http(s) или путь к файлу.
/// </summary>
public static class McIcons
{
    public static readonly string[] Builtin =
    [
        "meadow", "stone", "sand", "snow", "crystal", "magma", "planks", "amethyst", "gold", "ocean",
        "pickaxe", "sword", "potion", "heart", "star", "mushroom", "lantern", "gem",
    ];

    /// <summary>Встроенная картинка по умолчанию: всегда одна и та же для сборки (по её папке-имени).</summary>
    public static string DefaultFor(McInstance i)
    {
        var hash = 2166136261u;
        foreach (var c in i.Id) hash = (hash ^ c) * 16777619u;
        return Builtin[hash % (uint)Builtin.Length];
    }

    /// <summary>Что показать: своё значение или встроенную картинку по умолчанию.</summary>
    public static string Effective(McInstance i) => i.Icon is { Length: > 0 } icon ? icon : "builtin:" + DefaultFor(i);

    public static string AssetName(string builtin) => $"mc-icons/{builtin}.png";

    /// <summary>Своя картинка: копия в папке сборки (icon.png), уменьшенная до 256 точек.</summary>
    public static void SetCustom(McInstance i, string file)
    {
        using var input = File.OpenRead(file);
        using var bmp = Bitmap.DecodeToWidth(input, 256);
        var target = Path.Combine(i.Linked ? Path.Combine(Paths.DataDir, "minecraft", "icons") : i.Dir, i.Linked ? i.Id + ".png" : "icon.png");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        bmp.Save(target);
        Set(i, target);
    }

    public static void SetBuiltin(McInstance i, string name) => Set(i, "builtin:" + name);

    /// <summary>Вернуть картинку по умолчанию (у модпака — его значок, если он был).</summary>
    public static void Reset(McInstance i) => Set(i, null);

    static void Set(McInstance i, string? icon)
    {
        i.Icon = icon;
        Mc.Save(i);
        try { McProfiles.Sync(i); } catch { }
        Mc.Notify();
    }

    /// <summary>PNG картинки сборки (128 точек) для значка установки в официальном лаунчере.</summary>
    public static string? DataUrl(McInstance i)
    {
        try
        {
            var icon = Effective(i);
            Stream? source = null;
            if (icon.StartsWith("builtin:", StringComparison.Ordinal))
                source = Avalonia.Platform.AssetLoader.Open(new Uri("avares://ModLaunch/Assets/" + AssetName(icon[8..])));
            else if (Path.IsPathFullyQualified(icon) && File.Exists(icon))
                source = File.OpenRead(icon);
            else if (icon.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                // Значок модпака уже лежит в кэше картинок, если его хоть раз показывали.
                var cached = Path.Combine(Paths.CacheDir, "img", Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(icon))) + ".bin");
                if (File.Exists(cached)) source = File.OpenRead(cached);
            }
            if (source is null) return null;
            using (source)
            {
                using var bmp = Bitmap.DecodeToWidth(source, 128);
                using var ms = new MemoryStream();
                bmp.Save(ms);
                return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
            }
        }
        catch { return null; }
    }

    [SelfTest]
    static string EveryBuildGetsAPicture()
    {
        var a = new McInstance { Id = "vyzhivanie" };
        var b = new McInstance { Id = "vyzhivanie" };
        if (DefaultFor(a) != DefaultFor(b)) throw new Exception("default picture is not stable");
        var used = Enumerable.Range(0, 200).Select(n => DefaultFor(new McInstance { Id = "build-" + n })).Distinct().Count();
        if (used < Builtin.Length / 2) throw new Exception($"only {used} pictures used for 200 builds");
        if (Effective(new McInstance { Id = "x", Icon = "builtin:gem" }) != "builtin:gem") throw new Exception("chosen picture ignored");
        return $"stable default, {used} of {Builtin.Length} pictures across 200 builds";
    }
}
