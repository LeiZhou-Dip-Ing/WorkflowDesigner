namespace WorkflowCore.WpfDemo.ViewModels;

public interface IViewModelFactory
{
    T Create<T>() where T : class;

    void Release(object viewModel);
}
