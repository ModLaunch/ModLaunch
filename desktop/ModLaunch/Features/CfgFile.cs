using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Mods;

namespace ModLaunch.Features;

/// <summary>Одна настройка из .cfg файла BepInEx.</summary>
public sealed class CfgEntry
{
    public string Section = "";
    public string Key = "";
    public string Value = "";
    public string Default = "";
    public string Description = "";
    public string Type = "";
    public List<string> Choices = [];
    public int Line;
    /// <summary>Допустимый диапазон чисел («# Acceptable value range: From 0 to 100»).</summary>
    public double? Min, Max;
    /// <summary>Путь к значению во вложенном JSON (config.json модов SMAPI, настройки Hollow Knight).</summary>
    public string[]? JsonPath;
    /// <summary>Понятное название (переводы Content Patcher); без него показываем ключ.</summary>
    public string? Title;
    /// <summary>Значение хранится в JSON строкой ("true", "48") — так пишет Content Patcher.</summary>
    public bool AsText;
    /// <summary>Можно выбрать несколько вариантов через запятую (AllowMultiple у Content Patcher).</summary>
    public bool Multi;
    public bool Changed => Default != "" && !Same(Value, Default);
    public bool IsBool => Type.Equals("Boolean", StringComparison.OrdinalIgnoreCase);
    public bool IsNumber => Type is "Single" or "Double" or "Int32" or "Int64" or "Byte" or "Number" or "Decimal" or "UInt32" or "Int16";

    static bool Same(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase)
        || (double.TryParse(a, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)
            && double.TryParse(b, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y) && Math.Abs(x - y) < 1e-9);
}

/// <summary>
/// Редактор настроек: читает .cfg файлы BepInEx (там у каждой настройки есть описание, тип и
/// значение по умолчанию в комментариях) и меняет только нужную строчку, остальное не трогает.
/// </summary>
public static class CfgFile
{
    /// <summary>Файлы настроек модов игры: .cfg у BepInEx, config.json у SMAPI, *.GlobalSettings.json у Hollow Knight.</summary>
    public static List<string> Files(ModRegistry registry) => registry.Game.Loader switch
    {
        LoaderKind.Smapi => CfgJson.Files(registry),
        LoaderKind.HkApi => CfgJson.HkFiles(registry),
        _ => BepFiles(registry),
    };

    /// <summary>Подпись файла в списке: имя мода, а не «config.json».</summary>
    public static string Label(string path)
    {
        var name = Path.GetFileName(path);
        if (name.Equals("config.json", StringComparison.OrdinalIgnoreCase)) return CfgJson.Label(path);
        if (name.EndsWith(".GlobalSettings.json", StringComparison.OrdinalIgnoreCase)) return name[..^".GlobalSettings.json".Length];
        return name.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    static List<string> BepFiles(ModRegistry registry)
    {
        if (registry.Game.Loader != LoaderKind.Bepinex) return [];
        var dir = Path.Combine(registry.GamePath, "BepInEx", "config");
        try
        {
            return Directory.Exists(dir)
                ? Directory.EnumerateFiles(dir, "*.cfg", SearchOption.AllDirectories).OrderBy(Path.GetFileName).ToList()
                : [];
        }
        catch { return []; }
    }

    public static List<CfgEntry> Parse(string path)
    {
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return CfgJson.Parse(path);
        return ParseCfg(path);
    }

    static List<CfgEntry> ParseCfg(string path)
    {
        var result = new List<CfgEntry>();
        string[] lines;
        try { lines = File.ReadAllLines(path); } catch { return result; }
        return Parse(lines);
    }

    public static List<CfgEntry> Parse(string[] lines)
    {
        var result = new List<CfgEntry>();
        var section = "";
        var desc = new List<string>();
        string type = "", def = "";
        var choices = new List<string>();
        (double Lo, double Hi)? range = null;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) { desc.Clear(); type = def = ""; choices = []; range = null; continue; }
            if (line.StartsWith("[") && line.EndsWith("]")) { section = line[1..^1]; desc.Clear(); type = def = ""; choices = []; range = null; continue; }
            if (line.StartsWith("##")) { desc.Add(line.TrimStart('#').Trim()); continue; }
            if (line.StartsWith("#"))
            {
                var c = line.TrimStart('#').Trim();
                if (c.StartsWith("Setting type:", StringComparison.OrdinalIgnoreCase)) type = c[13..].Trim();
                else if (c.StartsWith("Default value:", StringComparison.OrdinalIgnoreCase)) def = c[14..].Trim();
                else if (c.StartsWith("Acceptable values:", StringComparison.OrdinalIgnoreCase))
                    choices = c[18..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
                else if (c.StartsWith("Acceptable value range:", StringComparison.OrdinalIgnoreCase)
                    && System.Text.RegularExpressions.Regex.Match(c, @"From\s+(-?[\d.]+)\s+to\s+(-?[\d.]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase) is { Success: true } m
                    && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lo)
                    && double.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hi))
                    range = (lo, hi);
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            result.Add(new CfgEntry
            {
                Section = section, Key = line[..eq].Trim(), Value = line[(eq + 1)..].Trim(), Default = def,
                Description = string.Join(" ", desc), Type = type, Choices = choices, Line = i,
                Min = range?.Lo, Max = range?.Hi,
            });
            desc.Clear(); type = def = ""; choices = []; range = null;
        }
        return result;
    }

    /// <summary>Записать новое значение в строку настройки. Остальной файл остаётся как был.</summary>
    public static void Set(string path, CfgEntry entry, string value)
    {
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) CfgJson.Set(path, entry, value);
        else SetCfg(path, entry, value);
    }

    /// <summary>Записать сразу несколько значений одного файла (пресеты, «сбросить всё»). Возвращает, сколько поменялось.</summary>
    public static int SetMany(string path, IEnumerable<(CfgEntry Entry, string Value)> changes)
    {
        var list = changes.Where(c => c.Entry.Value != c.Value).ToList();
        if (list.Count == 0) return 0;
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return CfgJson.SetMany(path, list);
        var lines = File.ReadAllLines(path);
        var fresh = Parse(lines);
        var n = 0;
        foreach (var (entry, value) in list)
        {
            var at = fresh.FirstOrDefault(e => e.Section == entry.Section && e.Key == entry.Key);
            if (at is null) continue;
            var line = lines[at.Line];
            lines[at.Line] = line[..line.IndexOf('=')].TrimEnd() + " = " + value.Replace("\r", "").Replace("\n", " ");
            entry.Value = value;
            n++;
        }
        if (n == 0) return 0;
        var temp = path + ".tmp";
        File.WriteAllLines(temp, lines);
        File.Move(temp, path, true);
        return n;
    }

    static void SetCfg(string path, CfgEntry entry, string value)
    {
        var lines = File.ReadAllLines(path);
        if (entry.Line >= lines.Length || !lines[entry.Line].TrimStart().StartsWith(entry.Key, StringComparison.Ordinal))
        {
            // файл поменялся, пока он был открыт — ищем настройку заново
            var fresh = Parse(lines).FirstOrDefault(e => e.Section == entry.Section && e.Key == entry.Key)
                ?? throw new InvalidOperationException(entry.Key);
            entry.Line = fresh.Line;
        }
        var line = lines[entry.Line];
        lines[entry.Line] = line[..line.IndexOf('=')].TrimEnd() + " = " + value.Replace("\r", "").Replace("\n", " ");
        var temp = path + ".tmp";
        File.WriteAllLines(temp, lines);
        File.Move(temp, path, true);
        entry.Value = value;
    }

    [SelfTest]
    static string ParsesAndKeepsComments()
    {
        string[] src =
        [
            "## Settings file", "", "[General]", "",
            "## How loud", "# Setting type: Single", "# Default value: 0.5", "# Acceptable value range: From 0 to 1", "Volume = 0.8", "",
            "## Mode", "# Setting type: Mode", "# Default value: Easy", "# Acceptable values: Easy, Hard", "Difficulty = Hard",
        ];
        var list = Parse(src);
        if (list.Count != 2) throw new Exception("expected 2 entries, got " + list.Count);
        if (list[0].Key != "Volume" || list[0].Value != "0.8" || list[0].Default != "0.5" || list[0].Description != "How loud") throw new Exception("Volume parsed wrong");
        if (list[1].Choices.Count != 2 || list[1].Section != "General") throw new Exception("choices/section wrong");

        var path = Path.Combine(Paths.DataDir, "cfg-test.cfg");
        Directory.CreateDirectory(Paths.DataDir);
        File.WriteAllLines(path, src);
        Set(path, list[0], "1");
        var after = File.ReadAllLines(path);
        if (after[8] != "Volume = 1" || after.Length != src.Length || after[4] != "## How loud") throw new Exception("write changed other lines");
        File.Delete(path);
        return "2 entries read, one line rewritten, comments kept";
    }
}
