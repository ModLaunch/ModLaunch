using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// Анимации в коде: появление лесенкой, въезд страниц и уведомлений. Всё
/// выключается в «Настройки → Внешний вид → Анимации».
/// </summary>
public static class Animate
{
    static Transitions Smooth(int ms = 320) =>
    [
        new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(ms * 0.8), Easing = new CubicEaseOut() },
        new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(ms), Easing = new CubicEaseOut() },
    ];

    static bool On => Look.Animations && !Program.Screenshot;

    /// <summary>Карточка выезжает снизу и проявляется; соседние — с небольшой задержкой.</summary>
    public static void Stagger(Control c, int index, double dy = 16)
    {
        if (!On) return;
        c.Opacity = 0;
        c.RenderTransform = TransformOperations.Parse($"translateY({dy}px)");
        var old = c.Transitions;
        c.AttachedToVisualTree += Once;
        void Once(object? s, VisualTreeAttachmentEventArgs e)
        {
            c.AttachedToVisualTree -= Once;
            DispatcherTimer.RunOnce(() =>
            {
                c.Transitions = Smooth();
                c.Opacity = 1;
                c.RenderTransform = TransformOperations.Parse("translateY(0px)");
                // Вернуть свои переходы кнопки (наведение, нажатие) после въезда.
                DispatcherTimer.RunOnce(() => c.Transitions = old, TimeSpan.FromMilliseconds(400));
            }, TimeSpan.FromMilliseconds(20 + Math.Min(index, 14) * 38));
        }
    }

    /// <summary>Новая страница въезжает чуть справа.</summary>
    public static void PageIn(Control c)
    {
        if (!On) return;
        c.Transitions = null;
        c.Opacity = 0;
        c.RenderTransform = TransformOperations.Parse("translateX(18px)");
        Dispatcher.UIThread.Post(() =>
        {
            c.Transitions = Smooth(280);
            c.Opacity = 1;
            c.RenderTransform = TransformOperations.Parse("translateX(0px)");
        }, DispatcherPriority.Background);
    }

    /// <summary>Уведомление въезжает справа, уходит — растворяясь.</summary>
    public static void ToastIn(Control c)
    {
        if (!On) return;
        c.Opacity = 0;
        c.RenderTransform = TransformOperations.Parse("translateX(40px)");
        Dispatcher.UIThread.Post(() =>
        {
            c.Transitions = Smooth(300);
            c.Opacity = 1;
            c.RenderTransform = TransformOperations.Parse("translateX(0px)");
        }, DispatcherPriority.Background);
    }

    public static void ToastOut(Control c, Action remove)
    {
        if (!On) { remove(); return; }
        c.Opacity = 0;
        c.RenderTransform = TransformOperations.Parse("translateX(40px)");
        DispatcherTimer.RunOnce(remove, TimeSpan.FromMilliseconds(260));
    }
}
