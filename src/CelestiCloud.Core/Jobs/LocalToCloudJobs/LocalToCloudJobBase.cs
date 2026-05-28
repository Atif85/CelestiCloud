using CelestiCloud.Core.Filtering;
using CelestiCloud.Core.IO;
using CelestiCloud.Core.Logging;
using CelestiCloud.Core.Models;
using CelestiCloud.Core.Providers;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace CelestiCloud.Core.Jobs;

public abstract class LocalToCloudJobBase : JobBase
{
    protected readonly ICloudProvider Provider;
    protected readonly IJobLogger Logger;
    protected abstract bool AllowDeletions { get; }

    private readonly IgnoreFilter _ignoreFilter;

    private readonly List<FileSystemWatcher> _watchers = [];
    private Channel<FileEvent>? _eventChannel;

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _debounceTicks = [];

    private int _filesFound;
    private int _filesProcessed;

    private readonly Dictionary<string, string> _folderNameCache = new(StringComparer.OrdinalIgnoreCase);

    protected LocalToCloudJobBase(JobConfig config, ICloudProvider provider, string appDataPath, IJobLogger logger)
        : base(config, appDataPath)
    {
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        Logger = logger;
        _ignoreFilter = new(config.IgnorePatterns);

        // Precompute and cache root folder names
        foreach (string localDir in config.LocalPaths)
        {
            string resolvedName = ResolveFolderName(localDir);
            _folderNameCache[localDir] = resolvedName;

            // Log this once on startup as a Debug message instead of millions of times during transfers
            Logger.Log(LogLevel.Debug, $"[Cache] Mapped local root '{localDir}' to folder name '{resolvedName}'");
        }
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

            foreach (var key in _debounceTicks.Keys)
            {
                CancelPendingDebounce(key);
            }
            _debounceTicks.Clear();

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
    private async Task CleanupOrphanedRemoteFilesAsync(CancellationToken cancellationToken)
    {
        foreach (string localDir in Config.LocalPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(localDir)) continue;

            // Calculate the root cloud target folder once (e.g., "/CelestiCloud/Downloads")
            string remoteFolderTarget = GetRemotePath(localDir, localDir);

            Logger.Log(LogLevel.Debug, $"Walking remote path '{remoteFolderTarget}' to clean offline deletions...");

            // Start recursion, passing the unchanging original roots
            await WalkAndDeleteOrphansAsync(
                currentRemoteFolder: remoteFolderTarget,
                originalRemoteRoot: remoteFolderTarget,
                originalLocalRoot: localDir,
                cancellationToken);
        }
    }
    private async Task WalkAndDeleteOrphansAsync(
        string currentRemoteFolder,
        string originalRemoteRoot,
        string originalLocalRoot,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var remoteItems = await Provider.ListFilesAsync(currentRemoteFolder);

        foreach (var item in remoteItems)
        {
            // Simple append to find the absolute remote path for this item
            string itemRemotePath = $"{currentRemoteFolder.TrimEnd('/')}/{item.Name}";

            if (item.IsFolder)
            {
                // Traverse subdirectories recursively, preserving the original absolute roots!
                await WalkAndDeleteOrphansAsync(itemRemotePath, originalRemoteRoot, originalLocalRoot, ct);
            }
            else
            {
                // Safe, absolute reverse-mapping using the unchanging originalRemoteRoot
                string relativePath = itemRemotePath.Substring(originalRemoteRoot.Length).TrimStart('/');
                string expectedLocalPath = Path.GetFullPath(Path.Combine(originalLocalRoot, relativePath));

                if (!File.Exists(expectedLocalPath))
                {
                    Logger.Log(LogLevel.Info, $"[Cleanup] Removing orphaned remote file: {item.Name}");
                    await Provider.DeleteRemoteFileAsync(itemRemotePath, moveToTrash: true);
                }
                else if (_ignoreFilter.ShouldIgnore(originalLocalRoot, expectedLocalPath))
                {
                    Logger.Log(LogLevel.Info, $"[Cleanup] Removing newly ignored remote file: {item.Name}");
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
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
            };

            // Hook up events to push tasks to our async channel
            watcher.Created += (s, e) => QueueEvent(e.FullPath, e.FullPath, localDir, FileEventType.Created);
            watcher.Changed += (s, e) => QueueEvent(e.FullPath, e.FullPath, localDir, FileEventType.Changed);
            watcher.Deleted += (s, e) => QueueEvent(e.FullPath, e.FullPath, localDir, FileEventType.Deleted);
            watcher.Renamed += (s, e) => QueueEvent(e.OldFullPath, e.FullPath, localDir, FileEventType.Renamed);

            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
    }

    private void QueueEvent(string oldLocalPath, string newLocalPath, string localRootPath, FileEventType type)
    {
        bool isDirectory = Directory.Exists(newLocalPath);

        if (type == FileEventType.Changed && isDirectory)
        {
            return;
        }

        if (type == FileEventType.Created && isDirectory)
        {
            try
            {
                // Recursively find all files in the newly restored/created directory
                string[] files = Directory.GetFiles(newLocalPath, "*", SearchOption.AllDirectories);
                Logger.Log(LogLevel.Debug, $"Directory created/restored: '{newLocalPath}'. Queueing {files.Length} files.");

                foreach (string file in files)
                {
                    // Unpack and queue each file as an individual Create event
                    QueueEvent(file, file, localRootPath, FileEventType.Created);
                }
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.Error, $"Error scanning newly created/restored directory '{newLocalPath}': {ex.Message}");
            }
            return;
        }

        if (type == FileEventType.Created || type == FileEventType.Changed)
        {
            if (_ignoreFilter.ShouldIgnore(localRootPath, newLocalPath))
            {
                return;
            }

            // Debounce: Cancel any existing pending timer for this exact path
            CancelPendingDebounce(newLocalPath);

            var cts = new CancellationTokenSource();
            _debounceTicks[newLocalPath] = cts;

            // Spawn a delayed task to push the event only after the file goes quiet
            _ = Task.Run(async () =>
            {
                try
                {
                    // Wait for the filesystem to stabilize (2 seconds)
                    await Task.Delay(2000, cts.Token);

                    if (_debounceTicks.TryRemove(newLocalPath, out _))
                    {
                        _eventChannel?.Writer.TryWrite(new FileEvent(oldLocalPath, newLocalPath, localRootPath, type));
                    }
                }
                catch (TaskCanceledException)
                {
                    // Suppressed: A newer event took over or the file was deleted/renamed
                }
            });
        }
        else
        {
            // For Renames or Deletes, immediately cancel any pending Creation/Change timers
            CancelPendingDebounce(oldLocalPath);
            CancelPendingDebounce(newLocalPath);

            _eventChannel?.Writer.TryWrite(new FileEvent(oldLocalPath, newLocalPath, localRootPath, type));
        }
    }

    private void CancelPendingDebounce(string path)
    {
        if (_debounceTicks.TryRemove(path, out var cts))
        {
            try
            {
                cts.Cancel();
                cts.Dispose();
            }
            catch { /* Suppress */ }
        }

        string folderPrefix = path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
        foreach (var key in _debounceTicks.Keys)
        {
            if (key.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
            {
                if (_debounceTicks.TryRemove(key, out var childCts))
                {
                    try
                    {
                        childCts.Cancel();
                        childCts.Dispose();
                    }
                    catch { /* Suppress */ }
                }
            }
        }
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
                else if (fileEvent.Type == FileEventType.Created || fileEvent.Type == FileEventType.Changed)
                {
                    if (File.Exists(fileEvent.NewLocalPath))
                    {
                        await ReconcileFileAsync(fileEvent.NewLocalPath, remotePath, cancellationToken);
                    }
                }
                else if (fileEvent.Type == FileEventType.Deleted)
                {
                    if (AllowDeletions)
                    {
                        Logger.Log(LogLevel.Info, $"[Delete] Remote item: {Path.GetFileName(remotePath)}");
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
                    await using var rawFileStream = new FileStream(
                        localPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);

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
        }

    }

    private string GetRemotePath(string localPath, string localRootPath)
    {
        if (!_folderNameCache.TryGetValue(localRootPath, out string? folderName))
        {
            // Fallback safety check
            // Fallback safety check
            folderName = ResolveFolderName(localRootPath);
        }

        // Calculate the relative path from that local root to the specific file
        string relativePath = Path.GetRelativePath(localRootPath, localPath);

        // Normalize path separators to forward slashes for the cloud API
        string normalizedRelativePath = relativePath.Replace('\\', '/');

        // Prepend the folder name to preserve directory structures inside the cloud root
        string remoteRoot = Config.RemoteRootPath.TrimEnd('/');

        if (normalizedRelativePath == ".")
        {
            // This handles cases where we are resolving the root folder itself
            return $"{remoteRoot}/{folderName}";
        }

        return $"{remoteRoot}/{folderName}/{normalizedRelativePath}";
    }

    private string ResolveFolderName(string localRootPath)
    {
        // Extract the local folder leaf name (e.g., "Downloads")
        string folderName = Path.GetFileName(localRootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        // Fallback if the path is a root drive (e.g., "D:\") to prevent blank folder names
        if (string.IsNullOrEmpty(folderName))
        {
            folderName = localRootPath.Replace(":", "").Replace("\\", "").Replace("/", "").Trim();
            if (string.IsNullOrEmpty(folderName)) folderName = "Root";
        }

        return folderName;
    }


    #endregion

    #region Internal Structs

    private enum FileEventType
    {
        Created, 
        Changed,
        Deleted,
        Renamed
    }
    private readonly record struct FileEvent(string OldLocalPath, string NewLocalPath, string LocalRootPath, FileEventType Type);

    #endregion
}