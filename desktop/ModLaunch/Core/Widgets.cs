using System.Text.Json.Nodes;

namespace ModLaunch.Core;

/// <summary>
/// 10.0: виджеты главной, как на iPhone и Mac: s — квадрат, m — широкий (2×1), l — большой (2×2).
/// Раскладка — список «id размер» в settings.widgets; её же пишет и читает конфиг (widget clock m).
/// 12.0: у виджета есть настройки, как у виджетов iPhone (какая игра, цвет, текст заметки…):
/// третьим словом «ключ=значение;ключ=значение», значения экранированы.
/// </summary>
public static class Widgets
{
    /// <summary>Настройка вида: game — игра (своя или из Steam), choice — выбор из Choices, text — текст, toggle — 0/1.</summary>
    public sealed record Opt(string Key, string Type, string Default, string[] Choices);

    public sealed record Kind(string Id, string Icon, string[] Sizes, Opt[] Options);

    /// <summary>Виджет на главной: вид, размер и его настройки (только отличные от значений по умолчанию).</summary>
    public sealed record Entry(string Id, string Size, IReadOnlyDictionary<string, string> Opts)
    {
        public string Get(string key) => Opts.TryGetValue(key, out var v) ? v : Find(Id) is { } k ? OptionsOf(k).FirstOrDefault(o => o.Key == key)?.Default ?? "" : "";
    }

    static Opt Choice(string key, string def, params string[] choices) => new(key, "choice", def, choices);
    static Opt Game(string def = "") => new("game", "game", def, []);
    static Opt Toggle(string key, bool on) => new(key, "toggle", on ? "1" : "0", []);

    /// <summary>Цвет виджета: auto — как тема, остальные — свои. 13.0: ещё красный, бирюзовый, жёлтый и мятный.</summary>
    public static readonly string[] Tints = ["auto", "blue", "violet", "green", "orange", "pink", "red", "cyan", "yellow", "mint", "mono"];
    static readonly Opt Tint = Choice("tint", "auto", Tints);

    /// <summary>
    /// 13.0: вид — у любого виджета, поверх его собственных настроек: фон (сплошной, стекло, цветной, контур, без фона),
    /// цвет, углы, прозрачность, подпись (показывать и своя) — третьим словом раскладки, как и остальные настройки.
    /// </summary>
    public static readonly Opt[] Look =
    [
        Choice("style", "solid", "solid", "glass", "tinted", "outline", "clear"),
        Tint,
        Choice("corner", "auto", "auto", "round", "soft", "square"),
        Choice("opacity", "100", "100", "90", "75", "60"),
        Toggle("caption", true),
        new("label", "text", "", []),
    ];

    /// <summary>Настройки вида виджета: свои и общие «Вид» (без повторов: tint у часов — один).</summary>
    public static IEnumerable<Opt> OptionsOf(Kind k) => k.Options.Concat(Look.Where(o => k.Options.All(x => x.Key != o.Key)));

    /// <summary>
    /// 13.0: размеры как у всех: s 1×1, m 2×1, l 2×2 и новые t — высокий 1×2, w — широкий 3×1, xl — во всю ширину 4×2.
    /// Новый размер доступен виду, если у него есть «родной» размер того же склада (t → s, w → m, xl → l).
    /// </summary>
    public static readonly string[] AllSizes = ["s", "m", "l", "t", "w", "xl"];

    /// <summary>Родной размер для отрисовки: высокий рисуется как маленький, широкий — как средний, XL — как большой.</summary>
    public static string Base(string size) => size switch { "t" => "s", "w" => "m", "xl" => "l", _ => size };

    public static IEnumerable<string> SizesOf(Kind k) => AllSizes.Where(z => k.Sizes.Contains(Base(z)));

    /// <summary>13.0: плотность сетки (размер клетки) и зазор между виджетами — для всей главной.</summary>
    public static readonly string[] Densities = ["compact", "normal", "roomy"];
    public static readonly string[] Gaps = ["tight", "normal", "wide"];

    public static string Density => Settings.Data.Str("widgetCell") is { } v && Densities.Contains(v) ? v : "normal";
    public static string Gap => Settings.Data.Str("widgetGap") is { } v && Gaps.Contains(v) ? v : "normal";
    public static double CellSize => Density switch { "compact" => 140, "roomy" => 212, _ => 172 };
    public static double GapSize => Gap switch { "tight" => 8, "wide" => 22, _ => 14 };

    public static void SetGrid(string? density, string? gap)
    {
        if (density is not null && Densities.Contains(density)) Settings.Data["widgetCell"] = density;
        if (gap is not null && Gaps.Contains(gap)) Settings.Data["widgetGap"] = gap;
        Settings.Save();
        Notify();
    }

    public static readonly Kind[] Catalog =
    [
        new("continue", "Play", ["m", "l"], [Game()]),
        new("game", "Gamepad", ["s", "m", "l"], [Game()]),
        new("clock", "Clock", ["s", "m"], [Toggle("h12", false), Toggle("date", true), Tint]),
        new("note", "Edit", ["s", "m", "l"], [new("text", "text", "", []), Tint]),
        new("steam", "Layers", ["m", "l"], [Choice("sort", "recent", "recent", "name", "size")]),
        new("level", "Trophy", ["s", "m"], [Tint]),
        new("rewards", "Trophy", ["m", "l"], [Tint]),
        new("autopilot", "Wand", ["s", "m"], [Game()]),
        new("friends", "Users", ["m", "l"], []),
        new("tip", "Sparkles", ["m"], []),
        new("playtime", "Chart", ["s", "m"], [Game(), Tint]),
        new("builds", "Hourglass", ["s", "m"], [Tint]),
        new("downloads", "Download", ["m", "l"], []),
        new("library", "Grid", ["m", "l"], [Choice("sort", "recent", "recent", "name", "hours")]),
        new("attention", "Bell", ["s", "m"], []),
        new("streak", "Flame", ["s"], [Tint]),
        new("random", "Shuffle", ["s", "m"], [Choice("from", "mine", "mine", "steam", "all")]),
    ];

    /// <summary>11.0: по умолчанию — дело, а не счётчики: продолжить, часы по сборкам, друзья, что требует внимания.</summary>
    public static readonly (string Id, string Size)[] Default =
    [
        ("continue", "m"), ("builds", "s"), ("playtime", "s"),
        ("rewards", "w"), ("attention", "s"),
        ("friends", "m"), ("autopilot", "s"), ("downloads", "m"),
    ];

    /// <summary>
    /// 16.0: путь наград за уровни — на главную всем один раз (у кого своя раскладка — четвёртым, широким).
    /// Убрали сами — больше не возвращается.
    /// </summary>
    public static void EnsureRoad()
    {
        if (Settings.Data.Bool("v16road", false)) return;
        Settings.Data["v16road"] = true;
        if (Settings.Data["widgets"] is JsonArray && Entries().All(e => e.Id != "rewards")) Add("rewards", "w", Math.Min(3, Entries().Count));
        else Settings.Save();
    }

    public static Kind? Find(string id) => Catalog.FirstOrDefault(k => k.Id == id);

    /// <summary>Сколько клеток занимает размер: (ширина, высота).</summary>
    public static (int W, int H) Cells(string size) => size switch { "l" => (2, 2), "m" => (2, 1), "t" => (1, 2), "w" => (3, 1), "xl" => (4, 2), _ => (1, 1) };

    static readonly IReadOnlyDictionary<string, string> None = new Dictionary<string, string>();

    /// <summary>Строка раскладки → виджет; чужой вид или мусор — null.</summary>
    public static Entry? Parse(string line)
    {
        var parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || Find(parts[0]) is not { } k) return null;
        var size = parts.Length > 1 && SizesOf(k).Contains(parts[1]) ? parts[1] : k.Sizes[0];
        var opts = new Dictionary<string, string>();
        if (parts.Length > 2)
            foreach (var pair in parts[2].Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                // Только известные настройки этого вида: старый или чужой конфиг не протащит лишнего.
                if (kv.Length == 2 && OptionsOf(k).FirstOrDefault(o => o.Key == kv[0]) is { } o)
                {
                    var v = Uri.UnescapeDataString(kv[1]);
                    if (o.Type == "choice" && !o.Choices.Contains(v)) continue;
                    if (o.Type == "text" && v.Length > 400) v = v[..400];
                    opts[o.Key] = v;
                }
            }
        return new Entry(k.Id, size, opts);
    }

    /// <summary>Виджет → строка раскладки; настройки со значением по умолчанию не пишутся.</summary>
    public static string Format(Entry e)
    {
        var k = Find(e.Id)!;
        var opts = string.Join(";", OptionsOf(k)
            .Where(o => e.Opts.TryGetValue(o.Key, out var v) && v != o.Default)
            .Select(o => o.Key + "=" + Uri.EscapeDataString(e.Opts[o.Key])));
        return opts.Length > 0 ? $"{e.Id} {e.Size} {opts}" : $"{e.Id} {e.Size}";
    }

    public static List<Entry> Entries()
    {
        if (Settings.Data["widgets"] is not JsonArray list) return [.. Default.Select(d => new Entry(d.Id, d.Size, None))];
        return [.. list.Select(n => Parse(n?.ToString() ?? "")).OfType<Entry>()];
    }

    public static List<(string Id, string Size)> Layout() => [.. Entries().Select(e => (e.Id, e.Size))];

    public static void SetEntries(IEnumerable<Entry> layout)
    {
        var list = new JsonArray();
        foreach (var e in layout.Take(24))
            if (Find(e.Id) is { } k)
                list.Add(JsonValue.Create(Format(e with { Size = SizesOf(k).Contains(e.Size) ? e.Size : k.Sizes[0] })));
        Settings.Data["widgets"] = list;
        Settings.Save();
    }

    /// <summary>Раскладка без настроек (из конфига оформления).</summary>
    public static void Set(IEnumerable<(string Id, string Size)> layout) => SetEntries(layout.Select(l => new Entry(l.Id, l.Size, None)));

    /// <summary>Добавить виджет; 12.0: сразу нужного размера, на нужное место и со своими настройками.</summary>
    public static void Add(string id, string? size = null, int at = -1, IReadOnlyDictionary<string, string>? opts = null)
    {
        if (Find(id) is not { } k) return;
        var l = Entries();
        if (l.Count >= 24) return; // больше не помещается: вставка вытеснила бы последний виджет
        var item = new Entry(id, size is not null && SizesOf(k).Contains(size) ? size : k.Sizes[0], opts ?? None);
        if (at >= 0 && at <= l.Count) l.Insert(at, item); else l.Add(item);
        SetEntries(l);
        Notify();
    }

    /// <summary>Поменять виджет целиком (из «Изменить виджет»): размер и настройки.</summary>
    public static void Update(int index, string size, IReadOnlyDictionary<string, string> opts)
    {
        var l = Entries();
        if (index < 0 || index >= l.Count) return;
        l[index] = l[index] with { Size = size, Opts = opts };
        SetEntries(l);
        Notify();
    }

    /// <summary>Задать размер виджета (из меню правой кнопки).</summary>
    public static void SetSize(int index, string size)
    {
        var l = Entries();
        if (index < 0 || index >= l.Count || Find(l[index].Id) is not { } k || !SizesOf(k).Contains(size)) return;
        l[index] = l[index] with { Size = size };
        SetEntries(l);
        Notify();
    }

    public static void Remove(int index)
    {
        var l = Entries();
        if (index < 0 || index >= l.Count) return;
        l.RemoveAt(index);
        SetEntries(l);
        Notify();
    }

    /// <summary>Следующий размер по кругу (s → m → l → t → w → xl, только доступные виду).</summary>
    public static void Resize(int index)
    {
        var l = Entries();
        if (index < 0 || index >= l.Count || Find(l[index].Id) is not { } k) return;
        var sizes = SizesOf(k).ToList();
        var at = sizes.IndexOf(l[index].Size);
        l[index] = l[index] with { Size = sizes[(at + 1) % sizes.Count] };
        SetEntries(l);
        Notify();
    }

    public static void Move(int index, int delta)
    {
        var l = Entries();
        var to = index + delta;
        if (index < 0 || index >= l.Count || to < 0 || to >= l.Count) return;
        (l[index], l[to]) = (l[to], l[index]);
        SetEntries(l);
        Notify();
    }

    /// <summary>13.0: перетащить виджет с места from на место to (мышью в режиме правки).</summary>
    public static void MoveTo(int from, int to)
    {
        var l = Entries();
        if (from < 0 || from >= l.Count || to < 0 || to >= l.Count || from == to) return;
        var item = l[from];
        l.RemoveAt(from);
        l.Insert(to, item);
        SetEntries(l);
        Notify();
    }

    /// <summary>13.0: копия виджета со всеми настройками — сразу за ним.</summary>
    public static void Duplicate(int index)
    {
        var l = Entries();
        if (index < 0 || index >= l.Count || l.Count >= 24) return;
        l.Insert(index + 1, l[index]);
        SetEntries(l);
        Notify();
    }

    /// <summary>13.0: вид одного виджета (фон, цвет, углы, прозрачность) — всем виджетам главной.</summary>
    public static void ApplyLookToAll(IReadOnlyDictionary<string, string> opts)
    {
        var keys = new[] { "style", "tint", "corner", "opacity" };
        var l = Entries().Select(e =>
        {
            var o = new Dictionary<string, string>(e.Opts);
            foreach (var key in keys)
                if (opts.TryGetValue(key, out var v)) o[key] = v; else o.Remove(key);
            return e with { Opts = o };
        }).ToList();
        SetEntries(l);
        Notify();
    }

    public static void Reset() { Settings.Data.Remove("widgets"); Settings.Data.Remove("widgetCell"); Settings.Data.Remove("widgetGap"); Settings.Save(); Notify(); }

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
        // 13.0: высокий встаёт в столбец, широкий 3×1 оставляет место маленькому, XL — во всю ширину.
        var q = Pack(["t", "w", "s", "xl"], 4);
        if (q[0] != (0, 0) || q[1] != (1, 0) || q[2] != (1, 1) || q[3] != (0, 2)) throw new Exception("new sizes: " + string.Join(" ", q));
        return "widgets packed without holes";
    }

    /// <summary>Настройки виджета: экранирование, только известные ключи, значения по умолчанию не пишутся.</summary>
    [SelfTest]
    static string WidgetOptionsRoundTrip()
    {
        var note = new Entry("note", "m", new Dictionary<string, string> { ["text"] = "Купить; молоко = 2 шт.\nпотом", ["tint"] = "auto" });
        var line = Format(note);
        if (line.Contains("tint") || line.Count(c => c == ' ') != 2) throw new Exception("format: " + line);
        if (Parse(line) is not { } back || back.Get("text") != note.Opts["text"] || back.Get("tint") != "auto") throw new Exception("parse: " + line);
        if (Parse("clock m tint=neon;evil=1;h12=1") is not { } c || c.Opts.ContainsKey("tint") || c.Opts.ContainsKey("evil") || c.Get("h12") != "1") throw new Exception("unknown values kept");
        if (Parse("clock xl") is not { Size: "s" } || Parse("nope m") is not null) throw new Exception("size/kind fallback");
        if (Parse("game l game=steam%3A1245620") is not { } g || g.Get("game") != "steam:1245620") throw new Exception("steam game");
        // 13.0: вид — у любого виджета; новые размеры — только тем, у кого есть родной того же склада.
        if (Parse("friends xl style=glass;corner=square;label=%D0%9C%D0%BE%D0%B8;opacity=75") is not { Size: "xl" } f || f.Get("style") != "glass" || f.Get("label") != "Мои" || f.Get("opacity") != "75")
            throw new Exception("look options on friends");
        if (Parse("streak t") is not { Size: "t" } || Parse("streak w") is not { Size: "s" } || Parse("tip w") is not { Size: "w" } || Parse("continue t") is not { Size: "m" })
            throw new Exception("new sizes");
        if (Parse("clock s style=neon;opacity=5") is not { } bad || bad.Opts.Count != 0) throw new Exception("bad look values kept");
        if (Format(new Entry("clock", "t", new Dictionary<string, string> { ["style"] = "solid", ["caption"] = "0" })) != "clock t caption=0") throw new Exception("look defaults written");
        return "options escaped, filtered and defaults dropped; look on every widget; tall/wide/xl sizes";
    }
}
