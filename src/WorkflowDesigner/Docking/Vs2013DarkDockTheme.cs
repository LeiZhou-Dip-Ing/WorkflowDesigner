using System.Windows;
using AvalonDock.Themes;

namespace WorkflowCore.WpfDemo.Docking;

public sealed class Vs2013DarkDockTheme : DictionaryTheme
{
    public Vs2013DarkDockTheme()
        : base(new ResourceDictionary
        {
            Source = new Uri("/AvalonDock.Themes.VS2013;component/darktheme.xaml", UriKind.Relative)
        })
    {
    }
}
