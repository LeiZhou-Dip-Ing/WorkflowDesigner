using System.Windows;
using WorkflowCore.WpfDemo.ViewModels;
using WorkflowDesigner.WpfSdk;

namespace WorkflowCore.WpfDemo.Views;

public partial class ActionEditorWindow : Window
{
    public ActionEditorWindow(
        IWorkflowDesignerActionContext context,
        bool isImageEditor)
    {
        InitializeComponent();
        DataContext = new ActionEditorDialogViewModel(context, isImageEditor);
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
        => Close();

    private async void RunPreviewButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ActionEditorDialogViewModel viewModel
            || !viewModel.Context.CanRunPreview)
        {
            return;
        }

        RunPreviewButton.IsEnabled = false;
        try
        {
            await viewModel.Context.RunPreviewAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Preview failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            RunPreviewButton.GetBindingExpression(IsEnabledProperty)?.UpdateTarget();
        }
    }
}
