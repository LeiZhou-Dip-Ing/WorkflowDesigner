using System.IO;
using System.Text.Json.Nodes;
using WorkflowCore.WpfDemo.Editor;

namespace WorkflowCore.WpfDemo.Services;

public sealed class JsonEditorDocumentPersistence : IEditorDocumentPersistence
{
    private readonly WorkflowEditorJsonSerializer _serializer;

    public JsonEditorDocumentPersistence(WorkflowEditorJsonSerializer serializer)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    public WorkflowProject Import(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var json = File.ReadAllText(filePath);
        return Deserialize(json);
    }

    public void Export(WorkflowProject project, string filePath)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        File.WriteAllText(filePath, Serialize(project));
    }

    public WorkflowEditorDocument ImportDocument(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var json = File.ReadAllText(filePath);
        return DeserializeDocument(json);
    }

    public bool TryImportDocument(string filePath, out WorkflowEditorDocument? document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var json = File.ReadAllText(filePath);
        if (JsonNode.Parse(json) is not JsonObject root || !root.ContainsKey("documentType"))
        {
            document = null;
            return false;
        }

        document = DeserializeDocument(json);
        return true;
    }

    public void ExportDocument(WorkflowEditorDocument document, string filePath)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        File.WriteAllText(filePath, SerializeDocument(document));
    }

    public string Serialize(WorkflowProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return _serializer.Serialize(project);
    }

    public WorkflowProject Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        return _serializer.Deserialize(json);
    }

    public string SerializeDocument(WorkflowEditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return _serializer.SerializeDocument(document);
    }

    public WorkflowEditorDocument DeserializeDocument(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        return _serializer.DeserializeDocument(json);
    }
}
