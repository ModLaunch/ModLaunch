using System.Globalization;
using System.Text;
using ModLaunch.Core;

namespace ModLaunch.Games;

/// <summary>
/// 30.4: библиотека таблицей — все игры из всех лаунчеров с часами, последним запуском, размером, статусом,
/// избранным, коллекциями и заметками. CSV с «;» и меткой UTF-8: Excel открывает его по столбцам и с кириллицей.
/// </summary>
public static class LibraryExport
{
    /// <summary>Поле CSV: в кавычках, если есть разделитель, кавычка или перенос строки.</summary>
    internal static string Field(string value)
    {
        value ??= "";
        // Формулы в ячейках (=, +, -, @) Excel выполнил бы — ставим апостроф, как советует OWASP.
        if (value.Length > 0 && "=+-@".Contains(value[0])) value = "'" + value;
        return value.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    public static string Csv(IEnumerable<LibGame> games, Shelves shelves, Func<LibGame, (long Last, long Minutes)> played)
    {
        var sb = new StringBuilder();
        string[] head = ["v30.csv.name", "v30.csv.store", "v30.csv.installed", "v30.csv.hours", "v30.csv.last", "v30.csv.size", "v30.csv.status", "v30.csv.fav", "v30.csv.collections", "v30.csv.notes"];
        sb.AppendLine(string.Join(';', head.Select(k => Field(I18n.T(k)))));
        foreach (var g in games.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var (last, minutes) = played(g);
            var status = shelves.StatusOf(g.Key);
            string[] row =
            [
                g.Name, Stores.Title(g.Store), g.Installed ? "1" : "0",
                (minutes / 60.0).ToString("0.#", CultureInfo.InvariantCulture),
                last > 0 ? DateTimeOffset.FromUnixTimeSeconds(last).LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "",
                g.Size > 0 ? (g.Size / (double)(1L << 30)).ToString("0.##", CultureInfo.InvariantCulture) : "",
                status == "" ? "" : I18n.T("v30.status." + status),
                shelves.IsFavorite(g.Key) ? "1" : "0",
                string.Join(", ", shelves.Collections.Where(c => shelves.Contains(c, g.Key))),
                shelves.Note(g).Replace("\r\n", "\n"),
            ];
            sb.AppendLine(string.Join(';', row.Select(Field)));
        }
        return sb.ToString();
    }

    /// <summary>Записать с меткой UTF-8 (без неё Excel ломает кириллицу).</summary>
    public static Task Save(string path, string csv) => File.WriteAllTextAsync(path, csv, new UTF8Encoding(true));

    [SelfTest]
    static string LibraryExportEscapes()
    {
        if (Field("Hades") != "Hades" || Field("a;b") != "\"a;b\"" || Field("say \"hi\"") != "\"say \"\"hi\"\"\"" || Field("line\nbreak") != "\"line\nbreak\"")
            throw new Exception("csv escaping");
        if (Field("=cmd|' /C calc'!A0") != "'=cmd|' /C calc'!A0") throw new Exception("formula guard");
        var shelves = new Shelves(new System.Text.Json.Nodes.JsonObject());
        var g = new LibGame("steam:1", "steam", "Alpha; Beta", null, 1, true, 3L << 30);
        shelves.ToggleFavorite(g.Key);
        var csv = Csv([g], shelves, _ => (0, 90));
        var line = csv.Split('\n')[1].TrimEnd('\r');
        if (!line.StartsWith("\"Alpha; Beta\";Steam;1;1.5;;3;;1;;")) throw new Exception("row " + line);
        return "CSV fields quoted/escaped, formulas neutralised, hours and GB with dots";
    }
}
