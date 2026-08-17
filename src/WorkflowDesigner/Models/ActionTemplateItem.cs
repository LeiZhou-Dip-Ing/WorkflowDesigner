namespace WorkflowCore.WpfDemo.Models;

using System.Collections.ObjectModel;
using System.Windows.Media;

public sealed class ActionTemplateItem
{
    public string DisplayName { get; init; } = string.Empty;

    public string? ActionType { get; init; }

    public string? ActionId { get; init; }

    public string Description { get; init; } = string.Empty;

    public string? IconUri { get; init; }

    public string? IconContentType { get; init; }

    public ImageSource? IconImage { get; init; }

    public bool HasIcon => IconImage != null;

    public bool IsSvgIcon => string.Equals(
        IconContentType,
        "image/svg+xml",
        StringComparison.OrdinalIgnoreCase);

    public bool IsRasterIcon => IconContentType?.StartsWith(
        "image/",
        StringComparison.OrdinalIgnoreCase) == true && !IsSvgIcon;

    public ObservableCollection<ActionTemplateItem> Children { get; init; } = new();

    public bool IsCategory => string.IsNullOrWhiteSpace(ActionType);
}
