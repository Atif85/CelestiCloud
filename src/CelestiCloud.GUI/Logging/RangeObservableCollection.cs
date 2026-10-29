using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace CelestiCloud.GUI.Logging;

public class RangeObservableCollection<T> : ObservableCollection<T>
{
    /// <summary>Removes the first <paramref name="count"/> items with ONE change notification.</summary>
    public void RemoveFirst(int count)
    {
        CheckReentrancy();

        count = Math.Min(count, Items.Count);
        if (count <= 0) return;

        var removed = Items.Take(count).ToList();
        ((List<T>)Items).RemoveRange(0, count);

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Remove, removed, 0));
    }
}