using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.RegularExpressions;
using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Models;
using WorkflowCore.WpfDemo.Services.Scripting;
using WorkflowCore.WpfDemo.Services.Ui;
using WorkflowRuntime.Contracts;
using WorkflowRuntime.ScriptCompiler;

namespace WorkflowCore.WpfDemo.ViewModels;

/// <summary>Owns one C# script authoring document, local analysis, and isolated local test state.</summary>
public sealed class CSharpScriptEditorViewModel : ObservableObject, IEditableDockDocument, IDisposable
{
    private readonly MainWindowViewModel _owner;
    private readonly ISharpScriptCompiler _compiler;
    private readonly ISharpScriptLocalRunner _localRunner;
    private readonly ISharpScriptLibraryCache _libraryCache;
    private readonly IUiDispatcher _uiDispatcher;
    private CancellationTokenSource? _analysisCancellation;
    private CancellationTokenSource? _runCancellation;
    private bool _isDirty;
    private bool _isAnalyzing;
    private bool _isRunning;
    private bool _disposed;
    private string _statusText = "Ready";
    private SharpScriptContract? _contract;
    private SharpScriptDiagnosticItem? _selectedDiagnostic;
    private long _publishedRevision;
    private int _caretLine = 1;
    private int _caretColumn = 1;
    private SharpScriptLibraryDescriptorDto? _suggestedLibrary;
    private string _suggestedNamespace = string.Empty;

    public CSharpScriptEditorViewModel(
        WorkflowScript script,
        MainWindowViewModel owner,
        ISharpScriptCompiler compiler,
        ISharpScriptLocalRunner localRunner,
        ISharpScriptLibraryCache libraryCache,
        IUiDispatcher uiDispatcher)
    {
        Script = script ?? throw new ArgumentNullException(nameof(script));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _localRunner = localRunner ?? throw new ArgumentNullException(nameof(localRunner));
        _libraryCache = libraryCache ?? throw new ArgumentNullException(nameof(libraryCache));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));

        SaveScriptCommand = new RelayCommand(
            () => _owner.SaveWorkflowCommand.Execute(null),
            () => !IsRunning && IsDirty && _owner.SaveWorkflowCommand.CanExecute(null));
        RunScriptCommand = new RelayCommand(() => _ = RunAsync(), () => !IsRunning && !IsAnalyzing);
        DeployScriptCommand = new RelayCommand(
            () => _owner.DeploySelectedDocumentCommand.Execute(this),
            () => !IsRunning && _owner.DeploySelectedDocumentCommand.CanExecute(this));
        CompareScriptCommand = new RelayCommand(
            () => _ = _owner.CompareDocumentAsync(this),
            () => !IsRunning && _owner.IsRuntimeOnline && _owner.IsCurrentProjectActive);
        ExportScriptCommand = new RelayCommand(
            () => _owner.ExportJsonCommand.Execute(null),
            () => _owner.ExportJsonCommand.CanExecute(null));
        ResetTestValuesCommand = new RelayCommand(ResetTestValues, () => !IsRunning);
        SelectDiagnosticCommand = new RelayCommand(
            diagnostic => SelectedDiagnostic = diagnostic as SharpScriptDiagnosticItem);
        AddSuggestedLibraryCommand = new RelayCommand(
            () => _ = AddSuggestedLibraryAsync(),
            () => _suggestedLibrary != null && !IsRunning && !IsAnalyzing);

        Script.PropertyChanged += ScriptOnPropertyChanged;
        _owner.PropertyChanged += OwnerOnPropertyChanged;
        ScheduleAnalysis(immediate: true);
    }

    public WorkflowScript Script { get; }

    public MainWindowViewModel Owner => _owner;

    public string ContentId => $"script:{Script.Uid:N}";

    public string Title => IsDirty ? $"{DisplayFileName} *" : DisplayFileName;

    public string DisplayFileName
        => Script.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
           || Script.Name.EndsWith(".csx", StringComparison.OrdinalIgnoreCase)
            ? Script.Name
            : Script.Name + ".csx";

    public string Content
    {
        get => Script.Content;
        set
        {
            if (!string.Equals(Script.Content, value, StringComparison.Ordinal))
            {
                Script.Content = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (SetProperty(ref _isDirty, value))
            {
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(PublicationStatus));
                RaiseCommandStates();
            }
        }
    }

    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        private set
        {
            if (SetProperty(ref _isAnalyzing, value)) RaiseCommandStates();
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value)) RaiseCommandStates();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string BuildStatus => ErrorCount > 0 ? "Build failed" : IsAnalyzing ? "Analyzing" : "No issues found";

    public string RuntimeStatus => _owner.IsRuntimeOnline ? "Runtime: Online" : "Runtime: Offline";

    public string PublicationStatus
        => _publishedRevision == 0
            ? "Not published"
            : IsDirty ? "Local changes" : $"Published revision {_publishedRevision}";

    public int ErrorCount => Diagnostics.Count(item => item.Severity == SharpScriptDiagnosticSeverity.Error);

    public int WarningCount => Diagnostics.Count(item => item.Severity == SharpScriptDiagnosticSeverity.Warning);

    public int MessageCount => Diagnostics.Count(item => item.Severity == SharpScriptDiagnosticSeverity.Message);

    public bool HasMissingNamespaceSuggestion => _suggestedLibrary != null;

    public string MissingNamespaceSuggestionText => _suggestedLibrary == null
        ? string.Empty
        : $"Namespace '{_suggestedNamespace}' is provided by {_suggestedLibrary.DisplayName} {_suggestedLibrary.Version}.";

    public int CaretLine
    {
        get => _caretLine;
        set => SetProperty(ref _caretLine, Math.Max(1, value));
    }

    public int CaretColumn
    {
        get => _caretColumn;
        set => SetProperty(ref _caretColumn, Math.Max(1, value));
    }

    public SharpScriptDiagnosticItem? SelectedDiagnostic
    {
        get => _selectedDiagnostic;
        private set => SetProperty(ref _selectedDiagnostic, value);
    }

    public ObservableCollection<SharpScriptDiagnosticItem> Diagnostics { get; } = new();

    public ObservableCollection<SharpScriptDiagnosticItem> ErrorDiagnostics { get; } = new();

    public ObservableCollection<SharpScriptDiagnosticItem> WarningDiagnostics { get; } = new();

    public ObservableCollection<SharpScriptDiagnosticItem> MessageDiagnostics { get; } = new();

    public ObservableCollection<SharpScriptTestValue> Inputs { get; } = new();

    public ObservableCollection<SharpScriptTestValue> Outputs { get; } = new();

    public RelayCommand SaveScriptCommand { get; }

    public RelayCommand RunScriptCommand { get; }

    public RelayCommand DeployScriptCommand { get; }

    public RelayCommand CompareScriptCommand { get; }

    public RelayCommand ExportScriptCommand { get; }

    public RelayCommand ResetTestValuesCommand { get; }

    public RelayCommand SelectDiagnosticCommand { get; }

    public RelayCommand AddSuggestedLibraryCommand { get; }

    public WorkflowEditorDocument CreateExportDocument() => WorkflowEditorDocument.FromScript(Script);

    internal void ApplyPublication(SharpScriptPublishResponse publication)
    {
        if (!publication.Succeeded)
        {
            SetDiagnostics(publication.Diagnostics.Select(item => new SharpScriptDiagnostic
            {
                Severity = Enum.TryParse<SharpScriptDiagnosticSeverity>(item.Severity, true, out var severity)
                    ? severity
                    : SharpScriptDiagnosticSeverity.Message,
                Code = item.Code,
                Message = item.Message,
                FileName = item.FileName,
                Line = item.Line,
                Column = item.Column
            }));
            StatusText = "Runtime compilation failed; the active revision was kept.";
            return;
        }

        _publishedRevision = publication.ScriptRevision;
        OnPropertyChanged(nameof(PublicationStatus));
        StatusText = $"Published revision {_publishedRevision}.";
    }

    internal void RefreshScriptLibraries()
    {
        _localRunner.Retire(Script.Uid);
        ScheduleAnalysis(immediate: true);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Script.PropertyChanged -= ScriptOnPropertyChanged;
        _owner.PropertyChanged -= OwnerOnPropertyChanged;
        _analysisCancellation?.Cancel();
        _analysisCancellation?.Dispose();
        _runCancellation?.Cancel();
        _runCancellation?.Dispose();
        _localRunner.Retire(Script.Uid);
    }

    private void ScriptOnPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(WorkflowScript.Name))
        {
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(DisplayFileName));
        }

        if (args.PropertyName == nameof(WorkflowScript.Content))
        {
            OnPropertyChanged(nameof(Content));
            ScheduleAnalysis(immediate: false);
        }

        _owner.MarkDocumentChanged(Script);
    }

    private void OwnerOnPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainWindowViewModel.IsRuntimeOnline))
        {
            OnPropertyChanged(nameof(RuntimeStatus));
            RaiseCommandStates();
        }
    }

    private void ScheduleAnalysis(bool immediate)
    {
        _analysisCancellation?.Cancel();
        _analysisCancellation?.Dispose();
        _analysisCancellation = new CancellationTokenSource();
        _ = AnalyzeAfterDelayAsync(immediate, _analysisCancellation.Token);
    }

    private async Task AnalyzeAfterDelayAsync(bool immediate, CancellationToken cancellationToken)
    {
        try
        {
            if (!immediate)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
            }

            await _uiDispatcher.InvokeAsync(() => IsAnalyzing = true);
            var referencePaths = await _libraryCache.ResolveCompilationReferencesAsync(
                _owner.Project.ScriptLibraries,
                cancellationToken).ConfigureAwait(false);
            var request = new SharpScriptCompilationRequest
            {
                Source = Script.Content,
                FileName = DisplayFileName,
                ReferencePaths = referencePaths
            };
            var result = await Task.Run(() => _compiler.Analyze(request, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            await _uiDispatcher.InvokeAsync(() => ApplyAnalysis(result));
            await UpdateNamespaceSuggestionAsync(result.Diagnostics, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await _uiDispatcher.InvokeAsync(() => SetLibraryFailure(exception));
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested && !_uiDispatcher.HasShutdownStarted)
            {
                await _uiDispatcher.InvokeAsync(() => IsAnalyzing = false);
            }
        }
    }

    private void ApplyAnalysis(SharpScriptAnalysisResult result)
    {
        SetDiagnostics(result.Diagnostics);
        if (result.Contract != null)
        {
            ApplyContract(result.Contract, preserveInputValues: true);
            StatusText = $"{result.Contract.Inputs.Count} input(s), {result.Contract.Outputs.Count} output(s).";
        }
        else
        {
            _contract = null;
            Inputs.Clear();
            Outputs.Clear();
            StatusText = ErrorCount == 0 ? "No script contract found." : "Correct the script errors before running.";
        }
    }

    private async Task UpdateNamespaceSuggestionAsync(
        IReadOnlyList<SharpScriptDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        if (!diagnostics.Any(item => item.Severity == SharpScriptDiagnosticSeverity.Error
                                     && item.Code is "CS0234" or "CS0246"))
        {
            await _uiDispatcher.InvokeAsync(ClearNamespaceSuggestion);
            return;
        }

        var namespaces = Regex.Matches(
                Script.Content,
                @"(?m)^\s*using\s+(?<namespace>[A-Za-z_][A-Za-z0-9_.]*)\s*;")
            .Select(match => match.Groups["namespace"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (namespaces.Length == 0)
        {
            await _uiDispatcher.InvokeAsync(ClearNamespaceSuggestion);
            return;
        }

        var catalog = await _libraryCache.RefreshCatalogAsync(cancellationToken).ConfigureAwait(false);
        var candidate = catalog.Libraries
            .Where(library => !_owner.Project.ScriptLibraries.Any(reference =>
                string.Equals(reference.LibraryId, library.LibraryId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(reference.Version, library.Version, StringComparison.OrdinalIgnoreCase)))
            .SelectMany(library => namespaces
                .Where(namespaceName => library.Namespaces.Any(provided =>
                    string.Equals(provided, namespaceName, StringComparison.Ordinal)
                    || namespaceName.StartsWith(provided + ".", StringComparison.Ordinal)))
                .Select(namespaceName => (Library: library, Namespace: namespaceName)))
            .OrderBy(item => item.Library.DisplayName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        await _uiDispatcher.InvokeAsync(() =>
        {
            _suggestedLibrary = candidate.Library;
            _suggestedNamespace = candidate.Namespace ?? string.Empty;
            OnPropertyChanged(nameof(HasMissingNamespaceSuggestion));
            OnPropertyChanged(nameof(MissingNamespaceSuggestionText));
            AddSuggestedLibraryCommand.RaiseCanExecuteChanged();
        });
    }

    private async Task AddSuggestedLibraryAsync()
    {
        var library = _suggestedLibrary;
        if (library == null) return;
        try
        {
            var reference = new SharpScriptLibraryReferenceDto
            {
                LibraryId = library.LibraryId,
                Version = library.Version
            };
            var candidate = _owner.Project.ScriptLibraries.Concat([reference]).ToArray();
            await _libraryCache.ResolveCompilationReferencesAsync(candidate).ConfigureAwait(false);
            await _uiDispatcher.InvokeAsync(() =>
            {
                _owner.Project.ScriptLibraries.Add(reference);
                ClearNamespaceSuggestion();
                _owner.NotifyScriptLibrariesChanged();
            });
        }
        catch (Exception exception)
        {
            await _uiDispatcher.InvokeAsync(() => SetLibraryFailure(exception));
        }
    }

    private void ClearNamespaceSuggestion()
    {
        _suggestedLibrary = null;
        _suggestedNamespace = string.Empty;
        OnPropertyChanged(nameof(HasMissingNamespaceSuggestion));
        OnPropertyChanged(nameof(MissingNamespaceSuggestionText));
        AddSuggestedLibraryCommand.RaiseCanExecuteChanged();
    }

    private async Task RunAsync()
    {
        if (IsRunning) return;
        _runCancellation?.Cancel();
        _runCancellation?.Dispose();
        _runCancellation = new CancellationTokenSource();
        IsRunning = true;
        StatusText = "Running local script test...";
        try
        {
            var values = Inputs.ToDictionary(item => item.Name, item => item.ValueText, StringComparer.OrdinalIgnoreCase);
            var referencePaths = await _libraryCache.ResolveCompilationReferencesAsync(
                _owner.Project.ScriptLibraries,
                _runCancellation.Token).ConfigureAwait(false);
            var result = await _localRunner.RunAsync(
                Script.Uid,
                Script.Content,
                DisplayFileName,
                values,
                referencePaths,
                _runCancellation.Token).ConfigureAwait(false);
            await _uiDispatcher.InvokeAsync(() =>
            {
                SetDiagnostics(result.Diagnostics);
                if (result.Contract != null && !ReferenceEquals(_contract, result.Contract))
                {
                    ApplyContract(result.Contract, preserveInputValues: true);
                }

                foreach (var output in Outputs)
                {
                    output.ValueText = result.Outputs.TryGetValue(output.Name, out var value)
                        ? FormatOutput(value)
                        : string.Empty;
                }

                StatusText = result.Succeeded
                    ? result.Messages.Count == 0 ? "Local script test completed." : result.Messages[^1]
                    : "Local script test failed.";
            });
        }
        catch (OperationCanceledException)
        {
            await _uiDispatcher.InvokeAsync(() => StatusText = "Local script test cancelled.");
        }
        catch (Exception exception)
        {
            await _uiDispatcher.InvokeAsync(() => SetLibraryFailure(exception));
        }
        finally
        {
            if (!_uiDispatcher.HasShutdownStarted)
            {
                await _uiDispatcher.InvokeAsync(() => IsRunning = false);
            }
        }
    }

    private void ApplyContract(SharpScriptContract contract, bool preserveInputValues)
    {
        var previousInputs = preserveInputValues
            ? Inputs.ToDictionary(item => item.Name, item => item.ValueText, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _contract = contract;
        Inputs.Clear();
        foreach (var field in contract.Inputs)
        {
            var item = new SharpScriptTestValue(field, isOutput: false);
            if (previousInputs.TryGetValue(field.Name, out var value)) item.ValueText = value;
            Inputs.Add(item);
        }

        Outputs.Clear();
        foreach (var field in contract.Outputs) Outputs.Add(new SharpScriptTestValue(field, isOutput: true));
    }

    private void ResetTestValues()
    {
        foreach (var input in Inputs) input.Reset();
        foreach (var output in Outputs) output.Reset();
        StatusText = "Test values reset.";
    }

    private void SetDiagnostics(IEnumerable<SharpScriptDiagnostic> diagnostics)
    {
        Diagnostics.Clear();
        ErrorDiagnostics.Clear();
        WarningDiagnostics.Clear();
        MessageDiagnostics.Clear();
        foreach (var diagnostic in diagnostics)
        {
            var item = new SharpScriptDiagnosticItem
            {
                Severity = diagnostic.Severity,
                Code = diagnostic.Code,
                Description = diagnostic.Message,
                FileName = diagnostic.FileName,
                Line = diagnostic.Line,
                Column = diagnostic.Column
            };
            Diagnostics.Add(item);
            if (item.Severity == SharpScriptDiagnosticSeverity.Error) ErrorDiagnostics.Add(item);
            else if (item.Severity == SharpScriptDiagnosticSeverity.Warning) WarningDiagnostics.Add(item);
            else MessageDiagnostics.Add(item);
        }

        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(MessageCount));
        OnPropertyChanged(nameof(BuildStatus));
    }

    private void SetLibraryFailure(Exception exception)
    {
        SetDiagnostics(
        [
            new SharpScriptDiagnostic
            {
                Severity = SharpScriptDiagnosticSeverity.Error,
                Code = "WFSLIB",
                Message = exception.Message,
                FileName = DisplayFileName
            }
        ]);
        StatusText = "Resolve the Project Script Library issue before analyzing or running.";
    }

    private void RaiseCommandStates()
    {
        SaveScriptCommand.RaiseCanExecuteChanged();
        RunScriptCommand.RaiseCanExecuteChanged();
        DeployScriptCommand.RaiseCanExecuteChanged();
        CompareScriptCommand.RaiseCanExecuteChanged();
        ExportScriptCommand.RaiseCanExecuteChanged();
        ResetTestValuesCommand.RaiseCanExecuteChanged();
        AddSuggestedLibraryCommand.RaiseCanExecuteChanged();
    }

    internal void RefreshOwnerDependentCommandStates() => RaiseCommandStates();

    private static string FormatOutput(object? value)
        => value switch
        {
            null => "<null>",
            IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
}
