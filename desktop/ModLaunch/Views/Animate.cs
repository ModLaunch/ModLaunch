using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

    /// <summary>17.0: отложенный шаг анимации. Запись кадров (MotionShots) подменяет таймер своими часами.</summary>
    internal static Action<Action, int>? Delay;
    internal static bool Recording => Delay is not null;
    internal static void Later(Action a, int ms) { if (Delay is { } d) d(a, ms); else DispatcherTimer.RunOnce(a, TimeSpan.FromMilliseconds(ms)); }

    // Если анимацию перезапустили (быстро листаем игры), старый таймер не должен обрывать новую.
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, object> Runs = new();
    sealed record Original(Transitions? Transitions, double Opacity, bool OwnOpacity);
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, Original> Kept = new();

    /// <summary>
    /// Поставить элемент в начальное положение и через delay плавно вернуть на место.
    /// 17.0: кадры считаются сами, по времени кадра окна (RequestAnimationFrame), а не переходами Avalonia.
    /// У переходов между «поставили конечное значение» и первым тиком часов успевал проскочить кадр в конечном
    /// виде: страница на миг показывалась целиком, гасла и только потом проявлялась (мигание при каждом переходе).
    /// И конец больше не обрезается таймером: если поток был занят, анимация доигрывает, а не прыгает в конец.
    /// </summary>
    public static void From(Control c, string from, int ms = 320, int delay = 0, Easing? easing = null, double opacity = 0, bool force = false)
    {
        if (!On && !force) return;
        var run = new object();
        Runs.AddOrUpdate(c, run);
        // Исходное состояние запоминаем один раз: при перезапуске середина прошлой анимации — не «исходное».
        // У приглушённых карточек своя прозрачность — к ней и возвращаемся.
        if (!Kept.TryGetValue(c, out var orig)) { orig = new Original(c.Transitions, c.Opacity, c.IsSet(Visual.OpacityProperty)); Kept.AddOrUpdate(c, orig); }
        var (keep, target, ownOpacity) = (orig.Transitions, orig.Opacity, orig.OwnOpacity);
        var start = Moves.Parse(from);
        var ease = easing ?? new CubicEaseOut();
        var fade = new CubicEaseOut();
        c.Transitions = null;
        c.Opacity = opacity * target;
        c.RenderTransform = start.At(0);

        void Done()
        {
            if (!Runs.TryGetValue(c, out var still) || still != run) return;
            Runs.Remove(c);
            Kept.Remove(c);
            c.Transitions = keep;
            c.ClearValue(Visual.RenderTransformProperty);
            if (!ownOpacity) c.ClearValue(Visual.OpacityProperty); else c.Opacity = target;
        }
        void Go()
        {
            if (!Runs.TryGetValue(c, out var now) || now != run) return;
            if (TopLevel.GetTopLevel(c) is not { } top) { Done(); return; }
            TimeSpan? t0 = null;
            void Frame(TimeSpan t)
            {
                if (!Runs.TryGetValue(c, out var still) || still != run) return;
                t0 ??= t;
                var passed = (t - t0.Value).TotalMilliseconds;
                var k = Math.Clamp(passed / ms, 0, 1);
                c.Opacity = (opacity + (1 - opacity) * fade.Ease(Math.Clamp(passed / (ms * 0.7), 0, 1))) * target;
                c.RenderTransform = start.At(ease.Ease(k));
                if (k < 1 && c.IsAttachedToVisualTree()) top.RequestAnimationFrame(Frame);
                else Done();
            }
            top.RequestAnimationFrame(Frame);
        }
        if (delay <= 0) Dispatcher.UIThread.Post(Go, DispatcherPriority.Background);
        else Later(Go, delay);
    }

    /// <summary>
    /// Сдвиг, масштаб и поворот из строки вида «translateY(10px) scale(0.96)» — и любая точка пути до «none».
    /// </summary>
    sealed record Moves(double X, double Y, double Scale, double Turn)
    {
        public static Moves Parse(string text)
        {
            double x = 0, y = 0, k = 1, turn = 0;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, @"(\w+)\(([^)]*)\)"))
            {
                var args = m.Groups[2].Value.Split(',').Select(v => double.TryParse(v.Replace("px", "").Replace("deg", "").Trim(), System.Globalization.NumberStyles.Float, inv, out var d) ? d : 0).ToArray();
                switch (m.Groups[1].Value)
                {
                    case "translateX": x += args[0]; break;
                    case "translateY": y += args[0]; break;
                    case "translate": x += args[0]; y += args.Length > 1 ? args[1] : 0; break;
                    case "scale": k *= args[0]; break;
                    case "rotate": turn += args[0]; break;
                }
            }
            return new Moves(x, y, k, turn);
        }

        /// <summary>Положение на доле пути t: 0 — начальное, 1 — на месте.</summary>
        public ITransform At(double t)
        {
            var left = 1 - t;
            var b = TransformOperations.CreateBuilder(3);
            if (X != 0 || Y != 0) b.AppendTranslate(X * left, Y * left);
            if (Scale != 1) b.AppendScale(1 + (Scale - 1) * left, 1 + (Scale - 1) * left);
            if (Turn != 0) b.AppendRotate(Turn * left * Math.PI / 180);
            return b.Build();
        }
    }

    /// <summary>Страница въезжает снизу — только при переходе, не при перерисовке.</summary>
    /// 12.0: короче и ближе — переход не задерживает следующий клик.
    public static void PageIn(Control page) => From(page, "translateY(10px)", 220);

    /// <summary>
    /// Волна: карточки поднимаются по очереди (задержка ограничена, чтобы длинные списки не ждали).
    /// 9.5: без «отскока» и без увеличения — карточка не вылезает за край сетки и соседей.
    /// </summary>
    public static void Rise(Control c, int index) =>
        From(c, "translateY(10px)", 260, Math.Min(index * 16, 140), new QuinticEaseOut());

    /// <summary>Всплывающее окно появляется — чуть увеличиваясь, без «перелёта».</summary>
    public static void Pop(Control c) => From(c, "scale(0.96)", 260, 0, new CubicEaseOut());

    /// <summary>Уведомление въезжает справа, уходит — растворяясь.</summary>
    public static void ToastIn(Control c) => From(c, "translateX(40px)", 320, 0, new QuinticEaseOut());

    /// <summary>17.0: плавно сменить прозрачность с текущей (смена слайдов, затухание) — по кадрам.</summary>
    public static void FadeTo(Control c, double to, int ms)
    {
        var run = new object();
        Runs.AddOrUpdate(c, run);
        c.Transitions = null;
        var from = c.Opacity;
        if (!On || Math.Abs(from - to) < 0.01 || TopLevel.GetTopLevel(c) is not { } top) { c.Opacity = to; return; }
        var ease = new CubicEaseInOut();
        TimeSpan? t0 = null;
        void Frame(TimeSpan t)
        {
            if (!Runs.TryGetValue(c, out var still) || still != run) return;
            t0 ??= t;
            var k = Math.Clamp((t - t0.Value).TotalMilliseconds / ms, 0, 1);
            c.Opacity = from + (to - from) * ease.Ease(k);
            if (k < 1 && c.IsAttachedToVisualTree()) top.RequestAnimationFrame(Frame);
            else { c.Opacity = to; Runs.Remove(c); }
        }
        top.RequestAnimationFrame(Frame);
    }

    /// <summary>
    /// 17.0: полоска под вкладкой переезжает от прежней вкладки к новой — по кадрам и без «перелёта»
    /// (раньше BackEaseOut проскакивал мимо вкладки и возвращался, а первый кадр мог показать полоску уже на месте).
    /// </summary>
    public static void Underline(Border ink, (double X, double W)? from, (double X, double W) to)
    {
        static ITransform At(double x) { var b = TransformOperations.CreateBuilder(1); b.AppendTranslate(x, 0); return b.Build(); }
        ink.Transitions = null;
        var run = new object();
        Runs.AddOrUpdate(ink, run);
        if (from is not { } was || !On || Math.Abs(was.X - to.X) <= 1 || TopLevel.GetTopLevel(ink) is not { } top)
        {
            ink.Width = to.W;
            ink.RenderTransform = At(to.X);
            return;
        }
        ink.Width = was.W;
        ink.RenderTransform = At(was.X);
        var ease = new QuinticEaseOut();
        TimeSpan? t0 = null;
        void Frame(TimeSpan t)
        {
            if (!Runs.TryGetValue(ink, out var still) || still != run) return;
            t0 ??= t;
            var k = Math.Clamp((t - t0.Value).TotalMilliseconds / 300, 0, 1);
            var e = ease.Ease(k);
            ink.Width = was.W + (to.W - was.W) * e;
            ink.RenderTransform = At(was.X + (to.X - was.X) * e);
            if (k < 1 && ink.IsAttachedToVisualTree()) top.RequestAnimationFrame(Frame);
        }
        top.RequestAnimationFrame(Frame);
    }

    /// <summary>17.0: уход тоже по кадрам — без мига «исчезло и снова появилось», которое давали переходы.</summary>
    public static void ToastOut(Control c, Action remove)
    {
        if (!On || TopLevel.GetTopLevel(c) is not { } top) { remove(); return; }
        Runs.AddOrUpdate(c, new object());
        var away = Moves.Parse("translateX(40px) scale(0.96)");
        var ease = new CubicEaseIn();
        c.Transitions = null;
        TimeSpan? t0 = null;
        void Frame(TimeSpan t)
        {
            t0 ??= t;
            var k = Math.Clamp((t - t0.Value).TotalMilliseconds / 220, 0, 1);
            var e = ease.Ease(k);
            c.Opacity = 1 - e;
            c.RenderTransform = away.At(1 - e);
            if (k < 1 && c.IsAttachedToVisualTree()) top.RequestAnimationFrame(Frame);
            else remove();
        }
        top.RequestAnimationFrame(Frame);
    }
}
