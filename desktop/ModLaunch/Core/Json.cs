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
            // Ещё не дописанное на диск — свежее, чем файл.
            if (Writer.Pending(_path) is { } queued) return JsonNode.Parse(queued) as JsonObject;
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

    /// <summary>
    /// 9.5: в окне программы запись на диск идёт в фоне — нажатия не ждут диска (на Windows с
    /// антивирусом запись файла бывает и 20–50 мс). Текст снимается сразу, в момент вызова; частые
    /// сохранения одного файла склеиваются. При выходе всё дописывается (FlushAll).
    /// </summary>
    public static bool DeferWrites { get; set; }

    public void Save()
    {
        string text;
        lock (_lock) text = Data.ToJsonString(Pretty);
        if (DeferWrites && Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) Writer.Queue(_path, text);
        else Writer.Now(_path, text);
    }

    /// <summary>Дописать на диск всё, что ждёт в очереди (выход из программы).</summary>
    public static void FlushAll() => Writer.Flush();

    [SelfTest]
    static string DeferredWritesKeepNewest()
    {
        var path = Path.Combine(Paths.DataDir, "defer-test.json");
        var was = DeferWrites;
        DeferWrites = true;
        try
        {
            var f = new JsonFile(path, () => new JsonObject());
            for (var i = 1; i <= 50; i++) { f.Data["n"] = i; f.Save(); }
            if (new JsonFile(path, () => new JsonObject()).Data.Long("n") != 50) throw new Exception("a new reader did not see the queued text");
            FlushAll();
            if (JsonNode.Parse(File.ReadAllText(path))?["n"]?.GetValue<int>() != 50) throw new Exception("flush did not write the newest text");
            // Синхронная запись после фоновой: фоновая старая не перезапишет её.
            DeferWrites = false;
            f.Data["n"] = 99;
            f.Save();
            Thread.Sleep(300);
            if (JsonNode.Parse(File.ReadAllText(path))?["n"]?.GetValue<int>() != 99) throw new Exception("old queued text overwrote a newer save");
            return "50 saves queued, newest on disk after flush, a later direct save wins";
        }
        finally { DeferWrites = was; }
    }

    /// <summary>Очередь записи: на каждый файл — только последний текст; старый текст не перезапишет новый.</summary>
    static class Writer
    {
        static readonly object Gate = new(), WriteGate = new();
        static readonly Dictionary<string, (long Seq, string Text)> Queue_ = new(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, long> Written = new(StringComparer.OrdinalIgnoreCase);
        static long _seq;
        static bool _running;

        static Writer() => AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();

        public static string? Pending(string path) { lock (Gate) return Queue_.TryGetValue(path, out var q) ? q.Text : null; }

        public static void Queue(string path, string text)
        {
            lock (Gate)
            {
                Queue_[path] = (++_seq, text);
                if (_running) return;
                _running = true;
            }
            _ = Task.Run(async () =>
            {
                await Task.Delay(120);
                while (true)
                {
                    KeyValuePair<string, (long Seq, string Text)>[] batch;
                    lock (Gate)
                    {
                        if (Queue_.Count == 0) { _running = false; return; }
                        batch = [.. Queue_];
                        Queue_.Clear();
                    }
                    foreach (var (p, (seq, t)) in batch) Write(p, seq, t);
                }
            });
        }

        public static void Now(string path, string text)
        {
            long seq;
            lock (Gate) { seq = ++_seq; Queue_.Remove(path); }
            Write(path, seq, text, rethrow: true);
        }

        public static void Flush()
        {
            KeyValuePair<string, (long Seq, string Text)>[] batch;
            lock (Gate) { batch = [.. Queue_]; Queue_.Clear(); }
            foreach (var (p, (seq, t)) in batch) Write(p, seq, t);
        }

        static void Write(string path, long seq, string text, bool rethrow = false)
        {
            lock (WriteGate)
            {
                if (Written.TryGetValue(path, out var done) && done > seq) return;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    var tmp = path + ".tmp";
                    File.WriteAllText(tmp, text);
                    File.Move(tmp, path, true);
                    Written[path] = seq;
                }
                catch when (!rethrow) { }
            }
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
        if (jv.TryGetValue<int>(out var i)) return i;
        if (jv.TryGetValue<double>(out var d)) return (long)d;
        if (jv.TryGetValue<string>(out var s) && long.TryParse(s, out l)) return l;
        return 0;
    }

    public static double Double(this JsonNode? node, string key)
    {
        if (node is not JsonObject o || !o.TryGetPropertyValue(key, out var v) || v is not JsonValue jv) return 0;
        if (jv.TryGetValue<double>(out var d)) return d;
        if (jv.TryGetValue<long>(out var l)) return l;
        if (jv.TryGetValue<int>(out var i)) return i;
        if (jv.TryGetValue<decimal>(out var m)) return (double)m;
        if (jv.TryGetValue<string>(out var s) && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) return d;
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
