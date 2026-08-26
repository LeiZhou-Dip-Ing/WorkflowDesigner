using System.Windows;
using System.Windows.Media;
using WorkflowCore.WpfDemo.Theming;

namespace WorkflowCore.WpfDemo.Services.Ui;

public interface IWorkflowThemeService
{
    string CurrentTheme { get; }
    IReadOnlyList<string> AvailableThemes { get; }
    void ApplyTheme(string themeName);
}

public sealed class WorkflowThemeService : IWorkflowThemeService
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Palettes =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["OutlookLight"] = CreateLightPalette(),
            ["CompactLine"] = CreateLightPalette(),
            ["Dark"] = CreateDarkPalette()
        };

    public string CurrentTheme { get; private set; } = "OutlookLight";

    public IReadOnlyList<string> AvailableThemes { get; } = ["OutlookLight", "CompactLine", "Dark"];

    public void ApplyTheme(string themeName)
    {
        if (!Palettes.TryGetValue(themeName, out var palette))
        {
            return;
        }

        var resources = Application.Current?.Resources;
        if (resources == null)
        {
            return;
        }

        foreach (var (key, color) in palette)
        {
            ReplaceResource(resources, key, new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)));
        }

        CurrentTheme = AvailableThemes.First(value =>
            string.Equals(value, themeName, StringComparison.OrdinalIgnoreCase));
        WorkflowThemeContext.Apply(CurrentTheme);
    }

    private static bool ReplaceResource(ResourceDictionary resources, string key, object value)
    {
        if (resources.Contains(key))
        {
            if (resources[key] is SolidColorBrush existingBrush
                && value is SolidColorBrush nextBrush
                && !existingBrush.IsFrozen)
            {
                existingBrush.Color = nextBrush.Color;
            }
            else
            {
                resources[key] = value;
            }

            return true;
        }

        for (var index = resources.MergedDictionaries.Count - 1; index >= 0; index--)
        {
            if (ReplaceResource(resources.MergedDictionaries[index], key, value))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyDictionary<string, string> CreateLightPalette() => new Dictionary<string, string>
    {
        ["AppPrimaryBrush"] = "#FF0F6CBD", ["AppAccentBrush"] = "#FF0F6CBD",
        ["AppPageBrush"] = "#FFF4F7FB", ["AppPanelBrush"] = "#FFFFFFFF",
        ["AppCardBrush"] = "#FFFFFFFF", ["AppBorderBrush"] = "#FFD6E0EA",
        ["AppTextBrush"] = "#FF172033", ["AppMutedTextBrush"] = "#FF5B6B80",
        ["AppSubtleTextBrush"] = "#FF7A8798", ["AppSelectedBrush"] = "#FFEAF3FF",
        ["AppInputBackgroundBrush"] = "#FFFFFFFF", ["AppInputBorderBrush"] = "#FFB9C9DA",
        ["AppDataGridHeaderBrush"] = "#FFF5F8FC", ["AppDataGridRowBrush"] = "#FFFFFFFF",
        ["AppDataGridAlternateRowBrush"] = "#FFFBFCFE", ["AppDataGridSelectedRowBrush"] = "#FFEAF3FF",
        ["AppGridLineBrush"] = "#FFDFE7F0", ["AppHeaderBorderBrush"] = "#FFD6E0EA",
        ["AppButtonHoverBrush"] = "#FFF2F7FD", ["AppButtonPressedBrush"] = "#FFEAF3FF",
        ["AppFocusBorderBrush"] = "#FF0F6CBD", ["AppWarningBackgroundBrush"] = "#FFFFF6E6",
        ["AppDangerBackgroundBrush"] = "#FFFDECEE",
        ["AppHoverBrush"] = "#FFF2F7FD", ["AppDisabledBrush"] = "#FFF2F5F8",
        ["AppDisabledTextBrush"] = "#FF94A3B5", ["AppSidebarBrush"] = "#FFF8FAFC",
        ["AppHeaderBrush"] = "#FFFFFFFF", ["AppStatusBarBrush"] = "#FFF8FAFC",
        ["ChromeBrush"] = "#FFF8FAFC", ["PanelBrush"] = "#FFF8FAFC",
        ["AppDockBackgroundBrush"] = "#FFF4F7FB", ["AppDockPanelBrush"] = "#FFF8FAFC",
        ["AppDockHoverBrush"] = "#FFEAF3FC", ["AppDockActiveBrush"] = "#FFFFFFFF",
        ["AppDockBorderBrush"] = "#FFD5DEE8", ["AppDockTextBrush"] = "#FF1F2937",
        ["AppDockMutedTextBrush"] = "#FF64748B", ["AppDockAccentBrush"] = "#FF1677C8",
        ["SurfaceBrush"] = "#FFFFFFFF", ["SurfaceAltBrush"] = "#FFEFF4F8",
        ["BorderBrushSoft"] = "#FFD5DEE8", ["AccentBrush"] = "#FF1677C8",
        ["MutedTextBrush"] = "#FF64748B", ["SelectionBrush"] = "#FFDCECF9",
        ["SelectionBorderBrush"] = "#FF9FC5E6",
        ["WorkflowSdkPageBrush"] = "#FFF4F7FB", ["WorkflowSdkSurfaceBrush"] = "#FFFFFFFF",
        ["WorkflowSdkPanelBrush"] = "#FFFFFFFF", ["WorkflowSdkBorderBrush"] = "#FFD6E0EA",
        ["WorkflowSdkTextBrush"] = "#FF172033", ["WorkflowSdkMutedTextBrush"] = "#FF5B6B80",
        ["WorkflowSdkAccentBrush"] = "#FF0F6CBD", ["WorkflowSdkSuccessBrush"] = "#FF16A34A",
        ["WorkflowSdkWarningBrush"] = "#FFF59E0B", ["WorkflowSdkDangerBrush"] = "#FFDC2626",
        ["WorkflowSdkSelectedBrush"] = "#FFEAF3FF"
    };

    private static IReadOnlyDictionary<string, string> CreateDarkPalette() => new Dictionary<string, string>
    {
        ["AppPrimaryBrush"] = "#FF55A4FF", ["AppAccentBrush"] = "#FF55A4FF",
        ["AppPageBrush"] = "#FF1E1F22", ["AppPanelBrush"] = "#FF25272B",
        ["AppCardBrush"] = "#FF2B2D31", ["AppBorderBrush"] = "#FF454950",
        ["AppTextBrush"] = "#FFF2F4F6", ["AppMutedTextBrush"] = "#FFB3BAC4",
        ["AppSubtleTextBrush"] = "#FF929AA5", ["AppSelectedBrush"] = "#FF173A5E",
        ["AppInputBackgroundBrush"] = "#FF2B2D31", ["AppInputBorderBrush"] = "#FF555A63",
        ["AppDataGridHeaderBrush"] = "#FF303238", ["AppDataGridRowBrush"] = "#FF25272B",
        ["AppDataGridAlternateRowBrush"] = "#FF292B30", ["AppDataGridSelectedRowBrush"] = "#FF173A5E",
        ["AppGridLineBrush"] = "#FF454950", ["AppHeaderBorderBrush"] = "#FF454950",
        ["AppButtonHoverBrush"] = "#FF31343A", ["AppButtonPressedBrush"] = "#FF173A5E",
        ["AppFocusBorderBrush"] = "#FF55A4FF", ["AppWarningBackgroundBrush"] = "#FF4A3818",
        ["AppDangerBackgroundBrush"] = "#FF4C2528",
        ["AppHoverBrush"] = "#FF31343A", ["AppDisabledBrush"] = "#FF303238",
        ["AppDisabledTextBrush"] = "#FF777F89", ["AppSidebarBrush"] = "#FF25272B",
        ["AppHeaderBrush"] = "#FF25272B", ["AppStatusBarBrush"] = "#FF202226",
        ["ChromeBrush"] = "#FF202226", ["PanelBrush"] = "#FF25272B",
        ["AppDockBackgroundBrush"] = "#FF1E1F22", ["AppDockPanelBrush"] = "#FF25272B",
        ["AppDockHoverBrush"] = "#FF31343A", ["AppDockActiveBrush"] = "#FF2B2D31",
        ["AppDockBorderBrush"] = "#FF454950", ["AppDockTextBrush"] = "#FFF2F4F6",
        ["AppDockMutedTextBrush"] = "#FFB3BAC4", ["AppDockAccentBrush"] = "#FF55A4FF",
        ["SurfaceBrush"] = "#FF25272B", ["SurfaceAltBrush"] = "#FF2B2D31",
        ["BorderBrushSoft"] = "#FF454950", ["AccentBrush"] = "#FF55A4FF",
        ["MutedTextBrush"] = "#FFB3BAC4", ["SelectionBrush"] = "#FF173A5E",
        ["SelectionBorderBrush"] = "#FF3978B5",
        ["WorkflowSdkPageBrush"] = "#FF1E1F22", ["WorkflowSdkSurfaceBrush"] = "#FF2B2D31",
        ["WorkflowSdkPanelBrush"] = "#FF25272B", ["WorkflowSdkBorderBrush"] = "#FF454950",
        ["WorkflowSdkTextBrush"] = "#FFF2F4F6", ["WorkflowSdkMutedTextBrush"] = "#FFB3BAC4",
        ["WorkflowSdkAccentBrush"] = "#FF55A4FF", ["WorkflowSdkSuccessBrush"] = "#FF79C45A",
        ["WorkflowSdkWarningBrush"] = "#FFF4B942", ["WorkflowSdkDangerBrush"] = "#FFFF6B6B",
        ["WorkflowSdkSelectedBrush"] = "#FF173A5E"
    };
}
