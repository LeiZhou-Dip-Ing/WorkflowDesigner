using WorkflowCore.WpfDemo.Editor;

namespace WorkflowCore.WpfDemo.ViewModels;

public interface ICSharpScriptEditorViewModelFactory
{
    CSharpScriptEditorViewModel Create(WorkflowScript script, MainWindowViewModel owner);

    void Release(CSharpScriptEditorViewModel viewModel);
}
