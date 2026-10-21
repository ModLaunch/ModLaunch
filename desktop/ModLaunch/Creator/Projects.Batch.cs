namespace ModLaunch.Creator;

public static partial class Projects
{
    /// <summary>
    /// 12.0: один проход по проектам на одну постройку экрана. Главная Creator Hub, «Студия автора» и «Мои»
    /// звали <see cref="List"/> каждая сама — и каждый раз читали все main.mls с диска. Внутри
    /// <c>using (Projects.Batch())</c> список читается один раз; вне — как раньше, всегда свежий.
    /// Создавать и удалять проекты внутри прохода нельзя: он их не увидит.
    /// </summary>
    public static IDisposable Batch() => new Scope();

    /// <summary>Сколько раз проходы читали список с диска (для проверки).</summary>
    internal static int BatchReads;

    // Только свой поток: постройка экрана идёт в потоке интерфейса, фоновые задачи читают диск как раньше.
    [ThreadStatic] static int _depth;
    [ThreadStatic] static List<Project>? _cached;

    /// <summary>Список текущего прохода (копия, чтобы его не испортили) или null — прохода нет.</summary>
    static List<Project>? FromBatch()
    {
        if (_depth == 0) return null;
        if (_cached is null)
        {
            // Читаем «по-настоящему»: на время чтения проход выключен.
            var depth = _depth;
            _depth = 0;
            try { _cached = List(); BatchReads++; }
            finally { _depth = depth; }
        }
        return [.. _cached];
    }

    sealed class Scope : IDisposable
    {
        bool _done;
        public Scope() => _depth++;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            if (--_depth <= 0) { _depth = 0; _cached = null; }
        }
    }

    [SelfTest]
    static string ProjectsBatchReadsOnce()
    {
        var p = Create("Batch test", "mod \"Batch test\"\ngame valheim\n");
        try
        {
            var before = BatchReads;
            using (Batch())
            {
                List();
                using (Batch()) { List(); Get(p.Id); }
                if (List().All(x => x.Id != p.Id)) throw new Exception("project missing in batch");
            }
            if (BatchReads - before != 1) throw new Exception($"read {BatchReads - before} times instead of 1");
            // Вне прохода — снова с диска: новый проект виден сразу.
            var q = Create("Batch test 2", "mod \"Batch test 2\"\ngame valheim\n");
            var seen = List().Any(x => x.Id == q.Id);
            Delete(q);
            if (!seen) throw new Exception("stale list outside batch");
            return "one disk read per screen build";
        }
        finally { Delete(p); }
    }
}
