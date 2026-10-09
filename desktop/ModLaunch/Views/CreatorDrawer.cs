using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Games;

namespace ModLaunch.Views;

/// <summary>
/// 9.1, по эскизу: «Creator Hub — отдельное меню: если навести мышь на крайнюю левую часть
/// экрана, выдвигается полоска, там оно спрятано». Узкая полоса у левого края окна ловит мышь
/// (на ней — едва заметная «ручка»), через мгновение выезжает панель Creator Hub со всеми
/// разделами; мышь ушла — панель уезжает обратно.
/// </summary>
public sealed class CreatorDrawer : Panel
{
    const double PanelWidth = 300;
    readonly MainWindow _w;
    readonly Border _zone = new() { Width = 10, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Stretch, Background = Brushes.Transparent };
    readonly Border _handle = new() { Width = 4, Height = 64, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0), IsHitTestVisible = false };
    readonly Border _shade = new() { IsHitTestVisible = false, Opacity = 0 };
    readonly Border _panel = new() { Width = PanelWidth, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Stretch };
    IDisposable? _openTimer, _closeTimer;

    public bool IsOpen { get; private set; }

    public CreatorDrawer(MainWindow w)
    {
        _w = w;
        _handle.Background = Ui.Res("Brand");
        _handle.Opacity = 0.35;
        _handle.Transitions =
        [
            new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(180) },
            new DoubleTransition { Property = WidthProperty, Duration = TimeSpan.FromMilliseconds(180) },
        ];
        ToolTip.SetTip(_zone, "Creator Hub");
        ToolTip.SetPlacement(_zone, PlacementMode.Right);

        _shade.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(150, 0, 0, 0), 0), new GradientStop(Color.FromArgb(40, 0, 0, 0), 0.45), new GradientStop(Color.FromArgb(0, 0, 0, 0), 1) },
        };
        _shade.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(260) }];

        _panel.Background = Ui.Res("Surface");
        _panel.BorderBrush = Ui.Res("Line");
        _panel.BorderThickness = new Thickness(0, 0, 1, 0);
        _panel.BoxShadow = BoxShadows.Parse("18 0 60 0 #A0000000");
        _panel.RenderTransform = TransformOperations.Parse($"translateX(-{PanelWidth + 30}px)");
        _panel.Transitions = [new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(300), Easing = new CubicEaseOut() }];
        _panel.IsHitTestVisible = false;
        _panel.Opacity = 0;

        Children.Add(_shade);
        Children.Add(_zone);
        Children.Add(_handle);
        Children.Add(_panel);

        _zone.PointerEntered += (_, _) =>
        {
            _handle.Opacity = 1;
            _handle.Width = 6;
            _openTimer?.Dispose();
            // Небольшая задержка: случайный проход мышью мимо края не открывает панель.
            _openTimer = DispatcherTimer.RunOnce(Open, TimeSpan.FromMilliseconds(110));
        };
        _zone.PointerExited += (_, _) =>
        {
            _handle.Opacity = 0.35;
            _handle.Width = 4;
            if (!IsOpen) _openTimer?.Dispose();
        };
        _zone.PointerPressed += (_, e) => { e.Handled = true; Open(); };
        // Где мышь — смотрим по всему окну: ушла правее панели — панель уезжает, вернулась — остаётся.
        w.AddHandler(PointerMovedEvent, (_, e) =>
        {
            if (!IsOpen) return;
            if (e.GetPosition(this).X > PanelWidth + 12) CloseSoon(220);
            else _closeTimer?.Dispose();
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    public void Open()
    {
        _openTimer?.Dispose();
        if (IsOpen) return;
        IsOpen = true;
        Render();
        _panel.Opacity = 1;
        _panel.IsHitTestVisible = true;
        _panel.RenderTransform = TransformOperations.Parse("translateX(0px)");
        _shade.Opacity = 1;
    }

    public void Close()
    {
        _closeTimer?.Dispose();
        if (!IsOpen) return;
        IsOpen = false;
        _panel.IsHitTestVisible = false;
        _panel.RenderTransform = TransformOperations.Parse($"translateX(-{PanelWidth + 30}px)");
        _shade.Opacity = 0;
        DispatcherTimer.RunOnce(() => { if (!IsOpen) _panel.Opacity = 0; }, TimeSpan.FromMilliseconds(320));
    }

    void CloseSoon(int ms)
    {
        _closeTimer?.Dispose();
        _closeTimer = DispatcherTimer.RunOnce(Close, TimeSpan.FromMilliseconds(ms));
    }

    void Go(Func<Page> page)
    {
        Close();
        _w.Navigate(page);
    }

    /// <summary>Содержимое: шапка, «Создать» и «Опубликовать», разделы, недавние проекты, подсказка.</summary>
    void Render()
    {
        var current = _w.CurrentPage as CreatorPage;
        var col = new StackPanel { Spacing = 6, Margin = new Thickness(18, 20, 18, 18) };

        var title = Ui.Col(1, Ui.Text("Creator Hub", "h2"), Ui.Text(I18n.T("cr.subtitle"), "small muted"));
        title.VerticalAlignment = VerticalAlignment.Center;
        var head = Ui.Row(12, CreatorLogo.Tile(46), title);
        head.Margin = new Thickness(0, 0, 0, 12);
        head.Cursor = new Cursor(StandardCursorType.Hand);
        head.PointerPressed += (_, _) => Go(() => new CreatorPage());
        col.Children.Add(head);

        var create = Ui.Button(I18n.T("v91.cr.create"), () => { Close(); CreatorPage.CreateNew(); }, "primary", Icons.Plus);
        var publish = Ui.Button(I18n.T("v91.cr.publish"), () => { Close(); CreatorPage.PublishFromAnywhere(); }, "", Icons.Upload);
        create.HorizontalAlignment = publish.HorizontalAlignment = HorizontalAlignment.Stretch;
        create.HorizontalContentAlignment = publish.HorizontalContentAlignment = HorizontalAlignment.Center;
        var actions = Ui.Col(8, create, publish);
        actions.Margin = new Thickness(0, 0, 0, 14);
        col.Children.Add(actions);

        col.Children.Add(Caption(I18n.T("v91.drawer.sections")));
        foreach (var (id, key, icon) in CreatorPage.MainTabs) col.Children.Add(Item(id, I18n.T(key), icon, current));
        col.Children.Add(new Border { Height = 1, Background = Ui.Res("Line"), Margin = new Thickness(4, 10, 4, 8) });
        col.Children.Add(Caption(I18n.T("v91.drawer.more")));
        foreach (var (id, key, icon) in CreatorPage.MoreTabs) col.Children.Add(Item(id, I18n.T(key), icon, current));

        var recent = Projects.List().OrderByDescending(p => p.Updated).Take(3).ToList();
        if (recent.Count > 0)
        {
            col.Children.Add(new Border { Height = 1, Background = Ui.Res("Line"), Margin = new Thickness(4, 10, 4, 8) });
            col.Children.Add(Caption(I18n.T("v91.drawer.recent")));
            foreach (var p in recent)
            {
                var game = GameCatalog.ById(p.Game);
                Control pic = game is not null
                    ? new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = Ui.GameImage(game, 80, art: Images.Art.Cover) }
                    : new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(8), Background = Ui.Res("Surface3"), Child = Ui.Icon(Icons.Creator, 14, Ui.Res("Faint")) };
                var words = Ui.Col(0, Ui.Text(p.Name, "", 13.5), Ui.Text(game?.Name ?? p.Game, "small muted"));
                words.VerticalAlignment = VerticalAlignment.Center;
                var id = p.Id;
                var b = new Button
                {
                    Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 6),
                    Content = Ui.Row(10, pic, words),
                };
                b.Click += (_, _) => Go(() => new CreatorPage("mine", id));
                col.Children.Add(b);
            }
        }

        var hint = Ui.Text(I18n.T("v91.drawer.hint"), "small", wrap: true, color: Ui.Res("Faint"));
        hint.Margin = new Thickness(4, 16, 4, 0);
        col.Children.Add(hint);

        _panel.Child = new ScrollViewer { Content = col, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }

    static Control Caption(string text) => new TextBlock
    {
        Text = text.ToUpper(I18n.Culture), FontSize = 11, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.8, Foreground = Ui.Res("Faint"), Margin = new Thickness(6, 4, 0, 4),
    };

    Control Item(string tab, string text, string icon, CreatorPage? current)
    {
        var b = Ui.Button(text, () => Go(() => new CreatorPage(tab)), "tab", icon);
        b.HorizontalAlignment = HorizontalAlignment.Stretch;
        b.HorizontalContentAlignment = HorizontalAlignment.Left;
        b.Padding = new Thickness(12, 9);
        if (current?.Tab == tab || (tab == "mine" && current?.Tab == "published")) b.Classes.Add("active");
        return b;
    }
}
