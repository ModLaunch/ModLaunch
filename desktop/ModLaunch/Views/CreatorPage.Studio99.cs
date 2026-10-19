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
    /// 9.9: «Студия автора» — шапка главной Creator Hub: аватар в рамке, цифры по своим модам
    /// (проекты, в Hub, загрузки, лайки) и «путь автора» от первого проекта до первой продажи.
    /// </summary>
    Control StudioHeader()
    {
        var projects = Projects.List();
        var published = (_hub ?? []).Where(m => m.Mine).ToList();
        var selling = (_market ?? []).Where(l => l.Mine).ToList();
        var built = Directory.Exists(Projects.OutDir) && Directory.EnumerateFiles(Projects.OutDir).Any();

        var name = ProfileKit.PlayerName();
        var who = Ui.Col(4,
            new TextBlock { Text = I18n.T("v99.cr.studio").ToUpperInvariant(), FontSize = 11, FontWeight = FontWeight.Bold, LetterSpacing = 2, Foreground = Ui.Hex("#E9E3FF") },
            new TextBlock { Text = name, FontFamily = Look.Display, FontSize = 26, FontWeight = FontWeight.Bold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis },
            Ui.Row(18,
                Gx.Stat(projects.Count.ToString(), I18n.T("v99.cr.projects"), Brushes.White, 20, Ui.Hex("#D9D2FF")),
                Gx.Stat(published.Count.ToString(), I18n.T("v99.cr.published"), Brushes.White, 20, Ui.Hex("#D9D2FF")),
                Gx.Stat(I18n.Compact(published.Sum(m => m.Downloads)), I18n.T("v99.cr.downloads"), Brushes.White, 20, Ui.Hex("#D9D2FF")),
                Gx.Stat(I18n.Compact(published.Sum(m => m.Likes)), I18n.T("v99.cr.likes"), Brushes.White, 20, Ui.Hex("#D9D2FF"))));
        who.VerticalAlignment = VerticalAlignment.Center;

        // Путь автора: четыре шага, сделанные — с галочкой, следующий — подсвечен.
        var steps = new (string Key, bool Done, Action Go)[]
        {
            ("v99.cr.path.create", projects.Count > 0, CreateNew),
            ("v99.cr.path.build", built, () => Go("mine")),
            ("v99.cr.path.publish", published.Count > 0, PublishArchive),
            ("v99.cr.path.sell", selling.Count > 0, () => Go("studio")),
        };
        var next = Array.FindIndex(steps, s => !s.Done);
        var path = Ui.Col(6, new TextBlock { Text = I18n.T("v99.cr.path", ("done", steps.Count(s => s.Done)), ("all", steps.Length)), FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Brushes.White });
        for (var i = 0; i < steps.Length; i++)
        {
            var (key, done, go) = steps[i];
            var mark = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11),
                Background = done ? Ui.Res("Good") : i == next ? Brushes.White : new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
                Child = done ? Ui.Icon(Icons.Check, 12, Ui.Hex("#0F1116")) : new TextBlock { Text = (i + 1).ToString(), FontSize = 11, FontWeight = FontWeight.Bold, Foreground = i == next ? Ui.Hex("#5B3FD9") : Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            var row = new Border
            {
                Classes = { "home-row" }, CornerRadius = new CornerRadius(10), Padding = new Thickness(6, 4),
                Background = i == next ? new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)) : Brushes.Transparent,
                Child = Ui.Row(10, mark, new TextBlock { Text = I18n.T(key), FontSize = 13, FontWeight = i == next ? FontWeight.Bold : FontWeight.Normal, Foreground = Brushes.White, Opacity = done ? 0.7 : 1, VerticalAlignment = VerticalAlignment.Center }),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            };
            StoreKit.OnClick(row, go);
            path.Children.Add(row);
        }
        path.VerticalAlignment = VerticalAlignment.Center;
        path.MinWidth = 260;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 22, Margin = new Thickness(26, 22) };
        grid.Children.Add(ProfileKit.Avatar(name, 84, ring: 5));
        Grid.SetColumn(who, 1);
        grid.Children.Add(who);
        Grid.SetColumn(path, 2);
        grid.Children.Add(path);
        var back = new Panel
        {
            Children =
            {
                new Border { Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse("#5B3FD9"), 0), new GradientStop(Color.Parse("#B03A9E"), 0.6), new GradientStop(Color.Parse("#2A1650"), 1) },
                } },
                Gx.Scanlines(),
            },
        };
        return new Border { Classes = { "card" }, Padding = new Thickness(0), CornerRadius = new CornerRadius(20), ClipToBounds = true, Child = new Panel { Children = { back, grid } } };
    }

    /// <summary>9.9: быстрые инструменты — четыре цветные плитки: ModScript X, коды, модели, справка.</summary>
    Control Tools()
    {
        var row = new UniformGrid { Columns = 4 };
        void Add(string icon, string key, string from, string to, string tab)
        {
            var words = Ui.Col(2,
                new TextBlock { Text = I18n.T(key), FontWeight = FontWeight.Bold, FontSize = 15, Foreground = Brushes.White },
                new TextBlock { Text = I18n.T(key + ".text"), FontSize = 12, Foreground = Ui.Hex("#EDEAF6"), TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis });
            var tile = new Border
            {
                Classes = { "store-tile" }, CornerRadius = new CornerRadius(16), Padding = new Thickness(16, 14), Margin = new Thickness(row.Children.Count == 0 ? 0 : 7, 0, row.Children.Count == 3 ? 0 : 7, 0),
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse(from), 0), new GradientStop(Color.Parse(to), 1) },
                },
                Child = Ui.Row(12, new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), Child = Ui.Icon(icon, 20, Brushes.White) }, words),
            };
            StoreKit.OnClick(tile, () => Go(tab));
            row.Children.Add(tile);
        }
        Add(Icons.Zap, "v99.cr.tool.modx", "#7C5CFF", "#3A2A9A", "inject");
        Add(Icons.Code, "v99.cr.tool.codes", "#0EA5A4", "#0B4F6C", "codes");
        Add(Icons.Cube, "v99.cr.tool.models", "#F97316", "#9A3412", "models");
        Add(Icons.Book, "v99.cr.tool.docs", "#EC4899", "#7E1D52", "docs");
        return row;
    }
}
