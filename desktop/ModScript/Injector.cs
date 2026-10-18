using System.Text.Json;
using System.Text.Json.Nodes;
using ModLaunch.Creator;

namespace ModScriptX;

/// <summary>
/// Ставит мод из .modx в игру и снимает его. Что поставлено, записано в
/// &lt;игра&gt;/.modscript/installed.json, чтобы eject убрал ровно свои файлы.
/// </summary>
public static class Injector
{
    public sealed record Result(ModxDoc Doc, Target? Target, List<string> Files, string? Note);

    public static Target? Resolve(ModxDoc doc, string? gameDir, string? targetId, List<Diag> diags)
    {
        var id = targetId ?? doc.Engine;
        if (id != null)
        {
            var t = Target.Find(id);
            if (t == null) diags.Add(new Diag(0, $"unknown engine '{id}' (use {string.Join(", ", Target.All.Select(x => x.Id))})"));
            return t;
        }
        if (gameDir != null)
        {
            var t = Engines.Detect(gameDir);
            if (t == null) diags.Add(new Diag(0, "could not detect the game engine: add 'engine <id>' to the .modx or pass --target"));
            return t;
        }
        diags.Add(new Diag(0, "no engine: add 'engine <id>' to the .modx or pass --target"));
        return null;
    }

    /// <summary>Только перевести: файлы пишутся в outDir, игра не трогается.</summary>
    public static Result Build(string source, string outDir, string? targetId)
    {
        var doc = ModxDoc.Parse(source);
        var t = Resolve(doc, null, targetId, doc.Diags);
        if (t == null || !doc.Ok) return new Result(doc, t, [], null);
        var files = Write(t.Emit(doc, null), outDir);
        return new Result(doc, t, files, null);
    }

    public static Result Inject(string source, string gameDir, string? targetId)
    {
        var doc = ModxDoc.Parse(source);
        var ac = Engines.FindAntiCheat(gameDir);
        if (ac != null)
        {
            doc.Diags.Add(new Diag(0, $"{ac} found: ModScript X is for single-player mods and does not work with anti-cheat protected games"));
            return new Result(doc, null, [], null);
        }
        var t = Resolve(doc, gameDir, targetId, doc.Diags);
        if (t == null || !doc.Ok) return new Result(doc, t, [], null);
        var outFiles = t.Emit(doc, gameDir);
        var files = Write(outFiles, gameDir);
        var note = t.AfterInstall(doc, gameDir);
        Remember(gameDir, doc.Id, t.Id, outFiles.Select(f => f.Path).ToList());
        return new Result(doc, t, files, note);
    }

    public static List<string> Eject(string id, string gameDir)
    {
        var db = Load(gameDir);
        if (db[id] is not JsonObject entry) return [];
        var removed = new List<string>();
        foreach (var f in entry["files"]!.AsArray().Select(n => n!.GetValue<string>()))
        {
            var full = Path.Combine(gameDir, f);
            if (File.Exists(full)) { File.Delete(full); removed.Add(f); }
        }
        Target.Find(entry["engine"]!.GetValue<string>())?.AfterEject(id, gameDir);
        db.Remove(id);
        Save(gameDir, db);
        return removed;
    }

    public static IEnumerable<string> Installed(string gameDir) => Load(gameDir).Select(kv => $"{kv.Key} ({kv.Value?["engine"]})");

    static List<string> Write(List<OutFile> files, string root)
    {
        var written = new List<string>();
        foreach (var f in files)
        {
            var full = Path.GetFullPath(Path.Combine(root, f.Path));
            // Файлы мода — только внутри папки игры.
            if (!full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("path escapes the game folder: " + f.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            if (f.Bytes != null) File.WriteAllBytes(full, f.Bytes);
            else File.WriteAllText(full, f.Text);
            written.Add(f.Path);
        }
        return written;
    }

    static string DbPath(string gameDir) => Path.Combine(gameDir, ".modscript", "installed.json");

    static JsonObject Load(string gameDir)
    {
        var p = DbPath(gameDir);
        try { return File.Exists(p) ? JsonNode.Parse(File.ReadAllText(p)) as JsonObject ?? [] : []; }
        catch (JsonException) { return []; }
    }

    static void Save(string gameDir, JsonObject db)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath(gameDir))!);
        File.WriteAllText(DbPath(gameDir), db.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    static void Remember(string gameDir, string id, string engine, List<string> files)
    {
        var db = Load(gameDir);
        db[id] = new JsonObject { ["engine"] = engine, ["files"] = new JsonArray(files.Select(f => (JsonNode)f).ToArray()), ["at"] = DateTime.UtcNow.ToString("o") };
        Save(gameDir, db);
    }
}
