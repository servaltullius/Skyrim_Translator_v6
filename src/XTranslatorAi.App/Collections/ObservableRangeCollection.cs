using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace XTranslatorAi.App.Collections;

public sealed class ObservableRangeCollection<T> : ObservableCollection<T>
{
    public void AddRange(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            Items.Add(item);
        }

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public void ReplaceAll(IEnumerable<T> items)
    {
        Items.Clear();
        AddRange(items);
    }
}

