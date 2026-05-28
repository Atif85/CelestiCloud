using System;
using System.IO;
using CelestiCloud.Core.Logging;

namespace CelestiCloud.Core.IO;

public class FileLogger : IJobLogger
{
    private readonly string _logFilePath;
    private readonly Lock _lock = new();

    private const long MaxLogSizeInBytes = 5 * 1024 * 1024; // 5 MB per file
    private const int MaxArchiveFiles = 3;                  // Keep up to 3 older backup files

    public LogLevel MinimumLevel { get; set; } = LogLevel.Debug;

    public FileLogger(string appDataPath, string jobName)
    {
        string logsDir = Path.Combine(appDataPath, "logs");
        Directory.CreateDirectory(logsDir);

        // Clean the job name to make sure it doesn't contain invalid file characters
        string safeName = string.Join("_", jobName.Split(Path.GetInvalidFileNameChars()));
        _logFilePath = Path.Combine(logsDir, $"{safeName}.log");
    }

    public void Log(LogLevel level, string message)
    {
        if (level < MinimumLevel || level == LogLevel.None) return;

        string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}";

        lock (_lock)
        {
            try
            {
                // Check size and rotate files if necessary before appending
                RotateLogFilesIfNeeded();

                File.AppendAllText(_logFilePath, logEntry);
            }
            catch
            {
                // Fallback
            }
        }
    }

    /// <summary>
    /// Checks if the active log file is too big. If so, rolls the backups 
    /// and frees up a fresh active log file.
    /// </summary>
    private void RotateLogFilesIfNeeded()
    {
        if (!File.Exists(_logFilePath)) return;

        var fileInfo = new FileInfo(_logFilePath);
        if (fileInfo.Length < MaxLogSizeInBytes) return;

        // Shift existing historical backup files (e.g., .2.log -> deletes, .1.log -> .2.log)
        for (int i = MaxArchiveFiles - 1; i >= 1; i--)
        {
            string currentBackup = GetArchiveFilePath(i);
            string nextBackup = GetArchiveFilePath(i + 1);

            if (File.Exists(currentBackup))
            {
                if (i == MaxArchiveFiles - 1)
                {
                    // Delete the oldest backup file to respect the file count limit
                    File.Delete(currentBackup);
                }
                else
                {
                    // Move the backup file up one slot
                    File.Move(currentBackup, nextBackup, overwrite: true);
                }
            }
        }

        // Move the active file to become .1.log
        string archivePath1 = GetArchiveFilePath(1);
        File.Move(_logFilePath, archivePath1, overwrite: true);
    }

    private string GetArchiveFilePath(int index)
    {
        string dir = Path.GetDirectoryName(_logFilePath)!;
        string name = Path.GetFileNameWithoutExtension(_logFilePath);
        string ext = Path.GetExtension(_logFilePath); // Usually ".log"
        return Path.Combine(dir, $"{name}.{index}{ext}");
    }
}