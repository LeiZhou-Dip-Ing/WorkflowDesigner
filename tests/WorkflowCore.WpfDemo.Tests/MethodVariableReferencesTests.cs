using System.Text.Json.Nodes;
using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Services;
using WorkflowCore.WpfDemo.Services.Editing;
using WorkflowRuntime.Contracts;
using Xunit;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class MethodVariableReferencesTests
{
    private static readonly IReadOnlyList<WorkflowActionDescriptorDto> Descriptors =
    [
        Descriptor("log", Field("Message", "expression")),
        Descriptor("setVariable", Field("VariableName", "variable", "methodVariables"), Field("ValueExpression", "expression")),
        Descriptor("if", Field("Condition", "expression")),
        Descriptor("runMethod", Field("MethodName", "method"), Field("Parameters", "json", valueType: "object"), Field("ReturnVarNames", "text")),
        Descriptor("threadStart", Field("MethodName", "method"), Field("TaskVarName", "variable", "methodVariables"), Field("Parameters", "json", valueType: "object")),
        Descriptor("return", Field("ReturnValues", "expression"))
    ];

    [Fact]
    public void Discover_BuildsTheRealTimeMethodVariableListFromActionMetadata()
    {
        var method = new WorkflowMethod
        {
            Name = "Main",
            MethodVariables =
            [
                new WorkflowVariable { VariableName = "_$workerInput" },
                new WorkflowVariable { VariableName = "_$0x" },
                new WorkflowVariable { VariableName = "_$0backgroundTask" },
                new WorkflowVariable { VariableName = "_$0unused" }
            ]
        };
        method.MethodLines.Add(Line("setVariable", ("VariableName", "_$0x"), ("ValueExpression", "_$workerInput * 2")));
        method.MethodLines.Add(Line("log", ("Message", "'Loop body, x = ' + _$0x")));
        method.MethodLines.Add(Line("runMethod", ("MethodName", "Worker"), ("Parameters", new JsonObject { ["_$input"] = "_$0x" }), ("ReturnVarNames", "_$0workerResult")));
        method.MethodLines.Add(Line("threadStart", ("MethodName", "Background"), ("TaskVarName", "_$0backgroundTask"), ("Parameters", new JsonObject())));
        method.MethodLines.Add(Line("log", ("Message", "Plain status text")));

        var variables = MethodVariableReferences.Discover(method, Resolve);

        Assert.Equal(["_$0backgroundTask", "_$0unused", "_$0workerResult", "_$0x", "_$workerInput"], variables.Select(variable => variable.VariableName));
        Assert.True(variables.Single(variable => variable.VariableName == "_$workerInput").IsInput);
        Assert.True(variables.Single(variable => variable.VariableName == "_$0unused").IsDeclared);
    }

    [Fact]
    public void Discover_IncludesPluginOutputBindingsAndInfersTheirType()
    {
        var action = WorkflowAction.Create("sample.greeting", "Greeting");
        action.SetProperty("PersonName", JsonValue.Create("_$personName"));
        action.SetOutputBinding("Greeting", "_$0greetingText");
        var method = new WorkflowMethod
        {
            Name = "Main",
            MethodVariables = [new WorkflowVariable { VariableName = "_$personName", DataType = "string" }]
        };
        method.MethodLines.Add(MethodLine.Create(10, 0, action));
        var descriptor = new WorkflowActionDescriptorDto
        {
            ActionId = "sample.greeting",
            ActionType = "Greeting",
            DisplayName = "Greeting",
            Category = "External plugins",
            PluginId = "sample",
            Inputs = [Field("PersonName", "text")],
            Outputs =
            [
                new WorkflowActionFieldDto
                {
                    Name = "Greeting",
                    DisplayName = "Greeting",
                    Direction = "output",
                    ValueType = "string",
                    IsReadOnly = true
                }
            ]
        };

        var variables = MethodVariableReferences.Discover(
            method,
            actionType => string.Equals(actionType, "Greeting", StringComparison.OrdinalIgnoreCase)
                ? descriptor
                : null);

        Assert.Equal(["_$0greetingText", "_$personName"], variables.Select(variable => variable.VariableName));
        Assert.Equal("string", variables.Single(variable => variable.VariableName == "_$0greetingText").DataType);
        Assert.False(variables.Single(variable => variable.VariableName == "_$0greetingText").IsDeclared);
    }

    [Fact]
    public void Rename_UpdatesWinLissyVariablesInsidePluginExpressions()
    {
        var action = WorkflowAction.Create("sample.calculate", "Calculate");
        action.SetProperty("Expression", JsonValue.Create("_$left + _$right"));
        var method = new WorkflowMethod
        {
            Name = "Main",
            MethodVariables =
            [
                new WorkflowVariable { VariableName = "_$left" },
                new WorkflowVariable { VariableName = "_$right" }
            ]
        };
        method.MethodLines.Add(MethodLine.Create(10, 0, action));
        var project = new WorkflowProject { Methods = [method] };
        var descriptor = new WorkflowActionDescriptorDto
        {
            ActionId = "sample.calculate",
            ActionType = "Calculate",
            DisplayName = "Calculate",
            Category = "External plugins",
            PluginId = "sample",
            Inputs = [Field("Expression", "string", supportsVariableExpression: true)]
        };

        MethodVariableReferences.Rename(
            project,
            method,
            "_$left",
            "_$renamed",
            false,
            actionType => actionType == "Calculate" ? descriptor : null);

        Assert.Equal("_$renamed + _$right", action.GetProperty("Expression")!.GetValue<string>());
    }

    [Fact]
    public void Rename_UpdatesVariableReferencesWithoutChangingCommentsOrOrdinaryText()
    {
        var method = new WorkflowMethod
        {
            Name = "Main",
            MethodVariables = [new WorkflowVariable { VariableName = "_$0x" }]
        };
        method.MethodLines.Add(Line(
            "setVariable",
            [("VariableName", "_$0x"), ("ValueExpression", "{_$0x} + 1")],
            "Keep x in this comment."));
        method.MethodLines.Add(Line("if", ("Condition", "_$0x < 3")));
        method.MethodLines.Add(Line("log", ("Message", "Loop body, x = {_$0x}")));
        method.MethodLines.Add(Line("runMethod", ("MethodName", "x"), ("Parameters", new JsonObject { ["_$input"] = "_$0x" }), ("ReturnVarNames", "_$0x, _$0result")));
        var project = new WorkflowProject { Methods = [method] };

        var changes = MethodVariableReferences.Rename(project, method, "_$0x", "_$0count", false, Resolve);

        Assert.True(changes >= 6);
        Assert.Equal("_$0count", method.MethodVariables.Single().VariableName);
        Assert.Equal("_$0count", Property(method, 0, "VariableName"));
        Assert.Equal("{_$0count} + 1", Property(method, 0, "ValueExpression"));
        Assert.Equal("_$0count < 3", Property(method, 1, "Condition"));
        Assert.Equal("Loop body, x = {_$0count}", Property(method, 2, "Message"));
        Assert.Equal("x", Property(method, 3, "MethodName"));
        Assert.Equal("_$0count", method.MethodLines[3].Action!.GetProperty("Parameters")!["_$input"]!.GetValue<string>());
        Assert.Equal("_$0count, _$0result", Property(method, 3, "ReturnVarNames"));
        Assert.Equal("Keep x in this comment.", method.MethodLines[0].Comment);
    }

    [Fact]
    public void Rename_GlobalVariableCanUpdateEveryMethod()
    {
        var first = new WorkflowMethod { Name = "Main", MethodVariables = [new WorkflowVariable { VariableName = "_0shared" }] };
        first.MethodLines.Add(Line("setVariable", ("VariableName", "_0shared"), ("ValueExpression", "1")));
        var second = new WorkflowMethod { Name = "Worker" };
        second.MethodLines.Add(Line("log", ("Message", "Shared = {_0shared}")));
        var project = new WorkflowProject { Methods = [first, second] };

        MethodVariableReferences.Rename(project, first, "_0shared", "_0globalCount", true, Resolve);

        Assert.Equal("_0globalCount", first.MethodVariables.Single().VariableName);
        Assert.Equal("_0globalCount", Property(first, 0, "VariableName"));
        Assert.Equal("Shared = {_0globalCount}", Property(second, 0, "Message"));
    }

    [Fact]
    public void Refresh_DerivesTheDeclaredScopeFromTheVariablePrefix()
    {
        var method = new WorkflowMethod
        {
            Name = "Main",
            MethodVariables =
            [
                new WorkflowVariable { VariableName = "_0shared" }
            ]
        };
        method.MethodLines.Add(Line(
            "setVariable",
            ("VariableName", "_0shared"),
            ("ValueExpression", "1")));

        var changes = new VariableEditor().EnsureDeclarations(method, Resolve);
        var overview = MethodVariableReferences.Discover(method, Resolve).Single();

        Assert.Equal(0, changes);
        Assert.Equal(WorkflowVariableScope.GlobalInternal, method.MethodVariables.Single().VariableScope);
        Assert.Equal("Global (internal)", overview.ScopeDisplay);
    }

    [Fact]
    public void Refresh_RemovesAnUnreferencedActionDefaultButKeepsContractVariables()
    {
        var method = new WorkflowMethod
        {
            Name = "Main",
            Inputs =
            [
                new WorkflowMethodParameter
                {
                    Name = "input",
                    VariableName = "_$input",
                    ValueType = "number"
                }
            ],
            MethodVariables =
            [
                new WorkflowVariable { VariableName = "_$input", DataType = "number" },
                new WorkflowVariable { VariableName = "_$0x" },
                new WorkflowVariable { VariableName = "_$0actual" }
            ]
        };
        method.MethodLines.Add(Line(
            "setVariable",
            ("VariableName", "_$0actual"),
            ("ValueExpression", "_$input + 1")));

        var changes = new VariableEditor().EnsureDeclarations(method, Resolve);

        Assert.Equal(1, changes);
        Assert.Equal(
            ["_$0actual", "_$input"],
            method.MethodVariables.Select(variable => variable.VariableName).OrderBy(name => name));
    }

    private static WorkflowActionDescriptorDto? Resolve(string actionType)
        => Descriptors.FirstOrDefault(descriptor => string.Equals(descriptor.ActionType, actionType, StringComparison.OrdinalIgnoreCase));

    private static WorkflowActionDescriptorDto Descriptor(string actionType, params WorkflowActionFieldDto[] fields)
        => new()
        {
            ActionType = actionType,
            DisplayName = actionType,
            Category = "Test",
            Inputs = fields
        };

    private static WorkflowActionFieldDto Field(
        string name,
        string editor,
        string dataSource = "",
        string valueType = "string",
        bool supportsVariableExpression = false)
        => new()
        {
            Name = name,
            DisplayName = name,
            Editor = editor,
            ValueType = valueType,
            SupportsVariableExpression = supportsVariableExpression,
            EditorOptions = string.IsNullOrEmpty(dataSource)
                ? null
                : new WorkflowActionEditorOptionsDto { DataSource = dataSource }
        };

    private static MethodLine Line(string actionType, params (string Name, object? Value)[] properties)
        => Line(actionType, properties, null);

    private static MethodLine Line(string actionType, (string Name, object? Value)[] properties, string? comment)
    {
        var action = WorkflowAction.Create(actionType);
        foreach (var property in properties)
        {
            action.SetProperty(property.Name, JsonValueFrom(property.Value));
        }

        return MethodLine.Create(10, 0, action, comment);
    }

    private static JsonNode? JsonValueFrom(object? value)
        => value switch
        {
            null => null,
            JsonNode node => node,
            string text => JsonValue.Create(text),
            bool boolean => JsonValue.Create(boolean),
            int integer => JsonValue.Create(integer),
            double number => JsonValue.Create(number),
            _ => throw new InvalidOperationException($"Unsupported test value type {value.GetType().Name}.")
        };

    private static string Property(WorkflowMethod method, int lineIndex, string name)
        => method.MethodLines[lineIndex].Action!.GetProperty(name)!.GetValue<string>();
}
