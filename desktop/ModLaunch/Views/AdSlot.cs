using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// Место под рекламу (8.4): маленькая «таблетка» в шапке окна и широкий баннер на главной.
/// Объявления сменяют друг друга плавно; своё ведёт на экран программы, внешнее — в браузер
/// и помечено словом «Реклама».
/// </summary>
public static class AdSlot
{
    static string IconFor(Ad ad) => ad.Icon switch
    {
        "globe" => Icons.Globe,
        "creator" => Icons.Creator,
        "users" => Icons.Users,
        _ => Icons.Megaphone,
    };

    public static void Open(Ad ad)
    {
        var w = MainWindow.Current;
        if (ad.Go is not null && w is not null)
        {
            switch (ad.Go)
            {
                case "hub": w.Navigate(() => new MarketPage("workshop")); return;
                case "creator": w.Navigate(() => new CreatorPage()); return;
                case "friends": w.Navigate(() => new CommunityPage()); return;
            }
        }
        if (ad.Url is not null) Ui.OpenUrl(ad.Url);
    }

    /// <summary>Переключатель: показывает объявления по очереди, пока элемент на экране.</summary>
    static void Rotate(Control host, Action<Ad, int, int> show)
    {
        var index = 0;
        DispatcherTimer? timer = null;
        void Next(bool first)
        {
            var ads = Ads.Current();
            if (ads.Count == 0) { host.IsVisible = false; return; }
            host.IsVisible = true;
            if (!first) index = (index + 1) % ads.Count;
            index = Math.Clamp(index, 0, ads.Count - 1);
            show(ads[index], index, ads.Count);
        }
        void Changed() => Dispatcher.UIThread.Post(() => Next(true));
        var rendered = false;
        host.AttachedToVisualTree += (_, _) =>
        {
            // Страницу перерисовали и вставили баннер заново — показываем то же, без анимации.
            if (!rendered) Next(true);
            rendered = true;
            Ads.Changed += Changed;
            I18n.Changed += Changed;
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Ads.RotateSeconds) };
            timer.Tick += (_, _) => { if (!host.IsPointerOver) Next(false); };
            if (!Program.Screenshot) timer.Start();
        };
        host.DetachedFromVisualTree += (_, _) => { timer?.Stop(); Ads.Changed -= Changed; I18n.Changed -= Changed; };
    }

    static Control Mark(Ad ad, double size, double radius)
    {
        if (ad.Image is not null) return Ui.Thumb(ad.Image, ad.Title, size, radius, 96);
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(radius),
            Background = Ui.Res("Brand"),
            Child = Ui.Icon(IconFor(ad), size * 0.5, Brushes.White),
        };
    }

    /// <summary>Таблетка в шапке: значок и заголовок.</summary>
    public static Control Pill()
    {
        var content = new ContentControl();
        var button = new Button
        {
            Classes = { "ad-pill" }, Content = content, MaxWidth = 270, Height = 40,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Ad? current = null;
        button.Click += (_, _) => { if (current is not null) Open(current); };
        Rotate(button, (ad, _, _) =>
        {
            current = ad;
            var words = Ui.Col(0, Ui.Text(ad.Title, "small"), Ui.Text(ad.House ? ad.Text : I18n.T("ad.label") + " · " + ad.Text, "tiny muted"));
            words.VerticalAlignment = VerticalAlignment.Center;
            words.MaxWidth = 200;
            ((TextBlock)words.Children[0]).FontWeight = FontWeight.SemiBold;
            var row = Ui.Row(10, Mark(ad, 26, 8), words);
            content.Content = row;
            ToolTip.SetTip(button, ad.Title + (ad.Text != "" ? " — " + ad.Text : ""));
            Animate.From(row, "translateY(10px)", 380);
        });
        return button;
    }

    /// <summary>
    /// 14.0: на главной баннера больше нет — реклама живёт только в узкой колонке справа (Side),
    /// чтобы не падать в глаза. Старые места вызова получают пустой невидимый элемент.
    /// </summary>
    public static Control Banner() => new Border { IsVisible = false };

    /// <summary>Скрыто до перезапуска кнопкой «×» на самой колонке.</summary>
    public static bool SideClosed { get; set; }

    /// <summary>Ширина колонки (без отступов). Под неё рассчитаны картинки рекламодателей: 3:4, например 600×800.</summary>
    public const double SideWidth = 184;

    /// <summary>
    /// 14.0: место под рекламу справа — тихая карточка у края окна: серая подпись «Реклама»,
    /// картинка 3:4, заголовок и строка текста; без кнопок-призывов и движения, пока на неё не навели.
    /// </summary>
    public static Control Side(Action close)
    {
        var host = new Border
        {
            Classes = { "ad-side" }, Width = SideWidth, CornerRadius = new CornerRadius(10), ClipToBounds = true,
            Background = Ui.Res("Surface"), BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand), Opacity = 0.82, VerticalAlignment = VerticalAlignment.Top,
        };
        host.PointerEntered += (_, _) => host.Opacity = 1;
        host.PointerExited += (_, _) => host.Opacity = 0.82;
        Ad? current = null;
        host.PointerPressed += (_, e) =>
        {
            if (current is not null && e.GetCurrentPoint(host).Properties.IsLeftButtonPressed && e.Source is Visual v && v.FindAncestorOfType<Button>(true) is null) Open(current);
        };
        Rotate(host, (ad, index, count) =>
        {
            current = ad;
            var x = Ui.Button("", close, "icon ghost", Icons.Close, I18n.T("v14.ad.hide"));
            x.Width = x.Height = 22;
            x.Padding = new Thickness(0);
            var head = new DockPanel { Margin = new Thickness(10, 6, 4, 6) };
            DockPanel.SetDock(x, Dock.Right);
            head.Children.Add(x);
            head.Children.Add(new TextBlock
            {
                Text = (ad.House ? I18n.T("v14.ad.house") : I18n.T("ad.label")).ToUpper(I18n.Culture), FontSize = 9.5, LetterSpacing = 1, FontWeight = FontWeight.SemiBold,
                Foreground = Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Center,
            });
            Control art = ad.Image is not null
                ? new Border { Height = SideWidth * 4 / 3, ClipToBounds = true, Child = HomePage.WebArt(ad.Image, null, ad.Title, 400) }
                : new Border { Height = 96, Background = Ui.Res("Surface2"), Child = Ui.Icon(IconFor(ad), 30, Ui.Res("Muted")) };
            var title = new TextBlock { Text = ad.Title, FontSize = 13, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
            var text = new TextBlock { Text = ad.Text, FontSize = 11.5, Foreground = Ui.Res("Muted"), TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis };
            var dots = Ui.Row(4);
            for (var i = 0; i < count && count > 1; i++)
                dots.Children.Add(new Border { Width = 4, Height = 4, CornerRadius = new CornerRadius(2), Background = i == index ? Ui.Res("Muted") : Ui.Res("Line") });
            var words = Ui.Col(5, title, text, dots);
            words.Margin = new Thickness(12, 10, 12, 12);
            var col = new StackPanel { Children = { head, art, words } };
            host.Child = col;
            ToolTip.SetTip(host, ad.Url is not null && ad.Go is null ? ad.Url : null);
        });
        return host;
    }

    /// <summary>Широкий баннер 8.4 (оставлен для витрины «Разместить рекламу»).</summary>
    public static Control WideBanner()
    {
        var host = new Border { Classes = { "ad-banner" }, ClipToBounds = true, Cursor = new Cursor(StandardCursorType.Hand) };
        Ad? current = null;
        host.PointerPressed += (_, e) => { if (current is not null && e.GetCurrentPoint(host).Properties.IsLeftButtonPressed) Open(current); };
        Rotate(host, (ad, index, count) =>
        {
            current = ad;
            var label = new Border
            {
                Background = Ui.Res("Surface3"), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = Ui.Text(ad.House ? I18n.T("ad.house") : I18n.T("ad.label"), "tiny muted"),
            };
            var words = Ui.Col(6, label, Ui.Text(ad.Title, "h3"), Ui.Text(ad.Text, "muted small", wrap: true));
            words.VerticalAlignment = VerticalAlignment.Center;
            var go = Ui.Button(ad.House ? I18n.T("ad.open") : I18n.T("ad.more"), () => Open(ad), "primary", ad.Url is not null && ad.Go is null ? Icons.External : Icons.Forward);
            go.VerticalAlignment = VerticalAlignment.Center;
            var dots = Ui.Row(5);
            for (var i = 0; i < count; i++)
                dots.Children.Add(new Border { Width = i == index ? 16 : 6, Height = 6, CornerRadius = new CornerRadius(3), Background = i == index ? Ui.Res("Text") : Ui.Res("Line"), Classes = { "dot" } });
            dots.HorizontalAlignment = HorizontalAlignment.Right;
            dots.VerticalAlignment = VerticalAlignment.Bottom;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 18, Margin = new Thickness(18, 16, 20, 16) };
            grid.Children.Add(Mark(ad, 56, 14));
            Grid.SetColumn(words, 1);
            grid.Children.Add(words);
            Grid.SetColumn(go, 2);
            grid.Children.Add(go);
            var panel = new Panel { Children = { grid, new Border { Margin = new Thickness(0, 0, 20, 8), Child = dots, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom } } };
            host.Child = panel;
            Animate.From(grid, "translateX(24px)", 420);
        });
        return host;
    }
}
