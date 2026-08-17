using System.Diagnostics;
using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Services.Runtime;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.ViewModels;

/// <summary>Coordinates Project and script publication with the Runtime Action Catalog.</summary>
public sealed partial class MainWindowViewModel
{
    public string DeploymentStatusText
    {
        get
        {
            if (!IsRuntimeOnline)
            {
                return "Runtime offline";
            }

            if (!_session.RuntimeProjectId.HasValue)
            {
                return "No active Runtime Project";
            }

            if (!IsCurrentProjectActive)
            {
                return "Different Runtime Project - complete deploy only";
            }

            if (_session.RuntimeProjectJson == null)
            {
                return $"Same Project - Runtime r{_session.RuntimeRevision}";
            }

            if (HasUnsavedLocalChanges && HasUndeployedSavedChanges)
            {
                return $"Unsaved + not deployed - Runtime r{_session.RuntimeRevision}";
            }

            if (HasUnsavedLocalChanges)
            {
                return $"Unsaved local changes - Runtime r{_session.RuntimeRevision}";
            }

            return HasUndeployedSavedChanges
                ? $"Saved locally - not deployed - Runtime r{_session.RuntimeRevision}"
                : $"Synchronized - Runtime r{_session.RuntimeRevision}";
        }
    }

    internal async Task RefreshActionToolboxAfterScriptPublicationAsync(
        SharpScriptPublishResponse publication,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publication);
        var catalog = await _actionCatalog.RefreshAsync(cancellationToken);
        var publishedActionIsVisible = catalog.Actions.Any(descriptor =>
            string.Equals(descriptor.ActionId, publication.ActionId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(descriptor.ActionType, publication.ActionType, StringComparison.OrdinalIgnoreCase));
        if (!publishedActionIsVisible)
        {
            throw new InvalidOperationException(
                $"Runtime published script '{publication.ScriptUid:D}', but its Action "
                + $"'{publication.ActionId}' is missing from the Runtime Action Catalog.");
        }

        await _uiDispatcher.InvokeAsync(() =>
        {
            _projectActionCatalog.BindProject(Project, runtimeCatalogBelongsToProject: true);
            ReplaceActionToolbox();
            ApplyCatalogCheck(_runtimeSync.CheckActionsAgainstCatalog(Project));
            RefreshSelectedMethodLines(keepSelection: true);
            RefreshActionProperties();
        });
    }

    internal async Task RefreshActionToolboxAfterProjectPublicationAsync(
        IEnumerable<WorkflowScript> publishedScripts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publishedScripts);
        var scripts = publishedScripts.ToArray();
        var catalog = await _actionCatalog.RefreshAsync(cancellationToken);
        var missingScripts = scripts.Where(script => !catalog.Actions.Any(descriptor =>
                string.Equals(
                    descriptor.ActionId,
                    $"csharp-script:{script.Uid:D}",
                    StringComparison.OrdinalIgnoreCase)))
            .Select(script => script.Name)
            .ToArray();
        if (missingScripts.Length > 0)
        {
            throw new InvalidOperationException(
                "Runtime published the Project, but the following CSharp script Actions are missing "
                + $"from its Action Catalog: {string.Join(", ", missingScripts)}.");
        }

        await _uiDispatcher.InvokeAsync(() =>
        {
            _projectActionCatalog.BindProject(Project, runtimeCatalogBelongsToProject: true);
            ReplaceActionToolbox();
            ApplyCatalogCheck(_runtimeSync.CheckActionsAgainstCatalog(Project));
            RefreshSelectedMethodLines(keepSelection: true);
            RefreshActionProperties();
        });
    }

    private async Task DeployWorkflowAsync()
    {
        ObserveDocumentChanges();
        var unsavedDocuments = GetUnsavedDocumentNames();
        _isDeploymentOperationRunning = true;
        WorkflowDeployResult? deploymentResult = null;
        RaiseCommandStates();
        try
        {
            await SetRuntimeSynchronizationStateAsync(true, "Validating and deploying saved local Project...");
            var result = await _deployment.DeployAsync(unsavedDocuments);
            deploymentResult = result;
            if (result.RuntimeMatchesSavedProject)
            {
                await RefreshActionToolboxAfterProjectPublicationAsync(Project.Scripts);
            }

            UpdateDeploymentState();
            StatusText = result.Message;
        }
        catch (RuntimeDeploymentVerificationException exception)
        {
            Debug.WriteLine(exception);
            StatusText = exception.Message;
            _dialogs.ShowError("Project deployment verification failed", exception.Message);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            StatusText = deploymentResult?.RuntimeMatchesSavedProject == true
                ? $"Runtime Project is current, but the editor could not refresh its Actions: {exception.Message}"
                : $"Deploy failed; Runtime kept its previous revision: {exception.Message}";
            _dialogs.ShowError("Project deployment failed", StatusText);
        }
        finally
        {
            _isDeploymentOperationRunning = false;
            await SetRuntimeSynchronizationStateAsync(false, string.Empty);
            RaiseCommandStates();
        }
    }
}
