using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// Плавная прокрутка колесом, как в браузере: вместо рывка на 50 пикселей
/// страница доезжает до цели за ~200 мс, кадр за кадром в такт экрану
/// (RequestAnimationFrame). Работает для всех прокручиваемых областей окна.
/// </summary>
public static class SmoothScroll
{
    sealed class State
    {
        public double Target;
        public bool Running;
        public TimeSpan Last;
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ScrollViewer, State> States = new();
    public static bool On => Settings.Data.Bool("smoothScroll", true);

    public static void Attach(TopLevel top) =>
        top.AddHandler(InputElement.PointerWheelChangedEvent, (s, e) => OnWheel(top, e), RoutingStrategies.Tunnel);

    static void OnWheel(TopLevel top, PointerWheelEventArgs e)
    {
        if (!On || e.KeyModifiers != KeyModifiers.None || Math.Abs(e.Delta.Y) < 0.001) return;
        if (e.Source is not Visual source) return;
        // Редактор кода, выпадающие списки и поля ввода крутятся по-своему.
        if (source.FindAncestorOfType<AvaloniaEdit.TextEditor>(true) is not null || source.FindAncestorOfType<ComboBox>(true) is not null) return;

        // Ближайшая область, которую можно прокрутить в эту сторону (полки с горизонтальной прокруткой пропускаем).
        var down = e.Delta.Y < 0;
        ScrollViewer? sv = null;
        for (var v = source.FindAncestorOfType<ScrollViewer>(true); v is not null; v = v.FindAncestorOfType<ScrollViewer>())
        {
            var max = Math.Max(0, v.Extent.Height - v.Viewport.Height);
            if (max < 1 || v.VerticalScrollBarVisibility == Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled) continue;
            var state = States.GetValue(v, _ => new State());
            var from = state.Running ? state.Target : v.Offset.Y;
            if (down ? from < max - 0.5 : from > 0.5) { sv = v; break; }
        }
        if (sv is null) return;
        e.Handled = true;

        var st = States.GetValue(sv, _ => new State());
        var maxY = Math.Max(0, sv.Extent.Height - sv.Viewport.Height);
        // Колесо даёт ±1 за щелчок, тачпад — дробные значения: 100 пикселей на щелчок.
        var start = st.Running ? st.Target : sv.Offset.Y;
        st.Target = Math.Clamp(start - e.Delta.Y * 100, 0, maxY);
        if (!Animate.On)
        {
            sv.Offset = new Vector(sv.Offset.X, st.Target);
            return;
        }
        if (st.Running) return;
        st.Running = true;
        st.Last = TimeSpan.Zero;
        var viewer = sv;
        void Frame(TimeSpan now)
        {
            var dt = st.Last == TimeSpan.Zero ? 1 / 60.0 : Math.Clamp((now - st.Last).TotalSeconds, 0.001, 0.05);
            st.Last = now;
            var y = viewer.Offset.Y;
            var max = Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);
            st.Target = Math.Min(st.Target, max);
            // Экспоненциальное приближение: быстро в начале, мягко в конце, одинаково на 60 и 240 Гц.
            var next = y + (st.Target - y) * (1 - Math.Exp(-dt * 16));
            if (Math.Abs(st.Target - next) < 0.5) next = st.Target;
            viewer.Offset = new Vector(viewer.Offset.X, next);
            if (next == st.Target || !viewer.IsAttachedToVisualTree()) { st.Running = false; return; }
            top.RequestAnimationFrame(Frame);
        }
        top.RequestAnimationFrame(Frame);
    }
}
