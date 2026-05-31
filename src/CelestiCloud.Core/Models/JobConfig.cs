namespace CelestiCloud.Core.Models;

public enum JobType
{
    Sync,   // One-way mirroring (local edits upload, local deletes delete remote)
    Backup  // Cumulative backup (local edits upload, local deletes DO NOT delete remote)
}

public class JobConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public JobType JobType { get; set; } = JobType.Sync;
    public string TargetAccountId { get; set; } = string.Empty;
    public string RemoteRootPath { get; set; } = "/";
    public List<string> LocalPaths { get; set; } = [];
    public List<string> IgnorePatterns { get; set; } = [];
    public bool AutoStart { get; set; } = false;
    public int MaxConcurrentTransfers { get; set; } = 4;
    public int ReconciliationIntervalMinutes { get; set; } = 10;

    public override string ToString()
    {
        return $"Id: {Id}, Name: {Name}";
    }
}