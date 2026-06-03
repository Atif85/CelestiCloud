using CelestiCloud.Core.Models;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using System.Collections.Concurrent;
using System.Reflection;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace CelestiCloud.Core.Providers;
public class GoogleDriveProvider : ICloudProvider
{
    public string ProviderName => "Google Drive";
    private const string APP_NAME = "CelestiCloud";
    private readonly string _tokenDirectoryPath;

    private DriveService? _service;

    private readonly ConcurrentDictionary<string, string> _folderCache = new();
    private readonly SemaphoreSlim _folderLock = new(1, 1);
    private readonly ConcurrentDictionary<string, Task<string?>> _folderResolutionTasks = new(StringComparer.OrdinalIgnoreCase);

    private const string FolderMimeType = "application/vnd.google-apps.folder";
    private static readonly string[] Scopes = 
    [
        DriveService.Scope.DriveFile
    ];

    public GoogleDriveProvider(string tokenDirectoryPath)
    {
        _tokenDirectoryPath = tokenDirectoryPath;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        UserCredential credential;

        // Load the credentials
        var assembly = Assembly.GetExecutingAssembly();
        
        string resourceName = "CelestiCloud.Core.credentials.json";

        await using (var stream = assembly.GetManifestResourceStream(resourceName))
        {
            if (stream == null)
            {
                throw new InvalidOperationException(
                    "FATAL: credentials.json was not found embedded in the application binary.");
            }

            // Authorize using the embedded stream
            credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                GoogleClientSecrets.FromStream(stream).Secrets,
                Scopes,
                "user",
                cancellationToken,
                new FileDataStore(_tokenDirectoryPath, true));
        }

        // Create Drive API service.
        _service = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = APP_NAME
        });
    }

    public async Task RevokeAccessAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        if (_service?.HttpClientInitializer is UserCredential credential)
        {
            await credential.RevokeTokenAsync(cancellationToken);
        }
    }

    public async Task<string> GetAuthenticatedUserEmailAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        var request = _service!.About.Get();
        request.Fields = "user(emailAddress)";

        var about = await request.ExecuteAsync(cancellationToken);
        return about.User?.EmailAddress ?? "unknown-email";
    }

    public async Task UploadFileAsync(Stream sourceStream, string remotePath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        EnsureConnected();
      
        // Split the remote path to get the parent folder path and the file name
        string[] segments = remotePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

        string fileName = segments.Last();
        string parentPath = string.Join("/", segments.Take(segments.Length - 1));

        // Ensure the remote directory exists (creates it if it doesn't)
        string parentFolderId = await ResolvePathToIdAsync(parentPath, createIfMissing: true)
                                ?? throw new Exception("Failed to resolve or create remote parent folder.");

        // Check if file already exists so we know whether to Create or Update
        string? existingFileId = await ResolvePathToIdAsync(remotePath);

        bool remoteFileExists = existingFileId != null;

        var fileMetadata = new DriveFile { Name = fileName };
        string mimeType = GetMimeType(remotePath);

        ResumableUpload<DriveFile, DriveFile> uploadRequest;

        if (remoteFileExists)
        {
            uploadRequest = _service!.Files.Update(fileMetadata, existingFileId, sourceStream, mimeType);
        }
        else
        {
            fileMetadata.Parents = [parentFolderId];
            uploadRequest = _service!.Files.Create(fileMetadata, sourceStream, mimeType);
        }

        uploadRequest.ChunkSize = ResumableUpload.MinimumChunkSize * 2;

        // Attach progress reporter if provided
        if (progress != null)
        {
            long fileLength = sourceStream.CanSeek ? sourceStream.Length : 0;
            uploadRequest.ProgressChanged += uploadProgress =>
            {
                if (uploadProgress.Status == UploadStatus.Uploading)
                {
                    double percentage = uploadProgress.BytesSent / ((double)fileLength);
                    progress.Report(percentage);
                }
            };
        }

        var response = await uploadRequest.UploadAsync(cancellationToken);

        if (response.Status == UploadStatus.Failed)
        {
            if (response.Exception is OperationCanceledException || cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("Upload was canceled by the user.", response.Exception, cancellationToken);
            }

            throw new Exception($"Upload failed: {response.Exception?.Message}", response.Exception);
        }

        progress?.Report(1.0); // 100% complete
    }

    public async Task RenameRemoteFileAsync(string oldRemotePath, string newRemotePath)
    {
        EnsureConnected();

        string? fileId = await ResolvePathToIdAsync(oldRemotePath) 
            ?? throw new FileNotFoundException($"Cannot rename, remote file not found: {oldRemotePath}");

        string newName = Path.GetFileName(newRemotePath);
        string oldParentPath = Path.GetDirectoryName(oldRemotePath)?.Replace('\\', '/') ?? "/";
        string newParentPath = Path.GetDirectoryName(newRemotePath)?.Replace('\\', '/') ?? "/";

        var updateRequest = _service!.Files.Update(new DriveFile { Name = newName }, fileId);

        // If the file was moved to a different folder, we update the parents
        if (oldParentPath != newParentPath)
        {
            string? oldParentId = await ResolvePathToIdAsync(oldParentPath);
            string? newParentId = await ResolvePathToIdAsync(newParentPath, createIfMissing: true);

            if (oldParentId != null && newParentId != null)
            {
                updateRequest.RemoveParents = oldParentId;
                updateRequest.AddParents = newParentId;
            }
        }

        await updateRequest.ExecuteAsync();

        InvalidateCache(oldRemotePath);
    }


    public async Task DownloadFileAsync(string remotePath, Stream destinationStream, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        string? remoteFileId = await ResolvePathToIdAsync(remotePath);
        if (remoteFileId == null) 
            throw new FileNotFoundException($"Remote file not found: {remotePath}");

        long fileSize = 0;
        if (progress != null)
        {
            var metaRequest = _service!.Files.Get(remoteFileId);
            metaRequest.Fields = "size";
            var fileMeta = await metaRequest.ExecuteAsync();
            fileSize = fileMeta.Size ?? 0;
        }

        var request = _service!.Files.Get(remoteFileId);

        if (progress != null && fileSize > 0)
        {
            request.MediaDownloader.ProgressChanged += (Google.Apis.Download.IDownloadProgress downloadProgress) =>
            {
                if (downloadProgress.Status == Google.Apis.Download.DownloadStatus.Downloading)
                {
                    double percentage = (double)downloadProgress.BytesDownloaded / fileSize;
                    progress.Report(Math.Min(percentage, 1.0)); // Cap at 1.0 just in case
                }
            };
        }

        var response = await request.DownloadAsync(destinationStream, cancellationToken);

        if (response.Status == Google.Apis.Download.DownloadStatus.Failed)
        {
            throw new Exception($"Download failed: {response.Exception?.Message}", response.Exception);
        }

        progress?.Report(1.0); // 100% complete
    }

    public async Task DeleteRemoteFileAsync(string remotePath, bool moveToTrash = true)
    {
        EnsureConnected();

        string? remoteFileId = await ResolvePathToIdAsync(remotePath);

        if (remoteFileId == null) return;

        if (moveToTrash)
        {
            DriveFile fileMetadata = new() { Trashed = true };
            var request = _service!.Files.Update(fileMetadata, remoteFileId);
            await request.ExecuteAsync();
        }
        else
        {
            var request = _service!.Files.Delete(remoteFileId);
            await request.ExecuteAsync();
        }

        InvalidateCache(remotePath);
    }

    public async Task<IEnumerable<CloudFile>> ListFilesAsync(string remotePath)
    {
        EnsureConnected();

        string? folderId = await ResolvePathToIdAsync(remotePath);
        if (folderId == null) return [];

        var request = _service!.Files.List();
        request.Q = $"'{folderId}' in parents and trashed = false";
        request.Fields = "files(id, name, mimeType, size, modifiedTime)";

        var result = await request.ExecuteAsync();
        var files = new List<CloudFile>();

        foreach (var file in result.Files)
        {
            files.Add(new CloudFile
            {
                Id = file.Id,
                Name = file.Name,
                IsFolder = file.MimeType == FolderMimeType,
                Size = file.Size,
                ModifiedDate = file.ModifiedTimeDateTimeOffset?.DateTime
            });
        }

        return files;
    }

    public async Task<bool> FileExistsAsync(string remotePath)
    {
        EnsureConnected();
        string? id = await ResolvePathToIdAsync(remotePath, false);

        return id != null;
    }

    private async Task<string?> ResolvePathToIdAsync(string remotePath, bool createIfMissing = false)
    {
        string[] segments = remotePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return "root";

        string? currentParentId = "root";
        string currentPath = "";

        for (int i = 0; i < segments.Length; i++)
        {
            string segment = segments[i];
            currentPath = string.IsNullOrEmpty(currentPath) ? segment : $"{currentPath}/{segment}";

            bool isLastSegment = (i == segments.Length - 1);
            bool canUseCache = !isLastSegment || createIfMissing;

            // Fast Path: String cache hit
            if (canUseCache && _folderCache.TryGetValue(currentPath, out string? cachedId))
            {
                currentParentId = cachedId;
                continue;
            }

            string taskKey = $"{(createIfMissing ? "W" : "R")}:{currentPath}";

            string pathSnapshot = currentPath;
            string parentIdSnapshot = currentParentId;

            if (!createIfMissing)
            {
                currentParentId = await _folderResolutionTasks.GetOrAdd(pathSnapshot, async (_) =>
                {
                    // Double-check string cache inside the task context
                    if (_folderCache.TryGetValue(pathSnapshot, out string? id)) return id;

                    var request = _service!.Files.List();
                    string safeSegment = segment.Replace("'", "\\'");
                    request.Q = $"name = '{safeSegment}' and '{parentIdSnapshot}' in parents and trashed = false";
                    request.Fields = "files(id, mimeType)";

                    var result = await request.ExecuteAsync();
                    var file = result.Files.FirstOrDefault();

                    if (file != null)
                    {
                        // Only cache in string cache if it's explicitly a folder
                        if (canUseCache || file.MimeType == FolderMimeType)
                        {
                            _folderCache[pathSnapshot] = file.Id;
                        }
                        return file.Id;
                    }

                    return null;
                });

                // If the segment didn't resolve, remove the failed task so it can be retried later
                if (currentParentId == null)
                {
                    _folderResolutionTasks.TryRemove(currentPath, out _);
                    return null;
                }
            }
            else
            {
                // Lock used only when actively creating missing directories
                await _folderLock.WaitAsync();
                FileStream? crossProcessLock = null;
                try
                {
                    crossProcessLock = await AcquireCrossProcessLockAsync();

                    if (canUseCache && _folderCache.TryGetValue(currentPath, out cachedId))
                    {
                        currentParentId = cachedId;
                        continue;
                    }

                    var request = _service!.Files.List();
                    string safeSegment = segment.Replace("'", "\\'");
                    request.Q = $"name = '{safeSegment}' and '{currentParentId}' in parents and trashed = false";
                    request.Fields = "files(id, mimeType)";

                    var result = await request.ExecuteAsync();
                    var file = result.Files.FirstOrDefault();

                    if (file != null)
                    {
                        currentParentId = file.Id;
                        if (canUseCache || file.MimeType == FolderMimeType)
                        {
                            _folderCache[currentPath] = currentParentId;
                        }
                    }
                    else
                    {
                        var folderMetadata = new DriveFile
                        {
                            Name = segment,
                            MimeType = FolderMimeType,
                            Parents = [currentParentId]
                        };

                        var createRequest = _service.Files.Create(folderMetadata);
                        createRequest.Fields = "id";
                        var newFolder = await createRequest.ExecuteAsync();

                        currentParentId = newFolder.Id;
                        _folderCache[currentPath] = currentParentId;
                    }
                }
                finally
                {
                    if (crossProcessLock != null)
                    {
                        await crossProcessLock.DisposeAsync();
                    }
                    _folderLock.Release();
                }
            }
        }

        return currentParentId;
    }


    private async Task<FileStream> AcquireCrossProcessLockAsync()
    {
        Directory.CreateDirectory(_tokenDirectoryPath);
        string lockPath = Path.Combine(_tokenDirectoryPath, "folder_resolve.lock");

        int retries = 30; // Max wait of 30 seconds
        while (retries > 0)
        {
            try
            {
                // FileMode.OpenOrCreate + FileAccess.ReadWrite + FileShare.None is 100% cross-platform.
                // If another process is holding this handle, the OS will reject this call and throw IOException.
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                // Wait 1 second before trying again
                await Task.Delay(1000);
                retries--;
            }
        }

        throw new TimeoutException("Failed to acquire cross-process lock for folder resolution.");
    }

    private void InvalidateCache(string remotePath)
    {
        string[] segments = remotePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return;

        string normalizedPath = string.Join("/", segments);

        // Remove the target path itself from the cache
        _folderCache.TryRemove(normalizedPath, out _);

        // Remove any child directories that were inside this path
        string prefix = normalizedPath + "/";
        var keysToRemove = _folderCache.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var key in keysToRemove)
        {
            _folderCache.TryRemove(key, out _);
        }
    }

    private string GetMimeType(string fileName)
    {
        string ext = Path.GetExtension(fileName).ToLower();
        return ext switch
        {
            ".txt" => "text/plain",
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".json" => "application/json",
            ".zip" => "application/zip",
            _ => "application/octet-stream" // Default binary
        };
    }

    private void EnsureConnected()
    {
        if (_service == null)
            throw new InvalidOperationException("Not connected to Google Drive. Call ConnectAsync() first.");
    }
}
