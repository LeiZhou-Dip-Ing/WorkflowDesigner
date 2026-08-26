namespace WorkflowCore.WpfDemo.Theming;

public static class WorkflowThemeContext
{
    public static event EventHandler? Changed;

    public static string CurrentTheme { get; private set; } = "OutlookLight";

    public static bool IsDark =>
        string.Equals(CurrentTheme, "Dark", StringComparison.OrdinalIgnoreCase);

    public static void Apply(string? themeName)
    {
        string nextTheme = string.IsNullOrWhiteSpace(themeName) ? "OutlookLight" : themeName;
        if (string.Equals(CurrentTheme, nextTheme, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        CurrentTheme = nextTheme;
        Changed?.Invoke(null, EventArgs.Empty);
    }
}
