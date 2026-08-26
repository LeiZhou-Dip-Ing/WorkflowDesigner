namespace WorkflowCore.WpfDemo.ViewModels;

internal sealed class WorkflowDocumentEditState
{
    private const int MaximumUndoDepth = 100;
    private readonly List<string> _undoSnapshots = new();
    private string? _savedSnapshot;
    private string? _editBaseline;
    private int? _editUndoStartIndex;

    private WorkflowDocumentEditState(string currentSnapshot, string? savedSnapshot)
    {
        CurrentSnapshot = currentSnapshot;
        _savedSnapshot = savedSnapshot;
    }

    public string CurrentSnapshot { get; private set; }

    public bool IsDirty => !string.Equals(CurrentSnapshot, _savedSnapshot, StringComparison.Ordinal);

    public bool CanUndo => _undoSnapshots.Count > 0;

    public bool IsUnsavedCreation => _savedSnapshot == null;

    public static WorkflowDocumentEditState CreateSaved(string snapshot)
        => new(snapshot, snapshot);

    public static WorkflowDocumentEditState CreateUnsaved(string snapshot)
        => new(snapshot, null);

    public bool Observe(string snapshot)
    {
        if (string.Equals(CurrentSnapshot, snapshot, StringComparison.Ordinal))
        {
            return false;
        }

        _undoSnapshots.Add(CurrentSnapshot);
        if (_undoSnapshots.Count > MaximumUndoDepth)
        {
            _undoSnapshots.RemoveAt(0);
        }

        CurrentSnapshot = snapshot;
        return true;
    }

    /// <summary>
    /// Updates the serialized representation without manufacturing an Undo step.
    /// Preview rendering, autosave, and dirty-state observation are not user edits.
    /// </summary>
    public void Synchronize(string snapshot)
    {
        CurrentSnapshot = snapshot;
    }

    public string? Undo()
    {
        if (_undoSnapshots.Count == 0)
        {
            return null;
        }

        var index = _undoSnapshots.Count - 1;
        CurrentSnapshot = _undoSnapshots[index];
        _undoSnapshots.RemoveAt(index);
        return CurrentSnapshot;
    }

    public void BeginEdit(string baselineSnapshot)
    {
        if (_editBaseline != null && _editUndoStartIndex.HasValue)
        {
            return;
        }

        Synchronize(baselineSnapshot);
        _editBaseline = baselineSnapshot;
        _editUndoStartIndex = _undoSnapshots.Count;
    }

    public void CompleteEdit(string snapshot)
    {
        if (_editBaseline == null || !_editUndoStartIndex.HasValue)
        {
            Observe(snapshot);
            return;
        }

        var baseline = _editBaseline;
        var undoStartIndex = _editUndoStartIndex.Value;
        _editBaseline = null;
        _editUndoStartIndex = null;

        if (_undoSnapshots.Count > undoStartIndex)
        {
            _undoSnapshots.RemoveRange(undoStartIndex, _undoSnapshots.Count - undoStartIndex);
        }

        CurrentSnapshot = snapshot;
        if (!string.Equals(baseline, snapshot, StringComparison.Ordinal))
        {
            _undoSnapshots.Add(baseline);
            if (_undoSnapshots.Count > MaximumUndoDepth)
            {
                _undoSnapshots.RemoveAt(0);
            }
        }
    }

    public void MarkSaved(string snapshot)
    {
        CurrentSnapshot = snapshot;
        _savedSnapshot = snapshot;
        _undoSnapshots.Clear();
        _editBaseline = null;
        _editUndoStartIndex = null;
    }
}
