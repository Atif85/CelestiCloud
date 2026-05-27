namespace CelestiCloud.Core.Logging;
public enum LogLevel
{
    Debug,   // Deep technical details (e.g., "Skipped file: size matched")
    Info,    // Standard user-facing events (e.g., "Uploaded: photo.jpg")
    Error,   // Exceptions and failures
    None     // Completely silent
}

public interface IJobLogger
{
    void Log(LogLevel level, string message);
}