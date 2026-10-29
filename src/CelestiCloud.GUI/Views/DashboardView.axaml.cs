using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CelestiCloud.GUI.ViewModels;
using System;
using System.Collections.Specialized;

namespace CelestiCloud.GUI.Views;

public partial class DashboardView : UserControl
{
    private DashboardViewModel? _vm;

    public DashboardView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        _vm?.LiveLogs.CollectionChanged -= OnLogsCollectionChanged;

        _vm = DataContext as DashboardViewModel;

        _vm?.LiveLogs.CollectionChanged += OnLogsCollectionChanged;
    }

    private void OnLogsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_vm is null || !_vm.IsLogViewerOpen)
            return;

        // Autoscroll ON: follow the newest log
        if (_vm.LogAutoScroll)
        {
            Dispatcher.UIThread.Post(() => LogsScrollViewer.ScrollToEnd(), DispatcherPriority.Background);
            return;
        }

        // Autoscroll OFF: when old logs are trimmed from the top, shift the
        // offset up by the removed height so the lines being read stay put.
        if (e.Action == NotifyCollectionChangedAction.Remove)
        {
            var oldExtent = LogsScrollViewer.Extent.Height;
            var oldOffset = LogsScrollViewer.Offset.Y;

            Dispatcher.UIThread.Post(() =>
            {
                var removedHeight = oldExtent - LogsScrollViewer.Extent.Height;
                if (removedHeight > 0)
                {
                    LogsScrollViewer.Offset = new Vector(
                        LogsScrollViewer.Offset.X,
                        Math.Max(0, oldOffset - removedHeight));
                }
            }, DispatcherPriority.Loaded);
        }
    }
}