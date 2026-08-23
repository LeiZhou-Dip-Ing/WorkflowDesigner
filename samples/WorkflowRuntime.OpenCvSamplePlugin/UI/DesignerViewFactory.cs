using System.Windows;

namespace WorkflowRuntime.OpenCvSamplePlugin.UI;

internal static class DesignerViewFactory
{
    public static Window CreateWindow(string xamlFile, object dataContext)
    {
        var window = Load<Window>(xamlFile);
        window.DataContext = dataContext ?? throw new ArgumentNullException(nameof(dataContext));
        return window;
    }

    public static FrameworkElement CreateView(string xamlFile, object dataContext)
    {
        var view = Load<FrameworkElement>(xamlFile);
        view.DataContext = dataContext ?? throw new ArgumentNullException(nameof(dataContext));
        return view;
    }

    public static ResourceDictionary LoadResources(string xamlFile)
        => Load<ResourceDictionary>(xamlFile);

    private static T Load<T>(string xamlFile) where T : class
    {
        var assemblyName = typeof(DesignerViewFactory).Assembly.GetName().Name;
        var uri = new Uri($"/{assemblyName};component/UI/{xamlFile}", UriKind.Relative);
        return Application.LoadComponent(uri) as T
            ?? throw new InvalidOperationException($"Designer resource '{xamlFile}' is not a {typeof(T).Name}.");
    }
}
