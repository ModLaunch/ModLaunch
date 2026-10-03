using System.Text;
using ModLaunch.Core;

namespace ModLaunch.Features;

/// <summary>Отчёт для поддержки: версия программы, игра, моды, найденные проблемы и хвост лога — одним текстом, который можно вставить в чат.</summary>
public static class Report
{
    public static string Build(GameState g)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"ModLaunch {Http.Version} · {Environment.OSVersion.VersionString} · {(Offline.On ? "offline" : "online")}");
        sb.AppendLine($"Game: {g.Def.Name} ({g.Def.Id}) · loader {g.Def.LoaderName} · {g.Status}");
        sb.AppendLine($"Path: {g.Path}");

        if (g.Registry is { } registry)
        {
            var mods = registry.List();
            sb.AppendLine().AppendLine($"Mods ({mods.Count}):");
            foreach (var m in mods)
                sb.AppendLine($"- {m.Str("id")} {m.Str("version")}{(m.Bool("enabled", true) ? "" : " [off]")}{(m.Bool("missing") ? " [missing]" : "")}");

            var issues = Health.Check(g);
            sb.AppendLine().AppendLine($"Health issues ({issues.Count}):");
            foreach (var i in issues) sb.AppendLine($"- [{i.Level}] {i.Title}");

            if (g.Path is not null && Logs.PathFor(g.Def, g.Path) is { } log && File.Exists(log))
            {
                sb.AppendLine().AppendLine("Log tail:");
                try { foreach (var line in ReadTail(log, 60)) sb.AppendLine(line); } catch { sb.AppendLine("(log is locked)"); }
            }
        }
        return Redact(sb.ToString());
    }

    /// <summary>Последние строки файла, даже если игра ещё держит его открытым.</summary>
    static IEnumerable<string> ReadTail(string path, int count)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var queue = new Queue<string>();
        while (reader.ReadLine() is { } line)
        {
            queue.Enqueue(line);
            if (queue.Count > count) queue.Dequeue();
        }
        return queue;
    }

    /// <summary>Имя пользователя Windows в путях заменяем — отчёт не должен раскрывать лишнего.</summary>
    public static string Redact(string text)
    {
        var user = Environment.UserName;
        return string.IsNullOrEmpty(user) ? text : text.Replace(user, "<user>", StringComparison.OrdinalIgnoreCase);
    }

    [SelfTest]
    static string HidesUserName()
    {
        var text = Redact($@"C:\Users\{Environment.UserName}\Games");
        if (text.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase) && Environment.UserName.Length > 0) throw new Exception("user name leaked: " + text);
        return "user name replaced in paths";
    }
}
