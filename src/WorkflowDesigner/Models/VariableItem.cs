namespace WorkflowCore.WpfDemo.Models;

public sealed class VariableItem
{
    public Guid RunId { get; set; }

    public Guid? LineUid { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    public string Time => Timestamp == default
        ? string.Empty
        : Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");

    public int? LineNumber { get; set; }

    public string Step => LineNumber.HasValue ? $"Step {LineNumber.Value}" : string.Empty;

    public ActionTemplateItem? ActionTemplate { get; set; }

    public string ActionName { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public string Scope { get; set; } = string.Empty;

    public string Type { get; set; } = "object";

    public string Status { get; set; } = "Changed";
}
