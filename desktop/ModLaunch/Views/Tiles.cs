using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Sources;
using Path = Avalonia.Controls.Shapes.Path;

namespace ModLaunch.Views;

/// <summary>
/// Сетка плиток одной ширины, которая сама решает, сколько колонок влезает
/// (как auto-fill в CSS и витрины Microsoft Store): на узком окне — две-три,
/// на 4K — шесть-семь, и плитки всегда добивают строку до края без пустого поля.
/// </summary>
public sealed class TileGrid : Panel
{
    public double MinItemWidth { get; set; } = 220;
    public double Gap { get; set; } = 12;
    public double RowGap { get; set; } = 16;
    /// <summary>0 — по ширине; иначе ровно столько колонок (для полок).</summary>
    public int Columns { get; set; }

    public int ColumnsFor(double width) =>
        Columns > 0 ? Columns : double.IsInfinity(width) ? 4 : Math.Max(1, (int)((width + Gap) / (MinItemWidth + Gap)));

    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? (MinItemWidth + Gap) * 4 : available.Width;
        var cols = ColumnsFor(width);
        var item = (width - Gap * (cols - 1)) / cols;
        double height = 0, row = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            Children[i].Measure(new Size(item, double.PositiveInfinity));
            row = Math.Max(row, Children[i].DesiredSize.Height);
            if (i % cols == cols - 1 || i == Children.Count - 1) { height += row + RowGap; row = 0; }
        }
        return new Size(width, Math.Max(0, height - RowGap));
    }

    protected override Size ArrangeOverride(Size final)
    {
        var cols = ColumnsFor(final.Width);
        var item = (final.Width - Gap * (cols - 1)) / cols;
        double y = 0;
        for (var start = 0; start < Children.Count; start += cols)
        {
            var count = Math.Min(cols, Children.Count - start);
            var row = Enumerable.Range(start, count).Max(i => Children[i].DesiredSize.Height);
            for (var k = 0; k < count; k++) Children[start + k].Arrange(new Rect(k * (item + Gap), y, item, row));
            y += row + RowGap;
        }
        return final;
    }
}

/// <summary>Блок с постоянным соотношением сторон: высота = ширина × Ratio.</summary>
public sealed class AspectBox : Decorator
{
    public double Ratio { get; set; } = 0.6;

    protected override Size MeasureOverride(Size available)
    {
        var w = double.IsInfinity(available.Width) ? 220 : available.Width;
        var size = new Size(w, Math.Round(w * Ratio));
        Child?.Measure(size);
        return size;
    }

    protected override Size ArrangeOverride(Size final)
    {
        var size = new Size(final.Width, Math.Round(final.Width * Ratio));
        Child?.Arrange(new Rect(size));
        return size;
    }
}

/// <summary>
/// Полка: заголовок «Популярное ›» и ровно столько плиток, сколько помещается,
/// со стрелками по страницам — как в Microsoft Store, без горизонтальной полосы прокрутки.
/// </summary>
public sealed class Shelf : UserControl
{
    IReadOnlyList<Func<Control>> _items;
    readonly TileGrid _grid;
    readonly Button _prev, _next;
    readonly double _minWidth;
    int _page, _perPage;

    /// <param name="inset">Внутренний отступ плиток (у плиток модов — 8): сетка сдвигается на него влево,
    /// чтобы картинки стояли ровно под заголовком и вровень с остальной страницей.</param>
    public Shelf(string title, string? eyebrow, IReadOnlyList<Func<Control>> items, Action? more = null, double minWidth = 220, Control? leading = null, double inset = 8)
    {
        _items = items;
        _minWidth = minWidth;
        _grid = new TileGrid { Gap = inset > 0 ? 4 : 14, RowGap = 0, Margin = new Thickness(-inset, 0) };
        _prev = Pager(Icons.Back, -1);
        _next = Pager(Icons.Forward, 1);

        var heading = Ui.Row(10);
        if (leading is not null) heading.Children.Add(leading);
        var titleCol = Ui.Col(2);
        if (eyebrow is not null) titleCol.Children.Add(Ui.Text(eyebrow.ToUpperInvariant(), "eyebrow"));
        var titleText = Ui.Text(title, "h2");
        if (more is not null)
        {
            // Заголовок сам — ссылка, со стрелкой рядом (как «Популярные игры ›» в Microsoft Store).
            var link = new Button { Classes = { "link" }, Padding = new Thickness(0), Content = Ui.Row(6, titleText, Ui.Icon(Icons.Forward, 16, Ui.Res("Muted"))) };
            link.Foreground = Ui.Res("Text");
            link.Click += (_, _) => more();
            titleCol.Children.Add(link);
        }
        else titleCol.Children.Add(titleText);
        heading.Children.Add(titleCol);
        heading.VerticalAlignment = VerticalAlignment.Bottom;

        var pagers = Ui.Row(6, _prev, _next);
        pagers.VerticalAlignment = VerticalAlignment.Bottom;
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(pagers, Dock.Right);
        head.Children.Add(pagers);
        head.Children.Add(heading);

        Content = Ui.Col(6, head, _grid);
        SizeChanged += (_, e) => Layout(e.NewSize.Width);
        Layout(1200);
    }

    /// <summary>Новые плитки (поменялось «установлен» и т. п.) — та же страница полки.</summary>
    public void SetItems(IReadOnlyList<Func<Control>> items)
    {
        _items = items;
        var pages = (int)Math.Ceiling(_items.Count / (double)Math.Max(1, _perPage));
        _page = Math.Clamp(_page, 0, Math.Max(0, pages - 1));
        Render();
    }

    Button Pager(string icon, int delta)
    {
        var b = new Button { Classes = { "pager" }, Content = Ui.Icon(icon, 14) };
        b.Click += (_, _) =>
        {
            var pages = (int)Math.Ceiling(_items.Count / (double)Math.Max(1, _perPage));
            _page = Math.Clamp(_page + delta, 0, Math.Max(0, pages - 1));
            Render();
            Animate.From(_grid, delta > 0 ? "translateX(40px)" : "translateX(-40px)", 320, 0, null, 0.2);
        };
        return b;
    }

    void Layout(double width)
    {
        if (width <= 0) return;
        var per = Math.Clamp((int)((width + 12) / (_minWidth + 12)), 1, 8);
        if (per == _perPage) return;
        var first = _page * Math.Max(1, _perPage);
        _perPage = per;
        _page = first / per;
        Render();
    }

    void Render()
    {
        _grid.Columns = _perPage;
        _grid.Children.Clear();
        foreach (var make in _items.Skip(_page * _perPage).Take(_perPage)) _grid.Children.Add(make());
        _prev.IsEnabled = _page > 0;
        _next.IsEnabled = (_page + 1) * _perPage < _items.Count;
        _prev.IsVisible = _next.IsVisible = _items.Count > _perPage;
    }
}

/// <summary>Плитки модов, обложки и рисованные заглушки.</summary>
public static class Tiles
{
    /// <summary>
    /// Плитка мода: картинка 16:10, под ней название, автор и цифры, справа —
    /// капсула «Установить». Без рамки: карточка видна только при наведении.
    /// </summary>
    public static Control Mod(GameDef game, ModInfo mod, bool installed, bool installing, Action install, Action open, string? badge = null, bool pick = false, bool showGame = false)
    {
        var art = new Panel { Children = { Art(mod.Icon, mod.Name, 520) } };
        var marks = Ui.Row(6);
        marks.Margin = new Thickness(10);
        marks.HorizontalAlignment = HorizontalAlignment.Left;
        marks.VerticalAlignment = VerticalAlignment.Top;
        if (Badge(badge) is { } b) marks.Children.Add(b);
        if (mod.Adult) marks.Children.Add(Label("18+", "#E5484D"));
        if (game.IsLegacy(mod.UpdatedAt))
        {
            var old = Label(I18n.T("badge.old"), "#F2B84B");
            ToolTip.SetTip(old, I18n.T("badge.old.hint"));
            marks.Children.Add(old);
        }
        if (mod.Source != game.PrimarySource) marks.Children.Add(Label(Catalog.Title(mod.Source), "#7FB4E6"));
        art.Children.Add(marks);
        if (pick) art.Children.Add(Sticker(I18n.T("badge.pick")));
        if (showGame)
            art.Children.Add(new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(8), ClipToBounds = true, Margin = new Thickness(10),
                BorderBrush = Ui.Hex("#55FFFFFF"), BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Child = Ui.GameImage(game, 64, art: Images.Art.Cover),
            });

        var picture = new Border { Classes = { "tile-art" }, CornerRadius = new CornerRadius(14), ClipToBounds = true, Child = new AspectBox { Ratio = 0.6, Child = art } };

        var title = new TextBlock { Text = mod.Name, FontSize = 15, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        var by = mod.Author != "" ? mod.Author : game.ShortName;
        var category = mod.Categories.FirstOrDefault();
        var sub = new TextBlock { Text = category is null ? by : $"{by} · {category}", FontSize = 12.5, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis };
        var meta = Ui.Row(10);
        if (Social.Reviews.Stats().GetValueOrDefault($"{game.Id}|{mod.Id}") is { } rating) meta.Children.Add(Meta(Icons.Star, $"{rating.Avg:0.0}"));
        if (mod.Downloads > 0) meta.Children.Add(Meta(Icons.Download, I18n.Compact(mod.Downloads)));
        // Дата обновления — только если больше сказать нечего: в строке ещё кнопка.
        if (meta.Children.Count == 0 && mod.UpdatedAt is not null) meta.Children.Add(Meta(Icons.Refresh, Ui.Ago(mod.UpdatedAt)));

        // Название и автор — во всю ширину, кнопка — в строке с цифрами: так длинные имена не режутся.
        var get = Get(installed, installing, install);
        get.VerticalAlignment = VerticalAlignment.Center;
        meta.VerticalAlignment = VerticalAlignment.Center;
        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        bottom.Children.Add(meta);
        Grid.SetColumn(get, 1);
        bottom.Children.Add(get);
        var foot = Ui.Col(2, title, sub, bottom);
        foot.Margin = new Thickness(2, 0, 2, 2);

        var card = new Button { Classes = { "tile-btn" }, Content = Ui.Col(10, picture, foot) };
        ToolTip.SetTip(card, mod.Description == "" ? mod.Name : mod.Description);
        card.Click += (_, _) => open();
        return card;
    }

    /// <summary>Капсула «Установить» / «Установлен» / «Ставим…» — как «Получить» в App Store.</summary>
    public static Button Get(bool installed, bool installing, Action install)
    {
        Button b;
        if (installed) { b = Ui.Button(I18n.T("mod.installed"), () => { }, "get done", Icons.Check); b.IsHitTestVisible = false; }
        else if (installing) { b = Ui.Button(I18n.T("tile.installing"), () => { }, "get"); b.IsEnabled = false; }
        else b = Ui.Button(I18n.T("tile.get"), install, "get");
        return b;
    }

    static Control Meta(string icon, string text) =>
        Ui.Row(4, Ui.Icon(icon, 11, Ui.Res("Faint")), new TextBlock { Text = text, FontSize = 12, Foreground = Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Center });

    /// <summary>Отметка на картинке: тёмная капсула с цветной точкой (без эмодзи).</summary>
    public static Border Label(string text, string color) => new()
    {
        Background = Ui.Hex("#CC0B0B0D"),
        CornerRadius = new CornerRadius(999),
        Padding = new Thickness(8, 3, 10, 3),
        Child = Ui.Row(6,
            new Ellipse { Width = 7, Height = 7, Fill = Ui.Hex(color), VerticalAlignment = VerticalAlignment.Center },
            new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White }),
    };

    public static Control? Badge(string? kind) => kind switch
    {
        "hit" => Label(I18n.T("badge.hit"), "#FF8A5B"),
        "best" => Label(I18n.T("badge.best"), "#F2C25C"),
        "new" => Label(I18n.T("badge.new"), "#5BD68F"),
        _ => null,
    };

    /// <summary>«Выбор редакции» — наклейка чуть наискосок, будто её прилепили руками.</summary>
    public static Control Sticker(string text) => new Border
    {
        Background = Ui.Res("Brand"),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(9, 4),
        Margin = new Thickness(0, 10, 10, 0),
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        RenderTransform = new RotateTransform(4),
        BoxShadow = BoxShadows.Parse("0 4 10 0 #66000000"),
        Child = new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeight.Bold, Foreground = Brushes.White },
    };

    /// <summary>
    /// Картинка мода во всю плитку. Широкая (скриншот, баннер Nexus) — на весь
    /// размер. Квадратный значок (Thunderstore, ModLinks) — по центру, а фоном
    /// тот же значок, размытый в цветное пятно: так плитки в сетке одной формы.
    /// </summary>
    public static Control Art(string? url, string name, int decode = 480)
    {
        var host = new Panel { Children = { Placeholder(name) } };
        if (string.IsNullOrEmpty(url)) return host;
        _ = Images.FromUrl(url, decode).ContinueWith(t =>
        {
            if (t.Result is not Bitmap bmp) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var ratio = bmp.PixelSize.Width / (double)Math.Max(1, bmp.PixelSize.Height);
                if (ratio is > 0.8 and < 1.3) ShowIcon(host, url, bmp);
                else host.Children.Add(new Image { Classes = { "zoom" }, Source = bmp, Stretch = Stretch.UniformToFill });
            });
        });
        return host;
    }

    static void ShowIcon(Panel host, string url, Bitmap bmp)
    {
        // Значок 24 точки шириной, растянутый на всю плитку, — это и есть мягкое размытие, почти даром.
        var blur = new Image { Stretch = Stretch.UniformToFill, Opacity = 0.9 };
        RenderOptions.SetBitmapInterpolationMode(blur, BitmapInterpolationMode.HighQuality);
        host.Children.Add(blur);
        _ = Images.FromUrl(url, 24).ContinueWith(t => { if (t.Result is Bitmap small) Avalonia.Threading.Dispatcher.UIThread.Post(() => blur.Source = small); });
        host.Children.Add(new Border { Background = Ui.Hex("#59000000") });
        var icon = new Border
        {
            CornerRadius = new CornerRadius(18), ClipToBounds = true, BoxShadow = BoxShadows.Parse("0 10 24 0 #80000000"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Child = new Image { Classes = { "zoom" }, Source = bmp, Stretch = Stretch.Uniform },
        };
        // Значок — примерно половина высоты плитки, сколько бы она ни была.
        host.SizeChanged += (_, e) => icon.Width = icon.Height = Math.Round(e.NewSize.Height * 0.56);
        icon.Width = icon.Height = Math.Round(Math.Max(48, host.Bounds.Height * 0.56));
        host.Children.Add(icon);
    }

    // Приглушённые пары цветов — не неон: обложка-заглушка не должна кричать громче настоящих.
    static readonly (string Bg, string Fg)[] Paper =
    [
        ("#2B3A55", "#3F5680"), ("#3D2B4F", "#5A4277"), ("#24453F", "#346A60"), ("#4A3423", "#71512F"),
        ("#3A3A44", "#575766"), ("#4A2730", "#713B49"), ("#22404D", "#316276"), ("#3F3F26", "#63633A"),
    ];

    /// <summary>
    /// Рисованная обложка для мода без картинки: спокойный цвет, простая фигура
    /// (круг, кольца, полоса или точки) и инициалы. Цвет и фигура зависят от
    /// названия — у одного мода обложка всегда одна и та же.
    /// </summary>
    public static Control Placeholder(string name)
    {
        var hash = name.Aggregate(2166136261u, (h, c) => (h ^ c) * 16777619u);
        var (bg, fg) = Paper[hash % (uint)Paper.Length];
        var light = Ui.Hex(fg);
        var canvas = new Canvas { Width = 160, Height = 100, Background = Ui.Hex(bg), ClipToBounds = true };
        switch (hash / 8 % 4)
        {
            case 0:
                canvas.Children.Add(Place(new Ellipse { Width = 150, Height = 150, Fill = light }, 78, 18));
                break;
            case 1:
                for (var r = 0; r < 4; r++)
                    canvas.Children.Add(Place(new Ellipse { Width = 60 + r * 46, Height = 60 + r * 46, Stroke = light, StrokeThickness = 9 }, -30 - r * 23, -30 - r * 23));
                break;
            case 2:
                canvas.Children.Add(new Path { Data = Geometry.Parse("M 70,0 L 120,0 L 50,100 L 0,100 Z"), Fill = light });
                canvas.Children.Add(new Path { Data = Geometry.Parse("M 135,0 L 150,0 L 80,100 L 65,100 Z"), Fill = light, Opacity = 0.6 });
                break;
            default:
                for (var x = 0; x < 9; x++)
                    for (var y = 0; y < 6; y++)
                        if ((x + y) % 2 == 0) canvas.Children.Add(Place(new Ellipse { Width = 7, Height = 7, Fill = light }, 8 + x * 18, 6 + y * 18));
                break;
        }
        var initials = string.Concat(name.Split(' ', '-', '_', '.').Where(w => w.Length > 0 && char.IsLetterOrDigit(w[0])).Take(2).Select(w => char.ToUpperInvariant(w[0])));
        var text = new TextBlock { Text = initials, FontSize = 30, FontWeight = FontWeight.Bold, LetterSpacing = 1, Foreground = Ui.Hex("#E6FFFFFF"), Width = 160, TextAlignment = TextAlignment.Center };
        canvas.Children.Add(Place(text, 0, 31));
        return new FillCenter { Child = canvas };
    }

    /// <summary>
    /// Растянуть рисунок «с запасом» (как UniformToFill) и поставить по центру.
    /// Viewbox прижимает лишнее к левому краю — в квадратной миниатюре буквы уезжали вправо.
    /// </summary>
    sealed class FillCenter : Decorator
    {
        protected override Size MeasureOverride(Size available)
        {
            Child?.Measure(Size.Infinity);
            return new Size(double.IsInfinity(available.Width) ? 0 : available.Width, double.IsInfinity(available.Height) ? 0 : available.Height);
        }

        protected override Size ArrangeOverride(Size final)
        {
            if (Child is null || Child.DesiredSize.Width <= 0 || Child.DesiredSize.Height <= 0) return final;
            var natural = Child.DesiredSize;
            var k = Math.Max(final.Width / natural.Width, final.Height / natural.Height);
            Child.RenderTransformOrigin = RelativePoint.TopLeft;
            Child.RenderTransform = new ScaleTransform(k, k);
            Child.Arrange(new Rect((final.Width - natural.Width * k) / 2, (final.Height - natural.Height * k) / 2, natural.Width, natural.Height));
            return final;
        }
    }

    static Control Place(Control c, double left, double top)
    {
        Canvas.SetLeft(c, left);
        Canvas.SetTop(c, top);
        return c;
    }

    /// <summary>Скелет плитки, пока каталог грузится.</summary>
    public static Control Skeleton() => new Border
    {
        Padding = new Thickness(8),
        Child = Ui.Col(10,
            new Border { CornerRadius = new CornerRadius(14), Background = Ui.Res("Surface2"), Child = new AspectBox { Ratio = 0.6 } },
            new Border { Height = 14, Width = 140, CornerRadius = new CornerRadius(7), Background = Ui.Res("Surface2"), HorizontalAlignment = HorizontalAlignment.Left },
            new Border { Height = 10, Width = 90, CornerRadius = new CornerRadius(5), Background = Ui.Res("Surface"), HorizontalAlignment = HorizontalAlignment.Left }),
    };

    /// <summary>Крупная цифра места в топе — контуром, как в чартах стримингов.</summary>
    public static Control Rank(int n)
    {
        var text = new FormattedText(n.ToString(), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("fonts:Inter#Inter"), FontStyle.Normal, FontWeight.ExtraBold), 120, Brushes.White);
        var geometry = text.BuildGeometry(new Point(0, 0));
        Control shape = geometry is null
            ? new TextBlock { Text = n.ToString(), FontSize = 120, FontWeight = FontWeight.ExtraBold, Foreground = Ui.Res("Surface3") }
            : new Path { Data = geometry, Stroke = Ui.Res("Faint"), StrokeThickness = 2.5, Fill = Ui.Res("Bg"), Stretch = Stretch.None };
        return new Border { Child = shape, Margin = new Thickness(0, 0, 0, -12), VerticalAlignment = VerticalAlignment.Bottom, ClipToBounds = false };
    }

    /// <summary>Плитка для топа: цифра выглядывает из-за левого края картинки.</summary>
    public static Control Ranked(int n, Control tile)
    {
        var number = Rank(n);
        number.HorizontalAlignment = HorizontalAlignment.Left;
        number.VerticalAlignment = VerticalAlignment.Top;
        number.Margin = new Thickness(-4, 30, 0, 0);
        number.IsHitTestVisible = false;
        tile.Margin = new Thickness(n >= 10 ? 92 : 56, 0, 0, 0);
        return new Panel { Children = { number, tile } };
    }
}
