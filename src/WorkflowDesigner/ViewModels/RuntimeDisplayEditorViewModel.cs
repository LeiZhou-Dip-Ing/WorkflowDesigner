using System.Collections.ObjectModel;
using WorkflowCore.WpfDemo.Editor;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.ViewModels;

public sealed class RuntimeDisplayEditorViewModel : ObservableObject, IEditableDockDocument
{
    private string _status = "Loading Grafana dashboard...";
    private bool _isDirty;
    private bool _isBindingsOpen;
    private string? _selectedProjectVariable;
    private WorkflowMethod? _selectedMethod;

    public RuntimeDisplayEditorViewModel(
        WorkflowProject project,
        RuntimeDisplayDefinition runtimeDisplay,
        Uri dashboardUri)
    {
        ArgumentNullException.ThrowIfNull(project);
        RuntimeDisplay = runtimeDisplay ?? throw new ArgumentNullException(nameof(runtimeDisplay));
        DashboardUri = dashboardUri ?? throw new ArgumentNullException(nameof(dashboardUri));
        ProjectVariables = new ObservableCollection<string>(project.Methods
            .SelectMany(method => method.MethodVariables)
            .Where(variable => variable.IsActive && WorkflowVariableNaming.IsGlobal(variable.VariableScope))
            .Select(variable => variable.VariableName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
        ProjectMethods = new ObservableCollection<WorkflowMethod>(project.Methods
            .OrderBy(method => method.Name, StringComparer.OrdinalIgnoreCase));
        VariableBindings = new ObservableCollection<RuntimeDisplayVariableBinding>(runtimeDisplay.VariableBindings);
        MethodBindings = new ObservableCollection<RuntimeDisplayMethodBinding>(runtimeDisplay.MethodBindings);
        ToggleBindingsCommand = new RelayCommand(() => IsBindingsOpen = !IsBindingsOpen);
        AddVariableBindingCommand = new RelayCommand(AddVariableBinding, CanAddVariableBinding);
        RemoveVariableBindingCommand = new RelayCommand(
            value => RemoveVariableBinding(value as RuntimeDisplayVariableBinding));
        AddMethodBindingCommand = new RelayCommand(AddMethodBinding, CanAddMethodBinding);
        RemoveMethodBindingCommand = new RelayCommand(
            value => RemoveMethodBinding(value as RuntimeDisplayMethodBinding));
    }

    public RuntimeDisplayDefinition RuntimeDisplay { get; }
    public Uri DashboardUri { get; }
    public ObservableCollection<string> ProjectVariables { get; }
    public ObservableCollection<WorkflowMethod> ProjectMethods { get; }
    public ObservableCollection<RuntimeDisplayVariableBinding> VariableBindings { get; }
    public ObservableCollection<RuntimeDisplayMethodBinding> MethodBindings { get; }
    public RelayCommand ToggleBindingsCommand { get; }
    public RelayCommand AddVariableBindingCommand { get; }
    public RelayCommand RemoveVariableBindingCommand { get; }
    public RelayCommand AddMethodBindingCommand { get; }
    public RelayCommand RemoveMethodBindingCommand { get; }

    public bool IsBindingsOpen
    {
        get => _isBindingsOpen;
        set => SetProperty(ref _isBindingsOpen, value);
    }

    public string? SelectedProjectVariable
    {
        get => _selectedProjectVariable;
        set
        {
            if (SetProperty(ref _selectedProjectVariable, value))
                AddVariableBindingCommand.RaiseCanExecuteChanged();
        }
    }

    public WorkflowMethod? SelectedMethod
    {
        get => _selectedMethod;
        set
        {
            if (SetProperty(ref _selectedMethod, value))
                AddMethodBindingCommand.RaiseCanExecuteChanged();
        }
    }
    public string ContentId => $"runtime-display:{RuntimeDisplay.RuntimeDisplayId:N}";
    public string Title => IsDirty ? $"{RuntimeDisplay.Name} *" : RuntimeDisplay.Name;
    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (SetProperty(ref _isDirty, value)) OnPropertyChanged(nameof(Title));
        }
    }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public void ReportNavigationCompleted(bool succeeded, string? error)
        => Status = succeeded ? string.Empty : $"Grafana dashboard could not be loaded: {error}";

    public WorkflowEditorDocument CreateExportDocument()
        => WorkflowEditorDocument.FromRuntimeDisplay(RuntimeDisplay);

    private bool CanAddVariableBinding()
        => !string.IsNullOrWhiteSpace(SelectedProjectVariable)
           && VariableBindings.All(binding => !string.Equals(
               binding.VariableName,
               SelectedProjectVariable,
               StringComparison.OrdinalIgnoreCase));

    private void AddVariableBinding()
    {
        if (!CanAddVariableBinding()) return;
        var variableName = SelectedProjectVariable!;
        VariableBindings.Add(new RuntimeDisplayVariableBinding
        {
            Alias = WorkflowVariableNaming.GetBaseName(variableName),
            VariableName = variableName
        });
        SynchronizeBindings();
        AddVariableBindingCommand.RaiseCanExecuteChanged();
    }

    private void RemoveVariableBinding(RuntimeDisplayVariableBinding? binding)
    {
        if (binding == null || !VariableBindings.Remove(binding)) return;
        SynchronizeBindings();
        AddVariableBindingCommand.RaiseCanExecuteChanged();
    }

    private bool CanAddMethodBinding()
        => SelectedMethod != null
           && MethodBindings.All(binding => binding.MethodId != SelectedMethod.Uid);

    private void AddMethodBinding()
    {
        if (!CanAddMethodBinding()) return;
        MethodBindings.Add(new RuntimeDisplayMethodBinding
        {
            Alias = SelectedMethod!.Name,
            MethodId = SelectedMethod.Uid
        });
        SynchronizeBindings();
        AddMethodBindingCommand.RaiseCanExecuteChanged();
    }

    private void RemoveMethodBinding(RuntimeDisplayMethodBinding? binding)
    {
        if (binding == null || !MethodBindings.Remove(binding)) return;
        SynchronizeBindings();
        AddMethodBindingCommand.RaiseCanExecuteChanged();
    }

    private void SynchronizeBindings()
    {
        RuntimeDisplay.VariableBindings = VariableBindings.ToList();
        RuntimeDisplay.MethodBindings = MethodBindings.ToList();
        IsDirty = true;
    }
}
