using System.Windows;
using WorkflowCore.WpfDemo.ViewModels;

namespace WorkflowCore.WpfDemo.Views;

public partial class SharpScriptLibraryManagerWindow : Window
{
    private bool _loaded;

    public SharpScriptLibraryManagerWindow()
    {
        InitializeComponent();
    }

    private void WindowOnLoaded(object sender, RoutedEventArgs args)
    {
        if (_loaded) return;
        _loaded = true;
        if (DataContext is SharpScriptLibraryManagerViewModel viewModel)
        {
            _ = viewModel.RefreshAsync();
        }
    }

    private void CloseOnClick(object sender, RoutedEventArgs args) => Close();
}
