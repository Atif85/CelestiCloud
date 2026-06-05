using CelestiCloud.Core.Config;
using CelestiCloud.Core.Filtering;
using CelestiCloud.Core.IO;
using CelestiCloud.Core.Logging;
using CelestiCloud.Core.Models;
using CelestiCloud.Core.Providers;
using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Threading.RateLimiting;

namespace CelestiCloud.Core.Jobs.LocalToCloudJobs;

public abstract class LocalToCloudJobBase : JobBase
{
    protected readonly ICloudProvider Provider;
    protected readonly RateLimiter? Limiter;
    protected readonly IJobLogger Logger;
    private readonly int _safeChunkSize;
    protected abstract bool AllowDeletions { get; }

    private readonly IgnoreFilter _ignoreFilter;
    private readonly List<FileSystemWatcher> _watchers = [];
    private Channel<FileEvent>? _eventChannel;

    private readonly SemaphoreSlim _networkCheckLock = new(1, 1);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _debounceTicks = [];
    private readonly ConcurrentDictionary<string, Dictionary<string, CloudFile>> _remoteDirectoryCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly SemaphoreSlim[] _stripedLocks = [.. Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1))];

    private int _filesFound;
    private int _filesProcessed;
    private readonly Dictionary<string, string> _folderNameCache = new(StringComparer.OrdinalIgnoreCase);

    protected LocalToCloudJobBase(JobConfig config, ICloudProvider provider, string appDataPath, RateLimiter? limiter, int safeChunkSize, IJobLogger logger)
        : base(config, appDataPath)
    {
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        Limiter = limiter;
        _safeChunkSize = safeChunkSize;
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
    protected override async Task ExecuteAsync(CancellationToken ct)
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
            workerTasks.Add(ProcessEventChannelWorkerAsync(i, ct));
        }

        var periodicReconciliationTask = Task.Run(async () =>
        {
            try
            {
                var interval = TimeSpan.FromMinutes(Config.ReconciliationIntervalMinutes > 0 ? Config.ReconciliationIntervalMinutes : 10);

                while (!ct.IsCancellationRequested)
                {
                    Logger.Log(LogLevel.Debug, "Starting file reconciliation pass...");
                    _remoteDirectoryCache.Clear();

                    bool passSuccessful = false;

                    while (!passSuccessful)
                    {
                        ct.ThrowIfCancellationRequested();

                        try
                        {
                            await ReconcileAndUploadAsync(ct);

                            if (AllowDeletions)
                            {
                                await CleanupOrphanedRemoteFilesAsync(ct);
                            }

                            passSuccessful = true;
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex) when (IsNetworkException(ex))
                        {
                            Logger.Log(LogLevel.Warning, "Network connection lost during reconciliation pass. Pausing sync loop...");
                            await WaitForConnectionAsync(ct);
                        }
                        catch (Exception ex)
                        {
                            Logger.Log(LogLevel.Error, $"Critical error during reconciliation pass: {ex.Message}");
                            passSuccessful = true;
                        }
                    }

                    _remoteDirectoryCache.Clear();
                    Logger.Log(LogLevel.Debug, "Memory optimized: Cleared remote directory metadata cache.");
                    Logger.Log(LogLevel.Debug, $"Pass complete. Next full run scheduled in {interval.TotalMinutes} minutes.");

                    await Task.Delay(interval, ct);
                }
            }
            catch (OperationCanceledException)
            {
                Logger.Log(LogLevel.Debug, "Periodic reconciliation loop stopped cleanly.");
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.Error, $"Reconciliation loop encountered a fatal error: {ex.Message}");
            }
        });

        try
        {
            // Keep the job alive indefinitely until StopAsync or a cancellation is requested
            await Task.Delay(Timeout.Infinite, ct);
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
            await periodicReconciliationTask;
            Logger.Log(LogLevel.Debug, "Job shut down cleanly.");
        }
    }

    #region Initial Reconciliation Pass

    private async Task ReconcileAndUploadAsync(CancellationToken cancellationToken)
    {
        _filesFound = 0;
        _filesProcessed = 0;
        State.FilesFound = 0;
        State.FilesProcessed = 0;

        var scanChannel = Channel.CreateBounded<ScanFileEvent>(new BoundedChannelOptions(5000)
        {
            SingleReader = false,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        // Spawn concurrent upload/reconciliation workers
        var workerTasks = new List<Task>();
        int concurrency = Config.MaxConcurrentTransfers;

        for (int i = 0; i < concurrency; i++)
        {
            workerTasks.Add(ProcessScanChannelWorkerAsync(scanChannel.Reader, cancellationToken));
        }

        // Start the single-threaded directory crawler to stream files into the queue
        try
        {
            foreach (string localDir in Config.LocalPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!Directory.Exists(localDir))
                {
                    string messege = $"Local root directory is missing: '{localDir}'. Halting job to prevent accidental cloud deletions.";
                    Logger.Log(LogLevel.Error, messege);

                    RequestNotification("Missing Directory", messege, "error");
                    _ = StopAsync();
                    return;
                }

                Logger.Log(LogLevel.Debug, $"Streaming file discovery for '{localDir}'...");
                await CrawlDirectoryAndQueueAsync(localDir, localDir, scanChannel.Writer, cancellationToken);
            }
        }
        finally
        {
            // Always complete the writer. This tells the workers that no more files
            // are coming, allowing them to exit their loops gracefully once the queue is empty.
            scanChannel.Writer.Complete();
        }

        // Wait for all concurrent workers to finish processing the backlog
        await Task.WhenAll(workerTasks);
    }

    private async Task CrawlDirectoryAndQueueAsync(string currentLocalDir, string localRootDir,
                                                   ChannelWriter<ScanFileEvent> writer,
                                                   CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Directory Pruning
        if (_ignoreFilter.ShouldIgnore(localRootDir, currentLocalDir))
        {
            return;
        }

        // Enumerate and queue files in the current folder
        try
        {
            foreach (string localFilePath in Directory.EnumerateFiles(currentLocalDir))
            {
                ct.ThrowIfCancellationRequested();

                if (!_ignoreFilter.ShouldIgnore(localRootDir, localFilePath))
                {
                    int currentFound = Interlocked.Increment(ref _filesFound);
                    State.FilesFound = currentFound;

                    await writer.WriteAsync(new ScanFileEvent(localFilePath, localRootDir), ct);
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            Logger.Log(LogLevel.Warning, $"Access denied to directory: '{currentLocalDir}'. Skipping files inside.");
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }

        // Recursively walk subdirectories
        try
        {
            var subDirectories = Directory.EnumerateDirectories(currentLocalDir);
            foreach (string subDir in subDirectories)
            {
                ct.ThrowIfCancellationRequested();
                await CrawlDirectoryAndQueueAsync(subDir, localRootDir, writer, ct);
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (DirectoryNotFoundException) { }
        catch (Exception ex)
        {
            Logger.Log(LogLevel.Error, $"Error reading subdirectories of '{currentLocalDir}': {ex.Message}");
        }
    }

    private async Task ProcessScanChannelWorkerAsync(ChannelReader<ScanFileEvent> reader, CancellationToken ct)
    {
        try
        {
            await foreach (var fileEvent in reader.ReadAllAsync(ct))
            {
                bool success = false;

                while (!success)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        string remoteFilePath = GetRemotePath(fileEvent.LocalPath, fileEvent.LocalRootPath);
                        await ReconcileFileAsync(fileEvent.LocalPath, remoteFilePath, ct);

                        success = true;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex) when (IsNetworkException(ex))
                    {
                        // Catch network drops, hold the thread, and retry this exact file once online
                        Logger.Log(LogLevel.Warning, $"[Network Interruption] Failed to process '{Path.GetFileName(fileEvent.LocalPath)}'. Retrying once online...");
                        await WaitForConnectionAsync(ct);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log(LogLevel.Error, $"Error reconciling file '{fileEvent.LocalPath}': {ex.Message}");
                        success = true;
                    }
                }

                // Progress is only updated after successful processing
                int currentProcessed = Interlocked.Increment(ref _filesProcessed);
                State.FilesProcessed = currentProcessed;
            }
        }
        catch (OperationCanceledException)
        {
            // Worker stopped cleanly
        }
    }

    private async Task ReconcileFileAsync(string localPath, string remotePath, CancellationToken ct)
    {
        var localInfo = new FileInfo(localPath);
        string parentRemoteFolder = Path.GetDirectoryName(remotePath)?.Replace('\\', '/') ?? "/";
        string fileName = Path.GetFileName(remotePath);

        // Fetch directories using local cache
        var directoryFiles = await GetCachedRemoteDirectoryAsync(parentRemoteFolder, ct);

        // If file doesn't exist in our folder metadata cache, treat it as new
        if (!directoryFiles.TryGetValue(fileName, out var targetRemoteFile) || targetRemoteFile.IsFolder)
        {
            Logger.Log(LogLevel.Info, $"[Upload] New file: {localInfo.Name}");
            await UploadFileWithThrottlingAsync(localPath, remotePath, null, assumeNew: true, ct);
            return;
        }

        // Compare sizes and modification dates
        bool sizeChanged = localInfo.Length != targetRemoteFile.Size;
        bool isLocalNewer = false;

        if (targetRemoteFile.ModifiedDate.HasValue)
        {
            long localTimeSec = new DateTimeOffset(localInfo.LastWriteTimeUtc).ToUnixTimeSeconds();
            long remoteTimeSec = new DateTimeOffset(targetRemoteFile.ModifiedDate.Value, TimeSpan.Zero).ToUnixTimeSeconds();

            isLocalNewer = localTimeSec > remoteTimeSec;
        }

        if (sizeChanged || isLocalNewer)
        {
            Logger.Log(LogLevel.Info, $"[Update] Modified file: {localInfo.Name}");
            await UploadFileWithThrottlingAsync(localPath, remotePath, targetRemoteFile.Id, assumeNew: false, ct);
        }
        else
        {
            Logger.Log(LogLevel.Debug, $"[Skip] File unchanged: {localInfo.Name}");
        }
    }

    private async Task<Dictionary<string, CloudFile>> GetCachedRemoteDirectoryAsync(string remoteFolderPath, CancellationToken ct)
    {
        if (_remoteDirectoryCache.TryGetValue(remoteFolderPath, out var cachedDir))
        {
            return cachedDir;
        }

        var directoryLock = GetStripedLock(remoteFolderPath);
        await directoryLock.WaitAsync(ct);
        try
        {
            if (_remoteDirectoryCache.TryGetValue(remoteFolderPath, out cachedDir))
            {
                return cachedDir;
            }

            var fileMap = new Dictionary<string, CloudFile>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var files = await Provider.ListFilesAsync(remoteFolderPath);

                if (files != null)
                {
                    foreach (var file in files)
                    {
                        fileMap[file.Name] = file;
                    }
                }
            }
            catch (DirectoryNotFoundException)
            {
                
            }
            catch (Exception ex)
            {
                if (IsNetworkException(ex))
                {
                    throw;
                }

                Logger.Log(LogLevel.Error, $"Failed to list remote directory '{remoteFolderPath}': {ex.Message}");
            }

            _remoteDirectoryCache[remoteFolderPath] = fileMap;
            return fileMap;
        }
        finally
        {
            directoryLock.Release();
        }
    }

    private void InvalidateRemoteDirectoryCache(string remotePath)
    {
        string parentRemoteFolder = Path.GetDirectoryName(remotePath)?.Replace('\\', '/') ?? "/";
        _remoteDirectoryCache.TryRemove(parentRemoteFolder, out _);
    }

    private async Task CleanupOrphanedRemoteFilesAsync(CancellationToken cancellationToken)
    {
        foreach (string localDir in Config.LocalPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(localDir)) continue;

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

        var remoteItemsDict = await GetCachedRemoteDirectoryAsync(currentRemoteFolder, ct);

        foreach (var kvp in remoteItemsDict)
        {
            var item = kvp.Value;
            string itemRemotePath = $"{currentRemoteFolder.TrimEnd('/')}/{item.Name}";

            if (item.IsFolder)
            {
                await WalkAndDeleteOrphansAsync(itemRemotePath, originalRemoteRoot, originalLocalRoot, ct);
            }
            else
            {
                string relativePath = itemRemotePath.Substring(originalRemoteRoot.Length).TrimStart('/');
                string expectedLocalPath = Path.GetFullPath(Path.Combine(originalLocalRoot, relativePath));

                if (!File.Exists(expectedLocalPath))
                {
                    Logger.Log(LogLevel.Info, $"[Cleanup] Removing orphaned remote file: {item.Name}");
                    await Provider.DeleteRemoteFileAsync(itemRemotePath, moveToTrash: true);
                    InvalidateRemoteDirectoryCache(itemRemotePath);
                }
                else if (_ignoreFilter.ShouldIgnore(originalLocalRoot, expectedLocalPath))
                {
                    Logger.Log(LogLevel.Info, $"[Cleanup] Removing newly ignored remote file: {item.Name}");
                    await Provider.DeleteRemoteFileAsync(itemRemotePath, moveToTrash: true);
                    InvalidateRemoteDirectoryCache(itemRemotePath);
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

            watcher.Error += (sender, args) =>
            {
                var exception = args.GetException();
                Logger.Log(LogLevel.Error, $"Critical error on watcher for '{localDir}': {exception?.Message ?? "Directory unmounted or deleted"}. Halting job.");

                _ = StopAsync();
            };

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

        try
        {
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
                            bool oldExistsOnCloud = await Provider.FileExistsAsync(oldRemotePath);

                            if (oldExistsOnCloud)
                            {
                                Logger.Log(LogLevel.Info, $"[Rename] {Path.GetFileName(fileEvent.OldLocalPath)} -> {Path.GetFileName(fileEvent.NewLocalPath)}");
                                await Provider.RenameRemoteFileAsync(oldRemotePath, remotePath);
                                InvalidateRemoteDirectoryCache(oldRemotePath);
                                InvalidateRemoteDirectoryCache(remotePath);
                            }
                            else
                            {
                                // Fallback
                                Logger.Log(LogLevel.Debug, $"[Rename Fallback] Old remote file not found: '{oldRemotePath}'. Uploading new file '{Path.GetFileName(fileEvent.NewLocalPath)}' instead.");

                                if (File.Exists(fileEvent.NewLocalPath))
                                {
                                    await ReconcileFileAsync(fileEvent.NewLocalPath, remotePath, cancellationToken);
                                }
                            }
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
                            InvalidateRemoteDirectoryCache(remotePath);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.Log(LogLevel.Error, $"Error processing event for {fileEvent.NewLocalPath}: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Worker stopped cleanly
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

    private async Task UploadFileWithThrottlingAsync(string localPath, string remotePath, string? existingFileId, bool assumeNew, CancellationToken ct)
    {
        // Retry logic in case the file is still locked by the operating system / editor
        const int maxRetries = 3;
        int retryDelayMs = 1000;

        object progressLock = new();
        bool isFinished = false;

        State.ActiveTransfers[localPath] = 0.0;

        var progressReporter = new SyncProgress<double>(percent =>
        {
            lock (progressLock)
            {
                // Only update the dictionary if the finally block hasn't cleared it [1.3.2]
                if (!isFinished)
                {
                    State.ActiveTransfers[localPath] = percent;
                }
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

                    await using var throttledStream = new ThrottledStream(rawFileStream, Limiter, _safeChunkSize);

                    await Provider.UploadFileAsync(throttledStream, remotePath, existingFileId, assumeNew, progressReporter, ct);
                    InvalidateRemoteDirectoryCache(remotePath);
                    return;
                }
                catch (OperationCanceledException)
                {
                    Logger.Log(LogLevel.Debug, $"Upload of '{Path.GetFileName(localPath)}' was canceled by user.");
                    return;
                }
                catch (Exception ex) when (ex.ToString().Contains("storageQuotaExceeded", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Log(LogLevel.Error, "Google Drive storage quota has been reached.");

                    RequestNotification(
                        "Storage Full",
                        "Your Google Drive storage is full. Please free up space to resume syncing.",
                        "error"
                    );

                    _ = StopAsync(); // Safely halt the job

                }
                catch (IOException) when (i < maxRetries - 1)
                {
                    // File is currently locked; wait a second and retry
                    await Task.Delay(retryDelayMs, ct);
                }
            }
        }
        finally
        {
            isFinished = true;

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

    private static string ResolveFolderName(string localRootPath)
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

    private SemaphoreSlim GetStripedLock(string path)
    {
        int hash = path.GetHashCode(StringComparison.OrdinalIgnoreCase);
        int index = Math.Abs(hash) % _stripedLocks.Length;
        return _stripedLocks[index];
    }

    #endregion

    #region Network Errors

    private static bool IsNetworkException(Exception ex)
    {
        if (ex is HttpRequestException || ex is TimeoutException || ex is System.Net.Sockets.SocketException)
            return true;

        if (ex is TaskCanceledException tce && !tce.CancellationToken.IsCancellationRequested)
        {
            // HttpClient timeout
            return true;
        }

        // Unwrap InnerExceptions recursively
        if (ex.InnerException != null)
        {
            return IsNetworkException(ex.InnerException);
        }

        return false;
    }

    private static bool IsNetworkAvailable()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = client.Send(new HttpRequestMessage(HttpMethod.Get, "https://www.google.com"));

            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task WaitForConnectionAsync(CancellationToken ct)
    {
        bool isPrimaryChecker = await _networkCheckLock.WaitAsync(0, ct);

        if (!isPrimaryChecker)
        {
            // This is a secondary thread. It waits on the lock until the primary thread 
            // releases it (which only happens after connection is restored)
            await _networkCheckLock.WaitAsync(ct);
            _networkCheckLock.Release();
            return;
        }

        try
        {
            Logger.Log(LogLevel.Warning, "Network connection lost. Pausing active transfers...");

            while (!ct.IsCancellationRequested)
            {
                if (IsNetworkAvailable())
                {
                    Logger.Log(LogLevel.Info, "Network connection restored. Resuming transfers...");
                    return;
                }

                await Task.Delay(5000, ct);
            }
        }
        finally
        {
            // Release the gate lock so all waiting threads can resume work concurrently [1.2.1]
            _networkCheckLock.Release();
        }
    }
    #endregion

    #region Internal Structs

    private readonly record struct ScanFileEvent(string LocalPath, string LocalRootPath);
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
