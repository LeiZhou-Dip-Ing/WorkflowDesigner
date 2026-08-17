using System.Text;

namespace WorkflowCore.WpfDemo.Services.Scripting;

public sealed class SharpScriptTemplateFactory : ISharpScriptTemplateFactory
{
    public string Create(string scriptName)
    {
        var className = CreateClassName(scriptName);
        return $$"""
            using WorkflowRuntime.ScriptSdk;

            public sealed class {{className}} : IWorkflowSharpScript
            {
                [ScriptInput("Input", Description = "Input value.", Required = true, Order = 0)]
                public string Input { get; set; } = string.Empty;

                [ScriptOutput("Output", Description = "Processed result.", Order = 0)]
                public string Output { get; private set; } = string.Empty;

                public ValueTask ExecuteAsync(
                    IWorkflowSharpScriptContext context,
                    CancellationToken cancellationToken)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Output = Input;
                    context.Log($"Output: {Output}");
                    return ValueTask.CompletedTask;
                }
            }
            """;
    }

    private static string CreateClassName(string scriptName)
    {
        var name = scriptName.Trim();
        if (name.EndsWith(".csx", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        else if (name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) name = name[..^3];
        var builder = new StringBuilder();
        var capitalize = true;
        foreach (var character in name)
        {
            if (!char.IsLetterOrDigit(character) && character != '_')
            {
                capitalize = true;
                continue;
            }

            builder.Append(capitalize ? char.ToUpperInvariant(character) : character);
            capitalize = false;
        }

        if (builder.Length == 0) builder.Append("Workflow");
        if (!char.IsLetter(builder[0]) && builder[0] != '_') builder.Insert(0, '_');
        if (!builder.ToString().EndsWith("Script", StringComparison.Ordinal)) builder.Append("Script");
        return builder.ToString();
    }
}
