using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Data;
using System.Net.Http;
using System.Text.Json;
using WorkflowCore.Execution;
using WorkflowCore.WpfDemo.Editor;

namespace WorkflowCore.WpfDemo.ViewModels;

public sealed class SqlScriptEditorViewModel : ObservableObject, IEditableDockDocument, IDisposable
{
    private readonly MainWindowViewModel _owner;
    private bool _isDirty;
    private bool _isBusy;
    private string _outputMessage = string.Empty;
    private bool _isParameterEntryOpen;
    private DataView? _resultRows;

    public SqlScriptEditorViewModel(WorkflowSqlScript script, MainWindowViewModel owner)
    {
        Script = script ?? throw new ArgumentNullException(nameof(script));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        SaveCommand = new RelayCommand(() => owner.SaveWorkflowCommand.Execute(null),
            () => !IsBusy && IsDirty && owner.SaveWorkflowCommand.CanExecute(null));
        DeployCommand = new RelayCommand(() =>
        {
            if (owner.DeploySelectedDocumentCommand.CanExecute(this)) owner.DeploySelectedDocumentCommand.Execute(this);
        });
        CompareCommand = new RelayCommand(() => _ = owner.CompareDocumentAsync(this));
        ExportCommand = new RelayCommand(() => owner.ExportJsonCommand.Execute(null),
            () => owner.ExportJsonCommand.CanExecute(null));
        ValidateCommand = new RelayCommand(() => _ = PreviewAsync(validateOnly: true), CanPreview);
        ExecuteCommand = new RelayCommand(PrepareExecution, CanPreview);
        ConfirmParametersCommand = new RelayCommand(() => _ = PreviewAsync(validateOnly: false), () => !IsBusy);
        CancelParametersCommand = new RelayCommand(() => IsParameterEntryOpen = false);
        ClearOutputCommand = new RelayCommand(() =>
        {
            OutputMessage = string.Empty;
            ResultRows = null;
        }, () => OutputMessage.Length > 0 || ResultRows != null);
        Script.PropertyChanged += OnScriptPropertyChanged;
    }

    private void OnScriptPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(WorkflowSqlScript.Content))
        {
            OnPropertyChanged(nameof(Content));
            ValidateCommand.RaiseCanExecuteChanged();
            ExecuteCommand.RaiseCanExecuteChanged();
            _owner.MarkSqlScriptChanged(Script);
        }
    }

    public WorkflowSqlScript Script { get; }
    public string ContentId => $"sql-script:{Script.Uid:N}";
    public string Title => IsDirty ? Script.DisplayFileName + " *" : Script.DisplayFileName;
    public string Content
    {
        get => Script.Content;
        set
        {
            if (Script.Content == value) return;
            Script.Content = value;
            OnPropertyChanged();
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
                SaveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            SaveCommand.RaiseCanExecuteChanged();
            ValidateCommand.RaiseCanExecuteChanged();
            ExecuteCommand.RaiseCanExecuteChanged();
            ConfirmParametersCommand.RaiseCanExecuteChanged();
        }
    }

    public ObservableCollection<SqlParameterInput> ParameterInputs { get; } = new();
    public bool IsParameterEntryOpen
    {
        get => _isParameterEntryOpen;
        private set => SetProperty(ref _isParameterEntryOpen, value);
    }

    public string OutputMessage
    {
        get => _outputMessage;
        private set
        {
            if (SetProperty(ref _outputMessage, value)) ClearOutputCommand.RaiseCanExecuteChanged();
        }
    }

    public DataView? ResultRows
    {
        get => _resultRows;
        private set
        {
            if (SetProperty(ref _resultRows, value)) ClearOutputCommand.RaiseCanExecuteChanged();
        }
    }

    public RelayCommand SaveCommand { get; }
    public RelayCommand DeployCommand { get; }
    public RelayCommand CompareCommand { get; }
    public RelayCommand ExportCommand { get; }
    public RelayCommand ValidateCommand { get; }
    public RelayCommand ExecuteCommand { get; }
    public RelayCommand ConfirmParametersCommand { get; }
    public RelayCommand CancelParametersCommand { get; }
    public RelayCommand ClearOutputCommand { get; }

    private bool CanPreview() => !IsBusy && !string.IsNullOrWhiteSpace(Content);

    private void PrepareExecution()
    {
        var names = SqlScriptParameters.GetNames(Content);
        if (names.Count == 0)
        {
            ParameterInputs.Clear();
            _ = PreviewAsync(validateOnly: false);
            return;
        }

        var previousValues = ParameterInputs.ToDictionary(input => input.Name, input => input.Value,
            StringComparer.OrdinalIgnoreCase);
        ParameterInputs.Clear();
        foreach (var name in names)
            ParameterInputs.Add(new SqlParameterInput(name,
                previousValues.TryGetValue(name, out var value) ? value : string.Empty));
        IsParameterEntryOpen = true;
    }

    private async Task PreviewAsync(bool validateOnly)
    {
        try
        {
            Dictionary<string, JsonElement> parameters = new(StringComparer.OrdinalIgnoreCase);
            if (!validateOnly)
            {
                foreach (var input in ParameterInputs)
                {
                    if (string.IsNullOrWhiteSpace(input.Value))
                    {
                        OutputMessage = $"Enter a value for '{input.Name}' (use null for SQL NULL).";
                        return;
                    }
                    parameters.Add(input.Name, ParseParameterValue(input.Value));
                }
            }

            IsBusy = true;
            var result = await _owner.PreviewSqlAsync(Content, parameters, validateOnly);
            if (result == null) return;
            IsParameterEntryOpen = false;
            OutputMessage = result.Message;
            ResultRows = result.Rows.Count == 0 ? null : BuildResultTable(result.Rows).DefaultView;
        }
        catch (Exception error) when (error is JsonException or FormatException or HttpRequestException
                                      or InvalidOperationException or NotSupportedException or TaskCanceledException)
        {
            OutputMessage = error.Message;
            ResultRows = null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static JsonElement ParseParameterValue(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Equals("null", StringComparison.OrdinalIgnoreCase)) return JsonSerializer.SerializeToElement<object?>(null);
        if (bool.TryParse(trimmed, out var boolean)) return JsonSerializer.SerializeToElement(boolean);
        if (long.TryParse(trimmed, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var integer))
            return JsonSerializer.SerializeToElement(integer);
        if (double.TryParse(trimmed, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var number))
            return JsonSerializer.SerializeToElement(number);
        return JsonSerializer.SerializeToElement(text);
    }

    private static DataTable BuildResultTable(IReadOnlyList<Dictionary<string, string?>> rows)
    {
        var table = new DataTable();
        foreach (var column in rows[0].Keys) table.Columns.Add(column);
        foreach (var row in rows)
        {
            var values = table.Columns.Cast<DataColumn>()
                .Select(column => row.TryGetValue(column.ColumnName, out var value) ? (object?)value ?? DBNull.Value : DBNull.Value)
                .ToArray();
            table.Rows.Add(values);
        }
        return table;
    }

    public WorkflowEditorDocument CreateExportDocument() => WorkflowEditorDocument.FromSqlScript(Script);

    public void Dispose() => Script.PropertyChanged -= OnScriptPropertyChanged;
}

public sealed class SqlParameterInput(string name, string value) : ObservableObject
{
    private string _value = value;
    public string Name { get; } = name;
    public string Value { get => _value; set => SetProperty(ref _value, value); }
}
