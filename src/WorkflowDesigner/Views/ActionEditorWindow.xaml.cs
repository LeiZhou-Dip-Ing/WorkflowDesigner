using System.Windows;
using WorkflowCore.WpfDemo.ViewModels;
using WorkflowDesigner.WpfSdk;

namespace WorkflowCore.WpfDemo.Views;

public partial class ActionEditorWindow : Window
{
    public ActionEditorWindow(IWorkflowDesignerActionContext context)
    {
        InitializeComponent();
        DataContext = new ActionEditorDialogViewModel(context);
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
        => Close();

}
