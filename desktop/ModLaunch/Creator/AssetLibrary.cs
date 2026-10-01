using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Games;

namespace ModLaunch.Creator;

public enum AssetKind { Model, Texture, Sound, Bundle, Other }

public sealed record AssetItem(string Id, string Name, AssetKind Kind, string File, string Game, long Size, DateTime Added)
{
    public string FullPath => Path.Combine(AssetLibrary.Root, AssetLibrary.Folder(Kind), File);
    public string Ext => Path.GetExtension(File).TrimStart('.').ToUpperInvariant();
}

/// <summary>
/// Библиотека ассетов Creator Hub: модели, текстуры, звук и бандлы Unity в одном месте
/// (%APPDATA%\ModHub\creator-assets). Из неё файл одним нажатием попадает в проект мода.
/// </summary>
public static class AssetLibrary
{
    public static string Root => Directory.CreateDirectory(Path.Combine(Paths.DataDir, "creator-assets")).FullName;
    static string Index => Path.Combine(Root, "assets.json");

    public static string Folder(AssetKind kind) => kind switch
    {
        AssetKind.Model => "models",
        AssetKind.Texture => "textures",
        AssetKind.Sound => "sounds",
        AssetKind.Bundle => "bundles",
        _ => "other",
    };

    static readonly Dictionary<AssetKind, string[]> Extensions = new()
    {
        [AssetKind.Model] = [".fbx", ".obj", ".glb", ".gltf", ".blend", ".dae", ".3ds", ".stl", ".prefab"],
        [AssetKind.Texture] = [".png", ".jpg", ".jpeg", ".dds", ".tga", ".bmp", ".psd", ".webp", ".exr", ".gif", ".tif", ".tiff"],
        [AssetKind.Sound] = [".wav", ".ogg", ".mp3", ".flac", ".m4a"],
        [AssetKind.Bundle] = [".bundle", ".assetbundle", ".unity3d"],
    };

    public static AssetKind KindOf(string file)
    {
        var ext = Path.GetExtension(file).ToLowerInvariant();
        foreach (var (kind, list) in Extensions) if (list.Contains(ext)) return kind;
        return AssetKind.Other;
    }

    /// <summary>Файл можно показать маленькой картинкой (то, что Avalonia умеет читать без плагинов).</summary>
    public static bool HasPreview(AssetItem a) => a.Kind == AssetKind.Texture && Path.GetExtension(a.File).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp";

    public static List<AssetItem> List()
    {
        try
        {
            if (!File.Exists(Index)) return [];
            var list = new List<AssetItem>();
            foreach (var n in JsonNode.Parse(File.ReadAllText(Index)) as JsonArray ?? [])
            {
                if (!Enum.TryParse<AssetKind>(n.Str("kind"), out var kind)) continue;
                var item = new AssetItem(n.Str("id") ?? "", n.Str("name") ?? "", kind, n.Str("file") ?? "", n.Str("game") ?? "", n.Long("size"), DateTime.TryParse(n.Str("added"), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var d) ? d : DateTime.UtcNow);
                if (File.Exists(item.FullPath)) list.Add(item);
            }
            return list.OrderByDescending(a => a.Added).ToList();
        }
        catch { return []; }
    }

    static void Write(List<AssetItem> list) =>
        File.WriteAllText(Index, new JsonArray(list.Select(a => (JsonNode)new JsonObject
        {
            ["id"] = a.Id, ["name"] = a.Name, ["kind"] = a.Kind.ToString(), ["file"] = a.File, ["game"] = a.Game, ["size"] = a.Size, ["added"] = a.Added.ToString("o"),
        }).ToArray()).ToJsonString());

    /// <summary>Положить файл в библиотеку (копией — исходник не трогаем). Имя при совпадении получает номер.</summary>
    public static AssetItem Import(string path, string game)
    {
        var kind = KindOf(path);
        var dir = Directory.CreateDirectory(Path.Combine(Root, Folder(kind))).FullName;
        var name = Regex.Replace(Path.GetFileName(path), @"[<>:""/\\|?*]", "_");
        var target = name;
        for (var i = 2; File.Exists(Path.Combine(dir, target)); i++) target = $"{Path.GetFileNameWithoutExtension(name)}-{i}{Path.GetExtension(name)}";
        File.Copy(path, Path.Combine(dir, target));
        var item = new AssetItem(Guid.NewGuid().ToString("N")[..10], Path.GetFileNameWithoutExtension(target), kind, target, game, new FileInfo(path).Length, DateTime.UtcNow);
        var list = List();
        list.Insert(0, item);
        Write(list);
        return item;
    }

    public static void Remove(AssetItem a)
    {
        try { File.Delete(a.FullPath); } catch { }
        Write(List().Where(x => x.Id != a.Id).ToList());
    }

    public static void SetGame(AssetItem a, string game) => Write(List().Select(x => x.Id == a.Id ? x with { Game = game } : x).ToList());

    /// <summary>
    /// Добавить ассет в проект ModScript: файл копируется в files/, а в конец скрипта дописывается команда copy
    /// (для BepInEx — в plugins/&lt;мод&gt;/, для Stardew — в assets/). Если такая строка уже есть, второй не будет.
    /// </summary>
    public static string AttachToScript(AssetItem a, Project p)
    {
        var files = Directory.CreateDirectory(Path.Combine(p.Dir, "files")).FullName;
        File.Copy(a.FullPath, Path.Combine(files, a.File), true);
        var game = GameCatalog.ById(p.Game);
        var to = game?.Loader switch
        {
            LoaderKind.Bepinex => $"plugins/{Projects.PackageName(p.Name)}/{a.File}",
            LoaderKind.Smapi => $"assets/{a.File}",
            _ => a.File,
        };
        var line = $"copy \"{a.File}\" to \"{to}\"";
        var text = File.ReadAllText(p.Script);
        if (!text.Contains(line)) File.WriteAllText(p.Script, text.TrimEnd() + "\n" + line + "\n");
        return line;
    }

    /// <summary>Добавить ассет в проект на C#: в папку assets/, откуда сборка копирует его рядом с модом.</summary>
    public static string AttachToCode(AssetItem a, CodeProject p)
    {
        var dir = Directory.CreateDirectory(Path.Combine(p.Dir, "assets")).FullName;
        File.Copy(a.FullPath, Path.Combine(dir, a.File), true);
        return Path.Combine("assets", a.File);
    }
}
