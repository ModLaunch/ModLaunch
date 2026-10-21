using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

public sealed partial class CreatorPage
{
    /// <summary>
    /// «Студия автора» — шапка главной Creator Hub: аватар, цифры по своим модам (проекты, в Hub,
    /// загрузки, лайки) и «путь автора» от первого проекта до первой продажи. 10.0 — спокойнее:
    /// обычная карточка без градиента и полос, следующий шаг пути — единственное, что выделено.
    /// </summary>
    Control StudioHeader()
    {
        var projects = Projects.List();
        var published = (_hub ?? []).Where(m => m.Mine).ToList();
        var selling = (_market ?? []).Where(l => l.Mine).ToList();
        // 12.0: сборки проектов лежат по папкам (_build/<проект>/) — ищем и в них.
        var built = Directory.Exists(Projects.OutDir) && Directory.EnumerateFiles(Projects.OutDir, "*.zip", SearchOption.AllDirectories).Any();

        var name = ProfileKit.PlayerName();
        var muted = Ui.Res("Muted");
        var who = Ui.Col(6,
            new TextBlock { Text = name, FontFamily = Look.Display, FontSize = 19, FontWeight = FontWeight.Bold, TextTrimming = TextTrimming.CharacterEllipsis },
            Ui.Row(22,
                Gx.Stat(projects.Count.ToString(), I18n.T("v99.cr.projects"), Ui.Res("Text"), 16, muted),
                Gx.Stat(published.Count.ToString(), I18n.T("v99.cr.published"), Ui.Res("Text"), 16, muted),
                Gx.Stat(I18n.Compact(published.Sum(m => m.Downloads)), I18n.T("v99.cr.downloads"), Ui.Res("Text"), 16, muted),
                Gx.Stat(I18n.Compact(published.Sum(m => m.Likes)), I18n.T("v99.cr.likes"), Ui.Res("Text"), 16, muted)));
        who.VerticalAlignment = VerticalAlignment.Center;

        // Путь автора: четыре шага в ряд; сделанные — приглушены с галочкой, следующий — обведён акцентом.
        var steps = new (string Key, bool Done, Action Go)[]
        {
            ("v99.cr.path.create", projects.Count > 0, CreateNew),
            ("v99.cr.path.build", built, () => Go("mine")),
            ("v99.cr.path.publish", published.Count > 0, PublishArchive),
            ("v99.cr.path.sell", selling.Count > 0, () => Go("studio")),
        };
        var next = Array.FindIndex(steps, s => !s.Done);
        var done = steps.Count(s => s.Done);
        var progress = new ProgressBar { Minimum = 0, Maximum = steps.Length, Value = done, Width = 140, Height = 4, MinHeight = 4, VerticalAlignment = VerticalAlignment.Center };
        var pathHead = Ui.Row(10, Ui.Text(I18n.T("v99.cr.path", ("done", done), ("all", steps.Length)), "small muted"), progress);
        pathHead.VerticalAlignment = VerticalAlignment.Center;

        var row = new UniformGrid { Columns = steps.Length, Margin = new Thickness(0, 14, 0, 0) };
        for (var i = 0; i < steps.Length; i++)
        {
            var (key, isDone, go) = steps[i];
            var isNext = i == next;
            var mark = new Border
            {
                Width = 20, Height = 20, CornerRadius = new CornerRadius(10),
                Background = isDone ? Ui.Res("Surface3") : Brushes.Transparent,
                BorderBrush = isNext ? Ui.Res("Brand") : Ui.Res("Line"), BorderThickness = new Thickness(isDone ? 0 : 1.5),
                Child = isDone ? Ui.Icon(Icons.Check, 11, muted) : new TextBlock { Text = (i + 1).ToString(), FontSize = 10.5, FontWeight = FontWeight.Bold, Foreground = isNext ? Ui.Res("Brand2") : muted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            var label = new TextBlock { Text = I18n.T(key), FontSize = 12.5, FontWeight = isNext ? FontWeight.SemiBold : FontWeight.Normal, Foreground = isNext ? Ui.Res("Text") : muted, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var step = new Border
            {
                Classes = { "home-row" }, CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 8), Margin = new Thickness(i == 0 ? 0 : 4, 0, i == steps.Length - 1 ? 0 : 4, 0),
                Background = isNext ? Ui.Res("Surface2") : Brushes.Transparent,
                BorderBrush = isNext ? Ui.Res("Line") : Brushes.Transparent, BorderThickness = new Thickness(1),
                Child = Ui.Row(10, mark, label),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            };
            StoreKit.OnClick(step, go);
            row.Children.Add(step);
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 18 };
        grid.Children.Add(ProfileKit.Avatar(name, 52, ring: 3));
        Grid.SetColumn(who, 1);
        grid.Children.Add(who);
        Grid.SetColumn(pathHead, 2);
        grid.Children.Add(pathHead);
        return new Border { Classes = { "card" }, Padding = new Thickness(20, 18), CornerRadius = new CornerRadius(16), Child = Ui.Col(0, grid, row) };
    }

    /// <summary>Быстрые инструменты: ModScript X, коды, модели, справка. 10.0 — одноцветные спокойные плитки.</summary>
    Control Tools()
    {
        var row = new UniformGrid { Columns = 4 };
        void Add(string icon, string key, string tab)
        {
            var words = Ui.Col(2,
                new TextBlock { Text = I18n.T(key), FontWeight = FontWeight.SemiBold, FontSize = 14 },
                new TextBlock { Text = I18n.T(key + ".text"), FontSize = 12, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis });
            words.VerticalAlignment = VerticalAlignment.Center;
            var tile = new Border
            {
                Classes = { "card", "store-tile" }, CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 12), Margin = new Thickness(row.Children.Count == 0 ? 0 : 6, 0, row.Children.Count == 3 ? 0 : 6, 0),
                Child = Ui.Row(12, new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(10), Background = Ui.Res("Surface2"), Child = Ui.Icon(icon, 17, Ui.Res("Brand2")) }, words),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            };
            StoreKit.OnClick(tile, () => Go(tab));
            row.Children.Add(tile);
        }
        Add(Icons.Zap, "v99.cr.tool.modx", "inject");
        Add(Icons.Code, "v99.cr.tool.codes", "codes");
        Add(Icons.Cube, "v99.cr.tool.models", "models");
        Add(Icons.Book, "v99.cr.tool.docs", "docs");
        return row;
    }
}
