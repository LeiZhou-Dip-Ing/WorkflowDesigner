using WorkflowCore.WpfDemo.Editor;
using System.Windows.Media;

namespace WorkflowCore.WpfDemo.Models.Comparison;

public enum ComparisonScope { Project, Method, Script }

public enum ComparisonChangeKind { Same, Added, Removed, Modified, Moved }

public sealed record ComparisonPropertyDifference(string Label, string LocalValue, string RuntimeValue);

public sealed class ComparisonActionRow
{
    public required Guid Uid { get; init; }
    public required ComparisonChangeKind ChangeKind { get; init; }
    public required int? LocalSequence { get; init; }
    public required int? RuntimeSequence { get; init; }
    public required string LocalName { get; init; }
    public required string RuntimeName { get; init; }
    public required string LocalDescription { get; init; }
    public required string RuntimeDescription { get; init; }
    public required ImageSource? LocalIconImage { get; init; }
    public required ImageSource? RuntimeIconImage { get; init; }
    public required string LocalMetadataSummary { get; init; }
    public required string RuntimeMetadataSummary { get; init; }
    public required IReadOnlyList<ComparisonPropertyDifference> PropertyChanges { get; init; }
    public string Change => ChangeKind.ToString();
    public bool HasLocalIcon => LocalIconImage != null;
    public bool HasRuntimeIcon => RuntimeIconImage != null;
    public string LocalSequenceText => LocalSequence.HasValue ? (LocalSequence.Value + 1).ToString() : string.Empty;
    public string RuntimeSequenceText => RuntimeSequence.HasValue ? (RuntimeSequence.Value + 1).ToString() : string.Empty;
    public string SearchText => string.Join(' ', new[] { LocalName, RuntimeName, LocalDescription, RuntimeDescription }
        .Concat(PropertyChanges.SelectMany(item => new[] { item.Label, item.LocalValue, item.RuntimeValue })));
}

public sealed class ComparisonValueRow
{
    public required Guid Uid { get; init; }
    public required ComparisonChangeKind ChangeKind { get; init; }
    public required string Name { get; init; }
    public required string LocalType { get; init; }
    public required string RuntimeType { get; init; }
    public required string LocalValue { get; init; }
    public required string RuntimeValue { get; init; }
    public required IReadOnlyList<ComparisonPropertyDifference> PropertyChanges { get; init; }
    public string Change => ChangeKind.ToString();
    public string SearchText => string.Join(' ', new[] { Name, LocalType, RuntimeType, LocalValue, RuntimeValue }
        .Concat(PropertyChanges.SelectMany(item => new[] { item.Label, item.LocalValue, item.RuntimeValue })));
}

public sealed class ComparisonMethod
{
    public required Guid Uid { get; init; }
    public required string Name { get; init; }
    public required ComparisonChangeKind ChangeKind { get; init; }
    public required IReadOnlyList<ComparisonActionRow> Actions { get; init; }
    public required IReadOnlyList<ComparisonValueRow> Variables { get; init; }
    public required IReadOnlyList<ComparisonValueRow> Inputs { get; init; }
    public required IReadOnlyList<ComparisonValueRow> Outputs { get; init; }
    public required IReadOnlyList<ComparisonPropertyDifference> PropertyChanges { get; init; }
    public int DifferenceCount => Actions.Count(IsDifferent)
                                  + Variables.Count(IsDifferent)
                                  + Inputs.Count(IsDifferent)
                                  + Outputs.Count(IsDifferent)
                                  + PropertyChanges.Count;
    public string DisplayName => $"{Name} ({DifferenceCount})";
    public string DifferenceLabel => DifferenceCount == 1 ? "1 difference" : $"{DifferenceCount} differences";
    private static bool IsDifferent(ComparisonActionRow row) => row.ChangeKind != ComparisonChangeKind.Same;
    private static bool IsDifferent(ComparisonValueRow row) => row.ChangeKind != ComparisonChangeKind.Same;
}

public sealed class ComparisonScript
{
    public required Guid Uid { get; init; }
    public required string Name { get; init; }
    public required ComparisonChangeKind ChangeKind { get; init; }
    public required string LocalText { get; init; }
    public required string RuntimeText { get; init; }
    public required int AddedLines { get; init; }
    public required int RemovedLines { get; init; }
    public required int ModifiedLines { get; init; }
    public int DifferenceCount => AddedLines + RemovedLines + ModifiedLines;
    public string DisplayName => $"{Name} ({DifferenceCount})";
    public string DifferenceLabel => DifferenceCount == 1 ? "1 difference" : $"{DifferenceCount} differences";
}

public sealed class DeploymentComparisonModel
{
    public required ComparisonScope Scope { get; init; }
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public required long RuntimeRevision { get; init; }
    public required bool HasUnsavedLocalChanges { get; init; }
    public required IReadOnlyList<ComparisonMethod> Methods { get; init; }
    public required IReadOnlyList<ComparisonScript> Scripts { get; init; }
    public required IReadOnlyList<WorkflowDifferenceItem> RawDifferences { get; init; }
    public int AddedCount => Methods.SelectMany(item => item.Actions).Count(item => item.ChangeKind == ComparisonChangeKind.Added)
                             + Methods.SelectMany(AllValues).Count(item => item.ChangeKind == ComparisonChangeKind.Added)
                             + Scripts.Count(item => item.ChangeKind == ComparisonChangeKind.Added);
    public int RemovedCount => Methods.SelectMany(item => item.Actions).Count(item => item.ChangeKind == ComparisonChangeKind.Removed)
                               + Methods.SelectMany(AllValues).Count(item => item.ChangeKind == ComparisonChangeKind.Removed)
                               + Scripts.Count(item => item.ChangeKind == ComparisonChangeKind.Removed);
    public int ModifiedCount => Methods.SelectMany(item => item.Actions).Count(item => item.ChangeKind is ComparisonChangeKind.Modified or ComparisonChangeKind.Moved)
                                + Methods.SelectMany(AllValues).Count(item => item.ChangeKind is ComparisonChangeKind.Modified or ComparisonChangeKind.Moved)
                                + Scripts.Count(item => item.ChangeKind is ComparisonChangeKind.Modified or ComparisonChangeKind.Moved)
                                + Methods.Sum(item => item.PropertyChanges.Count);
    public int DifferenceCount => AddedCount + RemovedCount + ModifiedCount;
    private static IEnumerable<ComparisonValueRow> AllValues(ComparisonMethod method)
        => method.Variables.Concat(method.Inputs).Concat(method.Outputs);
}
