using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using BitReaper.Controls;
using BitReaper.Util;

namespace BitReaper.Views;

/// <summary>Small factory helpers so the screens can be laid out in code with the theme's styles.</summary>
public static class Ui
{
    public static T Res<T>(string key) => (T)Application.Current.Resources[key];

    public static FontFamily DisplayFont => Res<FontFamily>("DisplayFont");
    public static FontFamily DisplayBoldFont => Res<FontFamily>("DisplayBoldFont");
    public static FontFamily IconFont => Res<FontFamily>("IconFont");
    public static FontFamily MonoFont => Res<FontFamily>("MonoFont");

    // Segoe Fluent Icons / MDL2 glyphs
    public const string GlyphBack = "";
    public const string GlyphForward = "";
    public const string GlyphUp = "";
    public const string GlyphRefresh = "";
    public const string GlyphDrive = "";
    public const string GlyphFolder = "";
    public const string GlyphNewFolder = "";
    public const string GlyphDelete = "";
    public const string GlyphAdd = "";
    public const string GlyphCheck = "";
    public const string GlyphClose = "";
    public const string GlyphPreview = "";
    public const string GlyphCopy = "";
    public const string GlyphZoomIn = "";
    public const string GlyphLock = "";
    public const string GlyphStar = "";
    public const string GlyphStarFilled = "";
    public const string GlyphInfo = "";
    public const string GlyphWarning = "";
    public const string GlyphChevronUp = "";
    public const string GlyphChevronDown = "";
    public const string GlyphChevronRight = "";
    public const string GlyphSettings = "";
    public const string GlyphOpen = "";
    public const string GlyphList = "";
    public const string GlyphChart = "";
    public const string GlyphShield = "";
    public const string GlyphSearch = "";
    public const string GlyphTarget = "";
    public const string GlyphUndo = "";
    public const string GlyphRecycle = "";
    public const string GlyphProperties = "";
    public const string GlyphStack = "";
    public const string GlyphSparkle = "";
    public const string GlyphCompleted = "";

    public static TextBlock Text(string text, double size = 13, Color? color = null, FontFamily? font = null,
                                 FontWeight? weight = null, bool wrap = false)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            Foreground = Theme.Brush(color ?? Theme.Text),
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (font != null) block.FontFamily = font;
        if (weight != null) block.FontWeight = weight.Value;
        return block;
    }

    public static TextBlock Display(string text, double size, Color? color = null, bool bold = false) =>
        Text(text, size, color, bold ? DisplayBoldFont : DisplayFont);

    public static TextBlock Secondary(string text, double size = 12, bool wrap = true) =>
        Text(text, size, Theme.TextSecondary, wrap: wrap);

    public static TextBlock Glyph(string glyph, double size = 14, Color? color = null) => new()
    {
        Text = glyph,
        FontFamily = IconFont,
        FontSize = size,
        Foreground = Theme.Brush(color ?? Theme.Text),
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    /// <summary>Neon bloom around text or shapes.</summary>
    public static T WithGlow<T>(this T element, Color color, double radius = 8, double opacity = 0.75) where T : UIElement
    {
        element.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            ShadowDepth = 0, BlurRadius = radius * 2, Color = color, Opacity = opacity,
        };
        return element;
    }

    public static StackPanel Row(double spacing, params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < children.Length; i++)
        {
            if (i > 0 && children[i] is FrameworkElement element)
                element.Margin = new Thickness(spacing + element.Margin.Left, element.Margin.Top, element.Margin.Right, element.Margin.Bottom);
            panel.Children.Add(children[i]);
        }
        return panel;
    }

    public static StackPanel Column(double spacing, params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical };
        for (int i = 0; i < children.Length; i++)
        {
            if (i > 0 && children[i] is FrameworkElement element)
                element.Margin = new Thickness(element.Margin.Left, spacing + element.Margin.Top, element.Margin.Right, element.Margin.Bottom);
            panel.Children.Add(children[i]);
        }
        return panel;
    }

    /// <summary>Glyph + label content for buttons.</summary>
    public static object Label(string? glyph, string text, double size = 13)
    {
        if (glyph == null) return text;
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        var icon = new TextBlock
        {
            Text = glyph, FontFamily = IconFont, FontSize = size - 1, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 1, 7, 0),
        };
        var row = Row(0, icon, label);
        row.Tag = text;   // the accessible name of the button showing it
        return row;
    }

    public enum Kind { Normal, Primary, Danger, Ghost }

    public static Button Button(object content, Kind kind = Kind.Normal, bool compact = false, Action? click = null, string? tip = null)
    {
        var button = new Button
        {
            Content = content,
            Style = Res<Style>(kind switch
            {
                Kind.Primary => "NeonPrimaryButton",
                Kind.Danger => "NeonDangerButton",
                Kind.Ghost => "NeonGhostButton",
                _ => "NeonButton",
            }),
        };
        if (compact)
        {
            button.Padding = new Thickness(10, 4, 10, 4);
            button.FontSize = 12;
            Neon.SetCornerRadius(button, new CornerRadius(8));
        }
        if (click != null) button.Click += (_, _) => click();
        if (tip != null) button.ToolTip = tip;
        if (content is FrameworkElement { Tag: string name }) System.Windows.Automation.AutomationProperties.SetName(button, name);
        return button;
    }

    public static Button IconButton(string glyph, Action click, string tip, double size = 32)
    {
        var button = new Button { Content = glyph, Style = Res<Style>("NeonIconButton"), ToolTip = tip, Width = size, Height = size };
        if (size < 32) button.FontSize = 12;
        System.Windows.Automation.AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => click();
        return button;
    }

    public static Button GlyphButton(string glyph, Action click, string tip, double size = 13)
    {
        var button = new Button { Content = glyph, Style = Res<Style>("PlainGlyphButton"), ToolTip = tip, FontSize = size };
        System.Windows.Automation.AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => click();
        return button;
    }

    public static Ellipse Dot(Color color, double size = 9)
    {
        var dot = new Ellipse { Width = size, Height = size, Fill = Theme.Brush(color), VerticalAlignment = VerticalAlignment.Center };
        return dot;
    }

    public static Image Image(string asset, double? height = null, double? width = null)
    {
        var image = new Image { Source = Asset(asset), Stretch = Stretch.Uniform };
        if (height != null) image.Height = height.Value;
        if (width != null) image.Width = width.Value;
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    private static readonly Dictionary<string, BitmapImage> Assets = new();

    public static BitmapImage Asset(string name)
    {
        if (Assets.TryGetValue(name, out var cached)) return cached;
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri($"pack://application:,,,/Assets/{name}.png");
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        Assets[name] = image;
        return image;
    }

    public static Image Icon(ImageSource source, double size) => new()
    {
        Source = source, Width = size, Height = size, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center,
    };

    public static NeonPanel Panel(UIElement child, Thickness padding, double corner = 14, Color? glow = null, Brush? fill = null, bool dashed = false)
    {
        var panel = new NeonPanel { Child = child, Padding = padding, CornerRadius = corner, Dashed = dashed };
        if (glow != null) panel.Glow = glow.Value;
        if (fill != null) panel.Fill = fill;
        return panel;
    }

    /// <summary>"DISKS ───" section heading in glowing cyan.</summary>
    public static UIElement SectionHeader(string title)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var text = Text(title.ToUpperInvariant(), 12, Theme.Cyan, DisplayBoldFont).WithGlow(Theme.Cyan, 4, 0.6);
        text.Margin = new Thickness(0, 0, 10, 0);
        var rule = new Rectangle { Height = 1, Fill = Res<Brush>("SectionRuleBrush"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(rule, 1);
        grid.Children.Add(text);
        grid.Children.Add(rule);
        // Letter spacing, roughly matching the macOS tracking.
        text.Text = string.Join(" ", title.ToUpperInvariant().ToCharArray());
        return grid;
    }

    /// <summary>A keyboard hint chip, e.g. "Ctrl+F".</summary>
    public static Border KeyHint(string text) => new()
    {
        BorderBrush = Theme.Brush(Theme.Line), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
        Padding = new Thickness(5, 1, 5, 1), VerticalAlignment = VerticalAlignment.Center,
        Child = Text(text, 10, Theme.TextTertiary),
    };

    public static Grid Spread(UIElement left, UIElement right)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    public static string Plural(int count, string noun) => $"{ByteFormat.Count(count)} {noun}{(count == 1 ? "" : "s")}";

    /// <summary>Shortens a path under the profile to %USERPROFILE%-relative "~\…" like the macOS tilde paths.</summary>
    public static string Abbreviate(string path)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path.StartsWith(home, StringComparison.OrdinalIgnoreCase) && (path.Length == home.Length || path[home.Length] == '\\'))
            return "~" + path[home.Length..];
        return path;
    }

    public static ContextMenu Menu(params object[] items)
    {
        var menu = new ContextMenu();
        foreach (var item in items) menu.Items.Add(item);
        return menu;
    }

    public static MenuItem MenuItem(string header, string? glyph, Action click, string? gesture = null, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        if (glyph != null) item.Icon = Glyph(glyph, 13, Theme.TextSecondary);
        if (gesture != null) item.InputGestureText = gesture;
        item.Click += (_, _) => click();
        return item;
    }

}
