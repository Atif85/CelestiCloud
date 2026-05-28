using CelestiCloud.Core.Logging;

namespace CelestiCloud.CLI.Util;

internal class ConsoleLogger : IJobLogger
{
    public LogLevel MinimumLevel { get; set; } = LogLevel.Debug;

    public void Log(LogLevel level, string message)
    {
        // Don't print if the log's level is less severe than our minimum setting
        if (level < MinimumLevel || level == LogLevel.None)
            return;

        // Make it pretty
        Console.ForegroundColor = level switch
        {
            LogLevel.Debug => ConsoleColor.DarkGray,
            LogLevel.Info => ConsoleColor.Cyan,
            LogLevel.Error => ConsoleColor.Red,
            _ => ConsoleColor.White
        };

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{level}] {message}");
        Console.ResetColor();
    }
}
