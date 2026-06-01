using System;
using CelestiCloud.Core.Logging;

namespace CelestiCloud.GUI.Logging;

public class LogMessage
{
    public DateTime Timestamp { get; set; }
    public LogLevel Level { get; set; }
    public string Text { get; set; } = string.Empty;

    // Helps the UI format it nicely
    public string FormattedTime => Timestamp.ToString("HH:mm:ss");

    // Avalonia will read this string directly to color the text!
    public string ColorHex => Level switch
    {
        LogLevel.Debug => "#888888",  // Gray
        LogLevel.Info => "#00BFFF",   // Deep Sky Blue
        LogLevel.Warning => "#FFCC00",    // Warning Yellow / Amber
        LogLevel.Error => "#FF4500",  // Orange Red
        _ => "#FFFFFF"
    };
}