using System.Text.Json.Nodes;

namespace ModLaunch.Core;

/// <summary>
/// 10.0: виджеты главной, как на iPhone и Mac: s — квадрат, m — широкий (2×1), l — большой (2×2).
/// Раскладка — список «id размер» в settings.widgets; её же пишет и читает конфиг (widget clock m).
/// </summary>
public static class Widgets
{
    public sealed record Kind(string Id, string Icon, string[] Sizes);

    public static readonly Kind[] Catalog =
    [
        new("continue", "Play", ["m", "l"]),
        new("clock", "Clock", ["s", "m"]),
        new("level", "Trophy", ["s", "m"]),
        new("autopilot", "Wand", ["s", "m"]),
        new("friends", "Users", ["m", "l"]),
        new("tip", "Sparkles", ["m"]),
        new("playtime", "Chart", ["s", "m"]),
        new("builds", "Hourglass", ["s", "m"]),
        new("downloads", "Download", ["m", "l"]),
        new("library", "Grid", ["m", "l"]),
        new("attention", "Bell", ["s", "m"]),
        new("streak", "Flame", ["s"]),
        new("random", "Shuffle", ["s", "m"]),
        new("lore", "Sparkles", ["m", "l"]),
    ];

    /// <summary>11.0: по умолчанию — дело, а не счётчики: продолжить, часы по сборкам, друзья, что требует внимания.</summary>
    public static readonly (string Id, string Size)[] Default =
    [
        ("continue", "m"), ("builds", "s"), ("playtime", "s"),
        ("friends", "m"), ("attention", "s"), ("autopilot", "s"),
        ("downloads", "m"),
    ];

    public static Kind? Find(string id) => Catalog.FirstOrDefault(k => k.Id == id);

    /// <summary>Сколько клеток занимает размер: (ширина, высота).</summary>
    public static (int W, int H) Cells(string size) => size switch { "l" => (2, 2), "m" => (2, 1), _ => (1, 1) };

    public static List<(string Id, string Size)> Layout()
    {
        if (Settings.Data["widgets"] is not JsonArray list) return [.. Default];
        var result = new List<(string, string)>();
        foreach (var n in list)
        {
            var parts = (n?.ToString() ?? "").Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || Find(parts[0]) is not { } k) continue;
            var size = parts.Length > 1 && k.Sizes.Contains(parts[1]) ? parts[1] : k.Sizes[0];
            result.Add((k.Id, size));
        }
        return result;
    }

    public static void Set(IEnumerable<(string Id, string Size)> layout)
    {
        var list = new JsonArray();
        foreach (var (id, size) in layout.Take(24))
            if (Find(id) is { } k)
                list.Add(JsonValue.Create($"{k.Id} {(k.Sizes.Contains(size) ? size : k.Sizes[0])}"));
        Settings.Data["widgets"] = list;
        Settings.Save();
    }

    /// <summary>Добавить виджет; 12.0: сразу нужного размера и на нужное место (из меню правой кнопки).</summary>
    public static void Add(string id, string? size = null, int at = -1)
    {
        if (Find(id) is not { } k) return;
        var l = Layout();
        if (l.Count >= 24) return; // больше не помещается: вставка вытеснила бы последний виджет
        var item = (id, size is not null && k.Sizes.Contains(size) ? size : k.Sizes[0]);
        if (at >= 0 && at <= l.Count) l.Insert(at, item); else l.Add(item);
        Set(l);
        Notify();
    }

    /// <summary>Задать размер виджета (из меню правой кнопки).</summary>
    public static void SetSize(int index, string size)
    {
        var l = Layout();
        if (index < 0 || index >= l.Count || Find(l[index].Id) is not { } k || !k.Sizes.Contains(size)) return;
        l[index] = (k.Id, size);
        Set(l);
        Notify();
    }

    public static void Remove(int index)
    {
        var l = Layout();
        if (index < 0 || index >= l.Count) return;
        l.RemoveAt(index);
        Set(l);
        Notify();
    }

    /// <summary>Следующий размер по кругу (s → m → l).</summary>
    public static void Resize(int index)
    {
        var l = Layout();
        if (index < 0 || index >= l.Count || Find(l[index].Id) is not { } k) return;
        var at = Array.IndexOf(k.Sizes, l[index].Size);
        l[index] = (k.Id, k.Sizes[(at + 1) % k.Sizes.Length]);
        Set(l);
        Notify();
    }

    public static void Move(int index, int delta)
    {
        var l = Layout();
        var to = index + delta;
        if (index < 0 || index >= l.Count || to < 0 || to >= l.Count) return;
        (l[index], l[to]) = (l[to], l[index]);
        Set(l);
        Notify();
    }

    public static void Reset() { Settings.Data.Remove("widgets"); Settings.Save(); Notify(); }

    public static event Action? Changed;
    public static void Notify() => Changed?.Invoke();

    /// <summary>
    /// Раскладка по сетке из columns столбцов, плотно, как на iPhone: каждый виджет встаёт в первое
    /// свободное место сверху вниз, слева направо. Возвращает (столбец, строка) для каждого.
    /// </summary>
    public static List<(int X, int Y)> Pack(IReadOnlyList<string> sizes, int columns)
    {
        var used = new HashSet<(int, int)>();
        var result = new List<(int, int)>();
        foreach (var size in sizes)
        {
            var (w, h) = Cells(size);
            w = Math.Min(w, columns);
            for (var y = 0; ; y++)
            {
                var placed = false;
                for (var x = 0; x + w <= columns; x++)
                {
                    var free = true;
                    for (var dx = 0; dx < w && free; dx++)
                        for (var dy = 0; dy < h && free; dy++)
                            if (used.Contains((x + dx, y + dy))) free = false;
                    if (!free) continue;
                    for (var dx = 0; dx < w; dx++)
                        for (var dy = 0; dy < h; dy++) used.Add((x + dx, y + dy));
                    result.Add((x, y));
                    placed = true;
                    break;
                }
                if (placed) break;
            }
        }
        return result;
    }

    [SelfTest]
    static string WidgetsPackDensely()
    {
        var p = Pack(["l", "s", "s", "s", "s", "m"], 4);
        // l в (0,0) 2×2; четыре s заполняют (2,0),(3,0),(2,1),(3,1); m — с новой строки.
        if (p[0] != (0, 0) || p[1] != (2, 0) || p[2] != (3, 0) || p[3] != (2, 1) || p[4] != (3, 1) || p[5] != (0, 2)) throw new Exception(string.Join(" ", p));
        return "widgets packed without holes";
    }
}
