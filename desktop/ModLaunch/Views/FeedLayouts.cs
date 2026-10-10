using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>Какого размера место в раскладке — от него зависит, какую плитку туда поставить.</summary>
public enum Slot
{
    /// <summary>Большое место (2/3 ширины и больше): баннер игры, мод дня, лот маркета.</summary>
    XL,
    /// <summary>Половина ряда.</summary>
    L,
    /// <summary>Треть ряда.</summary>
    M,
    /// <summary>Маленькая плитка (четверть ряда или половина столбца).</summary>
    S,
    /// <summary>Высокая и узкая: обложка игры или мод «стоя».</summary>
    T,
    /// <summary>Во всю ширину, невысокая: полоса-баннер.</summary>
    W,
    /// <summary>Список «Топ-5».</summary>
    List,
    /// <summary>Строка мода (значок, название, кнопка).</summary>
    R,
}

/// <summary>Клетка раскладки: где стоит и сколько занимает.</summary>
public sealed record FeedCell(int Col, int Row, int ColSpan, int RowSpan, Slot Slot);

/// <summary>
/// Раскладка блока ленты: столбцы и строки сетки (как в Grid), высота блока и клетки.
/// Hero — в раскладке есть большое место, ей можно начинать ленту.
/// Shelf — особая раскладка: полка обложек с прокруткой вбок.
/// </summary>
public sealed record FeedLayout(string Id, string Cols, string Rows, double Height, bool Hero, FeedCell[] Cells, bool Shelf = false);

/// <summary>
/// 9.3: раскладки плиток ленты — 25 штук. Каждый блок ленты берёт раскладку наугад; одна и та же
/// не встаёт больше трёх раз подряд. Все раскладки — ровные сетки с одинаковыми промежутками
/// (края плиток совпадают), поэтому лента разная, но не «потыканная как попало».
/// </summary>
public static class FeedLayouts
{
    static FeedCell C(int col, int row, Slot slot, int colSpan = 1, int rowSpan = 1) => new(col, row, colSpan, rowSpan, slot);

    public static readonly FeedLayout[] All =
    [
        new("solo", "*", "*", 340, true, [C(0, 0, Slot.XL)]),
        new("duo", "*,*", "*", 300, false, [C(0, 0, Slot.L), C(1, 0, Slot.L)]),
        new("trio", "*,*,*", "*", 280, false, [C(0, 0, Slot.M), C(1, 0, Slot.M), C(2, 0, Slot.M)]),
        new("quad", "*,*,*,*", "*", 250, false, [C(0, 0, Slot.S), C(1, 0, Slot.S), C(2, 0, Slot.S), C(3, 0, Slot.S)]),
        new("five", "*,*,*,*,*", "*", 230, false, [C(0, 0, Slot.S), C(1, 0, Slot.S), C(2, 0, Slot.S), C(3, 0, Slot.S), C(4, 0, Slot.S)]),
        new("bigL2", "2*,*", "*,*", 400, true, [C(0, 0, Slot.XL, rowSpan: 2), C(1, 0, Slot.S), C(1, 1, Slot.S)]),
        new("bigR2", "*,2*", "*,*", 400, true, [C(0, 0, Slot.S), C(0, 1, Slot.S), C(1, 0, Slot.XL, rowSpan: 2)]),
        new("bigL4", "2*,*,*", "*,*", 400, true, [C(0, 0, Slot.XL, rowSpan: 2), C(1, 0, Slot.S), C(2, 0, Slot.S), C(1, 1, Slot.S), C(2, 1, Slot.S)]),
        new("bigR4", "*,*,2*", "*,*", 400, true, [C(0, 0, Slot.S), C(1, 0, Slot.S), C(0, 1, Slot.S), C(1, 1, Slot.S), C(2, 0, Slot.XL, rowSpan: 2)]),
        new("tallMid", "*,*,*", "*,*", 440, false, [C(0, 0, Slot.S), C(0, 1, Slot.S), C(1, 0, Slot.T, rowSpan: 2), C(2, 0, Slot.S), C(2, 1, Slot.S)]),
        new("wideTop", "*,*,*", "3*,2*", 500, true, [C(0, 0, Slot.W, colSpan: 3), C(0, 1, Slot.S), C(1, 1, Slot.S), C(2, 1, Slot.S)]),
        new("wideBottom", "*,*,*", "2*,3*", 500, false, [C(0, 0, Slot.S), C(1, 0, Slot.S), C(2, 0, Slot.S), C(0, 1, Slot.W, colSpan: 3)]),
        new("mosaicL", "*,*,*,*", "*,*", 420, true, [C(0, 0, Slot.XL, 2, 2), C(2, 0, Slot.W, colSpan: 2), C(2, 1, Slot.S), C(3, 1, Slot.S)]),
        new("mosaicR", "*,*,*,*", "*,*", 420, true, [C(0, 0, Slot.W, colSpan: 2), C(0, 1, Slot.S), C(1, 1, Slot.S), C(2, 0, Slot.XL, 2, 2)]),
        new("listL", "*,2*", "*", 400, true, [C(0, 0, Slot.List), C(1, 0, Slot.XL)]),
        new("listR", "2*,*", "*", 400, true, [C(0, 0, Slot.XL), C(1, 0, Slot.List)]),
        new("towersL", "*,*,2*", "*", 400, false, [C(0, 0, Slot.T), C(1, 0, Slot.T), C(2, 0, Slot.L)]),
        new("towersR", "2*,*,*", "*", 400, false, [C(0, 0, Slot.L), C(1, 0, Slot.T), C(2, 0, Slot.T)]),
        new("wide32", "3*,2*", "*", 320, true, [C(0, 0, Slot.XL), C(1, 0, Slot.M)]),
        new("wide23", "2*,3*", "*", 320, true, [C(0, 0, Slot.M), C(1, 0, Slot.XL)]),
        new("stack3", "2*,*", "*,*,*", 420, true, [C(0, 0, Slot.XL, rowSpan: 3), C(1, 0, Slot.R), C(1, 1, Slot.R), C(1, 2, Slot.R)]),
        new("grid6", "*,*,*", "*,*", 420, false, [C(0, 0, Slot.S), C(1, 0, Slot.S), C(2, 0, Slot.S), C(0, 1, Slot.S), C(1, 1, Slot.S), C(2, 1, Slot.S)]),
        new("ladder", "*,*,*,*", "*,*", 440, false, [C(0, 0, Slot.T, rowSpan: 2), C(1, 0, Slot.W, colSpan: 2), C(1, 1, Slot.S), C(2, 1, Slot.S), C(3, 0, Slot.T, rowSpan: 2)]),
        new("listMid", "*,*,*", "*", 400, false, [C(0, 0, Slot.M), C(1, 0, Slot.List), C(2, 0, Slot.M)]),
        new("shelf", "*", "*", 270, false, [C(0, 0, Slot.T)], Shelf: true),
    ];

    static readonly Random Rng = new();
    static string? _last;
    static int _streak;

    /// <summary>Сколько раз подряд одна раскладка может встать в ленте.</summary>
    public const int MaxInARow = 3;

    /// <summary>Раскладка для следующего блока: наугад; та же — не больше трёх раз подряд.</summary>
    public static FeedLayout Next(bool heroOnly = false)
    {
        var pool = All.Where(l => !heroOnly || l.Hero).ToList();
        if (_last is string last && _streak >= MaxInARow) pool.RemoveAll(l => l.Id == last);
        var pick = pool[Rng.Next(pool.Count)];
        if (pick.Id == _last) _streak++;
        else { _last = pick.Id; _streak = 1; }
        return pick;
    }

    public static void Reset() { _last = null; _streak = 0; }

    /// <summary>Правило ленты: не меньше 19 раскладок, одна и та же — не больше трёх раз подряд.</summary>
    [SelfTest]
    static string NeverFourInARow()
    {
        var (last, streak) = (_last, _streak);
        try
        {
            if (All.Length < 19) throw new Exception($"only {All.Length} layouts");
            if (All.Select(l => l.Id).Distinct().Count() != All.Length) throw new Exception("duplicate layout ids");
            foreach (var l in All)
            {
                var cols = l.Cols.Split(',').Length;
                var rows = l.Rows.Split(',').Length;
                // Каждая клетка сетки занята ровно одной плиткой — без дыр и наложений.
                var used = new int[cols, rows];
                foreach (var c in l.Cells)
                    for (var x = c.Col; x < c.Col + c.ColSpan; x++)
                        for (var y = c.Row; y < c.Row + c.RowSpan; y++)
                        {
                            if (x >= cols || y >= rows) throw new Exception($"{l.Id}: cell outside the grid");
                            used[x, y]++;
                        }
                foreach (var n in used) if (n != 1) throw new Exception($"{l.Id}: grid has a hole or an overlap");
            }
            _last = null; _streak = 0;
            var seen = new HashSet<string>();
            var run = 0;
            string? prev = null;
            for (var i = 0; i < 20000; i++)
            {
                var l = Next(i % 7 == 0);
                seen.Add(l.Id);
                run = l.Id == prev ? run + 1 : 1;
                prev = l.Id;
                if (run > MaxInARow) throw new Exception($"{l.Id} came {run} times in a row");
            }
            if (seen.Count != All.Length) throw new Exception("not all layouts were picked");
            return $"{All.Length} feed layouts, every grid is full, never more than {MaxInARow} of the same in a row";
        }
        finally { (_last, _streak) = (last, streak); }
    }
}
