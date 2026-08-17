using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using WorkflowCore.WpfDemo.Services.Designer;
using WorkflowDesigner.WpfSdk;

namespace WorkflowCore.WpfDemo.ViewModels;

public sealed class ActionEditorDialogViewModel
{
    public ActionEditorDialogViewModel(
        IWorkflowDesignerActionContext context,
        bool isImageEditor)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        IsImageEditor = isImageEditor;
        PropertiesView = CollectionViewSource.GetDefaultView(Context.Properties);
        PropertiesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(IWorkflowPropertyEditorModel.Category)));
        PropertiesView.SortDescriptions.Add(new SortDescription(nameof(IWorkflowPropertyEditorModel.Category), ListSortDirection.Ascending));
        PropertiesView.SortDescriptions.Add(new SortDescription(nameof(IWorkflowPropertyEditorModel.Order), ListSortDirection.Ascending));
    }

    public IWorkflowDesignerActionContext Context { get; }

    public string Title => $"{Context.Descriptor.DisplayName} - Action Editor";

    public string Description => Context.Descriptor.Description;

    public bool IsImageEditor { get; }

    public bool IsPropertyEditor => !IsImageEditor;

    public ICollectionView PropertiesView { get; }

    public ICommand? CreateValueCommand
        => (Context as WorkflowDesignerActionContextAdapter)?.CreateValueCommand;

    public ICommand? ClearValueCommand
        => (Context as WorkflowDesignerActionContextAdapter)?.ClearValueCommand;
}
