using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Mods;

namespace ModLaunch.Features;

public sealed record Snapshot(string GameId, string File, DateTime At, string Reason, int Count, bool HasConfig);

/// <summary>
/// Снимки «как было» перед рискованными делами (обновить всё, поставить сборку или код,
/// применить профиль, удалить моды): состав модов с версиями и настройки. Хранятся последние 20.
/// </summary>
public static class Snapshots
{
    const int Keep = 20;

    static string Dir(string gameId) => Path.Combine(Paths.DataDir, "snapshots", gameId);

    /// <summary>Сделать снимок. reason — ключ: update, remove, import, code, profile, manager.
    /// keep — снимок, который сейчас возвращают: его не удаляем, даже если он самый старый.</summary>
    public static void Take(GameState g, string reason, Snapshot? keep = null)
    {
        if (g.Registry is not { } registry) return;
        try
        {
            var mods = ModSetup.Capture(registry);
            if (mods.Count == 0) return;
            var dir = Directory.CreateDirectory(Dir(g.Def.Id)).FullName;
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            var hasConfig = ModSetup.SaveConfig(registry, Path.Combine(dir, stamp + ".zip"));
            File.WriteAllText(Path.Combine(dir, stamp + ".json"), new JsonObject
            {
                ["at"] = DateTime.UtcNow.ToString("o"),
                ["reason"] = reason,
                ["config"] = hasConfig,
                ["mods"] = ModSetup.ToJson(mods),
            }.ToJsonString());
            // «Рабочая сборка» хранится одна и не вытесняется двадцатью другими снимками.
            foreach (var old in List(g.Def.Id).Where(s => s.Reason != "works" && s.File != keep?.File).Skip(Keep)) Delete(old);
        }
        catch { /* снимок — подстраховка, из-за него дело не должно сорваться */ }
    }

    /// <summary>
    /// 25.0: «рабочая сборка» — состав модов после игры без вылета (5+ минут). Хранится один такой снимок;
    /// состав не поменялся — новый не пишем. Начала игра падать после обновлений — одна кнопка вернёт как было.
    /// </summary>
    public static void RememberWorking(GameState g)
    {
        if (g.Registry is not { } registry) return;
        try
        {
            var mods = ModSetup.Capture(registry);
            if (mods.Count == 0) return;
            // Запоминаем только тот набор, с которым игру и запускали: моды меняли во время игры — не он.
            List<SetupMod>? launched;
            lock (AtLaunch) launched = AtLaunch.GetValueOrDefault(g.Def.Id);
            if (launched is null || !Same(launched, mods)) return;
            if (LastWorking(g.Def.Id) is { } last && Same(Mods(last), mods)) return;
            Take(g, "works");
            foreach (var old in List(g.Def.Id).Where(s => s.Reason == "works").Skip(1)) Delete(old);
        }
        catch { }
    }

    /// <summary>Набор модов в момент запуска игры — с ним сверяется «рабочая сборка» после выхода.</summary>
    static readonly Dictionary<string, List<SetupMod>> AtLaunch = [];

    public static void NoteLaunch(GameState g)
    {
        if (g.Registry is not { } registry) return;
        try
        {
            var mods = ModSetup.Capture(registry);
            lock (AtLaunch) AtLaunch[g.Def.Id] = mods;
        }
        catch { }
    }

    public static Snapshot? LastWorking(string gameId) => List(gameId).FirstOrDefault(s => s.Reason == "works");

    /// <summary>Отличается ли сейчас состав от рабочей сборки (иначе возвращать нечего).</summary>
    public static bool DiffersFromWorking(GameState g)
    {
        try { return g.Registry is { } registry && LastWorking(g.Def.Id) is { } last && !Same(Mods(last), ModSetup.Capture(registry)); }
        catch { return false; }
    }

    static bool Same(List<SetupMod> a, List<SetupMod> b) =>
        a.Count == b.Count && a.OrderBy(m => m.Id, StringComparer.Ordinal).Select(m => (m.Id, m.Version, m.Enabled))
            .SequenceEqual(b.OrderBy(m => m.Id, StringComparer.Ordinal).Select(m => (m.Id, m.Version, m.Enabled)));

    public static List<Snapshot> List(string gameId)
    {
        var dir = Dir(gameId);
        if (!Directory.Exists(dir)) return [];
        var result = new List<Snapshot>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                var data = JsonNode.Parse(File.ReadAllText(file));
                result.Add(new Snapshot(gameId, file,
                    DateTime.TryParse(data.Str("at"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var at) ? at : File.GetLastWriteTimeUtc(file),
                    data.Str("reason") ?? "", data.Arr("mods").Count, data.Bool("config")));
            }
            catch { }
        }
        return result.OrderByDescending(s => s.At).ToList();
    }

    public static List<SetupMod> Mods(Snapshot snapshot) => ModSetup.FromJson(JsonNode.Parse(File.ReadAllText(snapshot.File))?["mods"] as JsonArray);

    public static string? ConfigZip(Snapshot snapshot) => snapshot.HasConfig ? Path.ChangeExtension(snapshot.File, ".zip") : null;

    public static void Delete(Snapshot snapshot)
    {
        try { File.Delete(snapshot.File); } catch { }
        try { File.Delete(Path.ChangeExtension(snapshot.File, ".zip")); } catch { }
    }

    [SelfTest]
    static string KeepsLastTwenty()
    {
        var game = Path.Combine(Paths.DataDir, "snap-test", "Lethal Company");
        Directory.CreateDirectory(Path.Combine(game, "BepInEx", "plugins", "LethalLib"));
        var def = Games.GameCatalog.ById("lethal-company")!;
        var state = new GameState { Def = def, Path = game };
        state.Registry!.Add(new JsonObject { ["id"] = "Evaisa-LethalLib", ["name"] = "LethalLib", ["folder"] = "LethalLib", ["version"] = "0.16.1", ["source"] = "thunderstore" });
        for (var i = 0; i < Keep + 3; i++) { Take(state, "update"); Thread.Sleep(2); }
        var list = List(def.Id);
        if (list.Count != Keep) throw new Exception($"{list.Count} snapshots kept");
        var mods = Mods(list[0]);
        if (mods.SingleOrDefault(m => m.Id == "Evaisa-LethalLib")?.Version != "0.16.1") throw new Exception("snapshot content wrong");
        return $"{Keep} newest kept, content ok";
    }
}
