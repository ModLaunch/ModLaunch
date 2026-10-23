using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// 18.0: действия плитки или строки — только под курсором. Вместо постоянных кнопок в списках две аккуратные
/// кнопки «Запуск» и «Настройка» проявляются и чуть поднимаются, когда курсор над плиткой (или фокус внутри неё —
/// с клавиатуры их тоже видно), и гаснут, когда ушёл. По кадрам окна, как <see cref="Animate"/>: без вспышки
/// конечного значения, которую дают переходы Avalonia.
/// </summary>
public static class HoverActions
{
    /// <summary>Пара кнопок: «Запуск» (главная) и «Настройка». onArt — на картинке (тёмное стекло), иначе — на листе Hub.</summary>
    public static StackPanel Pair(Action launch, Action configure, bool onArt, string? launchText = null)
    {
        var go = Ui.Button(launchText ?? I18n.T("v18.hover.launch"), launch, "primary", Icons.Play);
        var set = Ui.Button(I18n.T("v18.hover.configure"), configure, "", Icons.Sliders);
        foreach (var b in new[] { go, set })
        {
            b.MinHeight = 32;
            b.Padding = new Thickness(12, 0);
            b.FontSize = 12.5;
        }
        if (onArt)
        {
            set.Background = new SolidColorBrush(Color.FromArgb(205, 14, 16, 24));
            set.Foreground = Brushes.White;
            set.BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));
            set.BorderThickness = new Thickness(1);
        }
        return new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Children = { go, set },
        };
    }

    /// <summary>Над картинкой плитки: затемнение и две кнопки по центру — проявляются при наведении на host.</summary>
    public static Control OnArt(Control host, Action launch, Action configure, string? launchText = null)
    {
        var pair = Pair(launch, configure, true, launchText);
        var scrim = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(120, 6, 8, 14), 0), new GradientStop(Color.FromArgb(215, 6, 8, 14), 1) },
            },
            Child = pair,
        };
        Attach(host, scrim, pair);
        return scrim;
    }

    /// <summary>В конце строки: кнопки справа, проявляются при наведении на host.</summary>
    public static Control InRow(Control host, Action launch, Action configure, string? launchText = null)
    {
        var pair = Pair(launch, configure, false, launchText);
        Attach(host, pair, pair);
        return pair;
    }

    /// <summary>Курсор или фокус внутри host — показать layer (и чуть поднять lift), иначе — спрятать.</summary>
    public static void Attach(Control host, Control layer, Control lift)
    {
        layer.Opacity = 0;
        layer.IsHitTestVisible = false;
        var shown = false;
        void Set(bool on)
        {
            if (on == shown) return;
            shown = on;
            layer.IsHitTestVisible = on;
            Run(layer, lift, on);
        }
        host.PointerEntered += (_, _) => Set(true);
        host.PointerExited += (_, _) => Set(host.IsKeyboardFocusWithin);
        host.GotFocus += (_, e) => { if (e.NavigationMethod != NavigationMethod.Pointer) Set(true); };
        host.LostFocus += (_, _) => Set(host.IsPointerOver || host.IsKeyboardFocusWithin);
    }

    /// <summary>Для снимков: показать действия сразу, без курсора.</summary>
    public static void Show(Control layer)
    {
        layer.Opacity = 1;
        layer.IsHitTestVisible = true;
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, object> Runs = new();

    /// <summary>Прозрачность и подъём кнопок (6 px) — по кадрам; без анимаций — сразу.</summary>
    static void Run(Control layer, Control lift, bool on)
    {
        var run = new object();
        Runs.AddOrUpdate(layer, run);
        var from = layer.Opacity;
        var to = on ? 1.0 : 0.0;
        if (!Animate.On || TopLevel.GetTopLevel(layer) is not { } top)
        {
            layer.Opacity = to;
            lift.RenderTransform = null;
            return;
        }
        var ms = on ? 170.0 : 120.0;
        var ease = new CubicEaseOut();
        TimeSpan? t0 = null;
        void Frame(TimeSpan t)
        {
            if (!Runs.TryGetValue(layer, out var still) || still != run) return;
            t0 ??= t;
            var k = Math.Clamp((t - t0.Value).TotalMilliseconds / ms, 0, 1);
            var e = ease.Ease(k);
            layer.Opacity = from + (to - from) * e;
            lift.RenderTransform = on ? new TranslateTransform(0, 6 * (1 - e)) : null;
            if (k < 1 && layer.IsAttachedToVisualTree()) top.RequestAnimationFrame(Frame);
            else { layer.Opacity = to; lift.RenderTransform = null; Runs.Remove(layer); }
        }
        top.RequestAnimationFrame(Frame);
    }
}
