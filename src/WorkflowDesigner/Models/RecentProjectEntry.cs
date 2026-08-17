namespace WorkflowCore.WpfDemo.Models;

/// <summary>Identifies one complete workflow project previously opened by the user.</summary>
public sealed record RecentProjectEntry(
    string FullPath,
    string DisplayName,
    DateTimeOffset LastOpenedAt);

public sealed record RecentProjectGroup(
    string Name,
    IReadOnlyList<RecentProjectEntry> Projects);
