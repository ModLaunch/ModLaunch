using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// Анимации в коде. Правило, из-за которого они не тормозят: двигаются только
/// RenderTransform и Opacity (без пересчёта раскладки), и каждая играет один
/// раз — при переходе на страницу, а не при каждой перерисовке.
/// Выключаются в «Настройки → Внешний вид → Анимации».
/// </summary>
public static class Animate
{
    public static bool On => Look.Animations && (!Program.Screenshot || Environment.GetEnvironmentVariable("MODLAUNCH_ANIM") == "1");

    static Transitions Smooth(int ms, Easing? easing = null) =>
    [
        new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(ms * 0.7), Easing = new CubicEaseOut() },
        new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(ms), Easing = easing ?? new CubicEaseOut() },
    ];

    // Если анимацию перезапустили (быстро листаем игры), старый таймер не должен обрывать новую.
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, object> Runs = new();
    sealed record Original(Transitions? Transitions, double Opacity, bool OwnOpacity);
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, Original> Kept = new();

    /// <summary>Поставить элемент в начальное положение и через delay плавно вернуть на место.</summary>
    public static void From(Control c, string from, int ms = 320, int delay = 0, Easing? easing = null, double opacity = 0)
    {
        if (!On) return;
        var run = new object();
        Runs.AddOrUpdate(c, run);
        // Исходное состояние запоминаем один раз: при перезапуске середина прошлой анимации — не «исходное».
        // У приглушённых карточек своя прозрачность — к ней и возвращаемся.
        if (!Kept.TryGetValue(c, out var orig)) { orig = new Original(c.Transitions, c.Opacity, c.IsSet(Visual.OpacityProperty)); Kept.AddOrUpdate(c, orig); }
        var (keep, target, ownOpacity) = (orig.Transitions, orig.Opacity, orig.OwnOpacity);
        c.Transitions = null;
        c.Opacity = opacity * target;
        c.RenderTransform = TransformOperations.Parse(from);
        void Go()
        {
            if (!Runs.TryGetValue(c, out var now) || now != run) return;
            c.Transitions = Smooth(ms, easing);
            c.Opacity = target;
            c.RenderTransform = TransformOperations.Parse("none");
            // Вернуть стилевые переходы (наведение и нажатие), когда въезд закончится.
            DispatcherTimer.RunOnce(() =>
            {
                if (!Runs.TryGetValue(c, out var still) || still != run) return;
                Runs.Remove(c);
                Kept.Remove(c);
                c.Transitions = keep; c.ClearValue(Visual.RenderTransformProperty); if (!ownOpacity) c.ClearValue(Visual.OpacityProperty); }, TimeSpan.FromMilliseconds(ms + 40));
        }
        if (delay <= 0) Dispatcher.UIThread.Post(Go, DispatcherPriority.Background);
        else DispatcherTimer.RunOnce(Go, TimeSpan.FromMilliseconds(delay));
    }

    /// <summary>Страница въезжает снизу — только при переходе, не при перерисовке.</summary>
    public static void PageIn(Control page) => From(page, "translateY(18px)", 340);

    /// <summary>Волна: карточки поднимаются по очереди (задержка ограничена, чтобы длинные списки не ждали).</summary>
    public static void Rise(Control c, int index) =>
        From(c, "translateY(26px) scale(0.94)", 460, Math.Min(index * 32, 420), new BackEaseOut());

    /// <summary>Всплывающее окно «выпрыгивает».</summary>
    public static void Pop(Control c) => From(c, "scale(0.9)", 360, 0, new BackEaseOut());

    /// <summary>Уведомление въезжает справа с отскоком, уходит — растворяясь.</summary>
    public static void ToastIn(Control c) => From(c, "translateX(60px) scale(0.96)", 420, 0, new BackEaseOut());

    public static void ToastOut(Control c, Action remove)
    {
        if (!On) { remove(); return; }
        c.Transitions = Smooth(240);
        c.Opacity = 0;
        c.RenderTransform = TransformOperations.Parse("translateX(40px) scale(0.96)");
        DispatcherTimer.RunOnce(remove, TimeSpan.FromMilliseconds(260));
    }
}
