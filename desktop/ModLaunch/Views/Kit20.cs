using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>
/// 20.0: общие детали нового вида (эскизы ModLaunch 20): карточка-лист, заголовок раздела, метка, кнопка
/// мода с понятными состояниями (Установить → идёт установка → Установлен ✓ → Обновить) и карточка мода
/// с обложкой — одна и та же на главной, в каталоге и на странице игры.
/// </summary>
public static class Kit20
{
    /// <summary>Цвет темы для текста: в светлых темах жёлтый и зелёный темнее, чтобы читались на белом.</summary>
    public static IBrush Readable(string key)
    {
        var brush = Ui.Res(key);
        if (!Look.IsLight || brush is not ISolidColorBrush s || key is "Text" or "Muted" or "Faint") return brush;
        return new SolidColorBrush(Look.Mix(s.Color, Colors.Black, 0.42));
    }

    /// <summary>Лист: фон карточки, тонкая линия, скругление 16.</summary>
    public static Border Card(Control child, Thickness? padding = null, double radius = 16) => new()
    {
        Background = Ui.Res("Surface"), BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(radius), Padding = padding ?? new Thickness(20), Child = child,
    };

    /// <summary>Лист-плитка с подсветкой рамки при наведении (рамку задаёт стиль tile20, а не код).</summary>
    public static Border TileCard(Control child, Thickness? padding = null, double radius = 16) => new()
    {
        Classes = { "tile20" }, Background = Ui.Res("Surface"),
        CornerRadius = new CornerRadius(radius), Padding = padding ?? new Thickness(20), Child = child,
    };

    /// <summary>Заголовок раздела (шрифт заголовков), подзаголовок и ссылка справа.</summary>
    public static Control SectionHead(string title, string? sub = null, string? link = null, Action? open = null)
    {
        var words = Ui.Col(4, new TextBlock { Text = title, Classes = { "h2" }, TextTrimming = TextTrimming.CharacterEllipsis });
        if (sub is not null) words.Children.Add(new TextBlock { Text = sub, FontSize = 14, Foreground = Ui.Res("Faint"), TextTrimming = TextTrimming.CharacterEllipsis });
        var dock = new DockPanel { LastChildFill = true };
        if (link is not null && open is not null)
        {
            var go = Link(link, open);
            go.VerticalAlignment = VerticalAlignment.Bottom;
            DockPanel.SetDock(go, Dock.Right);
            dock.Children.Add(go);
        }
        dock.Children.Add(words);
        return dock;
    }

    /// <summary>Ссылка цветом акцента со стрелкой.</summary>
    public static Button Link(string text, Action open, double size = 14)
    {
        var b = new Button
        {
            Classes = { "link20" }, Padding = new Thickness(6, 4),
            Content = Ui.Row(6, new TextBlock { Text = text, FontSize = size, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }, Ui.Icon(Icons.Forward, size, null)),
        };
        b.Click += (_, _) => open();
        return b;
    }

    /// <summary>Нейтральная метка-«пилюля» (раздел мода, тег).</summary>
    public static Border Tag(string text, double size = 12) => new()
    {
        Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(999), Padding = new Thickness(10, 3), VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = size, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis },
    };

    /// <summary>Значок «Выбор ModLaunch» поверх обложки — только у модов, отобранных вручную.</summary>
    public static Border PickBadge() => new()
    {
        Background = new SolidColorBrush(Color.FromArgb(214, 13, 12, 17)), CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 4),
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10),
        Child = Ui.Row(5, Ui.Icon(Icons.Star, 12, Ui.Res("Brand2"), fill: true), new TextBlock { Text = I18n.T("v20.pick"), FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Ui.Res("Brand2"), VerticalAlignment = VerticalAlignment.Center }),
    };

    /// <summary>
    /// Кнопка мода: «Установить» — мягкий акцент; идёт установка — серая; «Установлен ✓» — зелёная метка.
    /// </summary>
    public static Control InstallButton(GameState g, ModInfo m, bool wide = false)
    {
        var installed = Actions.IsInstalled(g, m.Id);
        var busy = Actions.IsBusy(g, m.Id);
        if (installed)
        {
            var done = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(30, 74, 222, 128)), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 0), Height = 36,
                Child = Ui.Row(5, Ui.Icon(Icons.Check, 15, Ui.Res("Good")), new TextBlock { Text = I18n.T("mod.installed"), FontSize = 13, FontWeight = FontWeight.Bold, Foreground = Ui.Res("Good"), VerticalAlignment = VerticalAlignment.Center }),
            };
            if (wide) { done.HorizontalAlignment = HorizontalAlignment.Stretch; ((StackPanel)done.Child).HorizontalAlignment = HorizontalAlignment.Center; }
            return done;
        }
        var gs = g; var mm = m;
        var b = new Button
        {
            Classes = { "soft" }, Height = 36, Padding = new Thickness(14, 0), CornerRadius = new CornerRadius(10), VerticalContentAlignment = VerticalAlignment.Center,
            Content = Ui.Row(6, Ui.Icon(busy ? Icons.Hourglass : Icons.Download, 15), new TextBlock { Text = busy ? I18n.T("v20.installing") : I18n.T("mod.install"), FontSize = 13.5, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center }),
            IsEnabled = !busy && g.Status == Detect.Found,
        };
        if (wide) { b.HorizontalAlignment = HorizontalAlignment.Stretch; b.HorizontalContentAlignment = HorizontalAlignment.Center; }
        b.Click += (_, _) => _ = Actions.Install(gs, mm);
        return b;
    }

    /// <summary>Строка под карточкой мода: загрузки и когда обновлён (что известно из каталога).</summary>
    public static string Facts(ModInfo m)
    {
        var parts = new List<string>();
        if (m.Downloads > 0) parts.Add("↓ " + I18n.Compact(m.Downloads));
        if (m.UpdatedAt is { } at) parts.Add(Ui.Ago(at));
        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Карточка мода с обложкой: картинка мода (нет картинки — арт игры с буквами названия), название,
    /// автор, две строки описания, раздел и кнопка. Нажатие — страница мода, правая кнопка — меню.
    /// </summary>
    public static Control ModTile(GameState g, ModInfo m, bool badge = false, bool showGame = false, double coverHeight = 140)
    {
        var cover = new Panel { Height = coverHeight, ClipToBounds = true, Children = { ModCover.Create(g, m, 640, iconShare: 0.56) } };
        if (badge) cover.Children.Add(PickBadge());
        var name = new TextBlock { Text = m.Name, FontSize = 16, FontWeight = FontWeight.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
        var byline = new List<string>();
        if (showGame) byline.Add(g.Def.ShortName);
        if (m.Author != "") byline.Add(m.Author);
        var body = Ui.Col(5, name);
        if (byline.Count > 0) body.Children.Add(new TextBlock { Text = string.Join(" · ", byline), FontSize = 13, Foreground = Ui.Res("Faint"), TextTrimming = TextTrimming.CharacterEllipsis });
        if (m.Description != "")
            body.Children.Add(new TextBlock
            {
                Text = m.Description, FontSize = 13.5, LineHeight = 19.5, Foreground = Ui.Res("Muted"), TextWrapping = TextWrapping.Wrap,
                MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0), Height = 39,
            });
        var foot = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 10, 0, 0) };
        var action = InstallButton(g, m);
        DockPanel.SetDock(action, Dock.Right);
        foot.Children.Add(action);
        Control left = m.Categories.FirstOrDefault() is { } cat ? Tag(cat) : new TextBlock { Text = Facts(m), FontSize = 12, Foreground = Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        left.HorizontalAlignment = HorizontalAlignment.Left;
        left.Margin = new Thickness(0, 0, 8, 0);
        foot.Children.Add(left);
        body.Children.Add(foot);
        body.Margin = new Thickness(16, 14, 16, 16);
        var dock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(cover, Dock.Top);
        dock.Children.Add(cover);
        dock.Children.Add(body);
        var tile = new Border
        {
            Classes = { "tile20" }, Background = Ui.Res("Surface"),
            CornerRadius = new CornerRadius(16), ClipToBounds = true, Child = dock,
        };
        var id = g.Def.Id;
        StoreKit.OnClick(tile, () => MainWindow.Current?.Navigate(() => new ModPage(id, m)));
        var installed = Actions.IsInstalled(g, m.Id);
        Ctx.Attach(tile, () => ModRow.Menu(g.Def, m, installed, Actions.IsBusy(g, m.Id), () => _ = Actions.Install(g, m), () => MainWindow.Current?.Navigate(() => new ModPage(id, m))));
        return tile;
    }

    /// <summary>Сетка карточек: столько столбцов, сколько помещается (не уже minWidth), строки одной высоты.</summary>
    public static Control Grid(IReadOnlyList<Control> items, double minWidth = 250, int maxColumns = 4, double gap = 20)
    {
        var grid = new Avalonia.Controls.Grid { ColumnSpacing = gap, RowSpacing = gap };
        void Layout(double width)
        {
            var cols = Math.Clamp((int)Math.Floor((width + gap) / (minWidth + gap)), 1, maxColumns);
            if (grid.ColumnDefinitions.Count == cols && grid.Children.Count == items.Count) return;
            grid.Children.Clear();
            grid.ColumnDefinitions.Clear();
            grid.RowDefinitions.Clear();
            for (var i = 0; i < cols; i++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var rows = (items.Count + cols - 1) / cols;
            for (var r = 0; r < rows; r++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (var i = 0; i < items.Count; i++)
            {
                var c = items[i];
                if (c.Parent is Panel p) p.Children.Remove(c);
                Avalonia.Controls.Grid.SetColumn(c, i % cols);
                Avalonia.Controls.Grid.SetRow(c, i / cols);
                grid.Children.Add(c);
            }
        }
        Layout(1100);
        grid.SizeChanged += (_, e) => { if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 1) Layout(e.NewSize.Width); };
        return grid;
    }

    /// <summary>Прямоугольник со штриховой рамкой (плитка «Реклама», «Добавить игру»).</summary>
    public static Control Dashed(Control child, double radius = 20, Thickness? padding = null)
    {
        var rect = new Rectangle
        {
            Stroke = Ui.Res("Surface3"), StrokeThickness = 1.5, StrokeDashArray = [5, 4], RadiusX = radius, RadiusY = radius, IsHitTestVisible = false,
        };
        return new Panel { Children = { rect, new Border { Padding = padding ?? new Thickness(20), Child = child } } };
    }

    /// <summary>Кольцо прогресса (уровень): серая дорожка, дуга акцента и число в середине.</summary>
    public static Control Ring(double progress, string label, double size = 48, double stroke = 4)
    {
        var track = new Arc { Width = size, Height = size, StartAngle = 0, SweepAngle = 360, Stroke = Ui.Res("Surface3"), StrokeThickness = stroke };
        var arc = new Arc
        {
            Width = size, Height = size, StartAngle = -90, SweepAngle = Math.Max(10, Math.Clamp(progress, 0, 1) * 360),
            Stroke = Ui.Res("Brand"), StrokeThickness = stroke, StrokeLineCap = PenLineCap.Round,
        };
        var text = new TextBlock { Text = label, FontFamily = Look.Display, FontSize = size * 0.34, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        return new Panel { Width = size, Height = size, Children = { track, arc, text } };
    }
}

/// <summary>
/// Сетка карточек, в которую можно добавлять по одной (каталог строит список порциями): столбцов —
/// сколько помещается при ширине не меньше MinTile, все карточки строки одной высоты.
/// </summary>
public sealed class TileGrid : Panel
{
    public double MinTile { get; set; } = 240;
    public int MaxColumns { get; set; } = 4;
    public double Gap { get; set; } = 18;

    int Cols(double width) => double.IsInfinity(width) ? MaxColumns : Math.Clamp((int)Math.Floor((width + Gap) / (MinTile + Gap)), 1, MaxColumns);

    protected override Size MeasureOverride(Size available)
    {
        var cols = Cols(available.Width);
        var w = double.IsInfinity(available.Width) ? MinTile : Math.Max(0, (available.Width - Gap * (cols - 1)) / cols);
        double total = 0, rowH = 0;
        var i = 0;
        foreach (var c in Children)
        {
            c.Measure(new Size(w, double.PositiveInfinity));
            rowH = Math.Max(rowH, c.DesiredSize.Height);
            if (++i % cols == 0) { total += rowH + Gap; rowH = 0; }
        }
        if (i % cols != 0) total += rowH;
        else if (i > 0) total -= Gap;
        return new Size(double.IsInfinity(available.Width) ? cols * w + Gap * (cols - 1) : available.Width, total);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var cols = Cols(final.Width);
        var w = Math.Max(0, (final.Width - Gap * (cols - 1)) / cols);
        double y = 0;
        var kids = Children.ToList();
        for (var r = 0; r < kids.Count; r += cols)
        {
            var row = kids.Skip(r).Take(cols).ToList();
            var h = row.Max(c => c.DesiredSize.Height);
            for (var j = 0; j < row.Count; j++) row[j].Arrange(new Rect(j * (w + Gap), y, w, h));
            y += h + Gap;
        }
        return final;
    }
}
