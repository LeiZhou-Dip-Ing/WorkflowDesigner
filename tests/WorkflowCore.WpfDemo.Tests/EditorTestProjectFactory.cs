using System.Text.Json.Nodes;
using WorkflowCore.WpfDemo.Editor;

namespace WorkflowCore.WpfDemo.Tests;

internal static class EditorTestProjectFactory
{
    public static WorkflowProject Create()
    {
        var main = new WorkflowMethod
        {
            Name = "Main",
            MethodVariables =
            [
                Variable("_$0x", "number", 0d),
                Variable("_$0workerResult", "number", 0d),
                Variable("_$0backgroundTask", "integer", null)
            ]
        };
        main.MethodLines.Add(Line(10, "setVariable", ("VariableName", "_$0x"), ("ValueExpression", "0")));
        main.MethodLines.Add(Line(20, "log", ("Message", "x = {_$0x}")));

        var worker = new WorkflowMethod
        {
            Name = "Worker",
            Inputs = [Parameter("input", "_$input", "number", required: true)],
            Outputs = [Parameter("workerResult", "_$0workerResult", "number")],
            MethodVariables =
            [
                Variable("_$input", "number", 0d),
                Variable("_$0workerResult", "number", 0d)
            ]
        };
        worker.MethodLines.Add(Line(10, "setVariable", ("VariableName", "_$0workerResult"), ("ValueExpression", "_$input * 2")));
        worker.MethodLines.Add(Line(20, "return", ("ReturnValues", "_$0workerResult")));

        var background = new WorkflowMethod
        {
            Name = "Background",
            Inputs = [Parameter("input", "_$input", "number", required: true)],
            MethodVariables = [Variable("_$input", "number", 0d)]
        };
        background.MethodLines.Add(Line(10, "log", ("Message", "background {_$input}")));

        return new WorkflowProject
        {
            Name = "Test Project",
            Version = "1.0",
            Methods = [main, worker, background]
        };
    }

    private static WorkflowVariable Variable(string name, string dataType, object? defaultValue)
        => new()
        {
            VariableName = name,
            DataType = dataType,
            Value = defaultValue,
            DefaultValue = defaultValue
        };

    private static WorkflowMethodParameter Parameter(
        string name,
        string variableName,
        string valueType,
        bool required = false)
        => new()
        {
            Name = name,
            DisplayName = name,
            VariableName = variableName,
            ValueType = valueType,
            Required = required
        };

    private static MethodLine Line(int lineNo, string actionType, params (string Name, string Value)[] properties)
    {
        var action = WorkflowAction.Create(actionType);
        foreach (var property in properties)
        {
            action.SetProperty(property.Name, JsonValue.Create(property.Value));
        }

        return MethodLine.Create(lineNo, 0, action);
    }
}
