using System.Text;

namespace ModLaunch.Games;

/// <summary>
/// 30.3: ярлык игры на рабочем столе для любой игры из «Всех игр» (Steam умеет так только для своих):
/// файл .url со ссылкой modlaunch://play/&lt;ключ&gt; и значком самой игры. Двойной щелчок — ModLaunch запускает игру
/// через её лаунчер (или с модами, если это игра ModLaunch), с учётом времени, оверлеем и статусом для друзей.
/// </summary>
public static class GameLinks
{
    const string Prefix = "modlaunch://play/";

    public static string Link(LibGame g) => Prefix + Uri.EscapeDataString(g.Key);

    /// <summary>Ключ игры из ссылки modlaunch://play/…; не такая ссылка — null.</summary>
    public static string? KeyOf(string link)
    {
        if (!link.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var key = Uri.UnescapeDataString(link[Prefix.Length..].TrimEnd('/'));
        return key.Length is > 0 and <= 200 ? key : null;
    }

    /// <summary>Имя файла из названия игры: без запрещённых символов, не длиннее 80.</summary>
    internal static string SafeName(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var s = new string(name.Select(c => bad.Contains(c) ? ' ' : c).ToArray());
        s = string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim('.', ' ');
        if (s.Length > 80) s = s[..80].TrimEnd('.', ' ');
        return s == "" ? "Game" : s;
    }

    /// <summary>Значок: exe игры из лаунчера, иначе самый крупный exe в её папке (без установщиков), иначе ModLaunch.</summary>
    internal static string IconOf(LibGame g)
    {
        if (g.Source?.Exe is { } exe && File.Exists(exe)) return exe;
        var dir = g.Source?.AltDir ?? g.Dir;
        if (dir is not null && Directory.Exists(dir))
        {
            try
            {
                var best = new DirectoryInfo(dir).EnumerateFiles("*.exe")
                    .Where(f => !System.Text.RegularExpressions.Regex.IsMatch(f.Name, "unins|crash|setup|redist|report|helper|update", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    .OrderByDescending(f => f.Length).FirstOrDefault();
                if (best is not null) return best.FullName;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return Environment.ProcessPath ?? "";
    }

    internal static string Content(LibGame g, string icon) =>
        "[InternetShortcut]\r\nURL=" + Link(g) + "\r\nIconFile=" + icon + "\r\nIconIndex=0\r\n";

    /// <summary>Положить ярлык на рабочий стол. Чужой файл с тем же именем не трогаем — назовём «… (ModLaunch)».</summary>
    public static string CreateDesktop(LibGame g)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var path = Path.Combine(desktop, SafeName(g.Name) + ".url");
        if (File.Exists(path) && !File.ReadAllText(path).Contains(Prefix, StringComparison.OrdinalIgnoreCase))
            path = Path.Combine(desktop, SafeName(g.Name) + " (ModLaunch).url");
        // UTF-16 с меткой: Windows читает .url как INI, и так путь к значку с кириллицей не ломается.
        File.WriteAllText(path, Content(g, IconOf(g)), Encoding.Unicode);
        return path;
    }

    [SelfTest]
    static string GameLinksRoundTrip()
    {
        var g = new LibGame("epic:Fortnite:Live/1", "epic", "Fortnite: Battle/Royale?", null, 0, true, 0);
        if (KeyOf(Link(g)) != g.Key) throw new Exception("round trip " + Link(g));
        if (KeyOf("modlaunch://mod/abc") is not null || KeyOf("modlaunch://play/") is not null) throw new Exception("other links");
        if (SafeName(g.Name) != "Fortnite Battle Royale") throw new Exception("file name " + SafeName(g.Name));
        if (!Content(g, @"C:\Игры\f.exe").Contains("URL=modlaunch://play/epic%3AFortnite%3ALive%2F1")) throw new Exception("content");
        return "modlaunch://play/<key> round trip, safe file name, .url content";
    }
}
