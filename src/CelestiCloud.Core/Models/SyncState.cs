using System.Collections.Concurrent;

namespace CelestiCloud.Core.Models;

public class SyncState
{
    public int FilesFound { get; set; }
    public int FilesProcessed { get; set; }

    // Tracks currently uploading files: Path -> Percentage (0.0 to 1.0)
    public ConcurrentDictionary<string, double> ActiveTransfers { get; } = new();
}
