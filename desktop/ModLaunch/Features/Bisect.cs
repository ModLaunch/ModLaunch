using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Mods;

namespace ModLaunch.Features;

/// <summary>
/// 25.0: «Найти виновника» — когда по логам причину не понять. Половина подозреваемых модов выключается,
/// человек запускает игру и отвечает «упала» или «работает»; круг подозреваемых каждый раз уменьшается
/// вдвое — из 30 модов виновник находится за 5 запусков. Библиотеки, на которых держатся другие моды,
/// не выключаются (иначе упало бы всё). В конце моды включаются как были, виновник остаётся выключенным.
/// Состояние хранится в настройках — поиск переживает перезапуск программы.
/// </summary>
public static class Bisect
{
    static JsonObject Store => Settings.Data.Obj("bisect");

    /// <summary>
    /// Before — все включённые записи до поиска (и вторые папки «мод#папка»); Suspects — подозреваемые;
    /// Testing — включённая сейчас половина; Verify — последний подозреваемый на проверочном запуске
    /// (он выключен, остальные включены); Applied — когда моды переключили (запуски раньше не считаются).
    /// </summary>
    public sealed record State(List<string> Before, List<string> Suspects, List<string> Testing, int Round, string? Verify = null, DateTime? Applied = null);

    public static State? Get(string gameId) => Store[gameId] is JsonObject o
        ? new State(Ids(o, "before"), Ids(o, "suspects"), Ids(o, "testing"), (int)o.Long("round"), o.Str("verify"),
            DateTime.TryParse(o.Str("applied"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var at) ? at : null)
        : null;

    public static bool Running(string gameId) => Store[gameId] is JsonObject;

    static List<string> Ids(JsonObject o, string key) => o.Arr(key).Select(x => x?.ToString()).OfType<string>().ToList();

    static void Save(string gameId, State s)
    {
        JsonArray A(IEnumerable<string> ids) => new(ids.Select(i => (JsonNode)i).ToArray());
        Store[gameId] = new JsonObject
        {
            ["before"] = A(s.Before), ["suspects"] = A(s.Suspects), ["testing"] = A(s.Testing), ["round"] = s.Round,
            ["verify"] = s.Verify, ["applied"] = (s.Applied ?? DateTime.UtcNow).ToString("o"),
        };
        Settings.Save();
    }

    static void Clear(string gameId) { Store.Remove(gameId); Settings.Save(); }

    /// <summary>Сколько запусков понадобится на n подозреваемых: деление пополам и один проверочный.</summary>
    public static int Runs(int suspects) => suspects <= 1 ? 0 : (int)Math.Ceiling(Math.Log2(suspects)) + 1;

    /// <summary>Сколько ещё запусков примерно понадобится.</summary>
    public static int RunsLeft(State s) => s.Verify is not null ? 1 : Runs(s.Suspects.Count);

    /// <summary>Этот запуск был уже с переключёнными модами (а не начатый до поиска или до шага).</summary>
    public static bool Counts(State s, DateTime started) => s.Applied is not { } at || started >= at.AddSeconds(-2);

    /// <summary>Записи одного мода: сам мод и его вторые папки «мод#папка» (выключаются вместе).</summary>
    static IEnumerable<string> Group(State s, string id) =>
        s.Before.Where(b => b == id || b.StartsWith(id + "#", StringComparison.Ordinal));

    /// <summary>Кого можно подозревать: включённые моды, кроме библиотек, на которых держатся другие включённые моды.
    /// Вторая папка мода идёт за своим модом; «мод#папка» без своего мода — подозреваемый сам по себе.</summary>
    public static (List<string> Enabled, List<string> Suspects) Candidates(ModRegistry registry)
    {
        var records = registry.List().Where(r => r.Str("kind") != "preset" && r.Str("target") is null).ToList();
        var all = records.Select(r => r.Str("id")!).ToHashSet(StringComparer.Ordinal);
        var enabled = records.Where(r => r.Bool("enabled", true) && !r.Bool("missing")).Select(r => r.Str("id")!).ToList();
        var heads = enabled.Where(id => id.IndexOf('#') is var at && (at <= 0 || !all.Contains(id[..at]))).ToList();
        var graph = DepGraph.Build(registry);
        var suspects = heads.Where(id => !graph.NeededByOf(id).Any(d => d.Bool("enabled", true) && !d.Bool("missing"))).ToList();
        return (enabled, suspects);
    }

    /// <summary>Почему поиск сейчас не начать (null — можно): уже идёт или игра запущена.</summary>
    public static string? CantStart(GameState g) =>
        Running(g.Def.Id) ? I18n.T("v25.bis.already") : Launcher.IsRunning(g.Def.Id) ? I18n.T("vanilla.running") : null;

    /// <summary>Начать: снимок «как было», первая половина подозреваемых остаётся включённой.</summary>
    public static State Start(GameState g)
    {
        var registry = g.Registry ?? throw new InvalidOperationException(I18n.T("err.gameNotFound"));
        // Второй поиск поверх первого затёр бы «как было» — выключенные первым моды так и остались бы выключенными.
        if (CantStart(g) is { } why) throw new InvalidOperationException(why);
        var (enabled, suspects) = Candidates(registry);
        if (suspects.Count < 2) throw new InvalidOperationException(I18n.T("v25.bis.few"));
        try { Snapshots.Take(g, "bisect"); } catch { }
        var s = new State(enabled, suspects, [], 0);
        return Apply(g, s);
    }

    /// <summary>Включить проверяемую половину, остальных подозреваемых выключить.</summary>
    static State Apply(GameState g, State s)
    {
        var testing = s.Suspects.Take(s.Suspects.Count / 2).ToList();
        var registry = g.Registry!;
        foreach (var id in s.Suspects)
            foreach (var part in Group(s, id))
                try { registry.SetEnabled(part, testing.Contains(id)); } catch { }
        var next = s with { Testing = testing, Verify = null, Applied = DateTime.UtcNow };
        Save(g.Def.Id, next);
        return next;
    }

    /// <summary>
    /// Проверочный запуск: остался один подозреваемый — выключен только он, все остальные моды включены как были.
    /// Работает — виновник найден; падает — дело не в нём (библиотека, загрузчик, сама игра или сочетание модов).
    /// </summary>
    static State ApplyVerify(GameState g, State s, string candidate)
    {
        var registry = g.Registry!;
        var off = Group(s, candidate).ToHashSet(StringComparer.Ordinal);
        foreach (var id in s.Before)
            try { registry.SetEnabled(id, !off.Contains(id)); } catch { }
        var next = s with { Suspects = [candidate], Testing = s.Before.Where(id => !off.Contains(id)).ToList(), Verify = candidate, Applied = DateTime.UtcNow };
        Save(g.Def.Id, next);
        return next;
    }

    /// <summary>Ответ после запуска. Вернёт виновника, «нет одного виновника» (Done без Culprit) или следующий шаг.</summary>
    public static (bool Done, string? Culprit, State? Next) Answer(GameState g, bool crashed)
    {
        if (Get(g.Def.Id) is not { } s) return (true, null, null);
        if (s.Verify is { } candidate)
        {
            // Без подозреваемого игра работает — это он; падает и без него — одного виновника нет.
            var culprit = crashed ? null : candidate;
            Finish(g, s, culprit);
            return (true, culprit, null);
        }
        // Упала с включённой половиной — виновник в ней; работает — в выключенной.
        var left = crashed ? s.Testing : s.Suspects.Except(s.Testing).ToList();
        if (left.Count == 0)
        {
            Finish(g, s, null);
            return (true, null, null);
        }
        var round = s with { Suspects = left, Round = s.Round + 1 };
        return (false, null, left.Count == 1 ? ApplyVerify(g, round, left[0]) : Apply(g, round));
    }

    /// <summary>Вернуть моды как были; виновника (со всеми его папками) — выключить.</summary>
    static void Finish(GameState g, State s, string? culprit)
    {
        var registry = g.Registry;
        var off = culprit is null ? [] : Group(s, culprit).ToHashSet(StringComparer.Ordinal);
        if (registry is not null)
            foreach (var id in s.Before)
                try { registry.SetEnabled(id, !off.Contains(id)); } catch { }
        Clear(g.Def.Id);
    }

    /// <summary>Остановить поиск и вернуть моды. Пока игра запущена — нельзя (её файлы заняты).</summary>
    public static void Cancel(GameState g)
    {
        if (Launcher.IsRunning(g.Def.Id)) throw new InvalidOperationException(I18n.T("vanilla.running"));
        if (Get(g.Def.Id) is { } s) Finish(g, s, null);
        else Clear(g.Def.Id);
    }

    [SelfTest]
    static string FindsCulpritInFewRuns()
    {
        // Чистая логика деления: 13 модов, виновник — «m9».
        var suspects = Enumerable.Range(0, 13).Select(i => "m" + i).ToList();
        var runs = 0;
        while (suspects.Count > 1)
        {
            var testing = suspects.Take(suspects.Count / 2).ToList();
            var crashed = testing.Contains("m9");
            suspects = crashed ? testing : suspects.Except(testing).ToList();
            runs++;
        }
        if (suspects.Single() != "m9" || runs > 4) throw new Exception($"found {suspects.Single()} in {runs}");
        // Число запусков, которое обещаем, — с проверочным: 13 → 4 + 1.
        if (Runs(13) != runs + 1 || Runs(1) != 0 || Runs(2) != 2) throw new Exception($"runs promised {Runs(13)}");
        var s = new State(["a", "a#cp", "b"], ["a", "b"], ["a"], 0, Applied: new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        if (string.Join(",", Group(s, "a")) != "a,a#cp" || Counts(s, new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc))) throw new Exception("groups or run time wrong");
        return $"13 mods → culprit in {runs} runs + 1 check; second folders follow their mod; runs before a step are ignored";
    }
}
