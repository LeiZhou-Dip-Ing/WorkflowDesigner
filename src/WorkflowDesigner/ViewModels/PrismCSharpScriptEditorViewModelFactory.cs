using Prism.Ioc;
using WorkflowCore.WpfDemo.Editor;

namespace WorkflowCore.WpfDemo.ViewModels;

public sealed class PrismCSharpScriptEditorViewModelFactory : ICSharpScriptEditorViewModelFactory
{
    private readonly IContainerProvider _containerProvider;

    public PrismCSharpScriptEditorViewModelFactory(IContainerProvider containerProvider)
    {
        _containerProvider = containerProvider;
    }

    public CSharpScriptEditorViewModel Create(WorkflowScript script, MainWindowViewModel owner)
        => _containerProvider.Resolve<CSharpScriptEditorViewModel>(
            (typeof(WorkflowScript), script),
            (typeof(MainWindowViewModel), owner));

    public void Release(CSharpScriptEditorViewModel viewModel)
        => viewModel.Dispose();
}
