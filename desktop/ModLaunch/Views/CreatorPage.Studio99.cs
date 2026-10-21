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
    /// <summary>12.1: цифры автора для шапки Hub — проекты, выложено в Hub и загрузки (null — список Hub ещё не пришёл).</summary>
    internal static (int Projects, int? Published, long? Downloads) AuthorStats()
    {
        var mine = _hub?.Where(m => m.Mine).ToList();
        return (Projects.List().Count, mine?.Count, mine?.Sum(m => m.Downloads));
    }

    /// <summary>
    /// «Путь автора» на Обзоре: от первого проекта до первой продажи — столбиком, как чек-лист в мастерской.
    /// Сделанные шаги — с галочкой, следующий — обведён фирменным цветом (12.1; раньше — шапка «Студии автора»,
    /// её цифры теперь в шапке Hub).
    /// </summary>
    Control AuthorPath()
    {
        var projects = Projects.List();
        var published = (_hub ?? []).Where(m => m.Mine).ToList();
        var selling = (_market ?? []).Where(l => l.Mine).ToList();
        // 12.0: сборки проектов лежат по папкам (_build/<проект>/) — ищем и в них.
        var built = Directory.Exists(Projects.OutDir) && Directory.EnumerateFiles(Projects.OutDir, "*.zip", SearchOption.AllDirectories).Any();
        var steps = new (string Key, bool Done, Action Go)[]
        {
            ("v99.cr.path.create", projects.Count > 0, CreateNew),
            ("v99.cr.path.build", built, () => Go("mine")),
            ("v99.cr.path.publish", published.Count > 0, PublishArchive),
            ("v99.cr.path.sell", selling.Count > 0, () => Go("studio")),
        };
        var next = Array.FindIndex(steps, s => !s.Done);
        var done = steps.Count(s => s.Done);
        var muted = Ui.Res("Muted");

        var list = Ui.Col(2);
        for (var i = 0; i < steps.Length; i++)
        {
            var (key, isDone, go) = steps[i];
            var isNext = i == next;
            var num = HubLook.MonoText((i + 1).ToString("00"), 10, isNext ? HubLook.Violet : muted);
            num.HorizontalAlignment = HorizontalAlignment.Center;
            var mark = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(3),
                Background = isDone ? HubLook.Raised : Brushes.Transparent,
                BorderBrush = isNext ? HubLook.Violet : HubLook.Edge, BorderThickness = new Thickness(isDone ? 0 : 1),
                Child = isDone ? Ui.Icon(Icons.Check, 12, HubLook.Cyan) : num,
            };
            var label = new TextBlock
            {
                Text = I18n.T(key), FontSize = 13, FontWeight = isNext ? FontWeight.SemiBold : FontWeight.Normal, Foreground = isNext ? Ui.Res("Text") : muted,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var row = new Button { Classes = { "hub-row" }, Content = Ui.Row(12, mark, label) };
            row.Click += (_, _) => go();
            list.Children.Add(row);
        }
        // Прогресс — тонкая фирменная полоса.
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions($"{Math.Max(done, 0.001)}*,{Math.Max(steps.Length - done, 0.001)}*"), Height = 3 };
        bar.Children.Add(new Border { Background = HubLook.Signature(), CornerRadius = new CornerRadius(2) });
        var rest = new Border { Background = HubLook.Edge, CornerRadius = new CornerRadius(2) };
        Grid.SetColumn(rest, 1);
        bar.Children.Add(rest);
        return HubLook.Frame(Ui.Col(12,
            HubLook.Head(I18n.T("v12d.home.path"), I18n.T("v12d.home.pathDone", ("done", done), ("all", steps.Length))),
            bar, list), new Thickness(18, 16));
    }

    /// <summary>Быстрые инструменты: ModScript X, коды, модели, справка — плитки с номером, как ящики верстака.</summary>
    Control Tools()
    {
        var row = new UniformGrid { Columns = 4 };
        void Add(string icon, string key, string tab)
        {
            var n = row.Children.Count;
            var head = new DockPanel();
            var num = HubLook.MonoText($"0{n + 1}", 10.5);
            num.VerticalAlignment = VerticalAlignment.Top;
            DockPanel.SetDock(num, Dock.Right);
            head.Children.Add(num);
            head.Children.Add(new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(3), BorderBrush = HubLook.Edge, BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left, Child = Ui.Icon(icon, 15, n % 2 == 0 ? HubLook.Violet : HubLook.Cyan),
            });
            var tile = new Button
            {
                Classes = { "card-btn" }, Padding = new Thickness(14, 12), Margin = new Thickness(n == 0 ? 0 : 6, 0, n == 3 ? 0 : 6, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = Ui.Col(10, head, Ui.Col(2,
                    new TextBlock { Text = I18n.T(key), FontWeight = FontWeight.SemiBold, FontSize = 14 },
                    new TextBlock { Text = I18n.T(key + ".text"), FontSize = 12, FontWeight = FontWeight.Normal, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis })),
            };
            tile.Click += (_, _) => Go(tab);
            row.Children.Add(tile);
        }
        Add(Icons.Zap, "v99.cr.tool.modx", "inject");
        Add(Icons.Code, "v99.cr.tool.codes", "codes");
        Add(Icons.Cube, "v99.cr.tool.models", "models");
        Add(Icons.Book, "v99.cr.tool.docs", "docs");
        return row;
    }
}
