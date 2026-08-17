using WorkflowCore.WpfDemo.Editor;
using WorkflowRuntime.Contracts;
using WorkflowRuntime.ScriptCompiler;
using System.Windows.Media;

namespace WorkflowCore.WpfDemo.Services.Projects;

/// <summary>
/// Presents the Actions available to one local Project without leaking script Actions
/// from a different Project currently active in Runtime.
/// </summary>
public sealed class ProjectActionCatalog : IEditorActionCatalog
{
    private const string CSharpScriptSourceKind = "CSharpScript";
    private const string CSharpScriptActionPrefix = "csharp-script:";
    private readonly IEditorActionCatalog _runtimeCatalog;
    private readonly ISharpScriptCompiler _scriptCompiler;
    private readonly Dictionary<Guid, LocalScriptDescriptorCacheEntry> _localScriptDescriptors = new();
    private WorkflowProject? _project;
    private bool _runtimeCatalogBelongsToProject;

    public ProjectActionCatalog(
        IEditorActionCatalog runtimeCatalog,
        ISharpScriptCompiler? scriptCompiler = null)
    {
        _runtimeCatalog = runtimeCatalog ?? throw new ArgumentNullException(nameof(runtimeCatalog));
        _scriptCompiler = scriptCompiler ?? new SharpScriptCompiler();
    }

    public ActionCatalogResponse Current => BuildCurrentCatalog();

    public void BindProject(WorkflowProject project, bool runtimeCatalogBelongsToProject)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _runtimeCatalogBelongsToProject = runtimeCatalogBelongsToProject;
        var activeScriptIds = project.Scripts.Select(script => script.Uid).ToHashSet();
        foreach (var scriptId in _localScriptDescriptors.Keys.Where(id => !activeScriptIds.Contains(id)).ToArray())
        {
            _localScriptDescriptors.Remove(scriptId);
        }
    }

    public string? GetCachedIconUri(ActionAssetReferenceDto? icon)
        => _runtimeCatalog.GetCachedIconUri(icon);

    public ImageSource? GetCachedIconImage(ActionAssetReferenceDto? icon)
        => _runtimeCatalog.GetCachedIconImage(icon);

    public async Task<ActionCatalogResponse> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _runtimeCatalog.RefreshAsync(cancellationToken).ConfigureAwait(false);
        return Current;
    }

    public async Task<bool> ApplyChangeAsync(
        ActionCatalogChangedDto change,
        CancellationToken cancellationToken = default)
    {
        return await _runtimeCatalog.ApplyChangeAsync(change, cancellationToken).ConfigureAwait(false);
    }

    private ActionCatalogResponse BuildCurrentCatalog()
    {
        var runtimeCatalog = _runtimeCatalog.Current;
        var actions = runtimeCatalog.Actions
            .Where(descriptor => !IsCSharpScriptAction(descriptor))
            .ToList();

        if (_project != null)
        {
            actions.AddRange(_project.Scripts.Select(script => CreateProjectScriptDescriptor(script, runtimeCatalog)));
        }

        return new ActionCatalogResponse
        {
            SchemaVersion = runtimeCatalog.SchemaVersion,
            CatalogVersion = runtimeCatalog.CatalogVersion,
            Actions = actions
        };
    }

    private WorkflowActionDescriptorDto CreateProjectScriptDescriptor(
        WorkflowScript script,
        ActionCatalogResponse runtimeCatalog)
    {
        if (_runtimeCatalogBelongsToProject)
        {
            var publishedDescriptor = runtimeCatalog.Actions.FirstOrDefault(descriptor =>
                IsDescriptorForScript(descriptor, script.Uid));
            if (publishedDescriptor != null)
            {
                return publishedDescriptor;
            }
        }

        if (_localScriptDescriptors.TryGetValue(script.Uid, out var cached)
            && string.Equals(cached.Source, script.Content, StringComparison.Ordinal))
        {
            return cached.Descriptor;
        }

        var descriptor = CreateLocalScriptDescriptor(script);
        _localScriptDescriptors[script.Uid] = new LocalScriptDescriptorCacheEntry(script.Content, descriptor);
        return descriptor;
    }

    private WorkflowActionDescriptorDto CreateLocalScriptDescriptor(WorkflowScript script)
    {
        SharpScriptContract? contract = null;
        try
        {
            contract = _scriptCompiler.Analyze(new SharpScriptCompilationRequest
            {
                Source = script.Content,
                FileName = script.DisplayFileName,
                AssemblyName = $"WorkflowSharpScript_{script.Uid:N}"
            }).Contract;
        }
        catch
        {
            // An unavailable external reference must not prevent the local Project from opening.
            // Existing Action values are still projected by the property editor and Canvas fallback.
        }

        return new WorkflowActionDescriptorDto
        {
            ActionId = GetScriptActionId(script.Uid),
            ActionType = GetScriptActionType(script.Uid),
            DisplayName = script.Name,
            Category = "CSharp Scripts",
            Description = $"Run CSharp script '{script.Name}' from the current Project.",
            DisplayTemplate = script.Name,
            SourceKind = CSharpScriptSourceKind,
            SourceId = script.Uid.ToString("D"),
            Inputs = contract?.Inputs.Select(MapInput).ToArray()
                     ?? Array.Empty<WorkflowActionFieldDto>(),
            Outputs = contract?.Outputs.Select(MapOutput).ToArray()
                      ?? Array.Empty<WorkflowActionFieldDto>()
        };
    }

    private static WorkflowActionFieldDto MapInput(SharpScriptFieldContract field)
        => new()
        {
            Name = field.Name,
            DisplayName = field.DisplayName,
            Description = field.Description,
            Order = field.Order,
            ValueType = field.ValueType,
            Direction = "input",
            Category = string.IsNullOrWhiteSpace(field.Group) ? "Inputs" : field.Group,
            Required = field.Required,
            IsReadOnly = false,
            DefaultValue = field.DefaultValue?.DeepClone(),
            Editor = GetEditor(field),
            EditorOptions = string.IsNullOrWhiteSpace(field.Placeholder)
                ? null
                : new WorkflowActionEditorOptionsDto
                {
                    Placeholder = field.Placeholder,
                    AllowCustomValue = true,
                    AllowClear = !field.Required
                },
            Minimum = field.Minimum,
            Maximum = field.Maximum,
            Step = field.Step,
            EnumValues = field.EnumValues,
            SupportsVariableExpression = true,
            SupportsOutputBinding = false
        };

    private static WorkflowActionFieldDto MapOutput(SharpScriptFieldContract field)
        => new()
        {
            Name = field.Name,
            DisplayName = field.DisplayName,
            Description = field.Description,
            Order = field.Order,
            ValueType = field.ValueType,
            Direction = "output",
            Category = string.IsNullOrWhiteSpace(field.Group) ? "Outputs" : field.Group,
            Required = false,
            IsReadOnly = true,
            Editor = "variable",
            EditorOptions = new WorkflowActionEditorOptionsDto
            {
                DataSource = "methodVariables",
                AllowCustomValue = true,
                AllowCreate = true,
                AllowClear = true,
                CreateKind = $"variable:{field.ValueType}",
                Placeholder = "Select or create an output variable"
            },
            Minimum = field.Minimum,
            Maximum = field.Maximum,
            Step = field.Step,
            EnumValues = field.EnumValues,
            SupportsVariableExpression = false,
            SupportsOutputBinding = true
        };

    private static string GetEditor(SharpScriptFieldContract field)
    {
        if (!string.IsNullOrWhiteSpace(field.EditorHint))
        {
            return field.EditorHint;
        }

        if (field.EnumValues.Count > 0)
        {
            return "select";
        }

        return field.ValueType switch
        {
            "integer" or "number" => "number",
            "boolean" => "checkbox",
            "array" or "object" => "json",
            _ => "text"
        };
    }

    private static bool IsDescriptorForScript(WorkflowActionDescriptorDto descriptor, Guid scriptUid)
        => string.Equals(descriptor.SourceId, scriptUid.ToString("D"), StringComparison.OrdinalIgnoreCase)
           || string.Equals(descriptor.ActionId, GetScriptActionId(scriptUid), StringComparison.OrdinalIgnoreCase)
           || string.Equals(descriptor.ActionType, GetScriptActionType(scriptUid), StringComparison.OrdinalIgnoreCase);

    private static bool IsCSharpScriptAction(WorkflowActionDescriptorDto descriptor)
        => string.Equals(descriptor.SourceKind, CSharpScriptSourceKind, StringComparison.OrdinalIgnoreCase)
           || descriptor.ActionId.StartsWith(CSharpScriptActionPrefix, StringComparison.OrdinalIgnoreCase)
           || descriptor.ActionType.StartsWith(CSharpScriptActionPrefix, StringComparison.OrdinalIgnoreCase);

    private static string GetScriptActionId(Guid scriptUid) => $"{CSharpScriptActionPrefix}{scriptUid:D}";

    private static string GetScriptActionType(Guid scriptUid) => $"{CSharpScriptActionPrefix}{scriptUid:N}";

    private sealed record LocalScriptDescriptorCacheEntry(
        string Source,
        WorkflowActionDescriptorDto Descriptor);
}
