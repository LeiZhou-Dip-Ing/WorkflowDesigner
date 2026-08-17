using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Services;
using Xunit;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class MethodStructureEditorTests
{
    [Fact]
    public void SurroundCurrentAction_CreatesCompleteConditionalBlock()
    {
        var method = Method("log", "delay");

        var inserted = MethodStructureEditor.InsertBlock(
            method,
            method.MethodLines[1],
            MethodBlockKind.If,
            surroundCurrent: true,
            WorkflowAction.Create);

        Assert.NotNull(inserted);
        Assert.Equal(new[] { "log", "if", "delay", "endIf" }, ActionTypes(method));
        Assert.Equal(new[] { 0, 0, 1, 0 }, method.MethodLines.Select(line => line.NestingLevel));
    }

    [Fact]
    public void InsertAfterCurrent_CreatesAdjacentLoopMarkers()
    {
        var method = Method("log", "delay");

        MethodStructureEditor.InsertBlock(
            method,
            method.MethodLines[0],
            MethodBlockKind.While,
            surroundCurrent: false,
            WorkflowAction.Create);

        Assert.Equal(new[] { "log", "while", "endWhile", "delay" }, ActionTypes(method));
        Assert.Equal(new[] { 0, 0, 0, 0 }, method.MethodLines.Select(line => line.NestingLevel));
    }

    [Fact]
    public void AddElseBranch_InsertsBeforeMatchingEndAndRejectsDuplicate()
    {
        var method = Method("if", "log", "endIf");
        method.MethodLines[1].NestingLevel = 1;

        var inserted = MethodStructureEditor.AddElseBranch(
            method,
            method.MethodLines[0],
            WorkflowAction.Create);

        Assert.NotNull(inserted);
        Assert.Equal(new[] { "if", "log", "else", "endIf" }, ActionTypes(method));
        Assert.False(MethodStructureEditor.CanAddElseBranch(method, method.MethodLines[0]));
        Assert.Null(MethodStructureEditor.AddElseBranch(method, method.MethodLines[3], WorkflowAction.Create));
    }

    [Fact]
    public void DeleteBegin_SelectsCompleteBlock()
    {
        var method = Method("log", "for", "delay", "endFor", "log");
        method.MethodLines[2].NestingLevel = 1;

        var deletion = MethodStructureEditor.GetDeletionSet(method, method.MethodLines[1]);

        Assert.Equal(new[] { "for", "delay", "endFor" }, deletion.Select(line => line.Action!.ActionType));
    }

    [Fact]
    public void DeleteElse_SelectsOnlyElseBranchAndLeavesEndMarker()
    {
        var method = Method("if", "log", "else", "delay", "endIf");
        method.MethodLines[1].NestingLevel = 1;
        method.MethodLines[3].NestingLevel = 1;

        var deletion = MethodStructureEditor.GetDeletionSet(method, method.MethodLines[2]);

        Assert.Equal(new[] { "else", "delay" }, deletion.Select(line => line.Action!.ActionType));
        Assert.False(MethodStructureEditor.CanDelete(method, method.MethodLines[4]));
    }

    private static WorkflowMethod Method(params string[] actionTypes)
    {
        var method = new WorkflowMethod { Name = "Test" };
        for (var index = 0; index < actionTypes.Length; index++)
        {
            method.MethodLines.Add(MethodLine.Create(
                (index + 1) * 10,
                0,
                WorkflowAction.Create(actionTypes[index])));
        }

        return method;
    }

    private static string[] ActionTypes(WorkflowMethod method)
        => method.MethodLines.Select(line => line.Action!.ActionType).ToArray();
}
