using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BitReaper.Util;

namespace BitReaper.Controls;

/// <summary>
/// Rounded panel with a translucent fill, a cyan → violet → magenta outline and a soft neon bloom.
/// The bloom is drawn as fading outlines rather than with an Effect, so lists inside stay fast.
/// </summary>
public sealed class NeonPanel : Decorator
{
    public static readonly DependencyProperty GlowProperty = DependencyProperty.Register(
        nameof(Glow), typeof(Color), typeof(NeonPanel), new FrameworkPropertyMetadata(Theme.Violet, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(NeonPanel), new FrameworkPropertyMetadata(Theme.Brush(Theme.Panel.WithAlpha(0.82)), FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(double), typeof(NeonPanel), new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty DashedProperty = DependencyProperty.Register(
        nameof(Dashed), typeof(bool), typeof(NeonPanel), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register(
        nameof(Padding), typeof(Thickness), typeof(NeonPanel), new FrameworkPropertyMetadata(new Thickness(0), FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty GlowStrengthProperty = DependencyProperty.Register(
        nameof(GlowStrength), typeof(double), typeof(NeonPanel), new FrameworkPropertyMetadata(0.22, FrameworkPropertyMetadataOptions.AffectsRender));

    public Color Glow { get => (Color)GetValue(GlowProperty); set => SetValue(GlowProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public double CornerRadius { get => (double)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    public bool Dashed { get => (bool)GetValue(DashedProperty); set => SetValue(DashedProperty, value); }
    public Thickness Padding { get => (Thickness)GetValue(PaddingProperty); set => SetValue(PaddingProperty, value); }
    public double GlowStrength { get => (double)GetValue(GlowStrengthProperty); set => SetValue(GlowStrengthProperty, value); }

    private static readonly Brush BorderBrush = (Brush)new LinearGradientBrush(
        new GradientStopCollection
        {
            new(Theme.Cyan.WithAlpha(0.6), 0), new(Theme.Violet.WithAlpha(0.6), 0.5), new(Theme.Magenta.WithAlpha(0.6), 1),
        }, new Point(0, 0.5), new Point(1, 0.5)).GetAsFrozen();

    protected override Size MeasureOverride(Size constraint)
    {
        var padding = Padding;
        double h = padding.Left + padding.Right, v = padding.Top + padding.Bottom;
        if (Child == null) return new Size(h, v);
        Child.Measure(new Size(Math.Max(0, constraint.Width - h), Math.Max(0, constraint.Height - v)));
        return new Size(Child.DesiredSize.Width + h, Child.DesiredSize.Height + v);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var padding = Padding;
        Child?.Arrange(new Rect(padding.Left, padding.Top,
                                Math.Max(0, arrangeSize.Width - padding.Left - padding.Right),
                                Math.Max(0, arrangeSize.Height - padding.Top - padding.Bottom)));
        return arrangeSize;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var rect = new Rect(RenderSize);
        if (rect.Width < 2 || rect.Height < 2) return;
        double radius = CornerRadius;
        // Bloom: outlines growing outwards and fading.
        const int steps = 9;
        for (int i = steps; i >= 1; i--)
        {
            double t = 1 - (double)i / (steps + 1);
            var pen = new Pen(Theme.Brush(Glow.WithAlpha(GlowStrength * t * t * 0.9)), 1.6);
            var grown = rect;
            grown.Inflate(i * 1.3, i * 1.3);
            dc.DrawRoundedRectangle(null, pen, grown, radius + i * 1.3, radius + i * 1.3);
        }
        var inner = rect;
        inner.Inflate(-0.6, -0.6);
        var border = new Pen(BorderBrush, 1.2);
        if (Dashed) border.DashStyle = new DashStyle(new double[] { 5, 3.5 }, 0);
        dc.DrawRoundedRectangle(Fill, border, inner, radius, radius);
    }
}

/// <summary>Glowing horizontal size bar used in the Size column.</summary>
public sealed class SizeBar : FrameworkElement
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction), typeof(double), typeof(SizeBar), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(Color), typeof(SizeBar), new FrameworkPropertyMetadata(Theme.Pink, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty DimmedProperty = DependencyProperty.Register(
        nameof(Dimmed), typeof(bool), typeof(SizeBar), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Fraction { get => (double)GetValue(FractionProperty); set => SetValue(FractionProperty, value); }
    public Color Color { get => (Color)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }
    public bool Dimmed { get => (bool)GetValue(DimmedProperty); set => SetValue(DimmedProperty, value); }

    private static readonly Brush Track = Theme.Brush(Theme.Line.WithAlpha(0.55));

    protected override void OnRender(DrawingContext dc)
    {
        double height = 7;
        var track = new Rect(2, (RenderSize.Height - height) / 2, Math.Max(0, RenderSize.Width - 4), height);
        if (track.Width <= 0) return;
        dc.DrawRoundedRectangle(Track, null, track, 3.5, 3.5);
        if (Fraction <= 0) return;
        var fill = track;
        fill.Width = Math.Max(5, track.Width * Math.Min(Fraction, 1));
        var color = Dimmed ? Color.WithAlpha(0.25) : Color;
        if (!Dimmed)
        {
            var glow = fill;
            glow.Inflate(1.5, 1.5);
            dc.DrawRoundedRectangle(Theme.Brush(Color.WithAlpha(0.22)), null, glow, 5, 5);
        }
        var end = Palette.BarEnd(Color);
        var brush = new LinearGradientBrush(color, Dimmed ? end.WithAlpha(0.25) : end, 0);
        brush.Freeze();
        dc.DrawRoundedRectangle(brush, null, fill, 3.5, 3.5);
    }
}

/// <summary>Horizontal neon gauge (disk usage, selection share, deletion progress).</summary>
public sealed class UsageGauge : FrameworkElement
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction), typeof(double), typeof(UsageGauge), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Fraction { get => (double)GetValue(FractionProperty); set => SetValue(FractionProperty, value); }

    private Color[] _colors = { Theme.Cyan, Theme.Mint };
    public Color[] Colors
    {
        get => _colors;
        set { _colors = value; InvalidateVisual(); }
    }

    public UsageGauge()
    {
        Height = 8;
    }

    private static readonly Brush Track = Theme.Brush(Theme.Line.WithAlpha(0.55));

    protected override void OnRender(DrawingContext dc)
    {
        double h = RenderSize.Height, w = RenderSize.Width;
        if (w <= 0) return;
        dc.DrawRoundedRectangle(Track, null, new Rect(0, 0, w, h), h / 2, h / 2);
        double filled = Math.Max(h, w * Math.Clamp(Fraction, 0, 1));
        var last = _colors[^1];
        for (int i = 3; i >= 1; i--)
            dc.DrawRoundedRectangle(null, new Pen(Theme.Brush(last.WithAlpha(0.12 * (4 - i) / 3)), 1.5),
                                    new Rect(-i, -i, filled + 2 * i, h + 2 * i), h / 2 + i, h / 2 + i);
        dc.DrawRoundedRectangle(Theme.Gradient(_colors), null, new Rect(0, 0, filled, h), h / 2, h / 2);
    }
}

/// <summary>Neon donut of a folder's biggest items; the selected item's slice is highlighted.</summary>
public sealed class DonutChart : FrameworkElement
{
    public sealed record Slice(double Fraction, Color Color, bool Dimmed, bool Highlighted);

    private IReadOnlyList<Slice> _slices = Array.Empty<Slice>();
    public IReadOnlyList<Slice> Slices
    {
        get => _slices;
        set { _slices = value; InvalidateVisual(); }
    }

    private static readonly Pen TrackPen = new(Theme.Brush(Theme.Line.WithAlpha(0.5)), 22);

    protected override void OnRender(DrawingContext dc)
    {
        double side = Math.Min(RenderSize.Width, RenderSize.Height);
        if (side <= 0) return;
        const double lineWidth = 22;
        double radius = side / 2 - lineWidth / 2 - 8;
        var center = new Point(RenderSize.Width / 2, RenderSize.Height / 2);
        dc.DrawEllipse(null, TrackPen, center, radius, radius);

        double gap = _slices.Count > 1 ? 1.6 : 0;
        double start = -90;
        foreach (var slice in _slices)
        {
            double sweep = slice.Fraction * 360;
            if (sweep <= 0.4)
            {
                start += sweep;
                continue;
            }
            double a0 = start + gap / 2, a1 = start + Math.Max(sweep - gap / 2, gap / 2 + 0.2);
            double width = slice.Highlighted ? lineWidth + 6 : lineWidth;
            if (!slice.Dimmed)
            {
                // Bloom behind the arc.
                for (int i = 3; i >= 1; i--)
                    dc.DrawGeometry(null, new Pen(Theme.Brush(slice.Color.WithAlpha(0.10)), width + i * 4) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat },
                                    Arc(center, radius, a0, a1));
            }
            var pen = new Pen(Theme.Brush(slice.Dimmed ? slice.Color.WithAlpha(0.3) : slice.Color), width);
            dc.DrawGeometry(null, pen, Arc(center, radius, a0, a1));
            start += sweep;
        }
    }

    internal static Geometry Arc(Point center, double radius, double fromDegrees, double toDegrees)
    {
        if (toDegrees - fromDegrees >= 359.99) return new EllipseGeometry(center, radius, radius);
        Point P(double degrees)
        {
            double r = degrees * Math.PI / 180;
            return new Point(center.X + radius * Math.Cos(r), center.Y + radius * Math.Sin(r));
        }
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(P(fromDegrees), false, false);
            context.ArcTo(P(toDegrees), new Size(radius, radius), 0, toDegrees - fromDegrees > 180, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }
}

/// <summary>Scanning progress ring: a neon rainbow arc, determinate or spinning.</summary>
public sealed class ProgressRing : FrameworkElement
{
    private double? _fraction;
    private double _spin;

    public double? Fraction
    {
        get => _fraction;
        set { _fraction = value; InvalidateVisual(); }
    }

    public double Thickness { get; set; } = 12;

    /// <summary>Advances the indeterminate spinner; call from a timer.</summary>
    public void Tick(double seconds)
    {
        _spin = seconds % 1.6 / 1.6 * 360;
        if (_fraction == null) InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double side = Math.Min(RenderSize.Width, RenderSize.Height);
        if (side <= 0) return;
        double radius = side / 2 - Thickness / 2 - 10;
        var center = new Point(RenderSize.Width / 2, RenderSize.Height / 2);
        dc.DrawEllipse(null, new Pen(Theme.Brush(Theme.Line.WithAlpha(0.6)), Thickness), center, radius, radius);

        double from = _fraction != null ? -90 : _spin - 90;
        double sweep = _fraction != null ? 360 * Math.Clamp(_fraction.Value, 0, 1) : 108;
        if (sweep < 1) return;
        const int segments = 60;
        double step = sweep / segments;
        for (int i = 0; i < segments; i++)
        {
            double a0 = from + i * step, a1 = a0 + step + 0.6;
            var color = RingColor((a0 - from + (_fraction != null ? 0 : _spin)) / 360);
            for (int g = 2; g >= 1; g--)
                dc.DrawGeometry(null, new Pen(Theme.Brush(color.WithAlpha(0.12)), Thickness + g * 6), DonutChart.Arc(center, radius, a0, a1));
            dc.DrawGeometry(null, new Pen(Theme.Brush(color), Thickness), DonutChart.Arc(center, radius, a0, a1));
        }
    }

    private static Color RingColor(double t)
    {
        var ring = Palette.Ring;
        t = (t % 1 + 1) % 1 * (ring.Length - 1);
        int index = (int)t;
        return Palette.Blend(ring[index], ring[Math.Min(index + 1, ring.Length - 1)], t - index);
    }
}
