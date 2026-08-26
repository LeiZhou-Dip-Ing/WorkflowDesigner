using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Models;
using WorkflowCore.WpfDemo.Models.Comparison;
using WorkflowCore.WpfDemo.Services.Runtime;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.Services.Comparison;

/// <summary>Builds the shared, typed Local/Runtime result for Project, Method and Script viewers.</summary>
public sealed class WorkflowComparisonBuilder
{
    private readonly IEditorActionCatalog _catalog;

    public WorkflowComparisonBuilder(IEditorActionCatalog catalog)
        => _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

    public DeploymentComparisonModel Build(WorkflowComparisonResult source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var methods = new List<ComparisonMethod>();
        var scripts = new List<ComparisonScript>();
        var scope = ComparisonScope.Project;
        var title = "Local / Runtime comparison";

        if (source.LocalDocument?.Kind == WorkflowEditorDocumentKind.Method)
        {
            scope = ComparisonScope.Method;
            title = $"Method comparison: {source.LocalDocument.Name}";
            methods.Add(BuildMethod(source.LocalDocument.Method, source.RuntimeEditorDocument?.Method));
        }
        else if (source.LocalDocument?.Kind == WorkflowEditorDocumentKind.CSharpScript)
        {
            scope = ComparisonScope.Script;
            title = $"Script comparison: {source.LocalDocument.Name}";
            scripts.Add(BuildScript(source.LocalDocument.Script, source.RuntimeEditorDocument?.Script));
        }
        else
        {
            methods.AddRange(AlignByUid(source.LocalProject?.Methods, source.RuntimeProject?.Methods, item => item.Uid)
                .Select(pair => BuildMethod(pair.Local, pair.Runtime)));
            scripts.AddRange(AlignByUid(source.LocalProject?.Scripts, source.RuntimeProject?.Scripts, item => item.Uid)
                .Select(pair => BuildScript(pair.Local, pair.Runtime)));
        }

        return new DeploymentComparisonModel
        {
            Scope = scope,
            Title = title,
            Summary = source.Summary,
            RuntimeRevision = source.RuntimeDocument.Revision,
            HasUnsavedLocalChanges = source.HasUnsavedLocalChanges,
            Methods = methods,
            Scripts = scripts,
            RawDifferences = source.Differences
        };
    }

    private ComparisonMethod BuildMethod(WorkflowMethod? local, WorkflowMethod? runtime)
    {
        var actions = AlignByUid(local?.MethodLines, runtime?.MethodLines, item => item.Uid)
            .Select(pair => BuildAction(pair.Local, pair.Runtime)).ToArray();
        var variables = AlignByUid(local?.MethodVariables, runtime?.MethodVariables, item => item.Uid)
            .Select(pair => BuildVariable(pair.Local, pair.Runtime)).ToArray();
        var inputs = AlignByUid(local?.Inputs, runtime?.Inputs, item => item.Uid)
            .Select(pair => BuildParameter(pair.Local, pair.Runtime)).ToArray();
        var outputs = AlignByUid(local?.Outputs, runtime?.Outputs, item => item.Uid)
            .Select(pair => BuildParameter(pair.Local, pair.Runtime)).ToArray();
        var properties = CompareProperties(MethodProperties(local), MethodProperties(runtime));
        var changed = actions.Any(IsDifferent) || variables.Any(IsDifferent) || inputs.Any(IsDifferent)
                      || outputs.Any(IsDifferent) || properties.Count > 0;
        return new ComparisonMethod
        {
            Uid = local?.Uid ?? runtime?.Uid ?? Guid.Empty,
            Name = local?.Name ?? runtime?.Name ?? "Method",
            ChangeKind = GetChange(local, runtime, changed),
            Actions = actions,
            Variables = variables,
            Inputs = inputs,
            Outputs = outputs,
            PropertyChanges = properties
        };
    }

    private ComparisonActionRow BuildAction(MethodLine? local, MethodLine? runtime)
    {
        var localDescriptor = local?.Action == null ? null : FindDescriptor(local.Action);
        var runtimeDescriptor = runtime?.Action == null ? null : FindDescriptor(runtime.Action);
        var changes = CompareProperties(
            ActionProperties(local, localDescriptor),
            ActionProperties(runtime, runtimeDescriptor));
        var movedOnly = local != null && runtime != null && local.SequenceNo != runtime.SequenceNo
                        && changes.All(item => item.Label == "Sequence");
        return new ComparisonActionRow
        {
            Uid = local?.Uid ?? runtime?.Uid ?? Guid.Empty,
            ChangeKind = local == null ? ComparisonChangeKind.Removed
                : runtime == null ? ComparisonChangeKind.Added
                : changes.Count == 0 ? ComparisonChangeKind.Same
                : movedOnly ? ComparisonChangeKind.Moved
                : ComparisonChangeKind.Modified,
            LocalSequence = local?.SequenceNo,
            RuntimeSequence = runtime?.SequenceNo,
            LocalName = ActionName(local, localDescriptor),
            RuntimeName = ActionName(runtime, runtimeDescriptor),
            LocalDescription = ActionDescription(local, localDescriptor),
            RuntimeDescription = ActionDescription(runtime, runtimeDescriptor),
            LocalIconImage = _catalog.GetCachedIconImage(localDescriptor?.Icon),
            RuntimeIconImage = _catalog.GetCachedIconImage(runtimeDescriptor?.Icon),
            LocalMetadataSummary = ActionMetadataSummary(local, localDescriptor),
            RuntimeMetadataSummary = ActionMetadataSummary(runtime, runtimeDescriptor),
            PropertyChanges = changes
        };
    }

    private static Dictionary<string, string> ActionProperties(
        MethodLine? line,
        WorkflowActionDescriptorDto? descriptor)
    {
        if (line?.Action == null) return EmptyProperties();
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (descriptor != null)
        {
            foreach (var field in descriptor.GetAllFields())
                labels[field.Name] = field.DisplayName;
        }
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Sequence"] = line.SequenceNo.ToString(CultureInfo.InvariantCulture),
            ["Nesting level"] = line.NestingLevel.ToString(CultureInfo.InvariantCulture),
            ["Comment"] = line.Comment ?? string.Empty,
            ["Deactivate"] = line.IsActive ? "No" : "Yes"
        };
        foreach (var property in line.Action.GetEditableProperties())
            result[labels.GetValueOrDefault(property.Key) ?? Humanize(property.Key)] = Format(property.Value);
        foreach (var binding in line.Action.GetOutputBindings())
            result[$"Output · {labels.GetValueOrDefault(binding.Key) ?? Humanize(binding.Key)}"] = binding.Value;
        return result;
    }

    private static Dictionary<string, string> MethodProperties(WorkflowMethod? method) => method == null
        ? EmptyProperties()
        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = method.Name,
            ["Method type"] = method.MethodType.ToString(),
            ["Initialize at start"] = Format(method.InitAtStart),
            ["Initialization method"] = method.InitMethodName ?? string.Empty
        };

    private ComparisonValueRow BuildVariable(WorkflowVariable? local, WorkflowVariable? runtime)
    {
        var changes = CompareProperties(VariableProperties(local), VariableProperties(runtime));
        return new ComparisonValueRow
        {
            Uid = local?.Uid ?? runtime?.Uid ?? Guid.Empty,
            ChangeKind = GetChange(local, runtime, changes.Count > 0),
            Name = local?.Label ?? runtime?.Label ?? string.Empty,
            LocalType = local?.DataType ?? string.Empty,
            RuntimeType = runtime?.DataType ?? string.Empty,
            LocalValue = Format(local?.Value),
            RuntimeValue = Format(runtime?.Value),
            PropertyChanges = changes
        };
    }

    private static Dictionary<string, string> VariableProperties(WorkflowVariable? value) => value == null
        ? EmptyProperties()
        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = value.Label, ["Data type"] = value.DataType, ["Value"] = Format(value.Value),
            ["Default value"] = Format(value.DefaultValue), ["Description"] = value.Description ?? string.Empty,
            ["Active"] = Format(value.IsActive), ["Array"] = Format(value.DataIsArray)
        };

    private ComparisonValueRow BuildParameter(WorkflowMethodParameter? local, WorkflowMethodParameter? runtime)
    {
        var changes = CompareProperties(ParameterProperties(local), ParameterProperties(runtime));
        return new ComparisonValueRow
        {
            Uid = local?.Uid ?? runtime?.Uid ?? Guid.Empty,
            ChangeKind = GetChange(local, runtime, changes.Count > 0),
            Name = local?.DisplayName ?? runtime?.DisplayName ?? local?.Name ?? runtime?.Name ?? string.Empty,
            LocalType = local?.ValueType ?? string.Empty,
            RuntimeType = runtime?.ValueType ?? string.Empty,
            LocalValue = Format(local?.DefaultValue),
            RuntimeValue = Format(runtime?.DefaultValue),
            PropertyChanges = changes
        };
    }

    private static Dictionary<string, string> ParameterProperties(WorkflowMethodParameter? value) => value == null
        ? EmptyProperties()
        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = value.Name, ["Display name"] = value.DisplayName, ["Variable"] = value.VariableName,
            ["Data type"] = value.ValueType, ["Default value"] = Format(value.DefaultValue),
            ["Required"] = Format(value.Required), ["Description"] = value.Description
        };

    private static ComparisonScript BuildScript(WorkflowScript? local, WorkflowScript? runtime)
    {
        var localText = local?.Content ?? string.Empty;
        var runtimeText = runtime?.Content ?? string.Empty;
        var diff = new SideBySideDiffBuilder(new Differ()).BuildDiffModel(localText, runtimeText);
        return new ComparisonScript
        {
            Uid = local?.Uid ?? runtime?.Uid ?? Guid.Empty,
            Name = local?.DisplayFileName ?? runtime?.DisplayFileName ?? "Script.csx",
            ChangeKind = local == null ? ComparisonChangeKind.Removed : runtime == null ? ComparisonChangeKind.Added
                : string.Equals(localText, runtimeText, StringComparison.Ordinal) ? ComparisonChangeKind.Same : ComparisonChangeKind.Modified,
            LocalText = localText,
            RuntimeText = runtimeText,
            AddedLines = diff.NewText.Lines.Count(item => item.Type == ChangeType.Inserted),
            RemovedLines = diff.OldText.Lines.Count(item => item.Type == ChangeType.Deleted),
            ModifiedLines = Math.Max(diff.NewText.Lines.Count(item => item.Type == ChangeType.Modified), diff.OldText.Lines.Count(item => item.Type == ChangeType.Modified))
        };
    }

    private WorkflowActionDescriptorDto? FindDescriptor(WorkflowAction action)
        => _catalog.Current.Actions.FirstOrDefault(item => !string.IsNullOrWhiteSpace(action.ActionId)
                                                           && string.Equals(item.ActionId, action.ActionId, StringComparison.OrdinalIgnoreCase))
           ?? _catalog.Current.Actions.FirstOrDefault(item => string.Equals(item.ActionType, action.ActionType, StringComparison.OrdinalIgnoreCase));

    private static string ActionName(MethodLine? line, WorkflowActionDescriptorDto? descriptor)
    {
        if (line?.Action == null) return string.Empty;
        return descriptor?.DisplayName ?? (!string.IsNullOrWhiteSpace(line.Action.Name) ? line.Action.Name : line.Action.ActionType);
    }

    private static string ActionDescription(MethodLine? line, WorkflowActionDescriptorDto? descriptor)
        => line?.Action == null ? string.Empty : ActionDisplayTextFormatter.Format(descriptor, line.Action);

    private static string ActionMetadataSummary(MethodLine? line, WorkflowActionDescriptorDto? descriptor)
    {
        if (line?.Action == null) return string.Empty;
        if (descriptor == null) return line.Action.ActionType;

        var metadata = new List<string> { descriptor.DisplayName };
        if (!string.IsNullOrWhiteSpace(descriptor.Category)) metadata.Add(descriptor.Category);
        if (!string.IsNullOrWhiteSpace(descriptor.Description)) metadata.Add(descriptor.Description);
        metadata.Add($"Type: {descriptor.ActionType}");
        if (!string.IsNullOrWhiteSpace(descriptor.ActionId)) metadata.Add($"Action ID: {descriptor.ActionId}");
        metadata.Add($"Version: {descriptor.ActionVersion}");
        if (!string.IsNullOrWhiteSpace(descriptor.SourceKind)) metadata.Add($"Source: {descriptor.SourceKind}");
        if (!string.IsNullOrWhiteSpace(descriptor.SourceId)) metadata.Add($"Source ID: {descriptor.SourceId}");
        if (!string.IsNullOrWhiteSpace(descriptor.SourceVersion)) metadata.Add($"Source version: {descriptor.SourceVersion}");
        if (descriptor.SourceRevision.HasValue) metadata.Add($"Source revision: {descriptor.SourceRevision}");
        if (!string.IsNullOrWhiteSpace(descriptor.PluginId))
            metadata.Add(string.IsNullOrWhiteSpace(descriptor.PluginVersion)
                ? $"Plugin: {descriptor.PluginId}"
                : $"Plugin: {descriptor.PluginId} {descriptor.PluginVersion}");
        if (!string.IsNullOrWhiteSpace(descriptor.BlockRole)) metadata.Add($"Block role: {descriptor.BlockRole}");
        if (descriptor.IsDeprecated) metadata.Add("Deprecated");
        return string.Join(Environment.NewLine, metadata);
    }

    private static ComparisonChangeKind GetChange<T>(T? local, T? runtime, bool changed) where T : class
        => local == null ? ComparisonChangeKind.Removed : runtime == null ? ComparisonChangeKind.Added
            : changed ? ComparisonChangeKind.Modified : ComparisonChangeKind.Same;
    private static bool IsDifferent(ComparisonActionRow item) => item.ChangeKind != ComparisonChangeKind.Same;
    private static bool IsDifferent(ComparisonValueRow item) => item.ChangeKind != ComparisonChangeKind.Same;

    private static IReadOnlyList<ComparisonPropertyDifference> CompareProperties(
        IReadOnlyDictionary<string, string> local, IReadOnlyDictionary<string, string> runtime)
        => local.Keys.Concat(runtime.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .Where(key => !string.Equals(local.GetValueOrDefault(key) ?? string.Empty, runtime.GetValueOrDefault(key) ?? string.Empty, StringComparison.Ordinal))
            .Select(key => new ComparisonPropertyDifference(key, local.GetValueOrDefault(key) ?? string.Empty, runtime.GetValueOrDefault(key) ?? string.Empty))
            .ToArray();

    private static IReadOnlyList<(T? Local, T? Runtime)> AlignByUid<T>(
        IEnumerable<T>? localItems, IEnumerable<T>? runtimeItems, Func<T, Guid> uidSelector) where T : class
    {
        var local = AddOccurrenceKeys(localItems ?? Array.Empty<T>(), uidSelector);
        var runtime = AddOccurrenceKeys(runtimeItems ?? Array.Empty<T>(), uidSelector);
        var localByKey = local.ToDictionary(item => item.Key);
        var runtimeByKey = runtime.ToDictionary(item => item.Key);
        return localByKey.Keys.Concat(runtimeByKey.Keys).Distinct().Select(key =>
            {
                var hasLocal = localByKey.TryGetValue(key, out var left);
                var hasRuntime = runtimeByKey.TryGetValue(key, out var right);
                return (Local: hasLocal ? left.Item : null, Runtime: hasRuntime ? right.Item : null,
                    Order: Math.Min(hasLocal ? left.Index : int.MaxValue, hasRuntime ? right.Index : int.MaxValue));
            }).OrderBy(item => item.Order).Select(item => (item.Local, item.Runtime)).ToArray();
    }

    private static IReadOnlyList<(T Item, int Index, (Guid Uid, int Occurrence) Key)> AddOccurrenceKeys<T>(IEnumerable<T> items, Func<T, Guid> uidSelector)
    {
        var occurrences = new Dictionary<Guid, int>();
        return items.Select((item, index) =>
        {
            var uid = uidSelector(item);
            var occurrence = occurrences.GetValueOrDefault(uid);
            occurrences[uid] = occurrence + 1;
            return (item, index, (uid, occurrence));
        }).ToArray();
    }

    private static Dictionary<string, string> EmptyProperties() => new(StringComparer.OrdinalIgnoreCase);

    private static string Format(JsonNode? value)
    {
        if (value == null) return string.Empty;
        if (value is JsonValue scalar)
        {
            if (scalar.TryGetValue<string>(out var text)) return text;
            if (scalar.TryGetValue<bool>(out var boolean)) return boolean ? "Yes" : "No";
            if (scalar.TryGetValue<long>(out var integer)) return integer.ToString(CultureInfo.InvariantCulture);
            if (scalar.TryGetValue<double>(out var number)) return number.ToString("G", CultureInfo.InvariantCulture);
        }
        return value.ToJsonString();
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty, bool boolean => boolean ? "Yes" : "No",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => value.ToString() ?? string.Empty
    };

    private static string Humanize(string value)
    {
        var result = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            if (index > 0 && char.IsUpper(value[index]) && !char.IsUpper(value[index - 1])) result.Append(' ');
            result.Append(index == 0 ? char.ToUpperInvariant(value[index]) : value[index]);
        }
        return result.ToString();
    }
}
