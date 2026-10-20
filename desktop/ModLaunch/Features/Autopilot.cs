using ModLaunch.Core;

namespace ModLaunch.Features;

/// <summary>Шаг автопилота: что не так, сколько отнимает от оценки сборки и можно ли починить самому.</summary>
public sealed record PilotStep(string Kind, HealthLevel Level, string Title, string Text, int Penalty, bool Auto, List<string> Mods, bool Optimize);

/// <summary>
/// 10.0: «умный автопилот» сборки. Берёт всё, что находит «Проверка игры» (зависимости, выключенные
/// библиотеки, дубли, пропавшие файлы, ошибки в логе, вылеты), добавляет оптимизацию (лишние
/// библиотеки, моды, поставленные вручную) и ставит сборке оценку из 100. Безопасные шаги
/// (поставить недостающее, включить нужное, выключить дубли и ненужные библиотеки, забыть пропавшее,
/// принять моды из папки) он делает сам — перед этим снимок «как было», так что всё возвращается
/// одной кнопкой. Рискованное (выключить мод из лога вылета, обновить моды) — только предлагает.
/// </summary>
public static class Autopilot
{
    /// <summary>Чинить сборку перед каждым запуском (Настройки → Запуск), по умолчанию выключено.</summary>
    public static bool BeforePlay => Settings.Data.Bool("autopilotBeforePlay");

    static readonly Dictionary<string, (int Penalty, bool Auto, bool Optimize)> Rules = new()
    {
        ["loader"] = (40, true, false),
        ["write"] = (30, false, false),
        ["deps"] = (25, true, false),
        ["depsOff"] = (20, true, false),
        ["conflicts"] = (12, true, true),
        ["log"] = (10, false, false),
        ["files"] = (8, true, false),
        ["crash"] = (6, false, false),
        ["disk"] = (6, false, false),
        ["updates"] = (4, false, true),
        ["unused"] = (3, true, true),
        ["unmanaged"] = (2, true, true),
    };

    public static (int Score, List<PilotStep> Steps) Plan(GameState g, bool quick = false)
    {
        var steps = new List<PilotStep>();
        foreach (var i in Health.Check(g, quick))
        {
            var (penalty, auto, optimize) = Rules.TryGetValue(i.Kind, out var r) ? r : (3, false, false);
            // Дубли без «старшей копии» не выключить вслепую — только показать.
            if (i.Kind == "conflicts" && i.Mods.Count == 0) auto = false;
            steps.Add(new PilotStep(i.Kind, i.Level, i.Title, i.Text, penalty, auto, i.Mods, optimize));
        }
        return (Score(steps), steps.OrderByDescending(s => s.Penalty).ToList());
    }

    public static int Score(IEnumerable<PilotStep> steps) => Math.Clamp(100 - steps.Sum(s => s.Penalty), 0, 100);

    /// <summary>Слово к оценке: 90+ — отлично, 70+ — хорошо, 40+ — есть проблемы, ниже — нужен ремонт.</summary>
    public static string Grade(int score) => score >= 90 ? "great" : score >= 70 ? "good" : score >= 40 ? "issues" : "broken";

    /// <summary>
    /// Быстрая починка перед запуском — только то, что не требует сети и не может навредить:
    /// включить выключенные зависимости, выключить старшие копии дублей, забыть пропавшие моды.
    /// Возвращает, сколько модов поправлено.
    /// </summary>
    public static int QuickFix(GameState g)
    {
        if (g.Registry is not { } registry) return 0;
        var steps = Plan(g, quick: true).Steps.Where(s => s.Kind is "depsOff" or "conflicts" or "files" && s.Auto).ToList();
        if (steps.Count == 0) return 0;
        Snapshots.Take(g, "autopilot");
        var n = 0;
        foreach (var s in steps)
            foreach (var id in s.Mods)
                try
                {
                    switch (s.Kind)
                    {
                        case "depsOff": registry.SetEnabled(id, true); n++; break;
                        case "conflicts": registry.SetEnabled(id, false); n++; break;
                        case "files":
                            registry.Reconcile();
                            if (registry.Get(id) is { } mod && mod.Bool("missing")) { registry.Remove(id); n++; }
                            break;
                    }
                }
                catch { }
        return n;
    }

    [SelfTest]
    static string AutopilotScores()
    {
        var bad = new List<PilotStep>
        {
            new("loader", HealthLevel.Critical, "", "", 40, true, [], false),
            new("deps", HealthLevel.Critical, "", "", 25, true, [], false),
            new("depsOff", HealthLevel.Critical, "", "", 20, true, [], false),
            new("log", HealthLevel.Warning, "", "", 30, false, [], false),
        };
        if (Score(bad) != 0 || Grade(Score(bad)) != "broken") throw new Exception("score below zero");
        if (Score([]) != 100 || Grade(100) != "great") throw new Exception("clean build is not 100");
        if (Grade(75) != "good" || Grade(50) != "issues") throw new Exception("grades");
        return "score 0…100 with four grades";
    }
}
