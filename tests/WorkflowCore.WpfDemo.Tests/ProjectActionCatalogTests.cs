using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Services;
using WorkflowCore.WpfDemo.Services.Editing;
using WorkflowCore.WpfDemo.Services.Projects;
using System.Text.Json.Nodes;
using WorkflowRuntime.Contracts;
using Xunit;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class ProjectActionCatalogTests
{
    [Fact]
    public void DifferentRuntimeProject_UsesOnlyCurrentLocalProjectScripts()
    {
        var localScript = new WorkflowScript { Uid = Guid.NewGuid(), Name = "Local Script" };
        var remoteScript = new WorkflowScript { Uid = Guid.NewGuid(), Name = "Remote Script" };
        var runtimeCatalog = new StaticActionCatalog(
        [
            CreateDescriptor("log", "Log", "BuiltIn"),
            CreateScriptDescriptor(remoteScript)
        ]);
        var catalog = new ProjectActionCatalog(runtimeCatalog);
        catalog.BindProject(
            new WorkflowProject { Name = "Local Project", Scripts = [localScript] },
            runtimeCatalogBelongsToProject: false);

        Assert.Contains(catalog.Current.Actions, descriptor => descriptor.ActionType == "log");
        var scriptDescriptor = Assert.Single(
            catalog.Current.Actions,
            descriptor => descriptor.SourceKind == "CSharpScript");
        Assert.Equal(localScript.Name, scriptDescriptor.DisplayName);
        Assert.Equal($"csharp-script:{localScript.Uid:D}", scriptDescriptor.ActionId);
        Assert.DoesNotContain(
            catalog.Current.Actions,
            descriptor => descriptor.SourceId == remoteScript.Uid.ToString("D"));
    }

    [Fact]
    public void SameRuntimeProject_ReusesPublishedScriptContractMetadata()
    {
        var script = new WorkflowScript { Uid = Guid.NewGuid(), Name = "Statistics" };
        var published = CreateScriptDescriptor(script, includeInput: true);
        var catalog = new ProjectActionCatalog(new StaticActionCatalog([published]));
        catalog.BindProject(
            new WorkflowProject { Name = "Statistics Project", Scripts = [script] },
            runtimeCatalogBelongsToProject: true);

        var descriptor = Assert.Single(catalog.Current.Actions);
        Assert.Same(published, descriptor);
        Assert.Equal("Values", Assert.Single(descriptor.Inputs).Name);
    }

    [Fact]
    public void LocalScriptContract_IsAvailableBeforeRuntimeDeployment()
    {
        var script = new WorkflowScript
        {
            Uid = Guid.NewGuid(),
            Name = "Average",
            Content = """
                using WorkflowRuntime.ScriptSdk;

                public sealed class AverageScript : IWorkflowSharpScript
                {
                    [ScriptInput("Input numbers", Order = 0)]
                    public string InputNumbers { get; set; } = string.Empty;

                    [ScriptOutput("Average", Order = 0)]
                    public double Output { get; private set; }

                    public ValueTask ExecuteAsync(
                        IWorkflowSharpScriptContext context,
                        CancellationToken cancellationToken)
                        => ValueTask.CompletedTask;
                }
                """
        };
        var catalog = new ProjectActionCatalog(new StaticActionCatalog([]));
        catalog.BindProject(
            new WorkflowProject { Name = "Local Project", Scripts = [script] },
            runtimeCatalogBelongsToProject: false);

        var descriptor = Assert.Single(catalog.Current.Actions);
        Assert.Equal("InputNumbers", Assert.Single(descriptor.Inputs).Name);
        Assert.Equal("Output", Assert.Single(descriptor.Outputs).Name);
        Assert.True(descriptor.Inputs[0].SupportsVariableExpression);
        Assert.True(descriptor.Outputs[0].SupportsOutputBinding);
    }

    [Fact]
    public void LocalScriptAction_PreservesStoredPropertiesUntilPublishedContractIsAvailable()
    {
        var script = new WorkflowScript { Uid = Guid.NewGuid(), Name = "Local Calculator" };
        var catalog = new ProjectActionCatalog(new StaticActionCatalog([]));
        catalog.BindProject(
            new WorkflowProject { Name = "Local Project", Scripts = [script] },
            runtimeCatalogBelongsToProject: false);
        var action = WorkflowAction.Create(
            $"csharp-script:{script.Uid:D}",
            $"csharp-script:{script.Uid:N}");
        action.SetProperty("InputValue", JsonValue.Create("localValue"));
        var line = MethodLine.Create(10, 0, action);
        var method = new WorkflowMethod { Name = "Main", MethodLines = [line] };
        var propertyEditor = new ActionPropertyEditor(catalog, new VariableEditor());

        var properties = propertyEditor.BuildProperties(
            line,
            method,
            _ => null,
            _ => [],
            () => { },
            () => { });

        var input = Assert.Single(
            properties,
            property => string.Equals(property.Name, "InputValue", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("localValue", input.ValueText);
    }

    [Fact]
    public void PropertyEditor_PreservesStoredFieldsMissingFromPartialDescriptor()
    {
        var descriptor = new WorkflowActionDescriptorDto
        {
            ActionId = "partial",
            ActionType = "partial",
            DisplayName = "Partial Action",
            Category = "Actions",
            SourceKind = "Plugin",
            Inputs =
            [
                new WorkflowActionFieldDto
                {
                    Name = "KnownInput",
                    DisplayName = "Known input",
                    Direction = "input",
                    ValueType = "string"
                }
            ]
        };
        var action = WorkflowAction.Create("partial", "partial");
        action.SetProperty("KnownInput", JsonValue.Create("known"));
        action.SetProperty("LegacyInput", JsonValue.Create("preserved"));
        var line = MethodLine.Create(10, 0, action);
        var method = new WorkflowMethod { Name = "Main", MethodLines = [line] };
        var propertyEditor = new ActionPropertyEditor(
            new StaticActionCatalog([descriptor]),
            new VariableEditor());

        var properties = propertyEditor.BuildProperties(
            line,
            method,
            _ => null,
            _ => [],
            () => { },
            () => { });

        Assert.Contains(
            properties,
            property => string.Equals(property.Name, "KnownInput", StringComparison.OrdinalIgnoreCase));
        var legacy = Assert.Single(
            properties,
            property => string.Equals(property.Name, "LegacyInput", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("preserved", legacy.ValueText);
    }

    private static WorkflowActionDescriptorDto CreateDescriptor(
        string actionType,
        string displayName,
        string sourceKind)
        => new()
        {
            ActionId = actionType,
            ActionType = actionType,
            DisplayName = displayName,
            Category = "Actions",
            SourceKind = sourceKind
        };

    private static WorkflowActionDescriptorDto CreateScriptDescriptor(
        WorkflowScript script,
        bool includeInput = false)
        => new()
        {
            ActionId = $"csharp-script:{script.Uid:D}",
            ActionType = $"csharp-script:{script.Uid:N}",
            DisplayName = script.Name,
            Category = "CSharp Scripts",
            SourceKind = "CSharpScript",
            SourceId = script.Uid.ToString("D"),
            Inputs = includeInput
                ?
                [
                    new WorkflowActionFieldDto
                    {
                        Name = "Values",
                        DisplayName = "Values",
                        Direction = "input"
                    }
                ]
                : []
        };

    private sealed class StaticActionCatalog(IReadOnlyList<WorkflowActionDescriptorDto> actions)
        : IEditorActionCatalog
    {
        public ActionCatalogResponse Current { get; } = new() { Actions = actions };

        public string? GetCachedIconUri(ActionAssetReferenceDto? icon) => null;

        public Task<ActionCatalogResponse> RefreshAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Current);
    }
}
