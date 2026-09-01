using System.Windows;
using System.Windows.Media;

namespace NexusProgrammer;

internal static class ThemeService
{
    public const string DefaultThemeName = "Default";
    public const string HeritageThemeName = "Heritage";

    public static readonly string[] ThemeNames = [DefaultThemeName, HeritageThemeName];

    private static readonly IReadOnlyDictionary<string, string> DefaultColors = new Dictionary<string, string>
    {
        ["AppBackgroundBrush"] = "#F0F0F0",
        ["PanelBackgroundBrush"] = "#EFEFEF",
        ["ToolbarBackgroundBrush"] = "#E8E8E8",
        ["ToolbarButtonBackgroundBrush"] = "#E8E8E8",
        ["ToolbarButtonBorderBrush"] = "#E8E8E8",
        ["SurfaceBackgroundBrush"] = "#FFFFFF",
        ["SubtleBackgroundBrush"] = "#F6F6F6",
        ["InputBackgroundBrush"] = "#F7F7F7",
        ["ReadOnlyBackgroundBrush"] = "#F7F7F7",
        ["HoverBackgroundBrush"] = "#E9F3FF",
        ["PressedBackgroundBrush"] = "#DDEEFF",
        ["AccentBrush"] = "#0067C0",
        ["AccentSoftBrush"] = "#E5F1FB",
        ["AlternateRowBackgroundBrush"] = "#FBFBFB",
        ["TextBrush"] = "#000000",
        ["MutedTextBrush"] = "#333333",
        ["BorderBrush"] = "#B8B8B8",
        ["GridLineBrush"] = "#D9D9D9",
        ["LightGridLineBrush"] = "#F1F1F1",
        ["AddressBackgroundBrush"] = "#A8A8A8",
        ["AddressForegroundBrush"] = "#FFFFFF",
        ["SplitterBrush"] = "#D8D8D8",
        ["SelectionBackgroundBrush"] = "#DDEEFF",
        ["SelectionForegroundBrush"] = "#000000",
        ["ProgressTrackBrush"] = "#E5E5E5",
        ["StopBackgroundBrush"] = "#FFF0F0",
        ["StopForegroundBrush"] = "#B00000"
    };

    private static readonly IReadOnlyDictionary<string, string> HeritageColors = new Dictionary<string, string>
    {
        ["AppBackgroundBrush"] = "#F7F5F2",
        ["PanelBackgroundBrush"] = "#EFEAE3",
        ["ToolbarBackgroundBrush"] = "#E8E2DA",
        ["ToolbarButtonBackgroundBrush"] = "#F7F5F2",
        ["ToolbarButtonBorderBrush"] = "#C9BFB4",
        ["SurfaceBackgroundBrush"] = "#FFFFFF",
        ["SubtleBackgroundBrush"] = "#F2EFEA",
        ["InputBackgroundBrush"] = "#FFFFFF",
        ["ReadOnlyBackgroundBrush"] = "#F7F5F2",
        ["HoverBackgroundBrush"] = "#F3DED8",
        ["PressedBackgroundBrush"] = "#EAC5BB",
        ["AccentBrush"] = "#B8422E",
        ["AccentSoftBrush"] = "#F3DED8",
        ["AlternateRowBackgroundBrush"] = "#FBFAF8",
        ["TextBrush"] = "#1A1C1E",
        ["MutedTextBrush"] = "#6C7278",
        ["BorderBrush"] = "#B9B0A7",
        ["GridLineBrush"] = "#E5DED6",
        ["LightGridLineBrush"] = "#EEE8E1",
        ["AddressBackgroundBrush"] = "#6C7278",
        ["AddressForegroundBrush"] = "#FFFFFF",
        ["SplitterBrush"] = "#D8D0C6",
        ["SelectionBackgroundBrush"] = "#F3DED8",
        ["SelectionForegroundBrush"] = "#1A1C1E",
        ["ProgressTrackBrush"] = "#E8E2DA",
        ["StopBackgroundBrush"] = "#FFF1ED",
        ["StopForegroundBrush"] = "#B8422E"
    };

    public static void Apply(string? themeName)
    {
        var colors = string.Equals(themeName, HeritageThemeName, StringComparison.OrdinalIgnoreCase)
            ? HeritageColors
            : DefaultColors;

        foreach (var (key, value) in colors)
        {
            SetBrushColor(key, value);
        }
    }

    private static void SetBrushColor(string key, string value)
    {
        if (!Application.Current.Resources.Contains(key))
        {
            return;
        }

        Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
    }
}
