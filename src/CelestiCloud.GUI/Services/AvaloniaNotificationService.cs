using System;
using Avalonia.Controls;
using Avalonia.Controls.Notifications; 
using Avalonia.Threading;

namespace CelestiCloud.GUI.Services;

public class AvaloniaNotificationService : INotificationService
{
    private readonly WindowNotificationManager _notificationManager;

    public AvaloniaNotificationService(TopLevel topLevel)
    {
        _notificationManager = new WindowNotificationManager(topLevel)
        {
            Position = NotificationPosition.BottomRight,
            MaxItems = 3
        };
    }

    public void Show(string title, string message, NotificationLevel level = NotificationLevel.Info, TimeSpan? duration = null)
    {
        var type = level switch
        {
            NotificationLevel.Success => NotificationType.Success,
            NotificationLevel.Warning => NotificationType.Warning,
            NotificationLevel.Error => NotificationType.Error,
            _ => NotificationType.Information
        };

        Dispatcher.UIThread.Post(() =>
        {
            _notificationManager.Show(new Notification(
                title,
                message,
                type,
                duration ?? TimeSpan.FromSeconds(4))); 
        });
    }
}