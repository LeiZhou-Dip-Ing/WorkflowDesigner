using System.Collections.ObjectModel;
using WorkflowCore.WpfDemo.Models;

namespace WorkflowCore.WpfDemo.ViewModels;

public sealed partial class MainWindowViewModel
{
    private static ObservableCollection<HamburgerMenuItem> CreateHamburgerMenuItems()
        =>
        [
            new HamburgerMenuItem
            {
                Key = "Methods",
                Title = "Methods",
                IconKey = DocumentIconKeys.Method,
                HasSubmenu = true
            },
            new HamburgerMenuItem
            {
                Key = "CSharpScripts",
                Title = "CSharp Scripts",
                IconKey = DocumentIconKeys.CSharpScript,
                HasSubmenu = true
            },
            new HamburgerMenuItem
            {
                Key = "SqlScripts",
                Title = "SQL Scripts",
                IconKey = DocumentIconKeys.SqlScript,
                HasSubmenu = true
            },
            new HamburgerMenuItem
            {
                Key = "RuntimeDisplays",
                Title = "Runtime Displays",
                IconKey = DocumentIconKeys.RuntimeDisplay,
                HasSubmenu = true
            }
        ];

    private void SelectHamburgerMenuItem(object? parameter)
    {
        if (parameter is not HamburgerMenuItem item)
        {
            return;
        }

        SelectedHamburgerMenuItem = item;
    }
}
