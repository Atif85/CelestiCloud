using CelestiCloud.Core.Filtering;
using CelestiCloud.Core.IO;
using CelestiCloud.Core.Logging;
using CelestiCloud.Core.Models;
using CelestiCloud.Core.Providers;
using System.Threading.Channels;

namespace CelestiCloud.Core.Jobs;

public abstract class LocalToCloudJobBase : JobBase
{
    public SyncState State { get; } = new();

    protected readonly ICloudProvider Provider;
    protected readonly IJobLogger Logger;
    protected abstract bool AllowDeletions { get; }

    private readonly IgnoreFilter _ignoreFilter;

    private readonly List<FileSystemWatcher> _watchers = [];
    private Channel<FileEvent>? _eventChannel;

    private int _filesFound;
    private int _filesProcessed;

    protected LocalToCloudJobBase(JobConfig config, ICloudProvider provider, string appDataPath, IJobLogger logger)
        : base(config, appDataPath)
    {
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        Logger = logger;
        _ignoreFilter = new(config.IgnorePatterns);
    }

    /// <summary>
    /// The core execution flow. Runs the initial reconciliation pass, starts the watchers,
    /// and processes new filesystem events continuously.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        Logger.Log(LogLevel.Info, $"Starting job '{Config.Name}'...");

        // Set up our threadsafe event queue
        _eventChannel = Channel.CreateUnbounded<FileEvent>(new UnboundedChannelOptions
        {
            SingleReader = false, 
            SingleWriter = false
        });

        // Initialize FileSystemWatchers for all configured local directories
        InitializeWatchers();
        Logger.Log(LogLevel.Info, "Listening for real-time file changes...");

        // Start the background loop to process realtime filesystem events
        var workerTasks = new List<Task>();
        for (int i = 0; i < Config.MaxConcurrentTransfers; i++)
        {
            workerTasks.Add(ProcessEventChannelWorkerAsync(i, cancellationToken));
        }

        // Perform the initial full reconciliation pass (Size + ModTime checks)
        var initialScanTask = Task.Run(async () =>
        {
            Logger.Log(LogLevel.Debug, "Starting initial file reconciliation pass...");
            await ReconcileAndUploadAsync(cancellationToken);

            if (AllowDeletions)
            {
                await CleanupOrphanedRemoteFilesAsync(cancellationToken);
            }
            Logger.Log(LogLevel.Debug, "Initial pass complete.");
        }, cancellationToken);

        try
        {
            // Keep the job alive indefinitely until StopAsync or a cancellation is requested
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Logger.Log(LogLevel.Info, "Job cancellation requested. Shutting down...");
        }
        finally
        {
            // Clean up: Stop watching files and close the queue
            DisposeWatchers();
            _eventChannel.Writer.Complete();

            await Task.WhenAll(workerTasks);
            await initialScanTask;
            Logger.Log(LogLevel.Debug, "Job shut down cleanly.");
        }
    }

    #region Initial Reconciliation Pass

    private async Task ReconcileAndUploadAsync(CancellationToken cancellationToken)
    {
        foreach (string localDir in Config.LocalPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Directory.Exists(localDir))
                continue;

            // Gather all local files in the directory recursively
            string[] localFiles = Directory.GetFiles(localDir, "*", SearchOption.AllDirectories);

            var eligibleFiles = localFiles
                .Where(f => !_ignoreFilter.ShouldIgnore(localDir, f))
                .ToList();

            Interlocked.Add(ref _filesFound, eligibleFiles.Count);
            State.FilesFound = _filesFound;

            Logger.Log(LogLevel.Debug, $"Found {eligibleFiles.Count} eligible files in {localDir}. Checking remote state...");

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Config.MaxConcurrentTransfers,
                CancellationToken = cancellationToken
            };

            await Parallel.ForEachAsync(eligibleFiles, parallelOptions, async (localFilePath, ct) =>
            {
                string remoteFilePath = GetRemotePath(localFilePath, localDir);
                await ReconcileFileAsync(localFilePath, remoteFilePath, ct);

                Interlocked.Increment(ref _filesProcessed);
                State.FilesProcessed = _filesProcessed;
            });
        }
    }

    private async Task CleanupOrphanedRemoteFilesAsync(CancellationToken cancellationToken)
    {
        foreach (string localDir in Config.LocalPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(localDir)) continue;

            string remoteFolderTarget = GetRemotePath(localDir, localDir);

            // Recursively walk the remote directory
            await WalkAndDeleteOrphansAsync(remoteFolderTarget, localDir, cancellationToken);
        }
    }

    private async Task ReconcileFileAsync(string localPath, string remotePath, CancellationToken cancellationToken)
    {
        var localInfo = new FileInfo(localPath);

        // Check if file exists on the cloud
        bool remoteExists = await Provider.FileExistsAsync(remotePath);

        if (!remoteExists)
        {
            Logger.Log(LogLevel.Info, $"[Upload] New file: {localInfo.Name}");
            await UploadFileWithThrottlingAsync(localPath, remotePath, cancellationToken);
            return;
        }

        // Fetch parent files to check the target file metadata
        string parentRemoteFolder = Path.GetDirectoryName(remotePath)?.Replace('\\', '/') ?? "/";
        var remoteFiles = await Provider.ListFilesAsync(parentRemoteFolder);

        string targetName = Path.GetFileName(remotePath);
        CloudFile? targetRemoteFile = remoteFiles.FirstOrDefault(f =>
            f.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase) && !f.IsFolder);

        if (targetRemoteFile != null)
        {
            bool sizeChanged = localInfo.Length != targetRemoteFile.Size;
            bool isLocalNewer = false;

            if (targetRemoteFile.ModifiedDate.HasValue)
            {
                // Round to the nearest second to maintain reliability across different operating systems & APIs
                long localTimeSec = new DateTimeOffset(localInfo.LastWriteTimeUtc).ToUnixTimeSeconds();
                long remoteTimeSec = new DateTimeOffset(targetRemoteFile.ModifiedDate.Value, TimeSpan.Zero).ToUnixTimeSeconds();

                isLocalNewer = localTimeSec > remoteTimeSec;
            }

            if (sizeChanged || isLocalNewer)
            {
                Logger.Log(LogLevel.Info, $"[Update] Modified file: {localInfo.Name}");
                await UploadFileWithThrottlingAsync(localPath, remotePath, cancellationToken);
            }
            else
            {
                Logger.Log(LogLevel.Debug, $"[Skip] File unchanged: {localInfo.Name}");
            }
        }
    }

    private async Task WalkAndDeleteOrphansAsync(string remoteFolder, string localRoot, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var remoteItems = await Provider.ListFilesAsync(remoteFolder);

        foreach (var item in remoteItems)
        {
            string itemRemotePath = $"{remoteFolder.TrimEnd('/')}/{item.Name}";

            if (item.IsFolder)
            {
                // Traverse subdirectories recursively
                await WalkAndDeleteOrphansAsync(itemRemotePath, localRoot, ct);
            }
            else
            {
                // Reverse-map the remote path back to what the local path should be
                string relativePath = itemRemotePath.Substring(Config.RemoteRootPath.Length).TrimStart('/');
                string expectedLocalPath = Path.GetFullPath(Path.Combine(localRoot, relativePath));

                // If the file does not exist locally, it was deleted while the app was offline!
                if (!File.Exists(expectedLocalPath))
                {
                    await Provider.DeleteRemoteFileAsync(itemRemotePath, moveToTrash: true);
                }
                // Alternatively, if it exists locally but the user JUST added an ignore rule for it,
                // we delete the remote copy to respect the new ignore rule.
                else if (_ignoreFilter.ShouldIgnore(localRoot, expectedLocalPath))
                {
                    await Provider.DeleteRemoteFileAsync(itemRemotePath, moveToTrash: true);
                }
            }
        }
    }

    #endregion

    #region File System Watchers & Background Event Queue

    private void InitializeWatchers()
    {
        foreach (string localDir in Config.LocalPaths)
        {
            if (!Directory.Exists(localDir)) continue;

            var watcher = new FileSystemWatcher(localDir)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };

            // Hook up events to push tasks to our async channel
            watcher.Created += (s, e) => QueueEvent(e.FullPath, e.FullPath, localDir, FileEventType.CreatedOrChanged);
            watcher.Changed += (s, e) => QueueEvent(e.FullPath, e.FullPath, localDir, FileEventType.CreatedOrChanged);
            watcher.Deleted += (s, e) => QueueEvent(e.FullPath, e.FullPath, localDir, FileEventType.Deleted);
            watcher.Renamed += (s, e) => QueueEvent(e.OldFullPath, e.FullPath, localDir, FileEventType.Renamed);

            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
    }

    private void QueueEvent(string oldLocalPath, string newLocalPath, string localRootPath, FileEventType type)
    {
        // Directory events themselves are ignored; we reconcile files inside directories on-demand
        if (Directory.Exists(newLocalPath)) return;

        if (type == FileEventType.CreatedOrChanged && _ignoreFilter.ShouldIgnore(localRootPath, newLocalPath))
        {
            return;
        }

        _eventChannel?.Writer.TryWrite(new FileEvent(oldLocalPath, newLocalPath, localRootPath, type));
    }

    /// <summary>
    /// Processes queued file events sequentially.
    /// </summary>
    private async Task ProcessEventChannelWorkerAsync(int workerId, CancellationToken cancellationToken)
    {
        if (_eventChannel == null) return;

        Logger.Log(LogLevel.Debug, $"Started Event Worker #{workerId}");

        await foreach (var fileEvent in _eventChannel.Reader.ReadAllAsync(CancellationToken.None))
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                string remotePath = GetRemotePath(fileEvent.NewLocalPath, fileEvent.LocalRootPath);

                if (fileEvent.Type == FileEventType.Renamed)
                {
                    // Verify the new file isn't meant to be ignored before renaming it on the cloud
                    if (!_ignoreFilter.ShouldIgnore(fileEvent.LocalRootPath, fileEvent.NewLocalPath))
                    {
                        string oldRemotePath = GetRemotePath(fileEvent.OldLocalPath, fileEvent.LocalRootPath);
                        Logger.Log(LogLevel.Info, $"[Rename] {Path.GetFileName(fileEvent.OldLocalPath)} -> {Path.GetFileName(fileEvent.NewLocalPath)}");
                        await Provider.RenameRemoteFileAsync(oldRemotePath, remotePath);
                    }
                }
                else if (fileEvent.Type == FileEventType.CreatedOrChanged)
                {
                    // Basic file stabilization/debounce wait (gives applications time to finish saving)
                    await Task.Delay(2000, cancellationToken);

                    if (File.Exists(fileEvent.NewLocalPath))
                    {
                        await ReconcileFileAsync(fileEvent.NewLocalPath, remotePath, cancellationToken);
                    }
                }
                else if (fileEvent.Type == FileEventType.Deleted)
                {
                    if (AllowDeletions)
                    {
                        Logger.Log(LogLevel.Info, $"[Delete] Remote file: {Path.GetFileName(remotePath)}");
                        await Provider.DeleteRemoteFileAsync(remotePath, moveToTrash: true);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.Error, $"Error processing event for {fileEvent.NewLocalPath}: {ex.Message}");
            }
        }
    }

    private void DisposeWatchers()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();
    }

    #endregion

    #region Helper & Utility Methods

    private async Task UploadFileWithThrottlingAsync(string localPath, string remotePath, CancellationToken cancellationToken)
    {
        // Retry logic in case the file is still locked by the operating system / editor
        const int maxRetries = 3;
        int retryDelayMs = 1000;

        State.ActiveTransfers[localPath] = 0.0;

        int isCompleted = 0;

        var progressReporter = new Progress<double>(percent =>
        {
            if (Volatile.Read(ref isCompleted) == 0)
            {
                State.ActiveTransfers[localPath] = percent;
            }
        });
        try
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    await using var rawFileStream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read);

                    // TODO For now, limiter is null (unthrottled).
                    await using var throttledStream = new ThrottledStream(rawFileStream, limiter: null);

                    await Provider.UploadFileAsync(throttledStream, remotePath, progressReporter, cancellationToken);
                    return;
                }
                catch (IOException) when (i < maxRetries - 1)
                {
                    // File is currently locked; wait a second and retry
                    await Task.Delay(retryDelayMs, cancellationToken);
                }
            }
        }
        finally
        {
            Volatile.Write(ref isCompleted, 1);

            if (!State.ActiveTransfers.TryRemove(localPath, out _))
            {
                Logger.Log(LogLevel.Error, $"ActiveTransefers.TryRemove failed for {localPath}");
            }
            else
            {
                Logger.Log(LogLevel.Debug, $"ActiveTransefers.TryRemove worked for {localPath}");
            }
        }

    }

    private string GetRemotePath(string localPath, string localRootPath)
    {
        string relativePath = Path.GetRelativePath(localRootPath, localPath);
        string normalizedRelativePath = relativePath.Replace('\\', '/');
        return $"{Config.RemoteRootPath.TrimEnd('/')}/{normalizedRelativePath}";
    }

    #endregion

    #region Internal Structs

    private enum FileEventType
    { 
        CreatedOrChanged,
        Deleted,
        Renamed 
    }
    private readonly record struct FileEvent(string OldLocalPath, string NewLocalPath, string LocalRootPath, FileEventType Type);

    #endregion
}