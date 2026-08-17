using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Models;
using WorkflowCore.WpfDemo.Services;
using WorkflowCore.WpfDemo.ViewModels;
using Xunit;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class WorkflowDocumentEditStateTests
{
    [Fact]
    public void UnsavedChange_SetsDirtyAndUndoRestoresSavedSnapshot()
    {
        var state = WorkflowDocumentEditState.CreateSaved("saved");

        Assert.True(state.Observe("changed"));
        Assert.True(state.IsDirty);
        Assert.True(state.CanUndo);

        Assert.Equal("saved", state.Undo());
        Assert.False(state.IsDirty);
        Assert.False(state.CanUndo);
    }

    [Fact]
    public void MarkSaved_ClearsUndoHistoryAndDisablesUndo()
    {
        var state = WorkflowDocumentEditState.CreateSaved("saved");
        state.Observe("changed-once");
        state.Observe("changed-twice");

        state.MarkSaved("changed-twice");

        Assert.False(state.IsDirty);
        Assert.False(state.CanUndo);
        Assert.Null(state.Undo());
    }

    [Fact]
    public void NewlyCreatedDocument_CanUndoItsCreationWithoutAnEarlierSnapshot()
    {
        var state = WorkflowDocumentEditState.CreateUnsaved("new document");

        Assert.True(state.IsDirty);
        Assert.True(state.IsUnsavedCreation);
        Assert.False(state.CanUndo);
        Assert.Null(state.Undo());
    }

    [Fact]
    public void SavedDocument_IsNotTreatedAsAnUndoableCreation()
    {
        var state = WorkflowDocumentEditState.CreateSaved("saved document");

        Assert.False(state.IsUnsavedCreation);
    }

    [Fact]
    public void EditGroup_CoalescesAutomaticObservationsIntoOneUserUndo()
    {
        var state = WorkflowDocumentEditState.CreateSaved("saved");
        state.Observe("working");

        state.BeginEdit("working");
        state.Observe("deactivated");
        state.BeginEdit("deactivated");
        state.Observe("deactivated-and-normalized");
        state.CompleteEdit("deactivated-and-normalized");

        Assert.Equal("working", state.Undo());
    }

    [Fact]
    public void SavingOneState_DoesNotClearAnotherDocumentsDirtyHistory()
    {
        var first = WorkflowDocumentEditState.CreateSaved("first-saved");
        var second = WorkflowDocumentEditState.CreateSaved("second-saved");
        first.Observe("first-changed");
        second.Observe("second-changed");

        first.MarkSaved("first-changed");

        Assert.False(first.IsDirty);
        Assert.False(first.CanUndo);
        Assert.True(second.IsDirty);
        Assert.True(second.CanUndo);
        Assert.Equal("second-saved", second.Undo());
    }

    [Fact]
    public void SavingOneDocument_DoesNotApplyAnotherDirtyDocument()
    {
        var serializer = new WorkflowEditorJsonSerializer();
        var savedProject = EditorTestProjectFactory.Create();
        var workingProject = serializer.Deserialize(serializer.Serialize(savedProject));
        var originalMainJson = serializer.SerializeDocument(
            WorkflowEditorDocument.FromMethod(savedProject.Methods.Single(method => method.Name == "Main")));
        var worker = workingProject.Methods.Single(method => method.Name == "Worker");
        var background = workingProject.Methods.Single(method => method.Name == "Background");

        var savedWorker = serializer.DeserializeDocument(
            serializer.SerializeDocument(WorkflowEditorDocument.FromMethod(worker))).Method!;
        savedWorker.MethodLines.Add(MethodLine.Create(
            999,
            0,
            WorkflowAction.Create("log"),
            "Saved Worker change"));

        background.MethodLines.Add(MethodLine.Create(
            999,
            0,
            WorkflowAction.Create("log"),
            "Unsaved Background change"));

        MainWindowViewModel.UpsertSavedDocument(
            savedProject,
            WorkflowEditorDocument.FromMethod(savedWorker));

        Assert.Equal(
            originalMainJson,
            serializer.SerializeDocument(
                WorkflowEditorDocument.FromMethod(savedProject.Methods.Single(method => method.Name == "Main"))));
        Assert.Contains(
            savedProject.Methods.Single(method => method.Name == "Worker").MethodLines,
            line => line.Comment == "Saved Worker change");
        Assert.DoesNotContain(
            savedProject.Methods.Single(method => method.Name == "Background").MethodLines,
            line => line.Comment == "Unsaved Background change");
    }

    [Fact]
    public void DeactivateAction_UndoRestoresCompleteMethodWhenHistoryWasBehind()
    {
        var serializer = new WorkflowEditorJsonSerializer();
        var method = new WorkflowMethod { Name = "Main" };
        method.MethodLines.Add(MethodLine.Create(10, 0, WorkflowAction.Create("log")));
        method.MethodLines.Add(MethodLine.Create(20, 0, WorkflowAction.Create("delay")));
        var emptySavedMethod = new WorkflowMethod { Uid = method.Uid, Name = method.Name };
        var state = WorkflowDocumentEditState.CreateSaved(serializer.SerializeDocument(
            WorkflowEditorDocument.FromMethod(emptySavedMethod)));
        var line = method.MethodLines[0];
        string Snapshot() => serializer.SerializeDocument(WorkflowEditorDocument.FromMethod(method));
        var property = ActionPropertyItem.CreateDeactivate(
            line,
            () => state.Observe(Snapshot()),
            () => state.Observe(Snapshot()));

        property.BooleanValue = true;
        var undoSnapshot = state.Undo();
        var restored = serializer.DeserializeDocument(undoSnapshot!).Method!;

        Assert.Equal(2, restored.MethodLines.Count);
        Assert.True(restored.MethodLines[0].IsActive);
        Assert.True(restored.MethodLines[0].Action!.IsActive);
    }
}
