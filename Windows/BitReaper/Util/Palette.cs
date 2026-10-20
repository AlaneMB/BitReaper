using System.Windows.Media;

namespace BitReaper.Util;

/// <summary>Theme colours for code, mirroring Theme.xaml.</summary>
public static class Theme
{
    public static readonly Color Night = Hex(0x0B0824);
    public static readonly Color NightDeep = Hex(0x05030F);
    public static readonly Color Panel = Hex(0x120D30);
    public static readonly Color PanelRaised = Hex(0x1A1342);
    public static readonly Color Line = Hex(0x3A2A8C);
    public static readonly Color Magenta = Hex(0xFF2BD6);
    public static readonly Color Pink = Hex(0xFF3D9A);
    public static readonly Color Purple = Hex(0xA24BFF);
    public static readonly Color Violet = Hex(0x6C3BFF);
    public static readonly Color Blue = Hex(0x2F8BFF);
    public static readonly Color Cyan = Hex(0x22E4F5);
    public static readonly Color Yellow = Hex(0xFFC93C);
    public static readonly Color Orange = Hex(0xFF7A3D);
    public static readonly Color Mint = Hex(0x3DFFC5);
    public static readonly Color Danger = Hex(0xFF3D6E);
    public static readonly Color Text = Hex(0xF4F0FF);
    public static readonly Color TextSecondary = Hex(0xA99BD8);
    public static readonly Color TextTertiary = Hex(0x6E62A0);

    public static Color Hex(uint rgb, byte alpha = 255) =>
        Color.FromArgb(alpha, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    public static Color WithAlpha(this Color color, double alpha) =>
        Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), color.R, color.G, color.B);

    private static readonly Dictionary<Color, SolidColorBrush> Brushes = new();

    /// <summary>A frozen brush for <paramref name="color"/>, cached.</summary>
    public static SolidColorBrush Brush(Color color)
    {
        if (Brushes.TryGetValue(color, out var brush)) return brush;
        brush = new SolidColorBrush(color);
        brush.Freeze();
        Brushes[color] = brush;
        return brush;
    }

    public static LinearGradientBrush Gradient(params Color[] colors)
    {
        var brush = new LinearGradientBrush { StartPoint = new(0, 0.5), EndPoint = new(1, 0.5) };
        for (int i = 0; i < colors.Length; i++)
            brush.GradientStops.Add(new GradientStop(colors[i], colors.Length == 1 ? 0 : (double)i / (colors.Length - 1)));
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// Item colours. Every top-level item of the current folder gets a neon colour slot by size rank
/// (pink, blue, cyan, purple, yellow, …); its descendants inherit it, a little paler at each level.
/// </summary>
public static class Palette
{
    public static readonly Color[] Neon =
    {
        Theme.Pink, Theme.Blue, Theme.Cyan, Theme.Purple, Theme.Yellow, Theme.Orange, Theme.Magenta, Theme.Mint,
    };

    /// <summary>Items past the named slots share a calm blue, like the "smaller objects" in the overview.</summary>
    public static readonly Color Minor = Theme.Hex(0x4F6BFF);

    public static Color ColorFor(int? slot, int depth = 0)
    {
        if (slot is not { } s) return Theme.TextTertiary;
        var baseColor = s < Neon.Length ? Neon[s] : Minor;
        double fade = Math.Min(depth * 0.13, 0.45);
        return Blend(baseColor, Theme.Text, fade);
    }

    public static Color Blend(Color from, Color to, double fraction) => Color.FromArgb(
        255,
        (byte)Math.Round(from.R + (to.R - from.R) * fraction),
        (byte)Math.Round(from.G + (to.G - from.G) * fraction),
        (byte)Math.Round(from.B + (to.B - from.B) * fraction));

    /// <summary>The warm end of an item's size-bar gradient (pink → orange, blue → violet, …).</summary>
    public static Color BarEnd(Color color)
    {
        ToHsv(color, out double h, out double s, out double v);
        h += 0.07;
        if (h > 1) h -= 1;
        return FromHsv(h, s, Math.Min(1, v * 1.05), color.A);
    }

    /// <summary>Gauge colours for disk usage: mint while at least half is free, hot pink when almost full.</summary>
    public static Color[] UsageColors(double usedFraction) => usedFraction switch
    {
        < 0.5 => new[] { Theme.Cyan, Theme.Mint },
        < 0.75 => new[] { Theme.Cyan, Theme.Yellow },
        < 0.9 => new[] { Theme.Yellow, Theme.Orange },
        _ => new[] { Theme.Magenta, Theme.Danger },
    };

    public static readonly Color[] Ring =
    {
        Theme.Cyan, Theme.Blue, Theme.Violet, Theme.Magenta, Theme.Pink, Theme.Orange, Theme.Yellow, Theme.Cyan,
    };

    private static void ToHsv(Color c, out double h, out double s, out double v)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;
        v = max;
        s = max == 0 ? 0 : delta / max;
        if (delta == 0) { h = 0; return; }
        if (max == r) h = (g - b) / delta % 6;
        else if (max == g) h = (b - r) / delta + 2;
        else h = (r - g) / delta + 4;
        h /= 6;
        if (h < 0) h += 1;
    }

    private static Color FromHsv(double h, double s, double v, byte alpha)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs(h * 6 % 2 - 1));
        double m = v - c;
        (double r, double g, double b) = (int)(h * 6) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return Color.FromArgb(alpha, (byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
