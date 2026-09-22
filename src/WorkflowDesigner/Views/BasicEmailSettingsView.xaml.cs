using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using WorkflowCore.WpfDemo.ViewModels;

namespace WorkflowCore.WpfDemo.Views;

public partial class BasicEmailSettingsView : UserControl
{
    private EmailSettingsViewModel? _viewModel;

    public BasicEmailSettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel != null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = e.NewValue as EmailSettingsViewModel;
        if (_viewModel != null) _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        PasswordInput.Clear();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EmailSettingsViewModel.Password)
            && _viewModel?.Password.Length == 0 && PasswordInput.Password.Length != 0)
            PasswordInput.Clear();
    }

    private void PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null) _viewModel.Password = PasswordInput.Password;
    }

    private void OpenMicrosoftSignInPage(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null || !Uri.TryCreate(_viewModel.VerificationUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !(uri.Host.Equals("microsoft.com", StringComparison.OrdinalIgnoreCase)
                 || uri.Host.EndsWith(".microsoft.com", StringComparison.OrdinalIgnoreCase)))
            return;
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
}
