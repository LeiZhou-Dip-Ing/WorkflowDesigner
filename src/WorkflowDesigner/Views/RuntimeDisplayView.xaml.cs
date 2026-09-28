using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using WorkflowCore.WpfDemo.ViewModels;

namespace WorkflowCore.WpfDemo.Views;

public partial class RuntimeDisplayView : UserControl
{
    public RuntimeDisplayView()
    {
        InitializeComponent();
    }

    private void DashboardBrowserOnNavigationCompleted(
        object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (DataContext is RuntimeDisplayEditorViewModel viewModel)
            viewModel.ReportNavigationCompleted(e.IsSuccess, e.IsSuccess ? null : e.WebErrorStatus.ToString());
    }
}
