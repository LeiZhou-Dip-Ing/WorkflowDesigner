namespace WorkflowCore.WpfDemo.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task StepAsync()
    {
        try
        {
            IsDebugPaused = false;
            StatusText = "Running next action...";
            await _runSession.StepAsync();
        }
        catch (Exception exception)
        {
            IsDebugPaused = true;
            StatusText = $"Step failed: {exception.Message}";
        }
    }

    private async Task StepOverAsync()
    {
        try
        {
            IsDebugPaused = false;
            StatusText = "Running current action without entering child methods...";
            await _runSession.StepOverAsync();
        }
        catch (Exception exception)
        {
            IsDebugPaused = true;
            StatusText = $"Step over failed: {exception.Message}";
        }
    }

    private async Task ContinueAsync()
    {
        try
        {
            IsDebugPaused = false;
            StatusText = "Continuing workflow...";
            await _runSession.ContinueAsync();
        }
        catch (Exception exception)
        {
            IsDebugPaused = true;
            StatusText = $"Continue failed: {exception.Message}";
        }
    }

    private async Task PauseAsync()
    {
        try
        {
            await _runSession.PauseAsync();
            StatusText = "Pause requested; Runtime will stop after the current action.";
        }
        catch (Exception exception)
        {
            StatusText = $"Pause failed: {exception.Message}";
        }
    }

    private void SetDebugLocation(string? methodName, int? lineNumber, Guid? lineUid)
    {
        if (!string.IsNullOrWhiteSpace(methodName))
        {
            var method = Project.Methods.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, methodName, StringComparison.OrdinalIgnoreCase));
            if (method != null)
            {
                OpenMethod(method);
            }
        }

        foreach (var item in _allMethodLineItems)
        {
            item.IsDebugCurrent = lineUid.HasValue
                ? item.Line.Uid == lineUid.Value
                : string.Equals(SelectedMethod?.Name, methodName, StringComparison.Ordinal)
                  && item.DisplayIndex == lineNumber;
        }

        var current = _allMethodLineItems.FirstOrDefault(item => item.IsDebugCurrent);
        if (current != null) SelectedMethodLineItem = current;
    }

    private void ClearDebugLocation()
    {
        IsDebugPaused = false;
        foreach (var item in _allMethodLineItems) item.IsDebugCurrent = false;
    }
}
