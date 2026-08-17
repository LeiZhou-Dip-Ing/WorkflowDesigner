using WorkflowCore.WpfDemo.Services.Scripting;
using WorkflowRuntime.ScriptCompiler;
using Xunit;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class SharpScriptLocalRunnerTests
{
    [Fact]
    public async Task ExtractDigits_AcceptanceScript_UsesGeneratedInputsOutputsAndCachedRevision()
    {
        var compiler = new CountingCompiler(new SharpScriptCompiler());
        using var runner = new SharpScriptLocalRunner(compiler);
        var scriptUid = Guid.NewGuid();
        var inputs = new Dictionary<string, string>
        {
            ["SourceText"] = "f1234sk",
            ["Pattern"] = @"\d+"
        };

        var first = await runner.RunAsync(scriptUid, Source, "ExtractDigits.csx", inputs);
        var second = await runner.RunAsync(scriptUid, Source, "ExtractDigits.csx", inputs);

        Assert.True(first.Succeeded, string.Join(Environment.NewLine, first.Diagnostics.Select(item => item.Message)));
        Assert.Equal("1234", first.Outputs["ExtractedText"]);
        Assert.Equal(1234, first.Outputs["ExtractedNumber"]);
        Assert.Contains("Extracted number: 1234", first.Messages);
        Assert.Equal(first.Outputs["ExtractedNumber"], second.Outputs["ExtractedNumber"]);
        Assert.Equal(1, compiler.CompileCount);
        Assert.Equal(["SourceText", "Pattern"], first.Contract!.Inputs.Select(item => item.Name));
        Assert.Equal(["ExtractedText", "ExtractedNumber"], first.Contract.Outputs.Select(item => item.Name));
    }

    private sealed class CountingCompiler(ISharpScriptCompiler inner) : ISharpScriptCompiler
    {
        public int CompileCount { get; private set; }

        public SharpScriptAnalysisResult Analyze(
            SharpScriptCompilationRequest request,
            CancellationToken cancellationToken = default)
            => inner.Analyze(request, cancellationToken);

        public SharpScriptCompilation Compile(
            SharpScriptCompilationRequest request,
            CancellationToken cancellationToken = default)
        {
            CompileCount++;
            return inner.Compile(request, cancellationToken);
        }
    }

    private const string Source = """
        using System.Text.RegularExpressions;
        using WorkflowRuntime.ScriptSdk;

        public sealed class ExtractDigitsScript : IWorkflowSharpScript
        {
            [ScriptInput("Source Text", Description = "Text containing a number.", Required = true, Order = 0)]
            public string SourceText { get; set; } = "f1234sk";

            [ScriptInput("Regex Pattern", Description = "Pattern used to extract the first number.", Required = true, Order = 1)]
            public string Pattern { get; set; } = @"\d+";

            [ScriptOutput("Extracted Text", Description = "Matched number as text.", Order = 0)]
            public string ExtractedText { get; private set; } = string.Empty;

            [ScriptOutput("Extracted Number", Description = "Matched number converted to int.", Order = 1)]
            public int ExtractedNumber { get; private set; }

            public ValueTask ExecuteAsync(IWorkflowSharpScriptContext context, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var match = Regex.Match(SourceText ?? string.Empty, Pattern ?? string.Empty);
                ExtractedText = match.Success ? match.Value : string.Empty;
                ExtractedNumber = match.Success ? int.Parse(match.Value) : 0;
                context.Log($"Extracted number: {ExtractedNumber}");
                return ValueTask.CompletedTask;
            }
        }
        """;
}
