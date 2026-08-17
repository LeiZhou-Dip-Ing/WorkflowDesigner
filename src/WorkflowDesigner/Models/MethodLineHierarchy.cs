namespace WorkflowCore.WpfDemo.Models;

public static class MethodLineHierarchy
{
    public static void Apply(IReadOnlyList<MethodLineViewItem> items)
    {
        var containers = new Stack<Container>();

        foreach (var item in items)
        {
            var sourceLevel = Math.Max(0, item.Line.NestingLevel);
            var replacedContainer = false;

            while (containers.TryPeek(out var container)
                   && container.SourceLevel >= sourceLevel)
            {
                replacedContainer |= container.SourceLevel == sourceLevel;
                containers.Pop();
            }

            item.DisplayNestingLevel = containers.Count;

            var blockRole = item.Descriptor?.BlockRole;
            if (string.Equals(blockRole, "begin", StringComparison.OrdinalIgnoreCase)
                || (string.Equals(blockRole, "branch", StringComparison.OrdinalIgnoreCase)
                    && replacedContainer))
            {
                containers.Push(new Container(sourceLevel));
            }
        }
    }

    private readonly record struct Container(int SourceLevel);
}
