using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PCPulse.App.Services;

internal static class ThemeTokens
{
    public static readonly IBrush WindowBackground = Brush.Parse("#0B1118");
    public static readonly IBrush SidebarBackground = Brush.Parse("#101822");
    public static readonly IBrush CardBackground = Brush.Parse("#151F2C");
    public static readonly IBrush CardAltBackground = Brush.Parse("#1B2736");
    public static readonly IBrush BorderBrush = Brush.Parse("#253549");
    public static readonly IBrush AccentBrush = Brush.Parse("#38BDF8");
    public static readonly IBrush AccentSoftBrush = Brush.Parse("#16324A");
    public static readonly IBrush SuccessBrush = Brush.Parse("#34D399");
    public static readonly IBrush WarningBrush = Brush.Parse("#FBBF24");
    public static readonly IBrush DangerBrush = Brush.Parse("#F87171");
    public static readonly IBrush TextPrimary = Brush.Parse("#F1F5F9");
    public static readonly IBrush TextMuted = Brush.Parse("#94A3B8");

    public static Border CreateCard(Control child, Thickness? padding = null) =>
        new()
        {
            Background = CardBackground,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = padding ?? new Thickness(18),
            Child = child
        };

    public static Button CreatePrimaryButton(string text) =>
        new()
        {
            Content = text,
            Background = AccentBrush,
            Foreground = Brush.Parse("#08111B"),
            FontWeight = FontWeight.SemiBold,
            Padding = new Thickness(16, 9),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Left
        };

    public static Button CreateSecondaryButton(string text) =>
        new()
        {
            Content = text,
            Background = CardAltBackground,
            Foreground = TextPrimary,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 8),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Left
        };

    public static Button CreateDangerButton(string text) =>
        new()
        {
            Content = text,
            Background = Brush.Parse("#3B1720"),
            Foreground = DangerBrush,
            BorderBrush = Brush.Parse("#7F1D1D"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 8),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Left
        };
}
