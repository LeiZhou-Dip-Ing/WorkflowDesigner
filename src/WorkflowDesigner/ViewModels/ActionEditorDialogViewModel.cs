using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using WorkflowCore.WpfDemo.Services.Designer;
using WorkflowDesigner.WpfSdk;

namespace WorkflowCore.WpfDemo.ViewModels;

public sealed class ActionEditorDialogViewModel
{
    public ActionEditorDialogViewModel(
        IWorkflowDesignerActionContext context)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        PropertiesView = CollectionViewSource.GetDefaultView(Context.Properties);
        PropertiesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(IWorkflowPropertyEditorModel.Category)));
        PropertiesView.SortDescriptions.Add(new SortDescription(nameof(IWorkflowPropertyEditorModel.Category), ListSortDirection.Ascending));
        PropertiesView.SortDescriptions.Add(new SortDescription(nameof(IWorkflowPropertyEditorModel.Order), ListSortDirection.Ascending));
    }

    public IWorkflowDesignerActionContext Context { get; }

    public string Title => $"{Context.Descriptor.DisplayName} - Action Editor";

    public string Description => Context.Descriptor.Description;

    public ICollectionView PropertiesView { get; }

    public ICommand? CreateValueCommand
        => (Context as WorkflowDesignerActionContextAdapter)?.CreateValueCommand;

    public ICommand? ClearValueCommand
        => (Context as WorkflowDesignerActionContextAdapter)?.ClearValueCommand;
}
