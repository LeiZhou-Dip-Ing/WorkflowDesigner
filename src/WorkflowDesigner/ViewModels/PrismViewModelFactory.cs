using Prism.Ioc;

namespace WorkflowCore.WpfDemo.ViewModels;

public sealed class PrismViewModelFactory : IViewModelFactory
{
    private readonly IContainerProvider _containerProvider;

    public PrismViewModelFactory(IContainerProvider containerProvider)
    {
        _containerProvider = containerProvider;
    }

    public T Create<T>() where T : class => _containerProvider.Resolve<T>();

    public void Release(object viewModel)
    {
        if (viewModel is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
