using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// Анимации в коде — только въезд и уход уведомлений. Выключаются в
/// «Настройки → Внешний вид → Анимации».
/// </summary>
public static class Animate
{
    static Transitions Smooth(int ms = 320) =>
    [
        new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(ms * 0.8), Easing = new CubicEaseOut() },
        new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(ms), Easing = new CubicEaseOut() },
    ];

    static bool On => Look.Animations && !Program.Screenshot;

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
