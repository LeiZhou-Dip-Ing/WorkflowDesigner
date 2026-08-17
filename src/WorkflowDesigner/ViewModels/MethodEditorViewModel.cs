using System.ComponentModel;
using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Models;
using WorkflowCore.WpfDemo.Services.Editing;

namespace WorkflowCore.WpfDemo.ViewModels;

public sealed class MethodEditorViewModel : ObservableObject, IEditableDockDocument, IDisposable
{
    private bool _isActionsToolboxExpanded;
    private bool _isActionsToolboxPopupOpen;
    private bool _insertPopupActionAfterSelection;
    private bool _isDirty;

    public event EventHandler<ActionEditorRequestEventArgs>? ActionEditorRequested;

    public MethodEditorViewModel(WorkflowMethod method, MainWindowViewModel owner)
    {
        Method = method;
        Owner = owner;
        ToggleActionsToolboxCommand = new RelayCommand(
            () => IsActionsToolboxExpanded = !IsActionsToolboxExpanded);
        OpenActionsToolboxPopupCommand = new RelayCommand(
            OpenActionsToolboxPopup,
            () => !IsActionsToolboxExpanded);
        OpenActionInsertionPopupCommand = new RelayCommand(OpenActionInsertionPopup);
        KeepActionsToolboxPopupOpenCommand = new RelayCommand(
            () => IsActionsToolboxPopupOpen = true);
        AddActionFromToolboxPopupCommand = new RelayCommand(
            parameter => AddActionFromToolboxPopup(parameter as string),
            parameter => parameter is string actionType && !string.IsNullOrWhiteSpace(actionType));
        AddActionFromExpandedToolboxCommand = new RelayCommand(
            parameter => AddActionFromExpandedToolbox(parameter as string),
            parameter => parameter is string actionType && !string.IsNullOrWhiteSpace(actionType));
        DropActionCommand = new RelayCommand(
            parameter => DropAction(parameter as ActionDropRequest),
            parameter => parameter is ActionDropRequest);
        OpenSelectedActionEditorCommand = new RelayCommand(
            parameter => OpenSelectedActionEditor(parameter as MethodLineViewItem),
            parameter => CanOpenSelectedActionEditor(parameter as MethodLineViewItem));
        CommitEditorChangesCommand = new RelayCommand(Owner.MarkProjectChanged);
        ActivateCommand = new RelayCommand(Activate);
        Method.PropertyChanged += Method_OnPropertyChanged;
    }

    public WorkflowMethod Method { get; }

    public MainWindowViewModel Owner { get; }

    public string ContentId => $"method:{Method.Uid:N}";

    public string Title => IsDirty ? $"{Method.Name} *" : Method.Name;

    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (SetProperty(ref _isDirty, value))
            {
                OnPropertyChanged(nameof(Title));
            }
        }
    }

    public WorkflowEditorDocument CreateExportDocument()
        => WorkflowEditorDocument.FromMethod(Method);

    public bool IsActionsToolboxExpanded
    {
        get => _isActionsToolboxExpanded;
        set
        {
            if (!SetProperty(ref _isActionsToolboxExpanded, value))
            {
                return;
            }

            if (value)
            {
                IsActionsToolboxPopupOpen = false;
            }

            OpenActionsToolboxPopupCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsActionsToolboxPopupOpen
    {
        get => _isActionsToolboxPopupOpen;
        set => SetProperty(ref _isActionsToolboxPopupOpen, value);
    }

    public RelayCommand ToggleActionsToolboxCommand { get; }

    public RelayCommand OpenActionsToolboxPopupCommand { get; }

    public RelayCommand OpenActionInsertionPopupCommand { get; }

    public RelayCommand KeepActionsToolboxPopupOpenCommand { get; }

    public RelayCommand AddActionFromToolboxPopupCommand { get; }

    public RelayCommand AddActionFromExpandedToolboxCommand { get; }

    public RelayCommand DropActionCommand { get; }

    public RelayCommand OpenSelectedActionEditorCommand { get; }

    public RelayCommand CommitEditorChangesCommand { get; }

    public RelayCommand ActivateCommand { get; }

    public void Activate()
    {
        Owner.SelectedMethod = Method;
    }

    public void Dispose()
    {
        Method.PropertyChanged -= Method_OnPropertyChanged;
    }

    private void Method_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkflowMethod.Name))
        {
            OnPropertyChanged(nameof(Title));
        }

        Owner.MarkDocumentChanged(Method);
    }

    private void AddActionFromToolboxPopup(string? actionType)
    {
        if (string.IsNullOrWhiteSpace(actionType))
        {
            return;
        }

        Activate();
        if (_insertPopupActionAfterSelection)
        {
            Owner.AddActionFromToolboxAfterSelection(actionType);
        }
        else
        {
            Owner.AddActionFromToolbox(actionType);
        }

        _insertPopupActionAfterSelection = false;
        IsActionsToolboxPopupOpen = false;
    }

    private void OpenActionsToolboxPopup()
    {
        Activate();
        _insertPopupActionAfterSelection = false;
        IsActionsToolboxPopupOpen = true;
    }

    private void OpenActionInsertionPopup()
    {
        Activate();
        _insertPopupActionAfterSelection = true;
        IsActionsToolboxPopupOpen = true;
    }

    private void AddActionFromExpandedToolbox(string? actionType)
    {
        if (string.IsNullOrWhiteSpace(actionType))
        {
            return;
        }

        Activate();
        Owner.AddActionFromToolbox(actionType);
    }

    private bool CanOpenSelectedActionEditor(MethodLineViewItem? item)
    {
        if (item?.Line.Action == null)
        {
            return false;
        }

        var descriptor = Owner.ResolveActionDescriptor(item.Line.Action);
        return ActionPresentationPolicy.CanOpenOnDoubleClick(descriptor);
    }

    private void OpenSelectedActionEditor(MethodLineViewItem? item)
    {
        if (item?.Line.Action == null)
        {
            return;
        }

        Activate();
        Owner.SelectedMethodLineItem = item;
        var descriptor = Owner.ResolveActionDescriptor(item.Line.Action);
        var editorKey = ActionPresentationPolicy.GetDoubleClickEditor(descriptor);
        if (descriptor == null || string.IsNullOrWhiteSpace(editorKey))
        {
            return;
        }

        ActionEditorRequested?.Invoke(
            this,
            new ActionEditorRequestEventArgs(item, descriptor, editorKey));
    }

    private void DropAction(ActionDropRequest? request)
    {
        if (request == null)
        {
            return;
        }

        Activate();
        Owner.AddActionFromToolbox(request.ActionType, request.InsertBeforeLineNo);
    }
}
