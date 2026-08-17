using System.Diagnostics;
using WorkflowCore.WpfDemo.Editor;

namespace WorkflowCore.WpfDemo.ViewModels;

/// <summary>Coordinates script-specific shell commands; compilation and execution remain in scripting services.</summary>
public sealed partial class MainWindowViewModel
{
    public void MarkDocumentChanged(WorkflowScript script)
    {
        if (Project.Scripts.Contains(script)) RefreshJsonPreview();
    }

    private void ManageScriptLibraries()
    {
        if (_scriptLibraryManagerDialog == null)
        {
            _dialogs.ShowWarning("Manage Script Libraries", "The Script Library manager is not available in this editor session.");
            return;
        }

        if (_scriptLibraryManagerDialog.Show(Project)) NotifyScriptLibrariesChanged();
    }

    internal void NotifyScriptLibrariesChanged()
    {
        foreach (var editor in OpenedEditors.Select(item => item.Content).OfType<CSharpScriptEditorViewModel>())
        {
            editor.RefreshScriptLibraries();
        }

        RefreshJsonPreview();
        _documents.UpdateOpenDocumentStates();
        StatusText = "Project Script Libraries updated. Scripts are being analyzed with the selected references.";
    }

    private void CreateScriptFromDialog()
    {
        var name = NewMethodName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            CreateMethodError = "Script name is required.";
            return;
        }

        if (Project.Scripts.Any(script => string.Equals(script.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            CreateMethodError = $"CSharp Script '{name}' already exists.";
            return;
        }

        var script = new WorkflowScript
        {
            Name = name,
            Language = "CSharp",
            Content = _scriptTemplateFactory.Create(name)
        };

        Project.Scripts.Add(script);
        Scripts.Add(script);
        RefreshProjectScriptActions();
        OpenScript(script);
        CloseCreateMethodDialog();
        RefreshJsonPreview();
        StatusText = $"Created CSharp Script '{name}'.";
    }

    private void DeleteScript(object? parameter)
    {
        if (parameter is not WorkflowScript script
            || !_dialogs.Confirm(
                "Delete CSharp script",
                $"Delete local CSharp script '{script.Name}'? Runtime is not changed until the complete Project is deployed."))
        {
            return;
        }

        CloseScriptEditor(script);
        Project.Scripts.Remove(script);
        Scripts.Remove(script);
        RefreshProjectScriptActions();
        RefreshJsonPreview();
        StatusText = $"Deleted local CSharp script '{script.Name}'. Runtime was not changed.";
    }

    private void RefreshProjectScriptActions()
    {
        _projectActionCatalog.BindProject(Project, IsCurrentProjectActive);
        ReplaceActionToolbox();
        var catalogCheck = _runtimeSync.CheckActionsAgainstCatalog(Project);
        ApplyCatalogCheck(catalogCheck);
        RefreshSelectedMethodLines(keepSelection: true);
        RefreshActionProperties();
    }

    internal async Task CompareDocumentAsync(IEditableDockDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!IsRuntimeOnline || _isDeploymentOperationRunning)
        {
            return;
        }

        _isDeploymentOperationRunning = true;
        RaiseCommandStates();
        try
        {
            var comparison = await _deployment.CompareDocumentAsync(
                document.CreateExportDocument(),
                IsDocumentDirty(document.ContentId));
            ShowDeploymentComparison(comparison);
            StatusText = comparison.Summary;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            StatusText = $"Could not compare the document with Runtime: {exception.Message}";
        }
        finally
        {
            _isDeploymentOperationRunning = false;
            RaiseCommandStates();
        }
    }
}
