using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Games;

namespace ModLaunch.Views;

/// <summary>
/// 30.2: «Итоги» — сколько и во что играли за неделю, месяц, год или всё время, по играм из всех лаунчеров.
/// В Steam такое (Steam Replay) — раз в год и только по Steam. Неделя/месяц/год — из журнала по дням (PlayLog),
/// «всё время» — общие часы (у Steam — его собственные, у остальных — учёт ModLaunch).
/// </summary>
public sealed class RecapPage : Page
{
    public override string Title => I18n.T("v30.recap.title");

    static readonly string[] Periods = ["week", "month", "lastMonth", "year", "all"];
    string _period = Settings.Data.Str("recapPeriod") is { } p && Periods.Contains(p) ? p : "month";

    public RecapPage()
    {
        void Changed() => Dispatcher.UIThread.Post(() => { if (MainWindow.Current?.CurrentPage == this) Build(); });
        AttachedToVisualTree += (_, _) => { PlayLog.Changed += Changed; AllGames.Changed += Changed; };
        DetachedFromVisualTree += (_, _) => { PlayLog.Changed -= Changed; AllGames.Changed -= Changed; };
    }

    /// <summary>Границы периода (включительно) по местному времени.</summary>
    internal static (DateOnly From, DateOnly To) Range(string period, DateOnly today) => period switch
    {
        "week" => (today.AddDays(-(((int)today.DayOfWeek + 6) % 7)), today),
        "lastMonth" => (new DateOnly(today.Year, today.Month, 1).AddMonths(-1), new DateOnly(today.Year, today.Month, 1).AddDays(-1)),
        "year" => (new DateOnly(today.Year, 1, 1), today),
        _ => (new DateOnly(today.Year, today.Month, 1), today),
    };

    static string NameOf(string id) =>
        AllGames.Current?.FirstOrDefault(g => g.TimeId == id)?.Name
        ?? AppState.Games.FirstOrDefault(x => x.Def.Id == id)?.Def.Name
        ?? PlayLog.NameOf(id)
        ?? id;

    static string Duration(long seconds) => PlayTime.Format(seconds * 1000);

    public override void Build()
    {
        var tabs = Ui.Row(6);
        foreach (var p in Periods)
        {
            var id = p;
            tabs.Children.Add(Ui.Button(I18n.T("v30.recap." + p), () =>
            {
                _period = id;
                Settings.Data["recapPeriod"] = id;
                Settings.Save();
                Build();
            }, _period == p ? "primary" : "ghost"));
        }
        var body = Ui.Col(18,
            Ui.Col(4, Ui.Text(I18n.T("v30.recap.title"), "h1"), Ui.Text(I18n.T("v30.recap.lead"), "muted", wrap: true)),
            tabs);
        if (_period == "all") AllTime(body); else Period(body);
        body.Children.Add(Ui.Text(I18n.T("v30.recap.note"), "tiny muted", wrap: true));
        body.Margin = new Thickness(34, 28);
        body.MaxWidth = 1040;
        body.HorizontalAlignment = HorizontalAlignment.Left;
        Content = new ScrollViewer { Content = body };
    }

    // ---------------------------------------------------------------- неделя, месяц, год

    void Period(StackPanel body)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var (from, to) = Range(_period, today);
        var s = PlayLog.Summarize(from, to);
        if (s.Seconds == 0)
        {
            body.Children.Add(Ui.Card(Ui.Col(8, Ui.Text(I18n.T("v30.recap.empty"), "h3"),
                Ui.Text(I18n.T("v30.recap.empty.text"), "muted", wrap: true)), 22));
            return;
        }
        var days = to.DayNumber - from.DayNumber + 1;
        var tiles = new UniformGrid { Columns = 4 };
        tiles.Children.Add(Tile(I18n.T("v30.recap.played"), Duration(s.Seconds)));
        tiles.Children.Add(Tile(I18n.T("v30.recap.games"), s.Games.Count.ToString(I18n.Culture)));
        tiles.Children.Add(Tile(I18n.T("v30.recap.days"), I18n.T("v30.recap.daysOf", ("n", s.ActiveDays), ("of", days))));
        tiles.Children.Add(Tile(I18n.T("v30.recap.streak"), I18n.T("v30.recap.streak.n", ("n", s.Streak))));
        body.Children.Add(tiles);

        // По дням (неделя, месяц) или по месяцам (год) — одна серия, один цвет.
        List<(string Label, string Tip, long Seconds)> bars;
        if (_period == "year")
            bars = Enumerable.Range(1, 12).Select(m =>
            {
                var sum = s.Days.Where(kv => kv.Key.Month == m).Sum(kv => kv.Value);
                var name = I18n.Culture.DateTimeFormat.GetAbbreviatedMonthName(m);
                return (name, I18n.Culture.DateTimeFormat.GetMonthName(m) + ": " + Duration(sum), sum);
            }).ToList();
        else
            bars = Enumerable.Range(0, days).Select(i =>
            {
                var d = from.AddDays(i);
                var sum = s.Days.GetValueOrDefault(d);
                var label = _period == "week" ? I18n.Culture.DateTimeFormat.GetAbbreviatedDayName(d.DayOfWeek) : d.Day % 5 == 0 || d.Day == 1 ? d.Day.ToString(I18n.Culture) : "";
                return (label, d.ToDateTime(TimeOnly.MinValue).ToString("ddd, d MMM", I18n.Culture) + ": " + (sum > 0 ? Duration(sum) : I18n.T("v30.recap.none")), sum);
            }).ToList();
        body.Children.Add(Ui.Card(Ui.Col(12, Ui.Text(I18n.T(_period == "year" ? "v30.recap.byMonth" : "v30.recap.byDay"), "h3"), Bars(bars)), 20));

        var cols = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,2*"), ColumnSpacing = 16 };
        var top = Top(s.Games.Take(10).Select(g => (NameOf(g.Id), g.Seconds)).ToList(), s.Seconds);
        cols.Children.Add(top);
        var facts = Ui.Col(10, Ui.Text(I18n.T("v30.recap.facts"), "h3"));
        if (s.BestDay is { } best)
            facts.Children.Add(Fact(Icons.Star, I18n.T("v30.recap.best", ("day", best.ToDateTime(TimeOnly.MinValue).ToString("d MMMM", I18n.Culture)), ("time", Duration(s.Days[best])))));
        var weekday = Array.IndexOf(s.Weekdays, s.Weekdays.Max());
        if (s.ActiveDays >= 3)
            facts.Children.Add(Fact(Icons.Clock, I18n.T("v30.recap.weekday", ("day", I18n.Culture.DateTimeFormat.GetDayName((DayOfWeek)((weekday + 1) % 7))))));
        facts.Children.Add(Fact(Icons.Chart, I18n.T("v30.recap.perDay", ("time", Duration(s.Seconds / Math.Max(1, s.ActiveDays))))));
        if (s.Games.Count > 0)
            facts.Children.Add(Fact(Icons.Gamepad, I18n.T("v30.recap.share", ("name", NameOf(s.Games[0].Id)), ("pct", (int)Math.Round(100.0 * s.Games[0].Seconds / s.Seconds)))));
        var factsCard = Ui.Card(facts, 20);
        Grid.SetColumn(factsCard, 1);
        cols.Children.Add(factsCard);
        body.Children.Add(cols);
    }

    // ---------------------------------------------------------------- всё время

    static void AllTime(StackPanel body)
    {
        var games = (AllGames.Current ?? []).Select(g => (Game: g, Minutes: AllGames.Played(g).Minutes)).Where(x => x.Minutes > 0)
            .OrderByDescending(x => x.Minutes).ToList();
        if (games.Count == 0)
        {
            body.Children.Add(Ui.Card(Ui.Text(I18n.T("v30.recap.empty"), "muted", wrap: true), 22));
            return;
        }
        var total = games.Sum(x => x.Minutes) * 60;
        var tiles = new UniformGrid { Columns = 3 };
        tiles.Children.Add(Tile(I18n.T("v30.recap.played"), Duration(total)));
        tiles.Children.Add(Tile(I18n.T("v30.recap.games"), games.Count.ToString(I18n.Culture)));
        tiles.Children.Add(Tile(I18n.T("v30.recap.stores"), games.Select(x => x.Game.Store).Distinct().Count().ToString(I18n.Culture)));
        body.Children.Add(tiles);
        body.Children.Add(Top(games.Take(15).Select(x => (x.Game.Name, x.Minutes * 60)).ToList(), total));
    }

    // ---------------------------------------------------------------- куски

    static Control Tile(string label, string value)
    {
        var c = Ui.Card(Ui.Col(4, Ui.Text(label, "small muted"), Ui.Text(value, "h2")), 16);
        c.Margin = new Thickness(0, 0, 12, 0);
        return c;
    }

    static Control Fact(string icon, string text) => Ui.Row(10, Ui.Icon(icon, 16, Ui.Res("Brand2")), Ui.Text(text, "small", wrap: true));

    /// <summary>Колонки-доли без строк: «0,55*» в русской культуре разметка не понимает.</summary>
    internal static ColumnDefinitions Stars(params double[] parts)
    {
        var defs = new ColumnDefinitions();
        foreach (var p in parts) defs.Add(new ColumnDefinition(new GridLength(Math.Max(0, p), GridUnitType.Star)));
        return defs;
    }

    /// <summary>Самые наигранные: имя, время, доля и тонкая полоса (одна серия — один цвет).</summary>
    static Control Top(List<(string Name, long Seconds)> games, long total)
    {
        var col = Ui.Col(12, Ui.Text(I18n.T("v30.recap.top"), "h3"));
        var max = Math.Max(1, games.Count > 0 ? games.Max(g => g.Seconds) : 1);
        foreach (var (name, seconds) in games)
        {
            var head = new DockPanel();
            var value = Ui.Text($"{Duration(seconds)} · {Math.Round(100.0 * seconds / Math.Max(1, total)):0}%", "small muted");
            DockPanel.SetDock(value, Dock.Right);
            head.Children.Add(value);
            head.Children.Add(new TextBlock { Text = name, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Ui.Res("Text") });
            var track = new Grid { Height = 6, ColumnDefinitions = Stars((double)seconds / max, 1 - (double)seconds / max) };
            track.Children.Add(new Border { Background = Ui.Res("Brand"), CornerRadius = new CornerRadius(0, 3, 3, 0), MinWidth = 3 });
            var row = Ui.Col(5, head, track);
            ToolTip.SetTip(row, $"{name}: {Duration(seconds)}");
            col.Children.Add(row);
        }
        return Ui.Card(col, 20);
    }

    /// <summary>
    /// Столбцы по дням или месяцам: тонкие, с зазором 2 px, скруглён только верх (низ стоит на оси), приглушённая ось
    /// и подпись максимума; точное значение — подсказкой при наведении на всю высоту колонки.
    /// </summary>
    static Control Bars(List<(string Label, string Tip, long Seconds)> bars)
    {
        const double Height = 140;
        var max = Math.Max(1, bars.Max(b => b.Seconds));
        var grid = new Grid { Height = Height + 22 };
        foreach (var _ in bars) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var i = 0; i < bars.Count; i++)
        {
            var (label, tip, seconds) = bars[i];
            var h = seconds <= 0 ? 0 : Math.Max(3, Height * seconds / max);
            var bar = new Border
            {
                Height = h, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(1, 0, 1, 22), MaxWidth = 34,
                Background = Ui.Res("Brand"), CornerRadius = new CornerRadius(4, 4, 0, 0),
            };
            // Зона наведения — вся колонка, а не только столбец: низкие дни тоже легко «поймать».
            var hit = new Panel { Background = Brushes.Transparent };
            hit.Children.Add(bar);
            hit.Children.Add(new TextBlock { Text = label, FontSize = 10.5, Foreground = Ui.Res("Faint"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom });
            ToolTip.SetTip(hit, tip);
            Grid.SetColumn(hit, i);
            grid.Children.Add(hit);
        }
        var axis = new Border { Height = 1, Background = Ui.Res("Line"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 21) };
        Grid.SetColumnSpan(axis, Math.Max(1, bars.Count));
        grid.Children.Add(axis);
        var peak = new TextBlock { Text = I18n.T("v30.recap.max", ("time", Duration(max))), FontSize = 10.5, Foreground = Ui.Res("Faint") };
        return Ui.Col(6, peak, grid);
    }

    [SelfTest]
    static string RecapRanges()
    {
        var sunday = new DateOnly(2026, 11, 15);
        if (Range("week", sunday) != (new DateOnly(2026, 11, 9), sunday)) throw new Exception("week starts Monday");
        if (Range("month", sunday) != (new DateOnly(2026, 11, 1), sunday)) throw new Exception("month");
        if (Range("lastMonth", sunday) != (new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31))) throw new Exception("last month");
        if (Range("lastMonth", new DateOnly(2026, 1, 10)) != (new DateOnly(2025, 12, 1), new DateOnly(2025, 12, 31))) throw new Exception("january");
        if (Range("year", sunday) != (new DateOnly(2026, 1, 1), sunday)) throw new Exception("year");
        return "week from Monday, this/last month (across New Year), year to date";
    }
}
