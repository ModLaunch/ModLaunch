using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>
/// 16.0: как выглядит путь наград за уровни — ступени на линии, у каждой награда и номер уровня.
/// Пройденные — в цвете, следующая — с обводкой, большие (каждый 5-й уровень) — крупнее и со свечением.
/// Один и тот же вид на главной (виджет «Путь наград»), в шапке профиля и в окне «Забрать».
/// </summary>
public static class RoadKit
{
    /// <summary>Уровень, очки и сколько до следующего — по всем достижениям.</summary>
    public static (int Level, int Xp, int Next) Now()
    {
        var (level, xp, next) = Achievements.Level(Achievements.All());
        return (Math.Min(level, LevelRoad.Max), xp, next);
    }

    /// <summary>Маленький вид награды для ступени: рамка — кольцо, баннер — полоска, титул — буква на плашке, значок — медаль.</summary>
    public static Control Mini(Reward r, double size)
    {
        switch (r.Kind)
        {
            case "frame":
                return new Border
                {
                    Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Padding = new Thickness(Math.Max(3, size / 8)), Background = ProfileKit.FrameBrush(r.Id),
                    Child = new Border { CornerRadius = new CornerRadius(size / 2), Background = Ui.Res("Surface"), Child = Ui.Icon(Icons.User, size * 0.36, Ui.Res("Muted")) },
                };
            case "banner":
                return new Border { Width = size * 1.3, Height = size * 0.78, CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = ProfileKit.Banner(r.Id) };
            case "title":
                var name = Cosmetics.Name(r);
                return new Border
                {
                    Height = size * 0.62, MinWidth = size * 0.9, MaxWidth = size * 1.6, CornerRadius = new CornerRadius(999), Padding = new Thickness(7, 0), ClipToBounds = true,
                    Child = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = ProfileKit.Title(r.Id, 11) },
                };
            case "config":
                return ProfileKit.ConfigShot(r.Id, size * 1.3);
            default:
                return ProfileKit.Badge(r.Id, size);
        }
    }

    /// <summary>Ступени пути вокруг уровня: count штук, с линией прогресса позади.</summary>
    public static Control Strip(int level, int count, double node = 46, IBrush? accent = null)
    {
        accent ??= Ui.Res("Brand");
        var steps = LevelRoad.Around(level, count);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("*", steps.Count))) };
        // Линия от центра первой ступени до центра последней: пройденная часть — цветом, остальное — серым.
        var reached = steps.Count(s => s.Level <= level);
        var span = Math.Max(1, steps.Count - 1);
        var done = Math.Clamp(reached - 1 + (reached > 0 && reached < steps.Count ? 0.5 : 0), 0, span);
        var progress = new Grid { ColumnDefinitions = new ColumnDefinitions($"{Math.Max(0.0001, done)}*,{Math.Max(0.0001, span - done)}*") };
        progress.Children.Add(new Border { Height = 3, CornerRadius = new CornerRadius(2), Background = accent, IsVisible = done > 0 });
        var rail = new Panel { Children = { new Border { Height = 3, CornerRadius = new CornerRadius(2), Background = Ui.Res("Surface3") }, progress } };
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions($"1*,{span * 2}*,1*"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, node / 2 - 1, 0, 0) };
        Grid.SetColumn(rail, 1);
        line.Children.Add(rail);
        Grid.SetColumnSpan(line, steps.Count);
        grid.Children.Add(line);

        for (var i = 0; i < steps.Count; i++)
        {
            var (lv, reward) = steps[i];
            var have = lv <= level;
            var next = lv == level + 1;
            var big = LevelRoad.Milestone(lv);
            var size = big ? node : node * 0.84;
            var mini = Mini(reward, size * 0.72);
            mini.HorizontalAlignment = HorizontalAlignment.Center;
            mini.VerticalAlignment = VerticalAlignment.Center;
            if (!have) mini.Opacity = 0.45;
            var disc = new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), HorizontalAlignment = HorizontalAlignment.Center,
                Background = have ? Ui.Res("Surface2") : Ui.Res("Surface"),
                BorderBrush = have ? accent : next ? Ui.Res("Brand2") : Ui.Res("Line"), BorderThickness = new Thickness(have || next ? 2 : 1),
                BoxShadow = big && have ? BoxShadows.Parse($"0 0 18 1 {Gx.Alpha((accent as ISolidColorBrush)?.Color ?? Colors.MediumPurple, 120)}") : default,
                Child = mini, Margin = new Thickness(0, (node - size) / 2, 0, 0),
            };
            var label = new TextBlock
            {
                Text = I18n.T("v16.road.lv", ("n", lv)), FontSize = 11, FontWeight = big || next ? FontWeight.Bold : FontWeight.Normal, HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = have ? Ui.Res("Text") : next ? Ui.Res("Brand2") : Ui.Res("Faint"), Margin = new Thickness(0, 6, 0, 0),
            };
            // 17.0: на уровне есть и сборка создателя — шестерёнка у края ступени.
            Control top = disc;
            var bonus = LevelRoad.BonusAt(lv);
            if (bonus is not null)
            {
                var gear = new Border
                {
                    Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = have ? accent : Ui.Res("Surface3"),
                    BorderBrush = Ui.Res("Surface"), BorderThickness = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(size * 0.8, (node - size) / 2 - 2, 0, 0),
                    Child = Ui.Icon(Icons.Settings, 10, have ? Ui.Res("OnBrand") : Ui.Res("Muted")),
                };
                top = new Panel { Children = { disc, gear } };
            }
            var cell = Ui.Col(0, top, label);
            var tip = I18n.T("v16.road.tip", ("n", lv), ("kind", Cosmetics.KindName(reward.Kind)), ("name", Cosmetics.Name(reward)));
            if (bonus is not null) tip += " + " + Cosmetics.KindName("config").ToLowerInvariant() + " «" + Cosmetics.Name(bonus) + "»";
            ToolTip.SetTip(cell, tip + (have ? "" : " · " + I18n.T("v16.road.closed")));
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }
        return grid;
    }

    /// <summary>Строка о следующей награде: «Рамка „Рубин“ — на 10 уровне».</summary>
    public static string NextLine(int level)
    {
        var next = LevelRoad.Road.FirstOrDefault(x => x.Level > level);
        return next.Reward is null ? I18n.T("v16.road.done") : I18n.T("v16.road.next", ("kind", Cosmetics.KindName(next.Reward.Kind)), ("name", Cosmetics.Name(next.Reward)), ("n", next.Level));
    }

    static Control ClaimCard(Reward r, int lv, Control preview)
    {
        var col = Ui.Col(8, new Border { Height = 66, Child = preview },
            new TextBlock { Text = Cosmetics.Name(r), FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = Cosmetics.KindName(r.Kind) + " · " + I18n.T("v16.road.lv", ("n", lv)), FontSize = 11.5, Foreground = Ui.Res("Muted"), HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        // 17.0: сборку создателя можно применить прямо из окна наград.
        if (r.Kind == "config")
        {
            var apply = Ui.Button(I18n.T("v10.set.apply"), () =>
            {
                Cosmetics.Toggle(r);
                MainWindow.Current?.CloseDialog();
                MainWindow.Current?.Toast(I18n.T("v17.cb.applied", ("name", Cosmetics.Name(r))));
            }, "primary", Icons.Check);
            apply.HorizontalAlignment = HorizontalAlignment.Center;
            col.Children.Add(apply);
        }
        return col;
    }

    /// <summary>Забрать новые награды: окно с тем, что получено, и кнопкой «В профиль».</summary>
    public static void Claim()
    {
        var w = MainWindow.Current;
        if (w is null) return;
        var (level, _, _) = Now();
        var got = LevelRoad.Claim(level);
        if (got.Count == 0) { w.Navigate(() => new ProfilePage("rewards")); return; }
        var wrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var (lv, r) in got.TakeLast(8))
        {
            var preview = ProfileKit.Preview(r, ProfileKit.PlayerName());
            preview.HorizontalAlignment = HorizontalAlignment.Center;
            wrap.Children.Add(new Border
            {
                Classes = { "card" }, Width = 170, Padding = new Thickness(12, 14), Margin = new Thickness(0, 0, 10, 10),
                Child = ClaimCard(r, lv, preview),
            });
        }
        var body = Ui.Col(14, Ui.Text(I18n.T("v16.road.claimed.text", ("n", got.Count), ("level", level)), "muted", wrap: true), wrap);
        w.Dialog(I18n.T("v16.road.claimed"), body, 780,
            Ui.Button(I18n.T("v16.road.wear"), () => { w.CloseDialog(); w.Navigate(() => new ProfilePage("rewards")); }, "primary", Icons.Trophy),
            Ui.Button(I18n.T("common.close"), w.CloseDialog));
        w.RefreshAccount();
        if (w.CurrentPage is HomePage home) home.Build();
    }
}
