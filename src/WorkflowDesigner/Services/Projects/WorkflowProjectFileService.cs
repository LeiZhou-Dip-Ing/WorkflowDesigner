using System.IO;
using System.Text.Json;
using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Models;

namespace WorkflowCore.WpfDemo.Services.Projects;

public sealed class WorkflowProjectFileService : IWorkflowProjectFileService
{
    private readonly IEditorDocumentPersistence _persistence;

    public WorkflowProjectFileService(IEditorDocumentPersistence persistence)
    {
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
    }

    public OpenedWorkflowProject Create(string filePath)
    {
        var fullPath = ProjectPathIdentity.Normalize(filePath);
        if (File.Exists(fullPath))
        {
            throw new IOException($"A project already exists at '{fullPath}'.");
        }

        var projectName = Path.GetFileNameWithoutExtension(fullPath).Trim();
        if (string.IsNullOrWhiteSpace(projectName))
        {
            throw new InvalidOperationException("The project file must have a name.");
        }

        var project = new WorkflowProject { Name = projectName, Version = "1.0" };
        Save(fullPath, project);
        return new OpenedWorkflowProject(fullPath, project);
    }

    public OpenedWorkflowProject Open(string filePath)
    {
        var fullPath = ProjectPathIdentity.Normalize(filePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The workflow project file no longer exists.", fullPath);
        }

        try
        {
            var project = _persistence.Import(fullPath);
            if (project.ProjectIdWasGenerated)
            {
                Save(fullPath, project);
                project.ProjectIdWasGenerated = false;
            }

            return new OpenedWorkflowProject(fullPath, project);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The selected file is not a valid current workflow project.", exception);
        }
    }

    public void Save(string filePath, WorkflowProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        AtomicFileWriter.WriteAllText(ProjectPathIdentity.Normalize(filePath), _persistence.Serialize(project));
    }
}
