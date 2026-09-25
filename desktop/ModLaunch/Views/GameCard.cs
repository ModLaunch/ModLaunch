using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// Карточки игр. Cover — вертикальная обложка, как в библиотеке Steam (для
/// сеток), Row — компактная строка с шапкой игры (для «Продолжить игру»).
/// </summary>
public static class GameCard
{
    public static string Status(GameState g) => g.Status switch
    {
        Detect.Searching => I18n.T("games.searching"),
        Detect.Found when !g.LoaderInstalled => I18n.T("home.loaderNeeded", ("loader", g.Def.LoaderName)),
        Detect.Found when g.ModCount > 0 => I18n.T("aside.mods." + I18n.Plural(g.ModCount, "one", "few", "many"), ("n", g.ModCount)),
        Detect.Found => I18n.T("games.loaderReady", ("loader", g.Def.LoaderName)),
        Detect.NotFound => I18n.T("games.notDetected"),
        _ => I18n.T("games.notSearched"),
    };

    /// <summary>Обложка 2:3 с названием под ней; у найденных игр при наведении — «Играть».</summary>
    public static Control Cover(GameState g, double width = 168)
    {
        var found = g.Status == Detect.Found;
        var height = Math.Round(width * 1.5);
        // Внешний слой — подъём и тень при наведении, внутренний — обрезка по скруглению и зум картинки.
        var art = new Border
        {
            Width = width, Height = height, CornerRadius = new CornerRadius(10), ClipToBounds = true,
            BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent,
            Child = Ui.GameImage(g.Def, (int)(width * 2), art: Images.Art.Cover),
        };
        var layers = new Panel { Children = { art } };
        if (found && g.LoaderInstalled)
        {
            var gs = g;
            var running = Features.Launcher.IsRunning(g.Def.Id);
            var play = running
                ? Ui.Button("", () => Features.Launcher.Stop(gs.Def.Id), "icon", Icons.Stop, I18n.T("v4.stop"))
                : Ui.Button("", () => Actions.Play(gs), "icon primary", Icons.Play, I18n.T("games.play"));
            play.Classes.Add("cover-play");
            play.HorizontalAlignment = HorizontalAlignment.Right;
            play.VerticalAlignment = VerticalAlignment.Bottom;
            play.Margin = new Thickness(8);
            if (running) play.Opacity = 1;
            layers.Children.Add(play);
        }
        if (g.Def.Custom)
            layers.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 12, 13, 18)), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 2),
                Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = I18n.T("lib.custom"), FontSize = 11, Foreground = Brushes.White },
            });

        var name = Ui.Text(g.Def.Name, "h3");
        name.FontSize = 14;
        var status = Ui.Text(Status(g), "small", color: found ? Ui.Res("Muted") : Ui.Res("Faint"));
        var card = new Button
        {
            Classes = { "cover-btn" },
            Width = width,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 16, 18),
            Opacity = found ? 1 : 0.5,
            Content = Ui.Col(8, new Border { Classes = { "cover-art" }, CornerRadius = new CornerRadius(10), Child = layers }, Ui.Col(1, name, status)),
        };
        ToolTip.SetTip(card, g.Def.Name);
        var id = g.Def.Id;
        card.Click += (_, _) => MainWindow.Current?.Navigate(() => new GamePage(id));
        return card;
    }

    /// <summary>Обложка «Добавить игру» того же размера.</summary>
    public static Control AddCover(double width = 168)
    {
        var box = new Border
        {
            Width = width, Height = Math.Round(width * 1.5), CornerRadius = new CornerRadius(10),
            BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1.5), Background = Ui.Res("Surface"),
            Child = Ui.Col(10,
                new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(24), Background = Ui.Res("Surface2"), Child = Ui.Icon(Icons.Plus, 22, Ui.Res("Muted")), HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = I18n.T("add.tile"), HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = I18n.T("add.tile.text"), HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12, Foreground = Ui.Res("Muted"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(12, 0) }),
        };
        if (box.Child is Control c) c.VerticalAlignment = VerticalAlignment.Center;
        var card = new Button { Classes = { "cover-btn" }, Width = width, Padding = new Thickness(0), Margin = new Thickness(0, 0, 16, 18), Content = new Border { Classes = { "cover-art" }, CornerRadius = new CornerRadius(10), Child = box }, VerticalAlignment = VerticalAlignment.Top };
        card.Click += (_, _) => MainWindow.Current?.Navigate(() => new AddGamePage());
        return card;
    }

    /// <summary>Широкая строка с шапкой игры: для «Продолжить игру».</summary>
    public static Control Row(GameState g, string line, bool lineGood = false)
    {
        var running = Features.Launcher.IsRunning(g.Def.Id);
        var art = new Border { Width = 128, Height = 60, CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = Ui.GameImage(g.Def, 256) };
        var info = Ui.Col(2, Ui.Text(g.Def.Name, "h3"), Ui.Text(line, "small", color: lineGood || running ? Ui.Res("Good") : Ui.Res("Muted")));
        info.VerticalAlignment = VerticalAlignment.Center;
        var gs = g;
        var play = running
            ? Ui.Button(I18n.T("v4.stop"), () => Features.Launcher.Stop(gs.Def.Id), "", Icons.Stop)
            : Ui.Button(I18n.T("games.play"), () => Actions.Play(gs), "primary", Icons.Play);
        play.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 14 };
        grid.Children.Add(art);
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);
        Grid.SetColumn(play, 2);
        grid.Children.Add(play);
        var card = new Button { Classes = { "card-btn" }, Width = 420, Padding = new Thickness(8, 8, 12, 8), Margin = new Thickness(0, 0, 12, 12), Content = grid };
        card.Click += (_, _) => MainWindow.Current?.Navigate(() => new GamePage(gs.Def.Id));
        return card;
    }
}
