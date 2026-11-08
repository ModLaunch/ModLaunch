using System.Text.RegularExpressions;
using ModLaunch.Core;

namespace ModLaunch.Features;

/// <summary>
/// 25.0: «Игра обновилась». После патча игры старые моды чаще всего и ломаются (план, этап 1, п. 3), поэтому
/// ModLaunch помнит номер сборки Steam (buildid в steamapps/appmanifest_*.acf) и при запуске замечает,
/// что игра обновилась: сразу ищет обновления модов этой игры и говорит об этом.
/// </summary>
public static partial class GamePatch
{
    [GeneratedRegex("\"buildid\"\\s+\"(\\d+)\"", RegexOptions.IgnoreCase)] private static partial Regex BuildRe();

    /// <summary>Номер сборки из текста appmanifest_*.acf.</summary>
    public static string? ParseBuild(string acf) => BuildRe().Match(acf) is { Success: true } m ? m.Groups[1].Value : null;

    /// <summary>appmanifest игры: папка игры — …/steamapps/common/&lt;Игра&gt;, манифест — в …/steamapps.</summary>
    public static string? Manifest(GameState g)
    {
        if (g.Def.SteamAppId <= 0 || g.Path is null) return null;
        try
        {
            var steamapps = new DirectoryInfo(g.Path).Parent?.Parent;
            if (steamapps is null || !steamapps.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase)) return null;
            var file = Path.Combine(steamapps.FullName, $"appmanifest_{g.Def.SteamAppId}.acf");
            return File.Exists(file) ? file : null;
        }
        catch { return null; }
    }

    public static string? Build(GameState g)
    {
        try { return Manifest(g) is { } f ? ParseBuild(File.ReadAllText(f)) : null; }
        catch { return null; }
    }

    /// <summary>
    /// Игры с модами, которые обновились с прошлого запуска ModLaunch. Номер сборки такой игры запоминается
    /// только после проверки модов (Commit) — не вышло проверить (нет сети), предупредим и в следующий раз.
    /// </summary>
    public static List<GameState> Changed()
    {
        var store = Settings.Data.Obj("gameBuilds");
        var list = new List<GameState>();
        foreach (var g in AppState.Games.Where(g => g.Status == Detect.Found && !g.Def.IsMinecraft))
        {
            if (Build(g) is not { } build) continue;
            var old = store.Str(g.Def.Id);
            if (old is not null && old != build && g.ModCount > 0) { list.Add(g); continue; }
            store[g.Def.Id] = build;
        }
        Settings.Save();
        return list;
    }

    /// <summary>Моды обновившейся игры проверены — запомнить её новую сборку.</summary>
    public static void Commit(GameState g)
    {
        if (Build(g) is not { } build) return;
        Settings.Data.Obj("gameBuilds")[g.Def.Id] = build;
        Settings.Save();
    }

    [SelfTest]
    static string ReadsSteamBuild()
    {
        const string acf = "\"AppState\"\n{\n\t\"appid\"\t\t\"413150\"\n\t\"name\"\t\t\"Stardew Valley\"\n\t\"buildid\"\t\t\"16091214\"\n\t\"LastOwner\"\t\t\"0\"\n}\n";
        if (ParseBuild(acf) != "16091214" || ParseBuild("\"appid\" \"1\"") is not null) throw new Exception("buildid not read");
        return "buildid 16091214 read from appmanifest";
    }
}
