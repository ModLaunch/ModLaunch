using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModLaunch.Core;

/// <summary>
/// JSON-файл на диске, который читается и пишется целиком. Неизвестные поля
/// сохраняются как есть — файл общий с версией на Electron.
/// </summary>
public sealed class JsonFile
{
    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    readonly string _path;
    readonly object _lock = new();

    public JsonObject Data { get; private set; }

    public JsonFile(string path, Func<JsonObject> defaults)
    {
        _path = path;
        Data = Load() ?? defaults();
    }

    JsonObject? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            return JsonNode.Parse(File.ReadAllText(_path)) as JsonObject;
        }
        catch
        {
            // Повреждённый файл не должен ронять программу: откладываем его в сторону.
            try { File.Copy(_path, _path + ".broken", true); } catch { }
            return null;
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, Data.ToJsonString(Pretty));
            File.Move(tmp, _path, true);
        }
    }
}

public static class JsonExt
{
    public static string? Str(this JsonNode? node, string key) =>
        node is JsonObject o && o.TryGetPropertyValue(key, out var v) && v is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : null;

    public static long Long(this JsonNode? node, string key)
    {
        if (node is not JsonObject o || !o.TryGetPropertyValue(key, out var v) || v is not JsonValue jv) return 0;
        if (jv.TryGetValue<long>(out var l)) return l;
        if (jv.TryGetValue<double>(out var d)) return (long)d;
        if (jv.TryGetValue<string>(out var s) && long.TryParse(s, out l)) return l;
        return 0;
    }

    public static bool Bool(this JsonNode? node, string key, bool fallback = false) =>
        node is JsonObject o && o.TryGetPropertyValue(key, out var v) && v is JsonValue jv && jv.TryGetValue<bool>(out var b) ? b : fallback;

    public static JsonArray Arr(this JsonNode? node, string key) =>
        node is JsonObject o && o.TryGetPropertyValue(key, out var v) && v is JsonArray a ? a : new JsonArray();

    public static JsonObject Obj(this JsonObject node, string key)
    {
        if (node.TryGetPropertyValue(key, out var v) && v is JsonObject o) return o;
        var created = new JsonObject();
        node[key] = created;
        return created;
    }
}
