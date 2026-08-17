using Prism.Ioc;
using WorkflowCore.WpfDemo.Editor;

namespace WorkflowCore.WpfDemo.ViewModels;

public sealed class PrismMethodEditorViewModelFactory : IMethodEditorViewModelFactory
{
    private readonly IContainerProvider _containerProvider;

    public PrismMethodEditorViewModelFactory(IContainerProvider containerProvider)
    {
        _containerProvider = containerProvider;
    }

    public MethodEditorViewModel Create(WorkflowMethod method, MainWindowViewModel owner)
        => _containerProvider.Resolve<MethodEditorViewModel>(
            (typeof(WorkflowMethod), method),
            (typeof(MainWindowViewModel), owner));

    public void Release(MethodEditorViewModel viewModel)
    {
        viewModel.Dispose();
    }
}
