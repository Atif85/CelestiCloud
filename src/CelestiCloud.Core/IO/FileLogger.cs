using System;
using System.IO;
using CelestiCloud.Core.Logging;

namespace CelestiCloud.Core.IO;

public class FileLogger : IJobLogger
{
    private readonly string _logFilePath;
    private readonly object _lock = new();
    public LogLevel MinimumLevel { get; set; } = LogLevel.Debug;

    public FileLogger(string appDataPath, string jobName)
    {
        string logsDir = Path.Combine(appDataPath, "logs");
        Directory.CreateDirectory(logsDir);

        // e.g., AppData/CelestiCloud/logs/WorkDocs_20260527.log
        string safeName = string.Join("_", jobName.Split(Path.GetInvalidFileNameChars()));
        _logFilePath = Path.Combine(logsDir, $"{safeName}_{DateTime.Now:yyyyMMdd}.log");
    }

    public void Log(LogLevel level, string message)
    {
        if (level < MinimumLevel || level == LogLevel.None) return;

        string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}";

        // Simple thread-safe append. 
        // For extremely high-throughput logging, you'd use a background writer queue, 
        // but this is perfectly fine for MVP.
        lock (_lock)
        {
            File.AppendAllText(_logFilePath, logEntry);
        }
    }
}