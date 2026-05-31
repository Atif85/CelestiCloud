using Avalonia.Threading;
using CelestiCloud.Core.Logging;
using System;
using System.Collections.ObjectModel;

namespace CelestiCloud.GUI.Logging;

public class ObservableUiLogger(IJobLogger fileLogger, int maxLogsInMemory = 1000) : IJobLogger
{
    private readonly IJobLogger _fileLogger = fileLogger;
    private readonly int _maxLogsInMemory = maxLogsInMemory;

    // The UI will bind directly to this collection
    public ObservableCollection<LogMessage> LiveLogs { get; } = [];

    public void Log(LogLevel level, string message)
    {
        // Write to the physical file using your FileLogger
        _fileLogger.Log(level, message);

        // Safely push to the GUI list on the UI Thread
        Dispatcher.UIThread.Post(() =>
        {
            // Insert at 0 so the NEWEST logs appear at the top! (No auto-scrolling needed)
            LiveLogs.Insert(0, new LogMessage
            {
                Timestamp = DateTime.Now,
                Level = level,
                Text = message
            });

            // Prevent memory leaks by dropping old logs from the UI (they are still safely in the file)
            if (LiveLogs.Count > _maxLogsInMemory)
            {
                LiveLogs.RemoveAt(LiveLogs.Count - 1);
            }
        });
    }
}