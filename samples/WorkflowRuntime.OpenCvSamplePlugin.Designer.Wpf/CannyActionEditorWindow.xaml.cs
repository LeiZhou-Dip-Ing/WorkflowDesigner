using System.Windows;
using WorkflowDesigner.WpfSdk;

namespace WorkflowRuntime.OpenCvSamplePlugin.Designer.Wpf;

public partial class CannyActionEditorWindow : Window
{
    public CannyActionEditorWindow(IWorkflowDesignerActionContext context)
    {
        InitializeComponent();
        DataContext = context ?? throw new ArgumentNullException(nameof(context));
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
}
