using ModLaunch.Core;
using ModLaunch.Games;

namespace ModLaunch.Features;

/// <summary>
/// 23.9: игра обновилась (в Steam сменился номер сборки) — старые моды после патча часто ломаются.
/// Запоминаем номер сборки каждой игры; сменился — сразу проверяем обновления её модов и говорим об этом.
/// </summary>
public static class GamePatch
{
    /// <summary>Игры, обновившиеся с прошлого запуска ModLaunch: когда заметили.</summary>
    public static readonly Dictionary<string, DateTime> Patched = [];

    static DateTime _lastCheck = DateTime.UtcNow;

    /// <summary>Номер сборки игры в Steam (appmanifest_*.acf → buildid) или null.</summary>
    public static string? BuildId(GameDef def, List<string>? libraries = null)
    {
        var app = def.SteamAppId;
        if (app <= 0) return null;
        foreach (var library in libraries ?? Locator.SteamLibraries())
        {
            try
            {
                var file = Path.Combine(library, "steamapps", $"appmanifest_{app}.acf");
                if (!File.Exists(file)) continue;
                return Vdf.Get(Vdf.Parse(File.ReadAllText(file)), "AppState", "buildid") as string;
            }
            catch { }
        }
        return null;
    }

    /// <summary>Сверить номера сборок. true — игра обновилась с прошлого раза (первый раз — просто запоминаем).</summary>
    public static bool Changed(GameState g, List<string> libraries)
    {
        var now = BuildId(g.Def, libraries);
        if (now is null) return false;
        var builds = Settings.Data.Obj("gameBuilds");
        var was = builds.Str(g.Def.Id);
        if (was == now) return false;
        builds[g.Def.Id] = now;
        Settings.Save();
        if (was is null) return false;
        Patched[g.Def.Id] = DateTime.UtcNow;
        return true;
    }

    /// <summary>Программа долго открыта — раз в 6 часов снова проверить обновления (окно стало активным).</summary>
    public static bool Due()
    {
        if (DateTime.UtcNow - _lastCheck < TimeSpan.FromHours(6)) return false;
        _lastCheck = DateTime.UtcNow;
        return true;
    }

    [SelfTest]
    static string NoticesSteamPatch()
    {
        var root = Path.Combine(Paths.DataDir, "patch-test");
        var apps = Path.Combine(root, "steamapps");
        Directory.CreateDirectory(apps);
        var def = GameCatalog.ById("lethal-company")!;
        var g = new GameState { Def = def };
        var builds = Settings.Data.Obj("gameBuilds");
        var had = builds.Str(def.Id);
        void Write(string id) => File.WriteAllText(Path.Combine(apps, $"appmanifest_{def.SteamAppId}.acf"),
            "\"AppState\"\n{\n\t\"appid\"\t\t\"" + def.SteamAppId + "\"\n\t\"buildid\"\t\t\"" + id + "\"\n\t\"installdir\"\t\t\"Lethal Company\"\n}\n");
        try
        {
            builds.Remove(def.Id);
            Write("15001");
            if (Changed(g, [root])) throw new Exception("first look counted as a patch");
            if (Changed(g, [root])) throw new Exception("same build counted as a patch");
            Write("15877");
            if (!Changed(g, [root]) || !Patched.ContainsKey(def.Id)) throw new Exception("new build not noticed");
            return "Steam build 15001 → 15877 noticed as a game patch";
        }
        finally
        {
            Patched.Remove(def.Id);
            if (had is null) builds.Remove(def.Id); else builds[def.Id] = had;
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
