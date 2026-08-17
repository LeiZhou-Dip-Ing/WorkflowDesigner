using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using WorkflowCore.WpfDemo.ViewModels;

namespace WorkflowCore.WpfDemo;

public partial class MainWindow : Window
{
    private readonly ApplicationShellViewModel _viewModel;

    public MainWindow(ApplicationShellViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = viewModel;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_viewModel.CanCloseApplication())
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }

    protected override async void OnClosed(EventArgs e)
    {
        await _viewModel.DisposeAsync();
        base.OnClosed(e);
    }

    private void RootGridOnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ExplorerRail.IsMouseOver || MethodsSubmenu.IsMouseOver || ScriptsSubmenu.IsMouseOver)
        {
            return;
        }

        if (RootGrid.DataContext is MainWindowViewModel { IsSubmenuOpen: true } workspace)
        {
            workspace.CloseSubmenuCommand.Execute(null);
        }
    }
}
