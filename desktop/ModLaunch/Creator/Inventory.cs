using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Games;

namespace ModLaunch.Creator;

/// <summary>Предмет инвентаря креатора: модель, текстура, звук, кусок кода или любой файл.</summary>
public sealed record InvItem(
    string Id, string Name, string Kind, string Origin, string? AssetId, string Author, List<string> Games, string Engine,
    string? FileName, DateTime Added, long Serial, string Note)
{
    public string Dir => Path.Combine(Inventory.Root, Id);
    public string? FilePath => FileName is null ? null : Path.Combine(Dir, FileName);
    public bool IsCode => Kind == "code";
    /// <summary>Взято из чужого установленного мода: пользоваться можно, выкладывать — нет.</summary>
    public bool Private => Origin == "mod";
    public long Size => FilePath is { } f && File.Exists(f) ? new FileInfo(f).Length : 0;
}

/// <summary>
/// Инвентарь креатора — один на все игры: модели, текстуры, звуки и куски кода,
/// которые можно вставить в любой проект (asset "имя" / use "имя"). Лежит в
/// creator/_inventory/&lt;предмет&gt;/ (item.json + сам файл). Купленное и взятое в
/// хабе скачивается сюда же; облачная часть (что тебе принадлежит) — в Market.
/// </summary>
public static partial class Inventory
{
    public static string Root => Directory.CreateDirectory(Path.Combine(Projects.Root, "_inventory")).FullName;

    /// <summary>Инвентарь поменялся — экраны перерисовываются.</summary>
    public static event Action? Changed;

    public static readonly string[] Kinds = ["model", "texture", "sound", "code", "other"];

    [GeneratedRegex(@"\.(bundle|assetbundle|unity3d|glb|gltf|fbx|obj|vrm|blend|dae|3ds|prefab)$", RegexOptions.IgnoreCase)] private static partial Regex ModelFile();
    [GeneratedRegex(@"\.(png|jpe?g|dds|tga|psd|bmp|webp|xnb)$", RegexOptions.IgnoreCase)] private static partial Regex TextureFile();
    [GeneratedRegex(@"\.(wav|ogg|mp3|flac|bank)$", RegexOptions.IgnoreCase)] private static partial Regex SoundFile();
    [GeneratedRegex(@"\.(mls|cs|lua|js|py|txt)$", RegexOptions.IgnoreCase)] private static partial Regex CodeFile();

    public static string KindOf(string fileName) =>
        ModelFile().IsMatch(fileName) ? "model" : TextureFile().IsMatch(fileName) ? "texture" : SoundFile().IsMatch(fileName) ? "sound"
        : CodeFile().IsMatch(fileName) ? "code" : "other";

    /// <summary>На чём работает игра: модели Unity подходят к любой игре на Unity, контент Stardew — только к Stardew.</summary>
    public static string EngineOf(GameDef? game) => game?.Loader switch
    {
        LoaderKind.Smapi => "stardew",
        LoaderKind.Bepinex or LoaderKind.HkApi => "unity",
        _ => "any",
    };

    /// <summary>Подходит ли предмет к игре: прямо указана, тот же движок или вещь без привязки (код, картинка, звук).</summary>
    public static bool Fits(InvItem item, GameDef? game)
    {
        if (game is null) return true;
        if (item.Games.Contains(game.Id)) return true;
        if (item.Kind is "texture" or "sound") return true;
        if (item.Engine is "" or "any") return item.Games.Count == 0;
        return item.Engine == EngineOf(game);
    }

    static InvItem? Read(string dir)
    {
        try
        {
            var file = Path.Combine(dir, "item.json");
            if (!File.Exists(file)) return null;
            var j = JsonNode.Parse(File.ReadAllText(file));
            return new InvItem(Path.GetFileName(dir), j.Str("name") ?? "?", j.Str("kind") ?? "other", j.Str("origin") ?? "local", j.Str("asset"),
                j.Str("author") ?? "", j.Arr("games").Select(x => x?.ToString()).OfType<string>().ToList(), j.Str("engine") ?? "any",
                j.Str("file"), DateTime.TryParse(j.Str("added"), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var d) ? d : Directory.GetCreationTimeUtc(dir),
                j.Long("serial"), j.Str("note") ?? "");
        }
        catch { return null; }
    }

    static void Write(InvItem item)
    {
        Directory.CreateDirectory(item.Dir);
        var j = new JsonObject
        {
            ["name"] = item.Name, ["kind"] = item.Kind, ["origin"] = item.Origin, ["asset"] = item.AssetId, ["author"] = item.Author,
            ["games"] = new JsonArray(item.Games.Select(g => (JsonNode)g).ToArray()), ["engine"] = item.Engine, ["file"] = item.FileName,
            ["added"] = item.Added.ToString("o"), ["serial"] = item.Serial, ["note"] = item.Note,
        };
        File.WriteAllText(Path.Combine(item.Dir, "item.json"), j.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    public static List<InvItem> List()
    {
        if (Program.Demo && DemoItems is { } demo) return demo;
        return Directory.EnumerateDirectories(Root).Select(Read).OfType<InvItem>().OrderByDescending(i => i.Added).ToList();
    }

    /// <summary>Для снимков экрана: инвентарь без диска.</summary>
    public static List<InvItem>? DemoItems;

    static bool Is(InvItem i, string key) =>
        i.Id.Equals(key, StringComparison.OrdinalIgnoreCase) || i.Name.Equals(key, StringComparison.OrdinalIgnoreCase)
        || (i.AssetId is not null && i.AssetId.Equals(key, StringComparison.OrdinalIgnoreCase));

    public static InvItem? Get(string key) => List().FirstOrDefault(i => Is(i, key));

    /// <summary>Кусок кода для use "имя" (по имени, номеру предмета или ассета).</summary>
    public static string? Snippet(string key)
    {
        try
        {
            var item = List().FirstOrDefault(i => i.IsCode && Is(i, key));
            return item?.FilePath is { } f && File.Exists(f) ? File.ReadAllText(f) : null;
        }
        catch { return null; }
    }

    /// <summary>Файл для asset "имя" — путь на диске.</summary>
    public static string? LocalFile(string key)
    {
        var item = List().FirstOrDefault(i => !i.IsCode && Is(i, key)) ?? List().FirstOrDefault(i => Is(i, key));
        return item?.FilePath is { } f && File.Exists(f) ? f : null;
    }

    static string NewId(string name)
    {
        var slug = Projects.PackageName(name).ToLowerInvariant();
        if (slug.Length > 40) slug = slug[..40];
        return $"{slug}-{Guid.NewGuid().ToString("N")[..6]}";
    }

    static string SafeFile(string name)
    {
        var s = Regex.Replace(Path.GetFileName(name), @"[<>:""/\\|?*\x00-\x1f]", "_").Trim();
        return s == "" ? "file" : s.Length > 120 ? s[^120..] : s;
    }

    /// <summary>Положить свой файл (или файл из установленного мода — origin "mod").</summary>
    public static InvItem AddFile(string path, string? name = null, string? kind = null, IEnumerable<string>? games = null, string? engine = null, string origin = "local", string note = "")
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException(path);
        if (info.Length > Hub.MaxFile * 4) throw new InvalidOperationException(I18n.T("inv.err.big"));
        var title = name is { Length: > 0 } n ? n : Path.GetFileNameWithoutExtension(path);
        var item = new InvItem(NewId(title), Hub.Clip(title, 60), kind ?? KindOf(path), origin, null, "", games?.ToList() ?? [], engine ?? "any",
            SafeFile(path), DateTime.UtcNow, 0, Hub.Clip(note, 300));
        Directory.CreateDirectory(item.Dir);
        File.Copy(path, item.FilePath!, true);
        Write(item);
        Changed?.Invoke();
        return item;
    }

    /// <summary>Кусок кода: свой, из редактора или взятый из мода в хабе.</summary>
    public static InvItem AddCode(string name, string code, string origin = "local", string author = "", IEnumerable<string>? games = null, string note = "", string? assetId = null)
    {
        var existing = assetId is null ? null : List().FirstOrDefault(i => i.AssetId == assetId);
        var title = Hub.Clip(name, 60) is { Length: > 0 } t ? t : "snippet";
        var item = new InvItem(existing?.Id ?? NewId(title), title, "code", origin, assetId, author, games?.ToList() ?? [], "any",
            Projects.PackageName(title) + ModScript.Extension, existing?.Added ?? DateTime.UtcNow, 0, Hub.Clip(note, 300));
        Directory.CreateDirectory(item.Dir);
        File.WriteAllText(item.FilePath!, code.Replace("\r\n", "\n"));
        Write(item);
        Changed?.Invoke();
        return item;
    }

    /// <summary>Скачанный ассет из хаба: заменяет прошлую копию того же ассета.</summary>
    public static InvItem AddAsset(Asset a, byte[] data, long serial)
    {
        var existing = List().FirstOrDefault(i => i.AssetId == a.Id);
        if (existing is not null) try { Directory.Delete(existing.Dir, true); } catch { }
        var file = a.Kind == "code" ? Projects.PackageName(a.Name) + ModScript.Extension : SafeFile(a.FileName is { Length: > 0 } f ? f : a.Name);
        var item = new InvItem(existing?.Id ?? NewId(a.Name), a.Name, a.Kind, "hub", a.Id, a.Author, a.Games, a.Engine, file,
            existing?.Added ?? DateTime.UtcNow, serial, a.Summary);
        Directory.CreateDirectory(item.Dir);
        File.WriteAllBytes(item.FilePath!, data);
        Write(item);
        Changed?.Invoke();
        return item;
    }

    public static void Rename(InvItem item, string name)
    {
        Write(item with { Name = Hub.Clip(name, 60) is { Length: > 0 } n ? n : item.Name });
        Changed?.Invoke();
    }

    public static void Remove(InvItem item)
    {
        try { Directory.Delete(item.Dir, true); } catch { }
        Changed?.Invoke();
    }

    /// <summary>Файлы установленного мода, которые можно взять в инвентарь: модели, текстуры, звуки, код.</summary>
    public static List<string> Takeable(string folder)
    {
        if (!Directory.Exists(folder)) return [];
        return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(f => KindOf(f) != "other" || f.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Take(400).ToList();
    }

    /// <summary>Строка для вставки в скрипт: use для кода, asset для файла (путь — по движку игры).</summary>
    public static string Usage(InvItem item, GameDef? game)
    {
        if (item.IsCode) return $"use \"{item.Name}\"\n";
        var file = item.FileName ?? item.Name;
        var to = EngineOf(game) == "stardew" ? $"assets/{file}" : $"plugins/{Projects.PackageName(item.Name)}/{file}";
        return $"asset \"{item.Name}\" to \"{to}\"\n";
    }
}
