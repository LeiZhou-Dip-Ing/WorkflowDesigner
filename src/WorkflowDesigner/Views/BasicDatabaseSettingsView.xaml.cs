using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using WorkflowCore.WpfDemo.ViewModels;

namespace WorkflowCore.WpfDemo.Views;

public partial class BasicDatabaseSettingsView : UserControl
{
    private DatabaseSettingsViewModel? _viewModel;

    public BasicDatabaseSettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel != null) _viewModel.PropertyChanged -= OnPropertyChanged;
        _viewModel = e.NewValue as DatabaseSettingsViewModel;
        if (_viewModel != null) _viewModel.PropertyChanged += OnPropertyChanged;
        ConnectionStringInput.Clear();
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DatabaseSettingsViewModel.ConnectionString)
            && _viewModel?.ConnectionString.Length == 0 && ConnectionStringInput.Password.Length != 0)
            ConnectionStringInput.Clear();
    }

    private void ConnectionStringChanged(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null) _viewModel.ConnectionString = ConnectionStringInput.Password;
    }
}
