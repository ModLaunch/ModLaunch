using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// Рамка окна 9.2 в духе Microsoft Store: сверху — шапка (назад, название, поиск по центру,
/// загрузки), слева — подписанные разделы, недавние игры и (9.3) аккаунт внизу, а страница
/// лежит на отдельном «листе» со скруглённым левым верхним углом.
/// </summary>
public sealed partial class MainWindow
{
    const double RailWidth = 76;
    /// <summary>Значок первого пункта — логотип ModLaunch (а не домик): «Лента» — лицо программы.</summary>
    const string LogoIcon = "@logo";

    /// <summary>Значок и подпись каждого раздела — чтобы перерисовать выбранный цветом акцента.</summary>
    readonly Dictionary<Button, (string Icon, string Label)> _nav = [];
    Border? _crumbSep;
    /// <summary>Невысокое окно: подписи под значками прячутся, пункты становятся ниже — всё помещается.</summary>
    bool _railCompact;
    Border? _railGamesHost;

    /// <summary>
    /// Сколько недавних игр показать на панели: столько, сколько влезает по высоте целиком,
    /// и всегда кнопка «+». Остальные игры — в библиотеке; панель не превращается в обрезанную ленту.
    /// </summary>
    void FitRailGames(double height)
    {
        if (height <= 0) return;
        const double item = 52, gap = 7, caption = 14, plus = 42;
        var slots = _railGames.Children.Where(c => c is Panel p && p.Children.OfType<Button>().Any(b => b.Classes.Contains("rail-game"))).ToList();
        var captionBlock = _railGames.Children.OfType<TextBlock>().FirstOrDefault();
        var free = height - 2 - plus - gap - (captionBlock is null ? 0 : caption + gap);
        var fit = Math.Max(0, (int)Math.Floor((free + gap) / (item + gap)));
        for (var i = 0; i < slots.Count; i++) slots[i].IsVisible = i < fit;
        if (captionBlock is not null) captionBlock.IsVisible = fit > 0;
    }
    /// <summary>Второстепенные пункты (друзья, «На чай», настройки) — всегда значками, без подписей.</summary>
    readonly HashSet<Button> _secondary = [];
    /// <summary>Слой над страницей для анимаций открытия игры (9.3): снимок старой страницы, вспышки, рамки.</summary>
    readonly Panel _pageFx = new() { IsHitTestVisible = false, IsVisible = false, ClipToBounds = true };
    /// <summary>Слой под страницей: снимок прежнего экрана, который новая страница «прорезает».</summary>
    readonly Panel _pageUnder = new() { IsHitTestVisible = false, IsVisible = false, ClipToBounds = true };
    internal Panel PageFx => _pageFx;
    internal Panel PageUnder => _pageUnder;
    internal Control PageHost => _page;

    /// <summary>20.0: пункт боковой панели — значок и подпись в строку (содержимое строит RenderRail).</summary>
    Button NavButton(string icon, string labelKey, Action onClick)
    {
        var b = new Button { Classes = { "nav20" }, CornerRadius = new CornerRadius(10) };
        b.Click += (_, _) => onClick();
        _nav[b] = (icon, labelKey);
        return b;
    }

    static string Label(string key) => key.Contains('.') ? I18n.T(key) : key;

    /// <summary>
    /// Значок над подписью; у выбранного — значок цветом акцента, светящаяся полоска слева
    /// (9.3: «игровая» подсветка), у логотипа — мягкое свечение вокруг.
    /// </summary>
    static Control NavContent(string icon, string label, bool active, Control? badge, bool compact)
    {
        Control glyph;
        if (icon == LogoIcon)
        {
            var size = compact ? 24 : 27;
            var logo = new Image { Source = Images.Asset("icon.png", 96), Width = size, Height = size };
            glyph = new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(size / 2.0), Child = logo,
                BoxShadow = active ? new BoxShadows(new BoxShadow { Blur = 16, Spread = 1, Color = WithAlpha(Ui.Res("Brand"), 150) }) : default,
            };
        }
        else glyph = Ui.Icon(icon, 20, active ? Ui.Res("Brand2") : Ui.Res("Muted"));
        Control iconHost = glyph;
        if (badge is not null)
        {
            if (badge.Parent is Panel old) old.Children.Remove(badge);
            iconHost = new Panel { Width = 28, Height = 22, Children = { glyph, badge } };
        }
        var col = new StackPanel { Spacing = 5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { iconHost } };
        if (!compact)
            col.Children.Add(new TextBlock
            {
                // Длинная подпись («Creator Hub») — чуть мельче, чтобы влезла целиком.
                Text = label, FontSize = label.Length > 9 ? 10 : 11, FontWeight = active ? FontWeight.SemiBold : FontWeight.Medium,
                Foreground = active ? Ui.Res("Text") : Ui.Res("Muted"), HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 64,
            });
        var pip = new Border
        {
            Width = 3, Height = active ? 20 : 0, CornerRadius = new CornerRadius(2), Background = Ui.Res("Brand"),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            BoxShadow = active && !Look.Calm ? new BoxShadows(new BoxShadow { Blur = 10, Spread = 1, Color = WithAlpha(Ui.Res("Brand"), 200) }) : default,
        };
        return new Panel { Children = { col, pip } };
    }

    static Color WithAlpha(IBrush brush, byte alpha) =>
        brush is ISolidColorBrush s ? Color.FromArgb(alpha, s.Color.R, s.Color.G, s.Color.B) : Color.FromArgb(alpha, 123, 92, 255);

    void SetNav(Button b, bool active, Control? badge = null)
    {
        if (!_nav.TryGetValue(b, out var n)) return;
        var secondary = _secondary.Contains(b);
        var compact = _railCompact || secondary;
        b.Classes.Set("active", active);
        b.Classes.Set("secondary", secondary);
        b.Height = secondary ? 42 : _railCompact ? 46 : 58;
        b.Content = NavContent(n.Icon, Label(n.Label), active, badge, compact);
        ToolTip.SetTip(b, compact ? Label(n.Label) : null);
        ToolTip.SetPlacement(b, PlacementMode.Right);
    }

    static Control RailLine(Thickness margin) => new Border { Height = 1, Width = 40, Margin = margin, Background = Ui.Res("Line"), HorizontalAlignment = HorizontalAlignment.Center };

}
