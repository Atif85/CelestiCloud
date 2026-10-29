using System.Collections.Concurrent;

namespace CelestiCloud.Core.Models;

public class SyncState
{
    public int FilesFound { get; set; }
    public int FilesProcessed { get; set; }

    public string CurrentStatus { get; set; } = "Initializing...";

    // Tracks currently uploading files: Path -> Percentage (0.0 to 1.0)
    public ConcurrentDictionary<string, double> ActiveTransfers { get; } = new();

    // Tracks non-upload operations in real time: Target Name -> Operation Description
    public ConcurrentDictionary<string, string> ActiveOperations { get; } = new();
}
