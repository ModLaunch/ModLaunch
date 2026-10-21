using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// 12.0: «лор» игры поверх арта — щупальца, частицы, лучи, помехи (слои Lore.Theme для стадии игрока).
/// Рисуется вручную в Render: никаких элементов и раскладки, только линии и круги, ~30 кадров в секунду,
/// пока виден. Всё медленное и полупрозрачное — спокойно, без вспышек. Fade — общая прозрачность
/// (для анимации открытия игры). Без анимаций — один неподвижный кадр.
/// </summary>
public sealed class LoreVeil : Control
{
    readonly Lore.Layer[] _layers;
    readonly System.Diagnostics.Stopwatch _clock = new();
    readonly DispatcherTimer _tick;
    readonly double _scale;
    readonly int _seed;

    /// <summary>Сила слоёв 0…1 (умножается на Amount каждого слоя).</summary>
    public double Strength { get; set; } = 1;
    /// <summary>Слои «растут» к своей силе за это время (щупальца выползают из краёв).</summary>
    public double GrowMs { get; set; } = 1400;
    /// <summary>Время для неподвижного кадра (снимки экрана, анимации выключены).</summary>
    public double StillMs { get; set; } = 2600;

    public LoreVeil(Games.GameDef def, int stage, double scale = 1)
    {
        _layers = Lore.For(def).Stages[Math.Clamp(stage, 0, 3)];
        _scale = scale;
        _seed = def.Id.Aggregate(17, (h, ch) => h * 31 + ch);
        IsHitTestVisible = false;
        ClipToBounds = true;
        _tick = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => InvalidateVisual());
        AttachedToVisualTree += (_, _) =>
        {
            _clock.Restart();
            if (Animate.On) _tick.Start();
        };
        DetachedFromVisualTree += (_, _) => _tick.Stop();
    }

    public static LoreVeil For(Games.GameDef def, double scale = 1) => new(def, Lore.Of(def).Stage, scale);

    double Now => Animate.On ? _clock.Elapsed.TotalMilliseconds : StillMs;

    public override void Render(DrawingContext dc)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w < 10 || h < 10 || Strength <= 0.01) return;
        var ms = Now;
        var t = ms / 1000;
        var grow = GrowMs <= 0 ? 1 : 1 - Math.Pow(1 - Math.Clamp(ms / GrowMs, 0, 1), 3);
        for (var i = 0; i < _layers.Length; i++)
        {
            var l = _layers[i];
            var c = Color.TryParse(l.Color, out var cc) ? cc : Colors.White;
            Color? c2 = l.Color2 is { } s2 && Color.TryParse(s2, out var x2) ? x2 : null;
            var a = Math.Clamp(l.Amount * Strength, 0, 1);
            var rng = new Random(_seed + i * 7919);
            switch (l.Kind)
            {
                case "motes": Motes(dc, rng, w, h, t, c, a * grow, up: true, square: false); break;
                case "pixels": Motes(dc, rng, w, h, t, c, a * grow, up: true, square: true); break;
                case "snow": Fall(dc, rng, w, h, t, c, a * grow, leaf: false); break;
                case "leaves": Fall(dc, rng, w, h, t, c, a * grow, leaf: true); break;
                case "rain": Rain(dc, rng, w, h, t, c, a * grow); break;
                case "bubbles": Bubbles(dc, rng, w, h, t, c, a * grow); break;
                case "tendrils": Tendrils(dc, rng, w, h, t, c, c2, a, grow); break;
                case "threads": Threads(dc, rng, w, h, t, c, c2, a, grow); break;
                case "rays": Rays(dc, rng, w, h, t, c, a * grow); break;
                case "aurora": Aurora(dc, w, h, t, c, c2 ?? c, a * grow); break;
                case "scan": Scan(dc, rng, w, h, t, c, a * grow); break;
                case "ring": Ring(dc, w, h, t, c, c2, a * grow); break;
                case "stars": Stars(dc, rng, w, h, t, c, a * grow); break;
                case "vignette": Vignette(dc, w, h, c, a * grow); break;
            }
        }
    }

    static Color A(Color c, double a) => Color.FromArgb((byte)Math.Clamp(a * 255, 0, 255), c.R, c.G, c.B);
    static double Wrap(double v, double m) => ((v % m) + m) % m;

    // ---------------------------------------------------------------- частицы

    void Motes(DrawingContext dc, Random rng, double w, double h, double t, Color c, double a, bool up, bool square)
    {
        var n = (int)(14 + 70 * a);
        for (var i = 0; i < n; i++)
        {
            var x0 = rng.NextDouble() * w;
            var y0 = rng.NextDouble() * h;
            var speed = (8 + rng.NextDouble() * 22) * _scale;
            var size = (square ? 4 + rng.NextDouble() * 5 : 1 + rng.NextDouble() * 2.4) * _scale;
            var phase = rng.NextDouble() * Math.PI * 2;
            var y = Wrap(y0 - (up ? 1 : -1) * t * speed, h + 20) - 10;
            var x = x0 + Math.Sin(t * 0.4 + phase) * 14 * _scale;
            // Ярче в середине пути, гаснут у краёв — частицы не «выскакивают».
            var life = Math.Sin(Math.Clamp(y / h, 0, 1) * Math.PI);
            var flicker = 0.65 + 0.35 * Math.Sin(t * 1.3 + phase * 3);
            var alpha = a * life * flicker;
            if (alpha < 0.02) continue;
            if (square)
            {
                dc.FillRectangle(new SolidColorBrush(A(c, alpha * 0.85)), new Rect(Math.Round(x), Math.Round(y), size, size));
            }
            else
            {
                dc.DrawEllipse(new SolidColorBrush(A(c, alpha * 0.18)), null, new Point(x, y), size * 3.2, size * 3.2);
                dc.DrawEllipse(new SolidColorBrush(A(c, alpha * 0.9)), null, new Point(x, y), size, size);
            }
        }
    }

    void Fall(DrawingContext dc, Random rng, double w, double h, double t, Color c, double a, bool leaf)
    {
        var n = (int)(12 + (leaf ? 30 : 70) * a);
        for (var i = 0; i < n; i++)
        {
            var x0 = rng.NextDouble() * w;
            var y0 = rng.NextDouble() * h;
            var speed = (leaf ? 18 + rng.NextDouble() * 16 : 14 + rng.NextDouble() * 30) * _scale;
            var size = (leaf ? 3 + rng.NextDouble() * 3 : 1 + rng.NextDouble() * 2.2) * _scale;
            var phase = rng.NextDouble() * Math.PI * 2;
            var y = Wrap(y0 + t * speed, h + 20) - 10;
            var x = Wrap(x0 + t * speed * 0.35 + Math.Sin(t * 0.7 + phase) * 18 * _scale, w + 20) - 10;
            var alpha = a * (0.45 + 0.4 * rng.NextDouble());
            if (leaf)
            {
                using (dc.PushTransform(Matrix.CreateRotation(t * 0.8 + phase) * Matrix.CreateTranslation(x, y)))
                    dc.DrawEllipse(new SolidColorBrush(A(c, alpha)), null, new Point(0, 0), size * 1.7, size * 0.7);
            }
            else dc.DrawEllipse(new SolidColorBrush(A(c, alpha)), null, new Point(x, y), size, size);
        }
    }

    void Rain(DrawingContext dc, Random rng, double w, double h, double t, Color c, double a)
    {
        var n = (int)(20 + 80 * a);
        var pen = new Pen(new SolidColorBrush(A(c, 0.25 + 0.3 * a)), 1 * _scale);
        for (var i = 0; i < n; i++)
        {
            var x0 = rng.NextDouble() * w;
            var y0 = rng.NextDouble() * h;
            var speed = (380 + rng.NextDouble() * 260) * _scale;
            var len = (14 + rng.NextDouble() * 18) * _scale;
            var y = Wrap(y0 + t * speed, h + len * 2) - len;
            var x = Wrap(x0 + (y - y0) * 0.12, w);
            dc.DrawLine(pen, new Point(x, y), new Point(x + len * 0.12, y + len));
        }
    }

    void Bubbles(DrawingContext dc, Random rng, double w, double h, double t, Color c, double a)
    {
        var n = (int)(8 + 30 * a);
        for (var i = 0; i < n; i++)
        {
            var x0 = rng.NextDouble() * w;
            var y0 = rng.NextDouble() * h;
            var speed = (20 + rng.NextDouble() * 40) * _scale;
            var r = (1.5 + rng.NextDouble() * 4) * _scale;
            var phase = rng.NextDouble() * Math.PI * 2;
            var y = Wrap(y0 - t * speed, h + 20) - 10;
            var x = x0 + Math.Sin(t * 1.6 + phase) * 4 * _scale;
            var alpha = a * Math.Sin(Math.Clamp(y / h, 0, 1) * Math.PI);
            dc.DrawEllipse(new SolidColorBrush(A(c, alpha * 0.12)), new Pen(new SolidColorBrush(A(c, alpha * 0.7)), 0.9 * _scale), new Point(x, y), r, r);
        }
    }

    void Stars(DrawingContext dc, Random rng, double w, double h, double t, Color c, double a)
    {
        var n = (int)(20 + 70 * a);
        for (var i = 0; i < n; i++)
        {
            var x = rng.NextDouble() * w;
            var y = rng.NextDouble() * h * 0.85;
            var r = (0.6 + rng.NextDouble() * 1.4) * _scale;
            var tw = 0.5 + 0.5 * Math.Sin(t * (0.6 + rng.NextDouble()) + rng.NextDouble() * 6.28);
            var alpha = a * (0.25 + 0.75 * tw);
            dc.DrawEllipse(new SolidColorBrush(A(c, alpha)), null, new Point(x, y), r, r);
        }
    }

    // ---------------------------------------------------------------- щупальца и нити

    /// <summary>
    /// Щупальца растут из краёв (больше — снизу и по бокам), медленно колышутся, к концу тоньше.
    /// С кромкой (rim) под каждым рисуется светлый контур чуть шире — «чёрные с белым».
    /// </summary>
    void Tendrils(DrawingContext dc, Random rng, double w, double h, double t, Color c, Color? rim, double a, double grow)
    {
        var n = (int)Math.Round(4 + 9 * a);
        var m = Math.Min(w, h);
        const int segs = 26;
        for (var i = 0; i < n; i++)
        {
            // Корень: левый край, правый край, низ — по кругу, с разбросом.
            var side = i % 3;
            var u = 0.08 + rng.NextDouble() * 0.84;
            var (root, dir) = side switch
            {
                0 => (new Point(-4, h * (0.35 + u * 0.65)), 0.0),
                1 => (new Point(w + 4, h * (0.35 + u * 0.65)), Math.PI),
                _ => (new Point(w * u, h + 4), -Math.PI / 2),
            };
            dir += (rng.NextDouble() - 0.5) * 0.9;
            var length = m * (0.22 + rng.NextDouble() * 0.3) * (0.55 + 0.45 * a) * grow;
            var width = (18 + rng.NextDouble() * 20) * _scale * (0.7 + 0.3 * a);
            var phase = rng.NextDouble() * Math.PI * 2;
            var curl = (rng.NextDouble() < 0.5 ? -1 : 1) * (0.9 + rng.NextDouble() * 1.4);
            var pts = new Point[segs + 1];
            pts[0] = root;
            var ang = dir;
            var step = length / segs;
            for (var s = 1; s <= segs; s++)
            {
                var k = (double)s / segs;
                ang += (Math.Sin(t * 0.55 + phase + k * 3.2) * 0.07 + curl * 0.012 * k * k * 4) ;
                pts[s] = new Point(pts[s - 1].X + Math.Cos(ang) * step, pts[s - 1].Y + Math.Sin(ang) * step);
            }
            if (rim is Color r)
                for (var s = 1; s <= segs; s++)
                {
                    var k = (double)s / segs;
                    var th = width * Math.Pow(1 - k * 0.94, 1.4) + 4 * _scale;
                    dc.DrawLine(new Pen(new SolidColorBrush(A(r, 0.55 * a * (1 - k * 0.5))), th, lineCap: PenLineCap.Round), pts[s - 1], pts[s]);
                }
            for (var s = 1; s <= segs; s++)
            {
                var k = (double)s / segs;
                var th = width * Math.Pow(1 - k * 0.94, 1.4);
                dc.DrawLine(new Pen(new SolidColorBrush(A(c, Math.Min(1, 0.55 + 0.45 * a))), th, lineCap: PenLineCap.Round), pts[s - 1], pts[s]);
            }
        }
    }

    /// <summary>Нити шёлка: тонкие провисающие дуги через экран, колышутся; с кромкой — блестят.</summary>
    void Threads(DrawingContext dc, Random rng, double w, double h, double t, Color c, Color? rim, double a, double grow)
    {
        var n = (int)Math.Round(3 + 8 * a);
        for (var i = 0; i < n; i++)
        {
            var y1 = rng.NextDouble() * h * 0.9;
            var y2 = rng.NextDouble() * h * 0.9;
            var sag = (40 + rng.NextDouble() * 120) * _scale;
            var phase = rng.NextDouble() * Math.PI * 2;
            var p1 = new Point(-10, y1);
            var p2 = new Point(-10 + (w + 20) * grow, y2 + (1 - grow) * 30);
            var mid = new Point((p1.X + p2.X) / 2 + Math.Sin(t * 0.4 + phase) * 30 * _scale, (y1 + y2) / 2 + sag + Math.Sin(t * 0.6 + phase) * 12 * _scale);
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(p1, false);
                ctx.QuadraticBezierTo(mid, p2);
                ctx.EndFigure(false);
            }
            var th = (0.8 + rng.NextDouble() * 1.6) * _scale;
            if (rim is Color r) dc.DrawGeometry(null, new Pen(new SolidColorBrush(A(r, 0.22 * a)), th + 2.5 * _scale), g);
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(A(c, 0.35 + 0.5 * a)), th), g);
        }
    }

    // ---------------------------------------------------------------- свет

    void Rays(DrawingContext dc, Random rng, double w, double h, double t, Color c, double a)
    {
        var n = 5;
        for (var i = 0; i < n; i++)
        {
            var x = w * (0.1 + 0.8 * rng.NextDouble());
            var spread = w * (0.04 + rng.NextDouble() * 0.06);
            var lean = w * 0.18;
            var alpha = a * (0.10 + 0.08 * Math.Sin(t * 0.35 + i * 1.7));
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(x - spread * 0.3, -2), true);
                ctx.LineTo(new Point(x + spread * 0.3, -2));
                ctx.LineTo(new Point(x + lean + spread, h * 0.95));
                ctx.LineTo(new Point(x + lean - spread, h * 0.95));
                ctx.EndFigure(true);
            }
            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(A(c, alpha), 0), new GradientStop(A(c, 0), 1) },
            };
            dc.DrawGeometry(brush, null, g);
        }
    }

    void Aurora(DrawingContext dc, double w, double h, double t, Color c1, Color c2, double a)
    {
        for (var band = 0; band < 3; band++)
        {
            var g = new StreamGeometry();
            var baseY = h * (0.12 + band * 0.08);
            var thick = h * (0.10 + band * 0.03);
            using (var ctx = g.Open())
            {
                const int steps = 24;
                Point P(int s, double off) { var x = w * s / steps; return new Point(x, baseY + off + Math.Sin(x / w * 5 + t * 0.25 + band * 1.3) * h * 0.04); }
                ctx.BeginFigure(P(0, 0), true);
                for (var s = 1; s <= steps; s++) ctx.LineTo(P(s, 0));
                for (var s = steps; s >= 0; s--) ctx.LineTo(P(s, thick));
                ctx.EndFigure(true);
            }
            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops = { new GradientStop(A(c1, 0), 0), new GradientStop(A(c1, a * 0.28), 0.35), new GradientStop(A(c2, a * 0.22), 0.7), new GradientStop(A(c2, 0), 1) },
            };
            dc.DrawGeometry(brush, null, g);
        }
    }

    // ---------------------------------------------------------------- экран и формы

    /// <summary>Как кадр с камеры: тонкие строки, медленная светлая полоса и редкие сдвиги.</summary>
    void Scan(DrawingContext dc, Random rng, double w, double h, double t, Color c, double a)
    {
        var line = new SolidColorBrush(A(Colors.Black, 0.10 + 0.12 * a));
        for (var y = 0.0; y < h; y += 3 * _scale) dc.FillRectangle(line, new Rect(0, y, w, 1));
        var bandY = Wrap(t * 60 * _scale, h + 120) - 60;
        dc.FillRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(A(c, 0), 0), new GradientStop(A(c, 0.07 * a + 0.02), 0.5), new GradientStop(A(c, 0), 1) },
        }, new Rect(0, bandY, w, 60 * _scale));
        // Пара «сбитых» полосок раз в несколько секунд — коротко и тускло.
        var slot = Math.Floor(t / 2.7);
        var r = new Random(_seed ^ (int)slot);
        if (Wrap(t, 2.7) < 0.12)
            for (var i = 0; i < 2; i++)
                dc.FillRectangle(new SolidColorBrush(A(c, 0.08 * a + 0.03)), new Rect(r.NextDouble() * w * 0.3, r.NextDouble() * h, w * (0.3 + r.NextDouble() * 0.5), (2 + r.NextDouble() * 5) * _scale));
    }

    void Ring(DrawingContext dc, double w, double h, double t, Color c, Color? c2, double a)
    {
        var center = new Point(w * 0.72, h * 0.42);
        var r = Math.Min(w, h) * (0.22 + 0.01 * Math.Sin(t * 0.8));
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(A(c, 0.12 * a)), 18 * _scale), center, r, r);
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(A(c, 0.55 * a)), 1.6 * _scale), center, r, r);
        if (c2 is Color k)
        {
            var r2 = r * (1.18 + 0.04 * Math.Sin(t * 0.5));
            dc.DrawEllipse(null, new Pen(new SolidColorBrush(A(k, 0.35 * a)), 1 * _scale, new DashStyle([2, 6], t * 4)), center, r2, r2);
        }
    }

    void Vignette(DrawingContext dc, double w, double h, Color c, double a)
    {
        var brush = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.45, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.5, 0.45, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative), RadiusY = new RelativeScalar(0.75, RelativeUnit.Relative),
            GradientStops = { new GradientStop(A(c, 0), 0.45), new GradientStop(A(c, a * 0.85), 1) },
        };
        dc.FillRectangle(brush, new Rect(0, 0, w, h));
    }
}
