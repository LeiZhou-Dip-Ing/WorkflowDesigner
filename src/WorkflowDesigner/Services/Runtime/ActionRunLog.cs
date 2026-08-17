using System.Collections.ObjectModel;
using WorkflowCore.WpfDemo.Models;
using WorkflowCore.WpfDemo.Services.Editing;
using WorkflowCore.WpfDemo.Services.Ui;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.Services.Runtime;

/// <summary>Projects backend runtime events into the append-only Action log and variable view.</summary>
public sealed class ActionRunLog : IDisposable
{
    private readonly IEditorActionCatalog pcatalog;
    private readonly IActionPropertyEditor ppropertyEditorService;
    private readonly IUiTimer pcountdownTimer;
    private readonly Dictionary<Guid, RuntimeEventItem> prunningActions = new();
    private int pactionExecutionCount;
    private bool pcurrentRunHasActionFailure;
    private bool prunCompletionAdded;
    private bool pdisposed;

    public ActionRunLog(
        IEditorActionCatalog catalog,
        IActionPropertyEditor propertyEditor,
        IUiTimerFactory timerFactory)
    {
        pcatalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        ppropertyEditorService = propertyEditor ?? throw new ArgumentNullException(nameof(propertyEditor));
        ArgumentNullException.ThrowIfNull(timerFactory);
        pcountdownTimer = timerFactory.Create(TimeSpan.FromMilliseconds(100));
        pcountdownTimer.Tick += CountdownTimerOnTick;
    }

    public ObservableCollection<RuntimeEventItem> Events { get; } = new();
    public ObservableCollection<VariableItem> Variables { get; } = new();

    public void Apply(WorkflowRuntimeEventDto runtimeEvent)
    {
        ArgumentNullException.ThrowIfNull(runtimeEvent);
        UpdateVariable(runtimeEvent.Message);

        if (string.Equals(runtimeEvent.EventType, "ActionStarted", StringComparison.Ordinal))
        {
            BeginAction(runtimeEvent);
        }
        else if (string.Equals(runtimeEvent.EventType, "ActionCompleted", StringComparison.Ordinal))
        {
            FinishAction(runtimeEvent, failed: false);
        }
        else if (string.Equals(runtimeEvent.EventType, "ActionFailed", StringComparison.Ordinal))
        {
            pcurrentRunHasActionFailure = true;
            FinishAction(runtimeEvent, failed: true);
        }
        else if (string.Equals(runtimeEvent.EventType, "RunCompleted", StringComparison.Ordinal))
        {
            CompleteRun(runtimeEvent);
        }
        else
        {
            CaptureActionOutput(runtimeEvent);
        }
    }

    public void Clear()
    {
        pcountdownTimer.Stop();
        Events.Clear();
        prunningActions.Clear();
        pactionExecutionCount = 0;
        pcurrentRunHasActionFailure = false;
        prunCompletionAdded = false;
    }

    public void ResetRunningActions()
    {
        pcountdownTimer.Stop();
        prunningActions.Clear();
        pcurrentRunHasActionFailure = false;
    }

    public void AddRunFailure(string methodName, string message)
    {
        // ActionFailed already carries the precise Action, line and error. The synthetic
        // Run row is only useful for failures that happen before an Action can start.
        if (pcurrentRunHasActionFailure)
        {
            return;
        }

        var timestamp = DateTimeOffset.Now;
        var item = new RuntimeEventItem
        {
            ActionExecutionId = Guid.NewGuid(),
            ActionName = "Run",
            MethodName = methodName
        };
        item.Start(timestamp);
        item.Fail(timestamp, message);
        Events.Add(item);
    }

    public void Dispose()
    {
        if (pdisposed)
        {
            return;
        }

        pdisposed = true;
        pcountdownTimer.Tick -= CountdownTimerOnTick;
        pcountdownTimer.Dispose();
    }

    private void BeginAction(WorkflowRuntimeEventDto runtimeEvent)
    {
        var actionType = runtimeEvent.ActionType ?? "Action";
        var descriptor = ppropertyEditorService.FindDescriptor(actionType);
        var iconImage = descriptor == null ? null : pcatalog.GetCachedIconImage(descriptor.Icon);
        var actionTemplate = new ActionTemplateItem
        {
            ActionId = descriptor?.ActionId,
            ActionType = actionType,
            DisplayName = descriptor?.DisplayName ?? actionType,
            Description = descriptor?.Description ?? string.Empty,
            IconImage = iconImage
        };
        var item = new RuntimeEventItem
        {
            ActionExecutionId = runtimeEvent.ActionExecutionId ?? Guid.NewGuid(),
            ActionTemplate = actionTemplate,
            ActionName = descriptor?.DisplayName ?? actionType,
            MethodName = runtimeEvent.MethodName ?? string.Empty,
            LineNumber = runtimeEvent.LineNumber
        };
        item.Start(runtimeEvent.Timestamp, runtimeEvent.DurationMilliseconds);
        Events.Add(item);
        prunningActions[item.ActionExecutionId] = item;
        pcountdownTimer.Start();
        pactionExecutionCount++;
    }

    private void CompleteRun(WorkflowRuntimeEventDto runtimeEvent)
    {
        if (prunCompletionAdded)
        {
            return;
        }

        prunCompletionAdded = true;
        var resultType = runtimeEvent.Payload?["resultType"]?.GetValue<string>();
        var failed = !string.IsNullOrWhiteSpace(resultType)
                     && !string.Equals(resultType, "OK", StringComparison.OrdinalIgnoreCase);
        var message = $"{runtimeEvent.Message} {pactionExecutionCount} Action executions recorded.";
        var item = new RuntimeEventItem
        {
            ActionExecutionId = Guid.NewGuid(),
            ActionName = "Run complete",
            MethodName = runtimeEvent.MethodName ?? string.Empty
        };
        item.Start(runtimeEvent.Timestamp);
        if (failed)
        {
            item.Fail(runtimeEvent.Timestamp, message);
        }
        else
        {
            item.CaptureOutput(message, isExplicitOutput: true);
            item.Complete(runtimeEvent.Timestamp);
        }

        Events.Add(item);
    }

    private void FinishAction(WorkflowRuntimeEventDto runtimeEvent, bool failed)
    {
        var item = FindRunningAction(runtimeEvent);
        if (item == null)
        {
            BeginAction(runtimeEvent);
            item = Events.LastOrDefault();
        }

        if (item == null)
        {
            return;
        }

        if (failed) item.Fail(runtimeEvent.Timestamp, runtimeEvent.Message);
        else item.Complete(runtimeEvent.Timestamp);
        prunningActions.Remove(item.ActionExecutionId);
        if (prunningActions.Count == 0)
        {
            pcountdownTimer.Stop();
        }
    }

    private void CaptureActionOutput(WorkflowRuntimeEventDto runtimeEvent)
    {
        var isLineOutput = string.Equals(runtimeEvent.EventType, "LineFinished", StringComparison.Ordinal)
                           && !runtimeEvent.Message.StartsWith("Progress +", StringComparison.OrdinalIgnoreCase);
        var isVariableOutput = string.Equals(runtimeEvent.EventType, "VariableChanged", StringComparison.Ordinal)
                               && runtimeEvent.LineNumber.HasValue
                               && !runtimeEvent.Message.StartsWith("Variables refreshed:", StringComparison.OrdinalIgnoreCase);
        if (isLineOutput || isVariableOutput)
        {
            FindRunningAction(runtimeEvent)?.CaptureOutput(runtimeEvent.Message, isLineOutput);
        }
    }

    private RuntimeEventItem? FindRunningAction(WorkflowRuntimeEventDto runtimeEvent)
    {
        if (runtimeEvent.ActionExecutionId.HasValue
            && prunningActions.TryGetValue(runtimeEvent.ActionExecutionId.Value, out var exactMatch))
        {
            return exactMatch;
        }

        return Events.LastOrDefault(item =>
            item.IsRunning
            && string.Equals(item.MethodName, runtimeEvent.MethodName, StringComparison.OrdinalIgnoreCase)
            && item.LineNumber == runtimeEvent.LineNumber
            && (string.IsNullOrWhiteSpace(runtimeEvent.ActionType)
                || string.Equals(item.ActionTemplate?.ActionType, runtimeEvent.ActionType, StringComparison.OrdinalIgnoreCase)));
    }

    private void UpdateVariable(string message)
    {
        var index = message.IndexOf(" = ", StringComparison.Ordinal);
        if (index <= 0)
        {
            return;
        }

        var name = message[..index].Trim();
        var value = message[(index + 3)..].Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Contains(' '))
        {
            return;
        }

        var existing = Variables.FirstOrDefault(variable =>
            string.Equals(variable.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            Variables.Add(new VariableItem { Name = name, Value = value, Scope = "Runtime" });
            return;
        }

        existing.Value = value;
        var existingIndex = Variables.IndexOf(existing);
        Variables.RemoveAt(existingIndex);
        Variables.Insert(existingIndex, existing);
    }

    private void CountdownTimerOnTick(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.Now;
        foreach (var item in prunningActions.Values)
        {
            item.UpdateCountdown(now);
        }
    }
}
