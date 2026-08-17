using System.Text.Json.Nodes;
using WorkflowCore.WpfDemo.Models;

namespace WorkflowCore.WpfDemo.Services;

public static class WorkflowJsonComparer
{
    private const int MaximumDifferences = 2_000;

    public static IReadOnlyList<WorkflowDifferenceItem> Compare(JsonNode? local, JsonNode? runtime)
    {
        var differences = new List<WorkflowDifferenceItem>();
        CompareNode(local, runtime, "$", differences);
        return differences;
    }

    public static bool AreEquivalent(JsonNode? local, JsonNode? runtime)
        => JsonNode.DeepEquals(local, runtime);

    private static void CompareNode(
        JsonNode? local,
        JsonNode? runtime,
        string path,
        List<WorkflowDifferenceItem> differences)
    {
        if (differences.Count >= MaximumDifferences || JsonNode.DeepEquals(local, runtime))
        {
            return;
        }

        if (local == null || runtime == null)
        {
            differences.Add(new WorkflowDifferenceItem
            {
                Kind = local == null ? WorkflowDifferenceKind.RuntimeOnly : WorkflowDifferenceKind.LocalOnly,
                Path = path,
                LocalValue = FormatValue(local),
                RuntimeValue = FormatValue(runtime)
            });
            return;
        }

        if (local is JsonObject localObject && runtime is JsonObject runtimeObject)
        {
            foreach (var propertyName in localObject.Select(item => item.Key)
                         .Concat(runtimeObject.Select(item => item.Key))
                         .Distinct(StringComparer.Ordinal))
            {
                localObject.TryGetPropertyValue(propertyName, out var localValue);
                runtimeObject.TryGetPropertyValue(propertyName, out var runtimeValue);
                CompareNode(localValue, runtimeValue, $"{path}.{propertyName}", differences);
            }

            return;
        }

        if (local is JsonArray localArray && runtime is JsonArray runtimeArray)
        {
            if (TryCreateUidMap(localArray, out var localByUid)
                && TryCreateUidMap(runtimeArray, out var runtimeByUid))
            {
                foreach (var uid in localByUid.Keys.Concat(runtimeByUid.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    localByUid.TryGetValue(uid, out var localValue);
                    runtimeByUid.TryGetValue(uid, out var runtimeValue);
                    var displayName = GetDisplayName(localValue) ?? GetDisplayName(runtimeValue) ?? uid;
                    CompareNode(localValue, runtimeValue, $"{path}['{EscapePath(displayName)}']", differences);
                }
            }
            else
            {
                for (var index = 0; index < Math.Max(localArray.Count, runtimeArray.Count); index++)
                {
                    CompareNode(
                        index < localArray.Count ? localArray[index] : null,
                        index < runtimeArray.Count ? runtimeArray[index] : null,
                        $"{path}[{index}]",
                        differences);
                }
            }

            return;
        }

        differences.Add(new WorkflowDifferenceItem
        {
            Kind = WorkflowDifferenceKind.Modified,
            Path = path,
            LocalValue = FormatValue(local),
            RuntimeValue = FormatValue(runtime)
        });
    }

    private static bool TryCreateUidMap(JsonArray array, out Dictionary<string, JsonNode?> values)
    {
        values = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        if (array.Count == 0)
        {
            return false;
        }

        foreach (var node in array)
        {
            if (node is not JsonObject item
                || item["uid"]?.GetValue<string>() is not { Length: > 0 } uid
                || !values.TryAdd(uid, node))
            {
                values.Clear();
                return false;
            }
        }

        return true;
    }

    private static string? GetDisplayName(JsonNode? node)
        => node is JsonObject item ? item["name"]?.GetValue<string>() : null;

    private static string EscapePath(string value) => value.Replace("'", "\\'", StringComparison.Ordinal);

    private static string FormatValue(JsonNode? value)
    {
        if (value == null)
        {
            return "—";
        }

        var text = value.ToJsonString();
        return text.Length <= 500 ? text : text[..497] + "...";
    }
}
