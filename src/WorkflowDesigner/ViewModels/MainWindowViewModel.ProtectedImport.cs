namespace WorkflowCore.WpfDemo.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly Services.Runtime.IProtectedWorkflowImportService? _protectedWorkflowImporter;

    private async Task ImportProtectedProjectAsync(string filePath)
    {
        if (!IsRuntimeOnline)
        {
            StatusText = "Protected workflows require an online Workflow Runtime.";
            return;
        }

        var importer = _protectedWorkflowImporter
            ?? throw new InvalidOperationException("Protected workflow import is not configured.");
        StatusText = "Deploying protected workflow...";
        var result = await importer.ImportAndShowAsync(filePath);
        StatusText = $"Protected workflow '{result.WorkflowId}' deployed at revision {result.Revision}; opened read-only presentation.";
    }
}
