using CelestiCloud.Core.Filtering;
using CelestiCloud.Core.IO;
using CelestiCloud.Core.Models;
using CelestiCloud.Core.Providers;
using System.Threading.Channels;

namespace CelestiCloud.Core.Jobs;

public abstract class LocalToCloudJobBase : JobBase
{
    protected readonly ICloudProvider Provider;
    protected abstract bool AllowDeletions { get; }

    private readonly IgnoreFilter _ignoreFilter;

    private readonly List<FileSystemWatcher> _watchers = [];
    private Channel<FileEvent>? _eventChannel;

    protected LocalToCloudJobBase(JobConfig config, ICloudProvider provider, string appDataPath)
        : base(config, appDataPath)
    {
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _ignoreFilter = new(config.IgnorePatterns);
    }

    /// <summary>
    /// The core execution flow. Runs the initial reconciliation pass, starts the watchers,
    /// and processes new filesystem events continuously.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        // Set up our threadsafe event queue
        _eventChannel = Channel.CreateUnbounded<FileEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,  // We only consume with one background loop
            SingleWriter = false  // Multiple watchers can write simultaneously
        });

        // Perform the initial full reconciliation pass (Size + ModTime checks)
        await ReconcileAndUploadAsync(cancellationToken);

        if (AllowDeletions)
        {
            await CleanupOrphanedRemoteFilesAsync(cancellationToken);
        }

        // Initialize FileSystemWatchers for all configured local directories
        InitializeWatchers();

        // Start the background loop to process real-time filesystem events
        var consumerTask = ProcessEventChannelAsync(cancellationToken);

        try
        {
            // Keep the job alive indefinitely until StopAsync or a cancellation is requested
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        finally
        {
            // Clean up: Stop watching files and close the queue
            DisposeWatchers();
            _eventChannel.Writer.Complete();

            // Wait for the consumer task to finish processing any pending events gracefully
            await consumerTask;
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

            foreach (string localFilePath in localFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_ignoreFilter.ShouldIgnore(localDir, localFilePath))
                    continue;

                string remoteFilePath = GetRemotePath(localFilePath, localDir);
                await ReconcileFileAsync(localFilePath, remoteFilePath, cancellationToken);
            }
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
                await UploadFileWithThrottlingAsync(localPath, remotePath, cancellationToken);
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
    private async Task ProcessEventChannelAsync(CancellationToken cancellationToken)
    {
        if (_eventChannel == null) return;

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
                        await Provider.RenameRemoteFileAsync(oldRemotePath, remotePath);
                    }
                }
                else if (fileEvent.Type == FileEventType.CreatedOrChanged)
                {
                    // Basic file stabilization/debounce wait (gives applications like MS Office time to finish saving)
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
                        await Provider.DeleteRemoteFileAsync(remotePath, moveToTrash: true);
                    }
                }
            }
            catch (Exception)
            {
                // In a production app, we would log individual file errors to a status reporter
                // and keep the thread running rather than letting it crash the job.
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

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                await using var rawFileStream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read);

                // Wrap in ThrottledStream. For now, limiter is null (unthrottled).
                // When we add global limits, we will grab the shared rate limiter.
                await using var throttledStream = new ThrottledStream(rawFileStream, limiter: null);

                await Provider.UploadFileAsync(throttledStream, remotePath);
                return;
            }
            catch (IOException) when (i < maxRetries - 1)
            {
                // File is currently locked; wait a second and retry
                await Task.Delay(retryDelayMs, cancellationToken);
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