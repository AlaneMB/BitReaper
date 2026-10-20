using System.Windows;
using System.Windows.Media;

namespace BitReaper.Controls;

/// <summary>Attached properties used by the neon control templates in Theme.xaml.</summary>
public static class Neon
{
    public static readonly DependencyProperty GlowProperty = DependencyProperty.RegisterAttached(
        "Glow", typeof(Color), typeof(Neon), new FrameworkPropertyMetadata(Colors.Transparent));

    public static Color GetGlow(DependencyObject element) => (Color)element.GetValue(GlowProperty);
    public static void SetGlow(DependencyObject element, Color value) => element.SetValue(GlowProperty, value);

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(CornerRadius), typeof(Neon), new FrameworkPropertyMetadata(new CornerRadius(10)));

    public static CornerRadius GetCornerRadius(DependencyObject element) => (CornerRadius)element.GetValue(CornerRadiusProperty);
    public static void SetCornerRadius(DependencyObject element, CornerRadius value) => element.SetValue(CornerRadiusProperty, value);

    /// <summary>Placeholder text for the search box template.</summary>
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached(
        "Placeholder", typeof(string), typeof(Neon), new FrameworkPropertyMetadata(""));

    public static string GetPlaceholder(DependencyObject element) => (string)element.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject element, string value) => element.SetValue(PlaceholderProperty, value);
}
