using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace WorkflowCore.WpfDemo.ViewModels;

/// <summary>Replaces a projected view in one notification so WPF does not rebuild once per row.</summary>
public sealed class ResettableObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceWith(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
