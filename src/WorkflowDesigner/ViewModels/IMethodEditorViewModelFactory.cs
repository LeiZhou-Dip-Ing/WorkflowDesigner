using WorkflowCore.WpfDemo.Editor;

namespace WorkflowCore.WpfDemo.ViewModels;

public interface IMethodEditorViewModelFactory
{
    MethodEditorViewModel Create(WorkflowMethod method, MainWindowViewModel owner);

    void Release(MethodEditorViewModel viewModel);
}
