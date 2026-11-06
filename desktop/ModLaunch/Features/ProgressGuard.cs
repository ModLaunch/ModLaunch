using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Creator;
using MX = ModScriptX;

namespace ModLaunch.Features;

/// <summary>
/// 23.0: защита прогресса. Когда в игре стоят моды, сделанные в Creator Hub (ModScript), ModLaunch перед игрой
/// кладёт копию сохранений в бэкапы, а после игры спрашивает: оставить новый прогресс или вернуть как было.
/// Если среди этих модов есть читы, советует вернуть: игра с читами остаётся в копии и не попадает
/// в настоящий прогресс. Так Creator Hub — мастерская, а не читерская программа: в играх с античитом
/// моды не ставятся вовсе (InGame.Apply, XStudio), а «нечестный» прогресс не смешивается с настоящим.
/// </summary>
public static class ProgressGuard
{
    /// <summary>Включено ли (Настройки → Запуск), по умолчанию да.</summary>
    public static bool Enabled => Settings.Data.Bool("protectProgress", true);

    static JsonObject Marks => Settings.Data.Obj("progressGuard");

    public sealed record Session(string GameId, string Backup, DateTime At, bool Cheats);

    /// <summary>Стоят ли в папке игры моды, поставленные из Creator Hub.</summary>
    public static bool HasStudioMods(string gamePath)
    {
        try { return MX.Injector.Installed(gamePath).Any(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Есть ли среди модов этой игры из Creator Hub читы (вид проекта «Читы»), стоящие в игре.</summary>
    static bool Cheats(GameState g)
    {
        if (g.Path is null) return false;
        foreach (var p in XProjects.List().Where(p => p.Game == g.Def.Id && p.Kind == "cheats" && p.Main.EndsWith(".modx", StringComparison.OrdinalIgnoreCase)))
            try { if (MX.Injector.InstalledAt(MX.ModxDoc.Parse(File.ReadAllText(p.Main)).Id, g.Path) is not null) return true; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return false;
    }

    /// <summary>Начать защиту: копия сохранений «до» (один раз за сеанс игры). Возвращает, начата ли она.</summary>
    public static bool Begin(GameState g, bool force = false)
    {
        if (!Enabled || g.Path is null || Current(g.Def.Id) is not null) return false;
        if (!force && !HasStudioMods(g.Path)) return false;
        var saves = Launcher.SavesDir(g.Def, g.Path);
        Backup? made;
        try { made = Backups.Create(g.Def.Id, saves, "guard", Backups.Keep + 2); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { return false; }
        if (made is null) return false;
        Marks[g.Def.Id] = new JsonObject { ["backup"] = made.Name, ["at"] = DateTime.UtcNow.ToString("o"), ["cheats"] = Cheats(g) };
        Settings.Save();
        return true;
    }

    /// <summary>Идёт ли защита для игры.</summary>
    public static Session? Current(string gameId)
    {
        if (Marks[gameId] is not JsonObject o || o.Str("backup") is not { } backup) return null;
        var at = DateTime.TryParse(o.Str("at"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t.ToUniversalTime() : DateTime.UtcNow;
        return new Session(gameId, backup, at, o.Bool("cheats"));
    }

    /// <summary>Отметить, что в этом сеансе появились читы (мод с читами поставили прямо из игры).</summary>
    public static void NoteCheats(string gameId)
    {
        if (Marks[gameId] is JsonObject o) { o["cheats"] = true; Settings.Save(); }
    }

    /// <summary>Закончить: keep — оставить прогресс; иначе вернуть сохранения из копии «до».</summary>
    public static string? Finish(GameState g, bool keep)
    {
        var s = Current(g.Def.Id);
        Marks.Remove(g.Def.Id);
        Settings.Save();
        if (s is null || keep || g.Path is null) return null;
        var saves = Launcher.SavesDir(g.Def, g.Path) ?? throw new InvalidOperationException(I18n.T("err.BACKUP_NO_SAVES"));
        return Backups.Restore(g.Def.Id, s.Backup, saves);
    }

    [SelfTest]
    static string GuardRestoresProgress()
    {
        // Своя «игра» для теста: настоящие бэкапы игр не трогаем (Create чистит старые копии сверх лимита).
        const string id = "selftest-guard";
        var saved = Marks[id]?.DeepClone();
        var dir = Path.Combine(Paths.DataDir, "guard-test");
        var saves = Directory.CreateDirectory(Path.Combine(dir, "saves")).FullName;
        try
        {
            File.WriteAllText(Path.Combine(saves, "slot1.sav"), "honest");
            var made = Backups.Create(id, saves, "guard") ?? throw new Exception("no backup");
            Marks[id] = new JsonObject { ["backup"] = made.Name, ["at"] = DateTime.UtcNow.ToString("o"), ["cheats"] = true };
            if (Current(id) is not { Cheats: true }) throw new Exception("session not remembered");
            File.WriteAllText(Path.Combine(saves, "slot1.sav"), "with cheats");
            Backups.Restore(id, Current(id)!.Backup, saves);
            Marks.Remove(id);
            if (File.ReadAllText(Path.Combine(saves, "slot1.sav")) != "honest") throw new Exception("progress not restored");
            if (Current(id) is not null) throw new Exception("session not finished");
            return "copy before play; cheat progress replaced by the honest save on request";
        }
        finally
        {
            if (saved is not null) Marks[id] = saved; else Marks.Remove(id);
            try { Directory.Delete(dir, true); } catch (IOException) { }
            try { Directory.Delete(Backups.FolderFor(id), true); } catch (IOException) { }
        }
    }
}
