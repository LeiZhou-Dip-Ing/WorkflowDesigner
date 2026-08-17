using System.Windows;
namespace WorkflowRuntime.OpenCvSamplePlugin.Designer.Wpf;
public partial class VisionToolEditorWindow : Window
{
    public VisionToolEditorWindow(VisionActionDesignerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
}
