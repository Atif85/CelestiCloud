using Avalonia.Controls;
using Avalonia.Threading;
using CelestiCloud.GUI.ViewModels;
using System;
using System.Collections.Specialized;

namespace CelestiCloud.GUI.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is DashboardViewModel vm)
        {
            vm.LiveLogs.CollectionChanged += OnLogsCollectionChanged;
        }
    }

    private void OnLogsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Only scroll if AutoScroll is checked and the modal is actually open
        if (DataContext is DashboardViewModel { LogAutoScroll: true, IsLogViewerOpen: true })
        {
            Dispatcher.UIThread.Post(() =>
            {
               LogsScrollViewer.ScrollToEnd();
            }, DispatcherPriority.Background);
        }
    }
}