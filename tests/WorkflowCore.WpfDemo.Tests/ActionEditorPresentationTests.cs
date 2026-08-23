using System.Text.Json.Nodes;
using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Models;
using WorkflowCore.WpfDemo.Services;
using WorkflowCore.WpfDemo.Services.Editing;
using WorkflowRuntime.ActionSdk;
using WorkflowDesigner.WpfSdk;
using WorkflowRuntime.Contracts;
using Xunit;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class ActionEditorPresentationTests
{
    [Fact]
    public void ActionPresentationPolicy_LegacyImageKeysFallBackToGenericProperties()
    {
        var descriptor = new WorkflowActionDescriptorDto
        {
            ActionType = "custom.any-name",
            DisplayName = "Custom Vision Tool",
            Category = "Custom",
            Presentation = new WorkflowActionPresentationDto
            {
                ActionKind = "vendor.image-processing",
                WorkspaceKind = "image",
                DoubleClickEditor = "image"
            }
        };

        Assert.Equal(WorkflowWorkspaceKeys.Properties, ActionPresentationPolicy.GetWorkspaceKey(descriptor));
        Assert.True(ActionPresentationPolicy.UsesPropertyWorkspace(descriptor));
        Assert.True(ActionPresentationPolicy.CanOpenOnDoubleClick(descriptor));
        Assert.Equal(WorkflowActionEditorKeys.Properties, ActionPresentationPolicy.GetDoubleClickEditor(descriptor));
    }


    [Fact]
    public void ActionPresentationPolicy_DoesNotInferWorkspaceFromActionKind()
    {
        var descriptor = new WorkflowActionDescriptorDto
        {
            ActionType = "custom.vision-with-properties",
            DisplayName = "Vision Setup",
            Category = "Custom",
            Presentation = new WorkflowActionPresentationDto
            {
                ActionKind = "vision",
                WorkspaceKind = "auto"
            }
        };

        Assert.True(ActionPresentationPolicy.UsesPropertyWorkspace(descriptor));
    }

    [Fact]
    public void ActionPresentationPolicy_DefaultsToPropertyPanelAndNoDoubleClickEditor()
    {
        var descriptor = new WorkflowActionDescriptorDto
        {
            ActionType = "vision.name-does-not-matter",
            DisplayName = "Normal Tool",
            Category = "Custom"
        };

        Assert.True(ActionPresentationPolicy.UsesPropertyWorkspace(descriptor));
        Assert.False(ActionPresentationPolicy.CanOpenOnDoubleClick(descriptor));
    }

    [Fact]
    public void DisplayText_UsesCurrentActionValuesAndMetadataDefaults()
    {
        var descriptor = new WorkflowActionDescriptorDto
        {
            ActionType = "for",
            DisplayName = "For",
            Category = "Control Flow",
            DisplayTemplate = "For {FromExpression} to {ToExpression} increment by {Step}",
            Inputs =
            [
                Field("FromExpression", JsonValue.Create("0")),
                Field("ToExpression", JsonValue.Create("3")),
                Field("Step", JsonValue.Create(1d))
            ]
        };
        var action = WorkflowAction.Create("for");
        action.SetProperty("fromExpression", JsonValue.Create("1"));
        action.SetProperty("toExpression", JsonValue.Create("12"));

        var display = ActionDisplayTextFormatter.Format(descriptor, action);

        Assert.Equal("For 1 to 12 increment by 1", display);
    }

    [Fact]
    public void GeneralProperties_WriteCommentAndDeactivateWithoutApplyStep()
    {
        var line = MethodLine.Create(10, 0, WorkflowAction.Create("log"));
        var changes = 0;
        var comment = ActionPropertyItem.CreateComment(line, () => changes++);
        var deactivate = ActionPropertyItem.CreateDeactivate(line, () => changes++);

        comment.ValueText = "Operator note";
        deactivate.BooleanValue = true;

        Assert.Equal("Operator note", line.Comment);
        Assert.False(line.IsActive);
        Assert.False(line.Action!.IsActive);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void CompositeLookup_UsesMetadataSuggestionsAndRequiredValidation()
    {
        var action = WorkflowAction.Create("setVariable");
        var descriptor = new WorkflowActionFieldDto
        {
            Name = "VariableName",
            DisplayName = "Variable name",
            Description = "Variable receiving the value.",
            Category = "Action",
            ValueType = "string",
            Direction = "input",
            Required = true,
            Editor = "variable",
            EditorOptions = new WorkflowActionEditorOptionsDto
            {
                DataSource = "methodVariables",
                AllowCreate = true,
                AllowClear = true,
                CreateKind = "variable"
            }
        };
        var item = new ActionPropertyItem(action, descriptor, () => { }, ["workerResult", "input"]);

        Assert.True(item.IsLookupEditor);
        Assert.Equal(["input", "workerResult"], item.Suggestions);
        Assert.True(item.HasValidationError);

        item.ValueText = "input";
        Assert.False(item.HasValidationError);
        Assert.Equal("input", action.GetProperty("VariableName")?.GetValue<string>());

        item.ClearValue();
        Assert.True(item.HasValidationError);
    }

    [Fact]
    public void LookupFirstSelection_RemainsSelectedDuringSuggestionRefresh()
    {
        var action = WorkflowAction.Create("setVariable");
        var descriptor = new WorkflowActionFieldDto
        {
            Name = "VariableName",
            DisplayName = "Variable name",
            ValueType = "string",
            Direction = "input",
            Editor = "variable",
            EditorOptions = new WorkflowActionEditorOptionsDto
            {
                DataSource = "methodVariables",
                AllowCustomValue = false
            }
        };
        ActionPropertyItem? item = null;
        var suggestions = new[] { "_$param1", "_$param2" };
        item = new ActionPropertyItem(
            action,
            descriptor,
            () => item!.RefreshSuggestions(suggestions),
            suggestions);
        var collectionChanges = 0;
        item.Suggestions.CollectionChanged += (_, _) => collectionChanges++;

        item.SelectedSuggestion = "_$param2";

        Assert.Equal("_$param2", item.ValueText);
        Assert.Equal("_$param2", item.SelectedSuggestion);
        Assert.Equal("_$param2", action.GetProperty("VariableName")?.GetValue<string>());
        Assert.Equal(0, collectionChanges);

        item.RefreshSuggestions(["_$param2", "_$param3"]);
        Assert.Equal("_$param2", item.SelectedSuggestion);
    }

    [Fact]
    public void PluginOutputBinding_StoresTheReceivingVariableSeparately()
    {
        var action = WorkflowAction.Create("sample.greeting", "Greeting");
        var output = new WorkflowActionFieldDto
        {
            Name = "Greeting",
            DisplayName = "Greeting",
            Description = "Generated greeting text.",
            Category = "Action",
            ValueType = "string",
            Direction = "output",
            IsReadOnly = true
        };
        var item = ActionPropertyItem.CreateOutputBinding(
            action,
            output,
            () => { },
            ["greetingText"]);

        item.ValueText = "greetingText";

        Assert.True(item.IsOutputBinding);
        Assert.True(item.IsLookupEditor);
        Assert.False(item.IsReadOnly);
        Assert.Equal("greetingText", action.GetOutputBinding("Greeting"));
        Assert.Null(action.GetProperty("Greeting"));
    }

    [Fact]
    public void DynamicFieldEditorsUseExplicitCapabilitiesWithoutPluginIdentityChecks()
    {
        var descriptor = new WorkflowActionDescriptorDto
        {
            ActionId = "script.test",
            ActionType = "script.test",
            DisplayName = "Script Test",
            Category = "CSharp Scripts",
            SourceKind = "CSharpScript",
            Inputs =
            [
                new WorkflowActionFieldDto
                {
                    Name = "Factor",
                    DisplayName = "Factor",
                    ValueType = "number",
                    Direction = "input",
                    SupportsVariableExpression = true
                }
            ],
            Outputs =
            [
                new WorkflowActionFieldDto
                {
                    Name = "Result",
                    DisplayName = "Result",
                    ValueType = "number",
                    Direction = "output",
                    IsReadOnly = true,
                    SupportsOutputBinding = true
                }
            ]
        };
        var editor = new ActionPropertyEditor(new TestCatalog([descriptor]), new VariableEditor());
        var action = WorkflowAction.Create("script.test", "script.test");
        var method = new WorkflowMethod
        {
            Name = "Main",
            MethodVariables = [new WorkflowVariable { VariableName = "_$factor" }]
        };
        var line = MethodLine.Create(10, 0, action);
        method.MethodLines.Add(line);

        var properties = editor.BuildProperties(
            line,
            method,
            _ => null,
            _ => ["_$factor"],
            () => { },
            () => { });

        var input = Assert.Single(properties, item => item.Name == "Factor");
        var output = Assert.Single(properties, item => item.Name == "Result");
        Assert.Equal("methodVariableExpressions", input.DataSource);
        Assert.False(input.IsOutputBinding);
        Assert.True(output.IsOutputBinding);
        Assert.False(output.IsReadOnly);
        Assert.Null(descriptor.PluginId);
    }

    [Fact]
    public void RunMethodProperties_AreGeneratedFromTheTargetMethodSignature()
    {
        var target = MethodWithSignature("Worker", "_$input", "_$0output");
        var caller = new WorkflowMethod
        {
            Name = "Caller",
            MethodVariables =
            [
                new WorkflowVariable { VariableName = "_$0source" },
                new WorkflowVariable { VariableName = "_$0destination" }
            ]
        };
        var action = WorkflowAction.Create("runMethod");
        action.SetProperty("MethodName", JsonValue.Create("Worker"));
        action.SetProperty("Parameters", new JsonObject { ["input"] = "_$0source" });
        action.SetProperty("ReturnVarNames", JsonValue.Create("_$0destination"));
        var line = MethodLine.Create(10, 0, action);
        caller.MethodLines.Add(line);
        var editor = CreateMethodBindingEditor();

        var properties = editor.BuildProperties(
            line,
            caller,
            name => string.Equals(name, target.Name, StringComparison.OrdinalIgnoreCase) ? target : null,
            dataSource => dataSource == "methodVariables" ? ["_$0destination"] : ["_$0source"],
            () => { },
            () => { });

        Assert.DoesNotContain(properties, item => item.Name is "Parameters" or "ReturnVarNames" or "ResultVariables");
        var input = properties.Single(item => item.Name == "Parameters.input");
        var output = properties.Single(item => item.Name == "ReturnVarNames.output");
        Assert.Equal("_$0source", input.ValueText);
        Assert.Equal("_$0destination", output.ValueText);
        Assert.True(input.IsLookupEditor);
        Assert.True(output.IsOutputBinding);
        Assert.DoesNotContain(properties, item => item.Name == "Parameters.notPublic");

        input.ValueText = "_$0source + 1";
        output.ValueText = "_$0newDestination";
        var creation = editor.CreatePropertyVariable(caller, output);

        Assert.Equal("_$0source + 1", action.GetProperty("Parameters")!["input"]!.GetValue<string>());
        Assert.Equal("_$0newDestination", action.GetProperty("ReturnVarNames")!.GetValue<string>());
        Assert.True(creation.Succeeded);
        Assert.Equal("number", caller.MethodVariables.Single(variable => variable.VariableName == "_$0newDestination").DataType);
    }

    [Fact]
    public void RunMethodTargetSelection_CommitsTheSelectedMethodAndRequestsAPropertyRebuild()
    {
        var caller = new WorkflowMethod { Name = "Caller" };
        var action = WorkflowAction.Create("runMethod");
        action.SetProperty("MethodName", JsonValue.Create(string.Empty));
        var line = MethodLine.Create(10, 0, action);
        caller.MethodLines.Add(line);
        var editor = CreateMethodBindingEditor();
        var properties = editor.BuildProperties(
            line,
            caller,
            _ => null,
            dataSource => dataSource == "methods" ? ["Worker"] : [],
            () => { },
            () => { });
        var methodProperty = properties.Single(item => item.Name == "MethodName");
        var appliedCount = 0;
        methodProperty.ValueApplied += (_, _) => appliedCount++;

        methodProperty.SelectedSuggestion = "Worker";

        Assert.False(methodProperty.AllowCustomValue);
        Assert.Equal("Worker", methodProperty.SelectedSuggestion);
        Assert.Equal("Worker", action.GetProperty("MethodName")!.GetValue<string>());
        Assert.Equal(1, appliedCount);
    }

    [Fact]
    public void ThreadWaitProperties_DoNotExposeRunMethodReturnMappings()
    {
        var caller = new WorkflowMethod { Name = "Caller" };
        var wait = WorkflowAction.Create("threadWait");
        wait.SetProperty("TaskVarName", JsonValue.Create("_$0backgroundTask"));
        var waitLine = MethodLine.Create(20, 0, wait);
        caller.MethodLines.Add(waitLine);
        var editor = CreateMethodBindingEditor();

        var properties = editor.BuildProperties(
            waitLine,
            caller,
            _ => null,
            _ => ["_$0backgroundTask"],
            () => { },
            () => { });

        Assert.Contains(properties, item => item.Name == "TaskVarName");
        Assert.DoesNotContain(properties, item => item.Name.StartsWith("ReturnVarNames", StringComparison.Ordinal));
    }

    [Fact]
    public void Hierarchy_FlattensLeafActionsWithoutARealParentBlock()
    {
        var items = new[]
        {
            Item("delay", 0),
            Item("log", 1),
            Item("log", 2),
            Item("incrementVariable", 1)
        };

        MethodLineHierarchy.Apply(items);

        Assert.All(items, item => Assert.Equal(0, item.DisplayNestingLevel));
    }

    [Fact]
    public void Hierarchy_KeepsLeafActionsUnderExistingBlockActions()
    {
        var items = new[]
        {
            Item("while", 0, "begin"),
            Item("log", 1),
            Item("if", 1, "begin"),
            Item("log", 2),
            Item("else", 1, "branch"),
            Item("log", 2),
            Item("endIf", 1, "end"),
            Item("endWhile", 0, "end")
        };

        MethodLineHierarchy.Apply(items);

        Assert.Equal(new[] { 0, 1, 1, 2, 1, 2, 1, 0 },
            items.Select(item => item.DisplayNestingLevel));
    }

    private static MethodLineViewItem Item(string actionType, int nestingLevel, string? blockRole = null)
    {
        var line = MethodLine.Create(10, nestingLevel, WorkflowAction.Create(actionType));
        var descriptor = new WorkflowActionDescriptorDto
        {
            ActionType = actionType,
            DisplayName = actionType,
            Category = "Test",
            BlockRole = blockRole
        };
        return new MethodLineViewItem(line, descriptor, null, true, () => { }, _ => { });
    }

    private static WorkflowActionFieldDto Field(string name, JsonNode defaultValue)
        => new()
        {
            Name = name,
            DisplayName = name,
            Description = name,
            Category = "Action",
            ValueType = defaultValue is JsonValue value && value.TryGetValue<double>(out _) ? "number" : "string",
            Direction = "input",
            DefaultValue = defaultValue
        };

    private static ActionPropertyEditor CreateMethodBindingEditor()
    {
        var catalog = new TestCatalog(
        [
            MethodActionDescriptor("runMethod", includeParameters: true),
            MethodActionDescriptor("threadStart", includeParameters: true),
            MethodActionDescriptor("threadWait", includeParameters: false)
        ]);
        return new ActionPropertyEditor(catalog, new VariableEditor());
    }

    private static WorkflowMethod MethodWithSignature(string name, string input, string output)
    {
        var method = new WorkflowMethod
        {
            Name = name,
            MethodVariables =
            [
                new WorkflowVariable { VariableName = input, DataType = "number", OrderIndex = 0 },
                new WorkflowVariable { VariableName = output, DataType = "number", OrderIndex = 1 },
                new WorkflowVariable { VariableName = "_$notPublic", DataType = "number", OrderIndex = 2 }
            ],
            Inputs =
            [
                new WorkflowMethodParameter
                {
                    Name = WorkflowVariableNaming.GetBaseName(input),
                    DisplayName = WorkflowVariableNaming.GetBaseName(input),
                    VariableName = input,
                    ValueType = "number",
                    Required = true
                }
            ],
            Outputs =
            [
                new WorkflowMethodParameter
                {
                    Name = WorkflowVariableNaming.GetBaseName(output),
                    DisplayName = WorkflowVariableNaming.GetBaseName(output),
                    VariableName = output,
                    ValueType = "number"
                }
            ]
        };
        var returnAction = WorkflowAction.Create("return");
        returnAction.SetProperty("ReturnValues", JsonValue.Create(output));
        method.MethodLines.Add(MethodLine.Create(10, 0, returnAction));
        return method;
    }

    private static WorkflowActionDescriptorDto MethodActionDescriptor(
        string actionType,
        bool includeParameters)
    {
        var fields = new List<WorkflowActionFieldDto>();
        if (actionType != "threadWait")
        {
            fields.Add(new WorkflowActionFieldDto
            {
                Name = "MethodName",
                DisplayName = "Method",
                Category = "Action",
                ValueType = "string",
                Editor = "method",
                Order = 0,
                EditorOptions = new WorkflowActionEditorOptionsDto
                {
                    DataSource = "methods",
                    AllowCustomValue = false,
                    AllowClear = true
                }
            });
        }

        if (includeParameters)
        {
            fields.Add(new WorkflowActionFieldDto
            {
                Name = "Parameters",
                DisplayName = "Method parameters",
                Category = "Action",
                ValueType = "object",
                Editor = "json",
                Order = 1
            });
        }

        fields.Add(new WorkflowActionFieldDto
        {
            Name = "TaskVarName",
            DisplayName = "Task id variable",
            Category = "Action",
            ValueType = "string",
            Editor = "variable",
            Order = 2
        });
        if (actionType == "runMethod")
        {
            fields.Add(new WorkflowActionFieldDto
            {
                Name = "ReturnVarNames",
                DisplayName = "Store return values in",
                Category = "Action",
                ValueType = "string",
                Editor = "text",
                Order = 3
            });
        }
        fields.Add(new WorkflowActionFieldDto
        {
            Name = "ResultVariables",
            DisplayName = "Resolved return values",
            Category = "Action",
            ValueType = "array",
            Direction = "output",
            IsReadOnly = true,
            Editor = "json",
            Order = 4
        });
        return new WorkflowActionDescriptorDto
        {
            ActionId = actionType,
            ActionType = actionType,
            DisplayName = actionType,
            Category = "Test",
            Inputs = fields
        };
    }

    private sealed class TestCatalog(IReadOnlyList<WorkflowActionDescriptorDto> actions) : IEditorActionCatalog
    {
        public ActionCatalogResponse Current { get; } = new() { Actions = actions };

        public string? GetCachedIconUri(ActionAssetReferenceDto? icon) => null;

        public Task<ActionCatalogResponse> RefreshAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Current);
    }
    [Fact]
    public void ActionPropertyItem_PreservesCustomNamespacedEditorKeyForRegistryResolution()
    {
        const string customKey = "sample.vendor.property.roi";
        var action = WorkflowAction.Create("custom.roi");
        var field = new WorkflowActionFieldDto
        {
            Name = "Roi",
            DisplayName = "ROI",
            Category = "Action",
            ValueType = "string",
            Direction = "input",
            Editor = customKey
        };

        var property = new ActionPropertyItem(action, field, () => { });

        Assert.Equal(customKey, property.EditorKey);
    }

    [Fact]
    public void DesignerKeyCompatibility_NormalizesLegacyKeysWithoutBlockingCustomKeys()
    {
        Assert.Equal(WorkflowPropertyEditorKeys.Number, DesignerKeyCompatibility.NormalizePropertyEditor("number"));
        Assert.Equal(WorkflowWorkspaceKeys.Properties, DesignerKeyCompatibility.NormalizeWorkspace("image"));
        Assert.Equal(WorkflowActionEditorKeys.Properties, DesignerKeyCompatibility.NormalizeActionEditor("vision"));
        Assert.Equal("vendor.custom.editor", DesignerKeyCompatibility.NormalizePropertyEditor("vendor.custom.editor"));
    }

}
