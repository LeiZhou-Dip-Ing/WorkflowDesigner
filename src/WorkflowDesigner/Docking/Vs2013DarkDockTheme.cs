using System.Windows;
using AvalonDock.Themes;

namespace WorkflowCore.WpfDemo.Docking;

public sealed class AutomationProDockTheme : DictionaryTheme
{
    public AutomationProDockTheme()
        : base(new ResourceDictionary
        {
            Source = new Uri("/AvalonDock.Themes.VS2013;component/lighttheme.xaml", UriKind.Relative)
        })
    {
    }
}
