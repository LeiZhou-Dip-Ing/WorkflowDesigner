using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WorkflowCore.WpfDemo.Services.Runtime;

namespace WorkflowCore.WpfDemo.Views;

public partial class VariableTraceView : UserControl
{
    private ActionRunLog? _log;

    public VariableTraceView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_log != null) _log.TraceEntries.CollectionChanged -= TraceEntriesOnCollectionChanged;
        _log = e.NewValue as ActionRunLog;
        if (_log != null) _log.TraceEntries.CollectionChanged += TraceEntriesOnCollectionChanged;
    }

    private void TraceEntriesOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (AutoScrollCheckBox.IsChecked == true && _log?.TraceEntries.LastOrDefault() is { } last)
        {
            TraceGrid.Dispatcher.BeginInvoke(
                () =>
                {
                    try
                    {
                        TraceGrid.ScrollIntoView(last);
                    }
                    catch (InvalidOperationException)
                    {
                        // WPF can briefly report an inconsistent ItemsControl while
                        // a virtualized grid is still applying collection changes.
                    }
                },
                DispatcherPriority.ContextIdle);
        }
    }

    private void ClearOnClick(object sender, RoutedEventArgs e) => _log?.ClearTrace();
}
