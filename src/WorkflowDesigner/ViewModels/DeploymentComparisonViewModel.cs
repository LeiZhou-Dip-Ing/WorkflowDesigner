using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using WorkflowCore.WpfDemo.Models;
using WorkflowCore.WpfDemo.Models.Comparison;

namespace WorkflowCore.WpfDemo.ViewModels;

public sealed class DeploymentComparisonViewModel : ObservableObject
{
    private ComparisonMethod? _selectedMethod;
    private ComparisonScript? _selectedScript;
    private ComparisonActionRow? _selectedAction;
    private ComparisonValueRow? _selectedValue;
    private string _searchText = string.Empty;
    private bool _ignoreWhitespace;
    private bool _ignoreLineEndings;
    private int _navigationRequest;
    private int _navigationDirection = 1;
    private int _selectedTabIndex;

    public DeploymentComparisonViewModel(DeploymentComparisonModel model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        Methods = new ObservableCollection<ComparisonMethod>(
            model.Methods.Where(item => item.ChangeKind != ComparisonChangeKind.Same));
        Scripts = new ObservableCollection<ComparisonScript>(
            model.Scripts.Where(item => item.ChangeKind != ComparisonChangeKind.Same));
        RawDifferences = new ObservableCollection<WorkflowDifferenceItem>(model.RawDifferences);
        NextDifferenceCommand = new RelayCommand(() => Navigate(1), () => DifferenceItems.Count > 0);
        PreviousDifferenceCommand = new RelayCommand(() => Navigate(-1), () => DifferenceItems.Count > 0);
        _selectedTabIndex = model.Scope switch
        {
            ComparisonScope.Method when Methods.Count > 0 => 1,
            ComparisonScope.Method => 0,
            ComparisonScope.Script when Scripts.Count > 0 => 2,
            ComparisonScope.Script => 0,
            _ => 0
        };
        SelectedMethod = Methods.FirstOrDefault();
        SelectedScript = Scripts.FirstOrDefault();
    }

    public DeploymentComparisonModel Model { get; }
    public ObservableCollection<ComparisonMethod> Methods { get; }
    public ObservableCollection<ComparisonScript> Scripts { get; }
    public ObservableCollection<WorkflowDifferenceItem> RawDifferences { get; }
    public RelayCommand NextDifferenceCommand { get; }
    public RelayCommand PreviousDifferenceCommand { get; }
    public string Title => Model.Title;
    public string Summary => Model.Summary;
    public long RuntimeRevision => Model.RuntimeRevision;
    public bool IsProjectComparison => Model.Scope == ComparisonScope.Project;
    public bool HasMethods => Methods.Count > 0;
    public bool HasScripts => Scripts.Count > 0;
    public int DifferenceCount => Model.DifferenceCount;
    public int AddedCount => Model.AddedCount;
    public int RemovedCount => Model.RemovedCount;
    public int ModifiedCount => Model.ModifiedCount;
    public int CurrentDifferenceIndex { get; private set; } = -1;
    public string DifferencePosition => DifferenceItems.Count == 0 ? "No differences" : $"{Math.Max(1, CurrentDifferenceIndex + 1)} of {DifferenceItems.Count}";

    public ComparisonMethod? SelectedMethod
    {
        get => _selectedMethod;
        set
        {
            if (!SetProperty(ref _selectedMethod, value)) return;
            CurrentDifferenceIndex = -1;
            OnPropertyChanged(nameof(ActionRowsView));
            OnPropertyChanged(nameof(VariableRowsView));
            OnPropertyChanged(nameof(InputOutputRowsView));
            RaiseNavigationState();
        }
    }

    public ComparisonScript? SelectedScript
    {
        get => _selectedScript;
        set { if (SetProperty(ref _selectedScript, value)) { CurrentDifferenceIndex = -1; RaiseNavigationState(); } }
    }

    public ComparisonActionRow? SelectedAction { get => _selectedAction; set => SetProperty(ref _selectedAction, value); }
    public ComparisonValueRow? SelectedValue { get => _selectedValue; set => SetProperty(ref _selectedValue, value); }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value)) return;
            RefreshViews();
            NavigationRequest++;
        }
    }

    public bool IgnoreWhitespace { get => _ignoreWhitespace; set { if (SetProperty(ref _ignoreWhitespace, value)) NavigationRequest++; } }
    public bool IgnoreLineEndings { get => _ignoreLineEndings; set { if (SetProperty(ref _ignoreLineEndings, value)) NavigationRequest++; } }
    public int NavigationRequest { get => _navigationRequest; private set => SetProperty(ref _navigationRequest, value); }
    public int NavigationDirection { get => _navigationDirection; private set => SetProperty(ref _navigationDirection, value); }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (!SetProperty(ref _selectedTabIndex, value)) return;
            CurrentDifferenceIndex = -1;
            OnPropertyChanged(nameof(IsTextComparisonActive));
            RaiseNavigationState();
        }
    }

    public bool IsTextComparisonActive => SelectedTabIndex == 2;
    public ICollectionView ActionRowsView => CreateView(
        SelectedMethod?.Actions.Where(IsDifferent).ToArray() ?? Array.Empty<ComparisonActionRow>());
    public ICollectionView VariableRowsView => CreateView(SelectedMethod?.Variables ?? Array.Empty<ComparisonValueRow>());
    public ICollectionView InputOutputRowsView => CreateView(SelectedMethod == null ? Array.Empty<ComparisonValueRow>() : SelectedMethod.Inputs.Concat(SelectedMethod.Outputs).ToArray());
    public ICollectionView RawDifferencesView => CreateView(RawDifferences);

    private IReadOnlyList<object> DifferenceItems => SelectedTabIndex switch
    {
        1 => SelectedMethod?.Actions.Where(IsDifferent).Cast<object>().ToArray() ?? Array.Empty<object>(),
        2 when SelectedScript is { DifferenceCount: > 0 } script => Enumerable.Range(0, script.DifferenceCount).Cast<object>().ToArray(),
        3 => SelectedMethod?.Variables.Where(IsDifferent).Cast<object>().ToArray() ?? Array.Empty<object>(),
        4 => SelectedMethod?.Inputs.Concat(SelectedMethod.Outputs).Where(IsDifferent).Cast<object>().ToArray() ?? Array.Empty<object>(),
        _ => (SelectedMethod?.Actions.Where(IsDifferent).Cast<object>()
                  .Concat(SelectedMethod.Variables.Where(IsDifferent))
                  .Concat(SelectedMethod.Inputs.Where(IsDifferent))
                  .Concat(SelectedMethod.Outputs.Where(IsDifferent)) ?? Enumerable.Empty<object>()).ToArray()
    };

    private void Navigate(int direction)
    {
        var items = DifferenceItems;
        if (items.Count == 0) return;
        CurrentDifferenceIndex = (CurrentDifferenceIndex + direction + items.Count) % items.Count;
        if (items[CurrentDifferenceIndex] is ComparisonActionRow action) SelectedAction = action;
        if (items[CurrentDifferenceIndex] is ComparisonValueRow value) SelectedValue = value;
        NavigationDirection = direction;
        NavigationRequest++;
        RaiseNavigationState();
    }

    private ICollectionView CreateView(object source)
    {
        var view = CollectionViewSource.GetDefaultView(source);
        view.Filter = item => string.IsNullOrWhiteSpace(SearchText) || item switch
        {
            ComparisonActionRow action => action.SearchText.Contains(SearchText, StringComparison.OrdinalIgnoreCase),
            ComparisonValueRow value => value.SearchText.Contains(SearchText, StringComparison.OrdinalIgnoreCase),
            WorkflowDifferenceItem raw => $"{raw.Path} {raw.LocalValue} {raw.RuntimeValue}".Contains(SearchText, StringComparison.OrdinalIgnoreCase),
            _ => true
        };
        return view;
    }

    private void RefreshViews()
    {
        OnPropertyChanged(nameof(ActionRowsView));
        OnPropertyChanged(nameof(VariableRowsView));
        OnPropertyChanged(nameof(InputOutputRowsView));
        OnPropertyChanged(nameof(RawDifferencesView));
    }

    private void RaiseNavigationState()
    {
        OnPropertyChanged(nameof(DifferencePosition));
        PreviousDifferenceCommand.RaiseCanExecuteChanged();
        NextDifferenceCommand.RaiseCanExecuteChanged();
    }

    private static bool IsDifferent(ComparisonActionRow item) => item.ChangeKind != ComparisonChangeKind.Same;
    private static bool IsDifferent(ComparisonValueRow item) => item.ChangeKind != ComparisonChangeKind.Same;
}
