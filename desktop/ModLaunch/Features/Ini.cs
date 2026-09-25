namespace ModLaunch.Features;

/// <summary>Простой INI: секции, ключ=значение. Ключи вне секций — в секции "".</summary>
public static class Ini
{
    public static Dictionary<string, Dictionary<string, string>> Parse(string? text)
    {
        var result = new Dictionary<string, Dictionary<string, string>>();
        var section = "";
        foreach (var raw in (text ?? "").TrimStart('﻿').Split('\n'))
        {
            var line = raw.Trim();
            if (line == "" || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1]; result.TryAdd(section, new()); continue; }
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            if (!result.TryGetValue(section, out var s)) result[section] = s = new();
            s[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        return result;
    }

    public static string? Get(this Dictionary<string, Dictionary<string, string>> ini, string section, string key) =>
        ini.TryGetValue(section, out var s) && s.TryGetValue(key, out var v) && v != "" ? v : null;

    /// <summary>Поменять значения в секции, не трогая остальной файл.</summary>
    public static string Patch(string? text, string section, IDictionary<string, string> values)
    {
        var clean = (text ?? "").TrimStart('﻿').Replace("\r\n", "\n");
        var lines = clean.Trim() == "" ? new List<string>() : clean.Split('\n').ToList();
        var start = lines.FindIndex(l => l.Trim() == $"[{section}]");
        if (start < 0)
        {
            if (lines.Count > 0 && lines[^1].Trim() != "") lines.Add("");
            lines.Add($"[{section}]");
            start = lines.Count - 1;
        }
        int End() { var e = lines.FindIndex(start + 1, l => l.Trim().StartsWith('[') && l.Trim().EndsWith(']')); return e < 0 ? lines.Count : e; }
        foreach (var (key, value) in values)
        {
            var end = End();
            var at = -1;
            for (var i = start + 1; i < end; i++) if (lines[i].Split('=')[0].Trim() == key) { at = i; break; }
            if (at >= 0) lines[at] = $"{key}={value}";
            else
            {
                var insert = end;
                while (insert - 1 > start && lines[insert - 1].Trim() == "") insert--;
                lines.Insert(insert, $"{key}={value}");
            }
        }
        return string.Join("\r\n", lines).TrimEnd('\r', '\n') + "\r\n";
    }
}
