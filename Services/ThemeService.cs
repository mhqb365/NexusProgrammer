using System.Windows;
using System.Windows.Media;

namespace NexusProgrammer;

internal static class ThemeService
{
    public const string DefaultThemeName = "Default";
    public const string ArcadeThemeName = "Arcade";
    public const string HeritageThemeName = "Heritage";
    public const string MatchaThemeName = "Matcha";
    public const string VioletThemeName = "Violet";

    public static readonly string[] ThemeNames =
    [
        DefaultThemeName,
        ArcadeThemeName,
        HeritageThemeName,
        MatchaThemeName,
        VioletThemeName
    ];

    private static readonly IReadOnlyDictionary<string, string> DefaultColors = new Dictionary<string, string>
    {
        ["AppBackgroundBrush"] = "#F0F0F0",
        ["PanelBackgroundBrush"] = "#EFEFEF",
        ["ToolbarBackgroundBrush"] = "#E8E8E8",
        ["ToolbarButtonBackgroundBrush"] = "#E8E8E8",
        ["ToolbarButtonHoverBackgroundBrush"] = "#E9F3FF",
        ["ToolbarButtonPressedBackgroundBrush"] = "#DDEEFF",
        ["ToolbarButtonBorderBrush"] = "#E8E8E8",
        ["ToolbarButtonForegroundBrush"] = "#000000",
        ["HexHeaderButtonBackgroundBrush"] = "#F7F7F7",
        ["HexHeaderButtonForegroundBrush"] = "#000000",
        ["HexHeaderButtonBorderBrush"] = "#B8B8B8",
        ["StandardButtonBackgroundBrush"] = "#F7F7F7",
        ["StandardButtonHoverBackgroundBrush"] = "#E9F3FF",
        ["StandardButtonPressedBackgroundBrush"] = "#DDEEFF",
        ["StandardButtonForegroundBrush"] = "#000000",
        ["StandardButtonBorderBrush"] = "#B8B8B8",
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
        ["ProgressTrackBrush"] = "#E5E5E5"
    };

    private static readonly IReadOnlyDictionary<string, string> HeritageColors = new Dictionary<string, string>
    {
        ["AppBackgroundBrush"] = "#F7F5F2",
        ["PanelBackgroundBrush"] = "#EFEAE3",
        ["ToolbarBackgroundBrush"] = "#E8E2DA",
        ["ToolbarButtonBackgroundBrush"] = "#B8422E",
        ["ToolbarButtonHoverBackgroundBrush"] = "#9F3525",
        ["ToolbarButtonPressedBackgroundBrush"] = "#82291D",
        ["ToolbarButtonBorderBrush"] = "#B8422E",
        ["ToolbarButtonForegroundBrush"] = "#FFFFFF",
        ["HexHeaderButtonBackgroundBrush"] = "#B8422E",
        ["HexHeaderButtonForegroundBrush"] = "#FFFFFF",
        ["HexHeaderButtonBorderBrush"] = "#B8422E",
        ["StandardButtonBackgroundBrush"] = "#B8422E",
        ["StandardButtonHoverBackgroundBrush"] = "#9F3525",
        ["StandardButtonPressedBackgroundBrush"] = "#82291D",
        ["StandardButtonForegroundBrush"] = "#FFFFFF",
        ["StandardButtonBorderBrush"] = "#B8422E",
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
        ["ProgressTrackBrush"] = "#E8E2DA"
    };

    private static readonly IReadOnlyDictionary<string, string> VioletColors = new Dictionary<string, string>
    {
        ["AppBackgroundBrush"] = "#F4F1FA",
        ["PanelBackgroundBrush"] = "#EAE4F5",
        ["ToolbarBackgroundBrush"] = "#DFD6F0",
        ["ToolbarButtonBackgroundBrush"] = "#6D4AFF",
        ["ToolbarButtonHoverBackgroundBrush"] = "#5B3BE0",
        ["ToolbarButtonPressedBackgroundBrush"] = "#492DC1",
        ["ToolbarButtonBorderBrush"] = "#6D4AFF",
        ["ToolbarButtonForegroundBrush"] = "#FFFFFF",
        ["HexHeaderButtonBackgroundBrush"] = "#6D4AFF",
        ["HexHeaderButtonForegroundBrush"] = "#FFFFFF",
        ["HexHeaderButtonBorderBrush"] = "#6D4AFF",
        ["StandardButtonBackgroundBrush"] = "#6D4AFF",
        ["StandardButtonHoverBackgroundBrush"] = "#5B3BE0",
        ["StandardButtonPressedBackgroundBrush"] = "#492DC1",
        ["StandardButtonForegroundBrush"] = "#FFFFFF",
        ["StandardButtonBorderBrush"] = "#6D4AFF",
        ["SurfaceBackgroundBrush"] = "#FFFFFF",
        ["SubtleBackgroundBrush"] = "#F1ECF8",
        ["InputBackgroundBrush"] = "#FFFFFF",
        ["ReadOnlyBackgroundBrush"] = "#F7F4FC",
        ["HoverBackgroundBrush"] = "#E8DDF8",
        ["PressedBackgroundBrush"] = "#D8C7F0",
        ["AccentBrush"] = "#6D4AFF",
        ["AccentSoftBrush"] = "#E8DDF8",
        ["AlternateRowBackgroundBrush"] = "#FBF9FE",
        ["TextBrush"] = "#191326",
        ["MutedTextBrush"] = "#655A78",
        ["BorderBrush"] = "#B8ADD0",
        ["GridLineBrush"] = "#E2DBEF",
        ["LightGridLineBrush"] = "#EEE9F6",
        ["AddressBackgroundBrush"] = "#4B3D66",
        ["AddressForegroundBrush"] = "#FFFFFF",
        ["SplitterBrush"] = "#D7CFE7",
        ["SelectionBackgroundBrush"] = "#E8DDF8",
        ["SelectionForegroundBrush"] = "#191326",
        ["ProgressTrackBrush"] = "#DED6EC"
    };

    private static readonly IReadOnlyDictionary<string, string> MatchaColors = new Dictionary<string, string>
    {
        ["AppBackgroundBrush"] = "#F3F6EF",
        ["PanelBackgroundBrush"] = "#E5ECDD",
        ["ToolbarBackgroundBrush"] = "#DCE6D2",
        ["ToolbarButtonBackgroundBrush"] = "#4D7C36",
        ["ToolbarButtonHoverBackgroundBrush"] = "#3F6B2B",
        ["ToolbarButtonPressedBackgroundBrush"] = "#335820",
        ["ToolbarButtonBorderBrush"] = "#4D7C36",
        ["ToolbarButtonForegroundBrush"] = "#FFFFFF",
        ["HexHeaderButtonBackgroundBrush"] = "#4D7C36",
        ["HexHeaderButtonForegroundBrush"] = "#FFFFFF",
        ["HexHeaderButtonBorderBrush"] = "#4D7C36",
        ["StandardButtonBackgroundBrush"] = "#4D7C36",
        ["StandardButtonHoverBackgroundBrush"] = "#3F6B2B",
        ["StandardButtonPressedBackgroundBrush"] = "#335820",
        ["StandardButtonForegroundBrush"] = "#FFFFFF",
        ["StandardButtonBorderBrush"] = "#4D7C36",
        ["SurfaceBackgroundBrush"] = "#FFFFFF",
        ["SubtleBackgroundBrush"] = "#EEF3E8",
        ["InputBackgroundBrush"] = "#FFFFFF",
        ["ReadOnlyBackgroundBrush"] = "#F7FAF2",
        ["HoverBackgroundBrush"] = "#DDEBCE",
        ["PressedBackgroundBrush"] = "#C9DDB7",
        ["AccentBrush"] = "#4D7C36",
        ["AccentSoftBrush"] = "#DDEBCE",
        ["AlternateRowBackgroundBrush"] = "#FAFCF7",
        ["TextBrush"] = "#182016",
        ["MutedTextBrush"] = "#5F6D58",
        ["BorderBrush"] = "#AEBBA4",
        ["GridLineBrush"] = "#DDE6D5",
        ["LightGridLineBrush"] = "#ECF1E7",
        ["AddressBackgroundBrush"] = "#5F6D58",
        ["AddressForegroundBrush"] = "#FFFFFF",
        ["SplitterBrush"] = "#D0DBC7",
        ["SelectionBackgroundBrush"] = "#DDEBCE",
        ["SelectionForegroundBrush"] = "#182016",
        ["ProgressTrackBrush"] = "#DCE6D2"
    };

    private static readonly IReadOnlyDictionary<string, string> ArcadeColors = new Dictionary<string, string>
    {
        ["AppBackgroundBrush"] = "#FFF0F6",
        ["PanelBackgroundBrush"] = "#F7E4F1",
        ["ToolbarBackgroundBrush"] = "#F1D8EA",
        ["ToolbarButtonBackgroundBrush"] = "#FF3DA5",
        ["ToolbarButtonHoverBackgroundBrush"] = "#E62F92",
        ["ToolbarButtonPressedBackgroundBrush"] = "#C9247E",
        ["ToolbarButtonBorderBrush"] = "#FF3DA5",
        ["ToolbarButtonForegroundBrush"] = "#FFFFFF",
        ["HexHeaderButtonBackgroundBrush"] = "#FF3DA5",
        ["HexHeaderButtonForegroundBrush"] = "#FFFFFF",
        ["HexHeaderButtonBorderBrush"] = "#FF3DA5",
        ["StandardButtonBackgroundBrush"] = "#FF3DA5",
        ["StandardButtonHoverBackgroundBrush"] = "#E62F92",
        ["StandardButtonPressedBackgroundBrush"] = "#C9247E",
        ["StandardButtonForegroundBrush"] = "#FFFFFF",
        ["StandardButtonBorderBrush"] = "#FF3DA5",
        ["SurfaceBackgroundBrush"] = "#FFFFFF",
        ["SubtleBackgroundBrush"] = "#FFF6FA",
        ["InputBackgroundBrush"] = "#FFFFFF",
        ["ReadOnlyBackgroundBrush"] = "#FFF8FB",
        ["HoverBackgroundBrush"] = "#FFD8EC",
        ["PressedBackgroundBrush"] = "#FFC1E0",
        ["AccentBrush"] = "#FF3DA5",
        ["AccentSoftBrush"] = "#FFD8EC",
        ["AlternateRowBackgroundBrush"] = "#FFFAFD",
        ["TextBrush"] = "#1A0B3C",
        ["MutedTextBrush"] = "#8A7CA8",
        ["BorderBrush"] = "#D4B9D7",
        ["GridLineBrush"] = "#EEDCEC",
        ["LightGridLineBrush"] = "#F7EAF5",
        ["AddressBackgroundBrush"] = "#1A0B3C",
        ["AddressForegroundBrush"] = "#FFFFFF",
        ["SplitterBrush"] = "#E4CDE3",
        ["SelectionBackgroundBrush"] = "#FFD8EC",
        ["SelectionForegroundBrush"] = "#1A0B3C",
        ["ProgressTrackBrush"] = "#F1D8EA"
    };

    public static void Apply(string? themeName)
    {
        var colors = themeName switch
        {
            var name when string.Equals(name, HeritageThemeName, StringComparison.OrdinalIgnoreCase) => HeritageColors,
            var name when string.Equals(name, "Crypto Violet", StringComparison.OrdinalIgnoreCase) => VioletColors,
            var name when string.Equals(name, VioletThemeName, StringComparison.OrdinalIgnoreCase) => VioletColors,
            var name when string.Equals(name, MatchaThemeName, StringComparison.OrdinalIgnoreCase) => MatchaColors,
            var name when string.Equals(name, ArcadeThemeName, StringComparison.OrdinalIgnoreCase) => ArcadeColors,
            _ => DefaultColors
        };

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
