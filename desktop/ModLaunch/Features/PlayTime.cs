using System.Text.Json.Nodes;
using ModLaunch.Core;

namespace ModLaunch.Features;

public sealed record Played(long TotalMs, int Sessions, DateTime? LastPlayed, bool Running);

/// <summary>Игровое время: playtime.json, как в 3.x. Сеансы короче 15 секунд не считаются.</summary>
public static class PlayTime
{
    static readonly JsonFile File = new(Path.Combine(Paths.DataDir, "playtime.json"), () => new JsonObject { ["games"] = new JsonObject() });
    static readonly Dictionary<string, DateTime> RunningSince = [];

    public static bool Track => Settings.Data.Bool("trackPlaytime", true);

    static JsonObject Game(string id)
    {
        var games = File.Data.Obj("games");
        if (games[id] is JsonObject g) return g;
        g = new JsonObject { ["totalMs"] = 0, ["sessions"] = 0, ["lastPlayed"] = null, ["lastSessionMs"] = 0, ["longestMs"] = 0 };
        games[id] = g;
        return g;
    }

    public static bool IsRunning(string id) => RunningSince.ContainsKey(id);

    public static void Start(string id)
    {
        RunningSince[id] = DateTime.UtcNow;
        Game(id)["lastPlayed"] = DateTime.UtcNow.ToString("o");
        File.Save();
    }

    public static (bool Counted, long Ms) Stop(string id)
    {
        if (!RunningSince.Remove(id, out var since)) return (false, 0);
        var ms = (long)(DateTime.UtcNow - since).TotalMilliseconds;
        if (ms < 15_000 || ms > 86_400_000) return (false, ms);
        var g = Game(id);
        g["totalMs"] = g.Long("totalMs") + ms;
        g["sessions"] = g.Long("sessions") + 1;
        g["lastSessionMs"] = ms;
        g["longestMs"] = Math.Max(g.Long("longestMs"), ms);
        File.Save();
        return (true, ms);
    }

    public static Played Get(string id)
    {
        var g = File.Data.Obj("games")[id];
        return new Played(g.Long("totalMs"), (int)g.Long("sessions"),
            DateTime.TryParse(g.Str("lastPlayed"), out var d) ? d.ToUniversalTime() : null, IsRunning(id));
    }

    public static void Reset(string? id)
    {
        if (id is null) File.Data["games"] = new JsonObject(); else File.Data.Obj("games").Remove(id);
        File.Save();
    }

    public static string Format(long ms)
    {
        var minutes = ms / 60000;
        if (minutes < 1) return I18n.T("time.lessMinute");
        if (minutes < 60) return I18n.T("time.m", ("m", minutes));
        var h = minutes / 60;
        var m = minutes % 60;
        return m == 0 ? I18n.T("time.h", ("h", h)) : I18n.T("time.hm", ("h", h), ("m", m));
    }
}
