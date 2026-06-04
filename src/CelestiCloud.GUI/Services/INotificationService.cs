using System;

namespace CelestiCloud.GUI.Services;

public enum NotificationLevel
{
    Info,
    Success,
    Warning,
    Error
}

public interface INotificationService
{
    /// <summary>
    /// Displays a transient, non-blocking toast warning/popup in the corner of the window.
    /// </summary>
    void Show(string title, string message, NotificationLevel level = NotificationLevel.Info, TimeSpan? duration = null);
}