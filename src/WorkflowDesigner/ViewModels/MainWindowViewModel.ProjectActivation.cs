namespace WorkflowCore.WpfDemo.ViewModels;

public sealed partial class MainWindowViewModel
{
    /// <summary>Rebuilds the visible document selection from the currently loaded Project.</summary>
    private void OpenInitialProjectDocument(bool closeExistingDocuments = true)
    {
        if (closeExistingDocuments)
        {
            CloseAllMethodEditors();
        }

        SelectedMethod = Project.Methods.FirstOrDefault(method => method.Name == "Main")
                         ?? Project.Methods.FirstOrDefault();
        if (SelectedMethod != null)
        {
            OpenMethod(SelectedMethod);
            return;
        }

        OpenScript(Project.Scripts.FirstOrDefault());
    }
}
