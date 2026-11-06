using System.Text.RegularExpressions;
using ModLaunch.Games;

namespace ModLaunch.Features;

public sealed record LogIssue(string Mod, string Message);
public sealed record LogReport(bool Available, string? Path, DateTime? Modified, List<LogIssue> Issues);

/// <summary>Лог загрузчика и моды, на которые он ругается (BepInEx, SMAPI, Modding API).</summary>
public static partial class Logs
{
    static string Roaming => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string? PathFor(GameDef game, string gamePath) => game.Loader switch
    {
        LoaderKind.Smapi => Path.Combine(Roaming, "StardewValley", "ErrorLogs", "SMAPI-latest.txt"),
        LoaderKind.HkApi => Path.Combine(Home, "AppData", "LocalLow", "Team Cherry", "Hollow Knight", "ModLog.txt"),
        LoaderKind.Minecraft => Path.Combine(Minecraft.Mc.Active?.Dir ?? gamePath, "logs", "latest.log"),
        _ => Path.Combine(gamePath, "BepInEx", "LogOutput.log"),
    };

    [GeneratedRegex(@"\[(Error|Fatal)\s*:\s*([^\]]+)\]\s*(.+)", RegexOptions.IgnoreCase)] private static partial Regex BepInExLine();
    [GeneratedRegex(@"\[ERROR\]:?\s*\[([^\]]+)\]\s*-?\s*(.+)")] private static partial Regex HkError();
    /// <summary>Minecraft: «[12:00:01] [main/ERROR] (Fabric Loader) …» или «[Render thread/ERROR]: …».</summary>
    [GeneratedRegex(@"\[([^\]/]+)/(?:ERROR|FATAL)\]:?\s*(?:\(([^)]+)\))?:?\s*(.+)")] private static partial Regex McError();

    public static List<LogIssue> Parse(GameDef game, string text)
    {
        var issues = new List<LogIssue>();
        void Add(string mod, string message)
        {
            mod = mod.Trim();
            if (issues.All(i => i.Mod != mod)) issues.Add(new LogIssue(mod, message.Trim()));
        }
        switch (game.Loader)
        {
            case LoaderKind.Smapi:
                // 24.0: строки настоящего SMAPI-latest.txt начинаются с метки времени «[19:59:49 ERROR SMAPI]» —
                // прежний разбор их не видел. Теперь — тот же разбор, что у «Почему игра упала».
                foreach (var c in CrashDoctor.Scan(LoaderKind.Smapi, text).Where(c => c.Mod != ""))
                    Add(c.Mod, c.Detail ?? c.Kind);
                break;
            case LoaderKind.HkApi:
                foreach (Match m in HkError().Matches(text)) Add(m.Groups[1].Value, m.Groups[2].Value);
                break;
            case LoaderKind.Minecraft:
                foreach (Match m in McError().Matches(text)) Add(m.Groups[2].Success ? m.Groups[2].Value : m.Groups[1].Value, m.Groups[3].Value);
                break;
            default:
                foreach (Match m in BepInExLine().Matches(text))
                    if (!m.Groups[2].Value.Trim().StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase)) Add(m.Groups[2].Value, m.Groups[3].Value);
                break;
        }
        return issues.Take(50).ToList();
    }

    /// <summary>Последние строки лога (файл может быть открыт игрой — читаем с общим доступом).</summary>
    public static List<string> Tail(string path, int maxLines)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            const int max = 1024 * 1024;
            if (fs.Length > max) fs.Position = fs.Length - max;
            var lines = new StreamReader(fs).ReadToEnd().Replace("\r", "").Split('\n');
            return lines.TakeLast(maxLines).ToList();
        }
        catch { return []; }
    }

    public static LogReport Read(GameDef game, string gamePath)
    {
        var path = PathFor(game, gamePath);
        if (path is null || !File.Exists(path)) return new LogReport(false, path, null, []);
        try
        {
            string text;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                const int max = 512 * 1024;
                if (fs.Length > max) fs.Position = fs.Length - max;
                text = new StreamReader(fs).ReadToEnd();
            }
            return new LogReport(true, path, File.GetLastWriteTime(path), Parse(game, text));
        }
        catch { return new LogReport(false, path, null, []); }
    }
}
