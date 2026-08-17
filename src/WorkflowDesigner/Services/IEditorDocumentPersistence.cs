using WorkflowCore.WpfDemo.Editor;

namespace WorkflowCore.WpfDemo.Services;

/// <summary>Serializes editor snapshots and imports or exports project, method, and script JSON files.</summary>
public interface IEditorDocumentPersistence
{
    WorkflowProject Import(string filePath);

    void Export(WorkflowProject project, string filePath);

    WorkflowEditorDocument ImportDocument(string filePath);

    bool TryImportDocument(string filePath, out WorkflowEditorDocument? document);

    void ExportDocument(WorkflowEditorDocument document, string filePath);

    string Serialize(WorkflowProject project);

    WorkflowProject Deserialize(string json);

    string SerializeDocument(WorkflowEditorDocument document);

    WorkflowEditorDocument DeserializeDocument(string json);
}
