using System.Windows;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfColors = System.Windows.Media.Colors;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace QuickNoteApp.Services;

public sealed class DialogTheme
{
    public WpfBrush BgDeepBrush { get; init; } = WpfBrushes.White;
    public WpfBrush BgPanelBrush { get; init; } = WpfBrushes.White;
    public WpfBrush BgInputBrush { get; init; } = WpfBrushes.White;
    public WpfBrush BorderSoftBrush { get; init; } = WpfBrushes.LightGray;
    public WpfBrush TextPrimaryBrush { get; init; } = WpfBrushes.Black;
    public WpfBrush TextSecondaryBrush { get; init; } = WpfBrushes.DimGray;
    public WpfBrush TextMutedBrush { get; init; } = WpfBrushes.Gray;
    public WpfBrush ButtonBrush { get; init; } = WpfBrushes.White;
    public WpfBrush AccentPurpleBrush { get; init; } = WpfBrushes.MediumPurple;
    public WpfBrush AccentCyanBrush { get; init; } = WpfBrushes.DeepSkyBlue;
    public WpfBrush ErrorBrush { get; init; } = WpfBrushes.IndianRed;
    public WpfColor AccentPurple { get; init; } = WpfColors.MediumPurple;
    public WpfColor AccentCyan { get; init; } = WpfColors.DeepSkyBlue;

    public static DialogTheme Current()
    {
        return new DialogTheme
        {
            BgDeepBrush = BrushResource("BgDeepBrush", "#F4F7FB"),
            BgPanelBrush = BrushResource("BgPanelBrush", "#FFFFFF"),
            BgInputBrush = BrushResource("BgInputBrush", "#FFFFFF"),
            BorderSoftBrush = BrushResource("BorderSoftBrush", "#DDE5F0"),
            TextPrimaryBrush = BrushResource("TextPrimaryBrush", "#172033"),
            TextSecondaryBrush = BrushResource("TextSecondaryBrush", "#46556A"),
            TextMutedBrush = BrushResource("TextMutedBrush", "#748198"),
            ButtonBrush = BrushResource("ButtonBrush", "#FFFFFF"),
            AccentPurpleBrush = BrushResource("AccentPurpleBrush", "#6D5CFF"),
            AccentCyanBrush = BrushResource("AccentCyanBrush", "#0EA5E9"),
            ErrorBrush = new WpfSolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#DC2626")),
            AccentPurple = ColorResource("AccentPurple", "#6D5CFF"),
            AccentCyan = ColorResource("AccentCyan", "#0EA5E9")
        };
    }

    private static WpfBrush BrushResource(string key, string fallback)
    {
        if (System.Windows.Application.Current?.TryFindResource(key) is WpfBrush brush)
        {
            return brush;
        }

        return new WpfSolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString(fallback));
    }

    private static WpfColor ColorResource(string key, string fallback)
    {
        if (System.Windows.Application.Current?.TryFindResource(key) is WpfColor color)
        {
            return color;
        }

        return (WpfColor)WpfColorConverter.ConvertFromString(fallback);
    }
}
