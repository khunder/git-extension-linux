using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  An <see cref="ObservableCollection{T}"/> that can add or replace many items with a single change notification.
///  Adding thousands of items one by one raises thousands of events, each of which makes the bound list react.
/// </summary>
public sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
    public RangeObservableCollection()
    {
    }

    /// <summary>
    ///  Appends all <paramref name="items"/> and raises one event for the batch.
    /// </summary>
    public void AddRange(IEnumerable<T> items)
    {
        List<T> added = [.. items];
        if (added.Count == 0)
        {
            return;
        }

        bool wasEmpty = Items.Count == 0;
        foreach (T item in added)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));

        // Lists handle a reset of an empty collection more cheaply than a range insertion.
        OnCollectionChanged(wasEmpty
            ? new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset)
            : new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, added, Items.Count - added.Count));
    }

    /// <summary>
    ///  Replaces the whole content with <paramref name="items"/> and raises one event.
    /// </summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        List<T> replacement = [.. items];
        if (Items.Count == 0 && replacement.Count == 0)
        {
            return;
        }

        Items.Clear();
        foreach (T item in replacement)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
