using System;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AccountaBall.App.Views;

/// Tiny code-first UI kit so the phase cards can be built in C# (no per-view XAML).
/// Keeps a consistent card look across the shell — a rounded, padded panel with a
/// title, body text, and primary/secondary buttons.
internal static class UiKit
{
    public static SolidColorBrush Brush(string hex) => new(Color(hex));

    public static Windows.UI.Color Color(string hex)
    {
        hex = hex.TrimStart('#');
        byte a = 255;
        if (hex.Length == 8)
        {
            a = Convert.ToByte(hex.Substring(0, 2), 16);
            hex = hex.Substring(2);
        }
        return Windows.UI.Color.FromArgb(a,
            Convert.ToByte(hex.Substring(0, 2), 16),
            Convert.ToByte(hex.Substring(2, 2), 16),
            Convert.ToByte(hex.Substring(4, 2), 16));
    }

    public static Border Card(UIElement content) => new()
    {
        CornerRadius = new CornerRadius(16),
        Background = Brush("#F7F7F9"),
        BorderBrush = Brush("#E2E2E6"),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(18),
        Child = content,
    };

    public static StackPanel VStack(double spacing = 10) => new() { Spacing = spacing };
    public static StackPanel HStack(double spacing = 8) => new() { Orientation = Orientation.Horizontal, Spacing = spacing };

    public static TextBlock Title(string t) => new()
    {
        Text = t,
        FontSize = 18,
        FontWeight = FontWeights.SemiBold,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Brush("#15151A"),
    };

    public static TextBlock Body(string t) => new()
    {
        Text = t,
        FontSize = 14,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Brush("#45454D"),
    };

    public static TextBlock Caption(string t) => new()
    {
        Text = t,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Brush("#86868B"),
    };

    public static Button Primary(string text, RoutedEventHandler onClick)
    {
        var b = new Button
        {
            Content = text,
            Padding = new Thickness(16, 8, 16, 8),
            CornerRadius = new CornerRadius(10),
            Background = Brush("#E8772E"),
            Foreground = Brush("#FFFFFF"),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        b.Click += onClick;
        return b;
    }

    public static Button Secondary(string text, RoutedEventHandler onClick)
    {
        var b = new Button
        {
            Content = text,
            Padding = new Thickness(16, 8, 16, 8),
            CornerRadius = new CornerRadius(10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        b.Click += onClick;
        return b;
    }

    /// A quiet, borderless text button for low-emphasis actions (e.g. Quit).
    public static Button Tertiary(string text, RoutedEventHandler onClick)
    {
        var b = new Button
        {
            Content = text,
            Padding = new Thickness(8, 4, 8, 4),
            Background = Brush("#00000000"),
            BorderThickness = new Thickness(0),
            Foreground = Brush("#86868B"),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        b.Click += onClick;
        return b;
    }
}
