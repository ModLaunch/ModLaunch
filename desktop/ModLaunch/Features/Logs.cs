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
        _ => Path.Combine(gamePath, "BepInEx", "LogOutput.log"),
    };

    [GeneratedRegex(@"\[(Error|Fatal)\s*:\s*([^\]]+)\]\s*(.+)", RegexOptions.IgnoreCase)] private static partial Regex BepInExLine();
    [GeneratedRegex(@"Skipped mods\s*[\s\S]*?(?=\n\[|\n\n\n|$)", RegexOptions.IgnoreCase)] private static partial Regex SmapiSkipped();
    [GeneratedRegex(@"^\s*-\s+(.+?)\s+because\s+(.+)$", RegexOptions.IgnoreCase)] private static partial Regex SmapiBecause();
    [GeneratedRegex(@"\[\s*ERROR\s+([^\]]+)\]\s*(.+)")] private static partial Regex SmapiError();
    [GeneratedRegex(@"\[ERROR\]:?\s*\[([^\]]+)\]\s*-?\s*(.+)")] private static partial Regex HkError();

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
                if (SmapiSkipped().Match(text) is { Success: true } skipped)
                    foreach (var line in skipped.Value.Split('\n'))
                        if (SmapiBecause().Match(line) is { Success: true } m) Add(m.Groups[1].Value, m.Groups[2].Value);
                foreach (Match m in SmapiError().Matches(text))
                    if (!m.Groups[1].Value.Trim().Equals("SMAPI", StringComparison.OrdinalIgnoreCase)) Add(m.Groups[1].Value, m.Groups[2].Value);
                break;
            case LoaderKind.HkApi:
                foreach (Match m in HkError().Matches(text)) Add(m.Groups[1].Value, m.Groups[2].Value);
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
