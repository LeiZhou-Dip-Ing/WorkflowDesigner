using System.Windows;
using WorkflowCore.WpfDemo.Services.Runtime;

namespace WorkflowCore.WpfDemo.Views;

public partial class ActionLogWindow : Window
{
    public ActionLogWindow() => InitializeComponent();

    private void ClearOnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is ActionRunLog log) log.Clear();
    }
}
