using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// Выбор оформления 9.2: три готовых стиля в духе Microsoft Store — «Тёмная», «Красно-белая»
/// и «Чёрно-зелёная». Каждая карточка — маленькая копия окна в цветах стиля, нажатие сразу
/// перекрашивает программу. Показывается один раз после обновления и живёт в «Настройки → Внешний вид».
/// </summary>
public static class StylePicker
{
    /// <summary>Карточка-образец: окно ModLaunch в миниатюре, название и пояснение.</summary>
    public static Control Card(Look.LookStyle style, Action onPick, double width = 250)
    {
        var p = Look.Palette(style.Theme);
        IBrush C(string key) => Ui.Hex(p[key]);
        var accent = Color.Parse(style.Accent);
        var h = Math.Round(width * 0.6);

        // Мини-окно: шапка с поиском, боковая панель, «лист» с баннером и обложками.
        Control Bar(double w, double hh, IBrush b, double r = 3) => new Border { Width = w, Height = hh, CornerRadius = new CornerRadius(r), Background = b };
        var rail = new StackPanel { Spacing = 7, Width = 26, Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        for (var i = 0; i < 4; i++)
            rail.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(3), Background = i == 0 ? new SolidColorBrush(accent) : C("Muted"), Opacity = i == 0 ? 1 : 0.55, HorizontalAlignment = HorizontalAlignment.Center });
        var posters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        for (var i = 0; i < 5; i++) posters.Children.Add(Bar(22, 32, C(i == 1 ? "Surface3" : "Surface2"), 3));
        var hero = new Border
        {
            Height = Math.Round(h * 0.36), CornerRadius = new CornerRadius(4),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(accent, 0), new GradientStop(Look.Mix(accent, Color.Parse(p["Surface"]), 0.75), 1) },
            },
            Child = new Border { Width = 26, Height = 8, CornerRadius = new CornerRadius(3), Background = Brushes.White, Opacity = 0.9, Margin = new Thickness(7), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom },
        };
        var tiles = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*"), ColumnSpacing = 5 };
        tiles.Children.Add(hero);
        var side = new Grid { RowDefinitions = new RowDefinitions("*,*"), RowSpacing = 5 };
        side.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = C("Surface2") });
        var small = new Border { CornerRadius = new CornerRadius(4), Background = C("Surface3") };
        Grid.SetRow(small, 1);
        side.Children.Add(small);
        Grid.SetColumn(side, 1);
        tiles.Children.Add(side);
        var sheet = new Border
        {
            Background = C("Layer"), CornerRadius = new CornerRadius(6, 0, 0, 0), BorderBrush = C("Line"), BorderThickness = new Thickness(1, 1, 0, 0), Padding = new Thickness(8),
            Child = Ui.Col(6, tiles, Bar(46, 5, C("Text"), 2), posters),
        };
        var title = new DockPanel { Height = 16, Margin = new Thickness(0, 0, 0, 2) };
        title.Children.Add(new Border { Width = 70, Height = 8, CornerRadius = new CornerRadius(4), Background = C("Surface"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        body.Children.Add(rail);
        Grid.SetColumn(sheet, 1);
        body.Children.Add(sheet);
        var window = new Border
        {
            Height = h, Background = C("Bg"), ClipToBounds = true, CornerRadius = new CornerRadius(8, 8, 0, 0), Padding = new Thickness(0, 4, 0, 0),
            Child = new DockPanel { Children = { Dock(title), body } },
        };
        static Control Dock(Control c) { DockPanel.SetDock(c, Avalonia.Controls.Dock.Top); return c; }

        var on = Look.Store && Look.CurrentStyle?.Id == style.Id;
        var name = Ui.Row(8, Ui.Dot(new SolidColorBrush(accent), 10), Ui.Text(I18n.T("v92.style." + style.Id), "h3"));
        var words = Ui.Col(4, name, Ui.Text(I18n.T("v92.style." + style.Id + ".text"), "small muted", wrap: true));
        words.Margin = new Thickness(14, 12, 14, 14);
        var footer = new Panel { Children = { words } };
        if (on)
            footer.Children.Add(new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(accent), Margin = new Thickness(12),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Child = Ui.Icon(Icons.Check, 13, Look.Luma(accent) > 0.55 ? Ui.Hex("#08110B") : Brushes.White),
            });
        var card = new Button
        {
            Classes = { "style-card" }, Width = width, CornerRadius = new CornerRadius(10), ClipToBounds = true,
            Content = Ui.Col(0, window, new Border { Background = Ui.Res("Surface"), Child = footer }),
        };
        if (on) card.Classes.Add("on");
        card.Click += (_, _) => onPick();
        return card;
    }

    /// <summary>Ряд из трёх карточек; после выбора — перерисовать (галочка переезжает).</summary>
    public static Control Row(Action? after = null, double width = 250)
    {
        var row = new WrapPanel();
        void Fill()
        {
            row.Children.Clear();
            foreach (var style in Look.Styles)
            {
                var s = style;
                var c = Card(s, () => { Look.SetStyle(s.Id); Fill(); after?.Invoke(); }, width);
                c.Margin = new Thickness(0, 0, 14, 14);
                row.Children.Add(c);
            }
        }
        Fill();
        return row;
    }

    /// <summary>Один раз после обновления до 9.2: окно «Выберите оформление».</summary>
    public static bool ShouldShow => !Program.Screenshot && !Settings.Data.Bool("stylePicked92");

    public static void Show(Action? then = null)
    {
        var w = MainWindow.Current;
        if (w is null) return;
        Settings.Data["stylePicked92"] = true;
        Settings.Save();
        var body = Ui.Col(16,
            Ui.Text(I18n.T("v92.picker.text"), "muted", wrap: true),
            Row(width: 232),
            Ui.Text(I18n.T("v92.picker.later"), "small muted", wrap: true));
        var done = Ui.Button(I18n.T("v92.picker.done"), () => { w.CloseDialog(); then?.Invoke(); }, "primary", Icons.Check);
        w.Dialog(I18n.T("v92.picker.title"), body, 790, done);
    }
}
