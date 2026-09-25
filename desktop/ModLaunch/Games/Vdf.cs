using System.Text;

namespace ModLaunch.Games;

/// <summary>
/// Разбор текстового формата Valve KeyValues (libraryfolders.vdf, appmanifest_*.acf).
/// Ключи без учёта регистра: в разных версиях клиента пишут по-разному.
/// </summary>
public static class Vdf
{
    public static Dictionary<string, object> Parse(string text)
    {
        var pos = 0;
        return ParseObject(text, ref pos);
    }

    static Dictionary<string, object> ParseObject(string s, ref int pos)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            var key = Next(s, ref pos);
            if (key is null || key == "}") return result;
            var value = Next(s, ref pos);
            if (value is null) return result;
            result[key] = value == "{" ? ParseObject(s, ref pos) : value;
        }
    }

    /// <summary>Следующая лексема: строка в кавычках, «{» или «}». Комментарии // пропускаются.</summary>
    static string? Next(string s, ref int pos)
    {
        while (pos < s.Length)
        {
            var c = s[pos];
            if (char.IsWhiteSpace(c)) { pos++; continue; }
            if (c == '/' && pos + 1 < s.Length && s[pos + 1] == '/')
            {
                while (pos < s.Length && s[pos] != '\n') pos++;
                continue;
            }
            if (c is '{' or '}') { pos++; return c.ToString(); }
            if (c == '"')
            {
                pos++;
                var sb = new StringBuilder();
                while (pos < s.Length && s[pos] != '"')
                {
                    if (s[pos] == '\\' && pos + 1 < s.Length)
                    {
                        pos++;
                        sb.Append(s[pos] switch { 'n' => '\n', 't' => '\t', _ => s[pos] });
                    }
                    else sb.Append(s[pos]);
                    pos++;
                }
                pos++;
                return sb.ToString();
            }
            // Строка без кавычек — до пробела.
            var start = pos;
            while (pos < s.Length && !char.IsWhiteSpace(s[pos]) && s[pos] is not ('{' or '}' or '"')) pos++;
            return s[start..pos];
        }
        return null;
    }

    public static object? Get(Dictionary<string, object> root, params string[] path)
    {
        object? node = root;
        foreach (var key in path)
        {
            if (node is not Dictionary<string, object> d || !d.TryGetValue(key, out node)) return null;
        }
        return node;
    }
}
