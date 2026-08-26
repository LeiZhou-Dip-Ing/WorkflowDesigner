namespace WorkflowCore.WpfDemo.Models;

public sealed record ActionMoveRequest(Guid SourceLineUid, Guid? InsertBeforeLineUid);
