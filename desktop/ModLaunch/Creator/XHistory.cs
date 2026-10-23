using System.Globalization;

namespace ModLaunch.Creator;

/// <summary>
/// 19.0: история файла в среде ModScript X. Каждое сохранение кладёт снимок рядом с файлом
/// (.history/&lt;имя файла&gt;/&lt;время&gt;), последние 60. По снимку видно, что поменялось, и его можно вернуть
/// одной правкой (её же отменяет Ctrl+Z). Работает без git и без сети.
/// </summary>
public static class XHistory
{
    const int Keep = 60;
    const string Stamp = "yyyyMMdd-HHmmss-fff";

    public sealed record Entry(string Path, DateTime At);

    static string Dir(string file) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(file)) ?? ".", ".history", Path.GetFileName(file));

    /// <summary>Снимки файла, новые сверху.</summary>
    public static List<Entry> List(string file)
    {
        var dir = Dir(file);
        if (!Directory.Exists(dir)) return [];
        try
        {
            return [.. Directory.EnumerateFiles(dir)
                .Select(p => DateTime.TryParseExact(Path.GetFileNameWithoutExtension(p), Stamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at) ? new Entry(p, at) : null)
                .OfType<Entry>().OrderByDescending(e => e.At)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>Положить снимок (если текст не такой же, как в последнем) и убрать самые старые.</summary>
    public static void Snap(string file, string text, DateTime? at = null)
    {
        try
        {
            var list = List(file);
            if (list.Count > 0 && File.ReadAllText(list[0].Path) == text) return;
            var dir = Directory.CreateDirectory(Dir(file)).FullName;
            var when = (at ?? DateTime.UtcNow).ToUniversalTime();
            if (list.Count > 0 && when <= list[0].At) when = list[0].At.AddMilliseconds(1);
            File.WriteAllText(Path.Combine(dir, when.ToString(Stamp, CultureInfo.InvariantCulture) + Path.GetExtension(file)), text);
            foreach (var old in List(file).Skip(Keep)) File.Delete(old.Path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public static string Read(Entry e)
    {
        try { return File.ReadAllText(e.Path); }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException) { return ""; }
    }

    /// <summary>
    /// Построчная разница a → b: ' ' — без изменений, '-' — убрана, '+' — добавлена. Наибольшая общая
    /// подпоследовательность; очень большие файлы — грубо (всё убрано, всё добавлено), чтобы не висеть.
    /// </summary>
    public static List<(char Op, string Text)> Diff(string a, string b)
    {
        var x = a.Replace("\r\n", "\n").Split('\n');
        var y = b.Replace("\r\n", "\n").Split('\n');
        var head = 0;
        while (head < x.Length && head < y.Length && x[head] == y[head]) head++;
        var tail = 0;
        while (tail < x.Length - head && tail < y.Length - head && x[^(tail + 1)] == y[^(tail + 1)]) tail++;
        var result = new List<(char, string)>();
        for (var i = 0; i < head; i++) result.Add((' ', x[i]));
        var xs = x[head..(x.Length - tail)];
        var ys = y[head..(y.Length - tail)];
        if ((long)xs.Length * ys.Length > 4_000_000)
        {
            result.AddRange(xs.Select(l => ('-', l)));
            result.AddRange(ys.Select(l => ('+', l)));
        }
        else
        {
            var lcs = new int[xs.Length + 1, ys.Length + 1];
            for (var i = xs.Length - 1; i >= 0; i--)
                for (var j = ys.Length - 1; j >= 0; j--)
                    lcs[i, j] = xs[i] == ys[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            int p = 0, q = 0;
            while (p < xs.Length || q < ys.Length)
            {
                if (p < xs.Length && q < ys.Length && xs[p] == ys[q]) { result.Add((' ', xs[p])); p++; q++; }
                else if (p < xs.Length && (q == ys.Length || lcs[p + 1, q] >= lcs[p, q + 1])) { result.Add(('-', xs[p])); p++; }
                else { result.Add(('+', ys[q])); q++; }
            }
        }
        for (var i = x.Length - tail; i < x.Length; i++) result.Add((' ', x[i]));
        return result;
    }

    [SelfTest]
    static string XHistoryKeepsVersions()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "modlaunch-hist-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var file = Path.Combine(dir, "main.modx");
            Snap(file, "a\nb\nc");
            Snap(file, "a\nb\nc");
            Snap(file, "a\nB\nc\nd");
            var list = List(file);
            if (list.Count != 2) throw new Exception($"snapshots: {list.Count}");
            if (Read(list[0]) != "a\nB\nc\nd" || Read(list[1]) != "a\nb\nc") throw new Exception("wrong order");
            var diff = Diff("a\nb\nc", "a\nB\nc\nd");
            var text = string.Concat(diff.Select(d => d.Op + d.Text + "|"));
            if (text != " a|-b|+B| c|+d|") throw new Exception("diff: " + text);
            return $"{list.Count} snapshots, diff {diff.Count(d => d.Op != ' ')} changes";
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
