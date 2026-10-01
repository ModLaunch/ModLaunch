using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Games;

namespace ModLaunch.Views;

/// <summary>«Студия» — главная Creator Hub: с чего начать, что редактировали недавно, какие бывают пути.</summary>
public sealed partial class CreatorPage
{
    Control Studio()
    {
        var col = new StackPanel { Spacing = 22 };

        // Шапка: коротко, что здесь делают, и две главные кнопки.
        var hero = new Border
        {
            CornerRadius = new CornerRadius(20), Padding = new Thickness(30, 28), BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#402A2350"), 0), new GradientStop(Color.Parse("#10171A21"), 0.7) },
            },
        };
        var left = Ui.Col(10,
            Ui.Text(I18n.T("cr.studio.title"), "h1"),
            Ui.Text(I18n.T("cr.studio.lead"), "muted", wrap: true),
            Ui.Row(10, Ui.Button(I18n.T("cr.studio.newScript"), NewScriptMod, "primary", Icons.Plus), Ui.Button(I18n.T("cr.studio.newCode"), () => { _wizard = true; Go("code"); }, "", Icons.Code),
                Ui.Button(I18n.T("cr.studio.learn"), () => Go("guides"), "ghost", Icons.Book)));
        left.MaxWidth = 720;
        left.HorizontalAlignment = HorizontalAlignment.Left;
        hero.Child = left;
        col.Children.Add(hero);

        // Что можно создать: шесть дорожек, каждая ведёт в свой раздел.
        col.Children.Add(Ui.Text(I18n.T("cr.studio.make"), "h2"));
        var make = new UniformGrid { Columns = 3 };
        foreach (var (id, icon, tab) in new[]
        {
            ("script", Icons.Edit, "mine"), ("code", Icons.Code, "code"), ("model", Icons.Package, "assets"),
            ("texture", Icons.Image, "assets"), ("sound", Icons.Music, "assets"), ("release", Icons.Upload, "pack"),
        })
        {
            var target = tab;
            var tile = new Button
            {
                Classes = { "tile" }, Margin = new Thickness(0, 0, 12, 12), Padding = new Thickness(18), HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Top, VerticalAlignment = VerticalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch,
                Content = TileContent(icon, I18n.T("cr.studio.t." + id), I18n.T("cr.studio.t." + id + ".text")),
            };
            tile.Click += (_, _) => Go(target);
            make.Children.Add(tile);
        }
        col.Children.Add(make);

        // Недавние проекты (оба вида) и цифры студии.
        var recent = new List<(string Name, string Game, string Kind, DateTime When, Action Open)>();
        foreach (var p in Projects.List().Take(6)) { var pp = p; recent.Add((p.Name, p.Game, I18n.T("cr.studio.kind.script"), p.Updated, () => { Go("mine"); Open(pp); Build(); })); }
        foreach (var p in CodeProjects.List().Take(6)) { var pp = p; recent.Add((p.Name, p.Game, CodeProjects.Label(p.Kind), p.Updated, () => { Go("code"); _codeOpen = pp; Build(); })); }
        var recentCol = Ui.Col(10, Ui.Text(I18n.T("cr.studio.recent"), "h2"));
        if (recent.Count == 0) recentCol.Children.Add(Ui.Card(Ui.Text(I18n.T("cr.studio.recent.none"), "muted", wrap: true), 20));
        foreach (var (name, game, kind, when, open) in recent.OrderByDescending(r => r.When).Take(6))
        {
            var def = GameCatalog.ById(game);
            var row = new Button { Classes = { "tile" }, Padding = new Thickness(14, 12), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
            grid.Children.Add(Ui.Thumb(def?.ArtUrl, def?.Name ?? game, 38, 10));
            var text = Ui.Col(1, Ui.Text(name, "h3"), Ui.Text($"{def?.Name ?? game} · {kind}", "small muted"));
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            var ago = Ui.Text(Ui.Ago(when), "small muted");
            ago.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(ago, 2);
            grid.Children.Add(ago);
            row.Content = grid;
            var go = open;
            row.Click += (_, _) => go();
            recentCol.Children.Add(row);
        }

        var stats = Ui.Col(10, Ui.Text(I18n.T("cr.studio.stats"), "h2"));
        var statGrid = new UniformGrid { Columns = 2 };
        void Stat(string key, int n, string tab)
        {
            var b = new Button { Classes = { "tile" }, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(16, 14), HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch, Content = Ui.Col(2, Ui.Text(n.ToString(), "h1"), Ui.Text(I18n.T("cr.studio.s." + key), "small muted")) };
            b.Click += (_, _) => Go(tab);
            statGrid.Children.Add(b);
        }
        Stat("mods", Projects.List().Count, "mine");
        Stat("code", CodeProjects.List().Count, "code");
        Stat("assets", AssetLibrary.List().Count, "assets");
        Stat("snippets", Snippets.Own().Count + Snippets.Builtin.Length, "snippets");
        stats.Children.Add(statGrid);

        var two = new Grid { ColumnDefinitions = new ColumnDefinitions("*,380"), ColumnSpacing = 24 };
        two.Children.Add(recentCol);
        Grid.SetColumn(stats, 1);
        two.Children.Add(stats);
        col.Children.Add(two);

        // Пути: гайды от идеи до результата.
        col.Children.Add(Ui.Text(I18n.T("cr.studio.paths"), "h2"));
        var paths = new UniformGrid { Columns = 3 };
        foreach (var g in Guides.All)
        {
            var guide = g;
            var card = new Button
            {
                Classes = { "tile" }, Margin = new Thickness(0, 0, 12, 12), Padding = new Thickness(18), HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Top, VerticalAlignment = VerticalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch,
                Content = Ui.Col(6, Ui.Text(g.Title, "h3"), new TextBlock { Text = g.Intro, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Foreground = Ui.Res("Muted") },
                    Ui.Text(I18n.T("cr.guides.steps", ("n", g.Steps.Length)), "small", color: Ui.Res("Brand2"))),
            };
            card.Click += (_, _) => { _guide = guide.Id; Go("guides"); };
            paths.Children.Add(card);
        }
        col.Children.Add(paths);
        return col;
    }

    /// <summary>Значок слева, заголовок и пояснение справа; пояснение переносится по ширине плитки.</summary>
    static Control TileContent(string icon, string title, string text)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 14 };
        grid.Children.Add(new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(13), Background = Ui.Res("BrandSoft"), VerticalAlignment = VerticalAlignment.Top, Child = Ui.Icon(icon, 22, Ui.Res("Brand2")) });
        var body = Ui.Col(3, Ui.Text(title, "h3"), new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Foreground = Ui.Res("Muted") });
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);
        return grid;
    }

    void NewScriptMod()
    {
        var p = Projects.Create(I18n.T("cr.new.name"), Templates.ById("blank")!.Code);
        _tab = "mine";
        Open(p);
        Build();
    }
}
