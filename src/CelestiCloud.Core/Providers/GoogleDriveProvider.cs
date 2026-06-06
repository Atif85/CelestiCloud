using CelestiCloud.Core.Logging;
using CelestiCloud.Core.Models;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Text.Json;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace CelestiCloud.Core.Providers;
public class GoogleDriveProvider : ICloudProvider
{
    public string ProviderName => "Google Drive";
    private const string APP_NAME = "CelestiCloud";
    private readonly string _tokenDirectoryPath;

    private readonly IJobLogger? _logger;

    private DriveService? _service;

    private readonly ConcurrentDictionary<string, string> _folderCache = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _resolutionTasks = new(StringComparer.OrdinalIgnoreCase);

    private const string FolderMimeType = "application/vnd.google-apps.folder";
    private static readonly string[] Scopes = 
    [
        DriveService.Scope.DriveFile
    ];

    private const int SmallFileThreshold = 4 * 1024 * 1024;   // 4 MB
    private const int SmallChunkSize = ResumableUpload.MinimumChunkSize;      // 256 KB
    private const int LargeChunkSize = ResumableUpload.MinimumChunkSize * 16; // 4 MB

    private readonly Random _jitter = new();

    public GoogleDriveProvider(string tokenDirectoryPath, IJobLogger? logger = null)
    {
        _tokenDirectoryPath = tokenDirectoryPath;
        _logger = logger;
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

        _service.HttpClient.Timeout = TimeSpan.FromSeconds(15);
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

        return await ExecuteWithRetryAsync(async () =>
        {
            var request = _service!.About.Get();
            request.Fields = "user(emailAddress)";

            var about = await request.ExecuteAsync(cancellationToken);
            return about.User?.EmailAddress ?? "unknown-email";
        }, cancellationToken);
    }

    private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action, CancellationToken ct = default)
    {
        int maxRetries = 6;
        double delaySeconds = 2.0;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                return await action();
            }
            catch (GoogleApiException ex) when (IsTransientError(ex))
            {
                if (i == maxRetries - 1)
                {
                    // Reached maximum retries, we have no choice but to throw
                    throw;
                }

                // Exponential backoff with random jitter
                double backoffDelay = delaySeconds + _jitter.NextDouble();

                _logger?.Log(LogLevel.Debug,
                    $"Google API Rate Limited (403/429). Backing off for {backoffDelay:F2}s before retry {i + 1}/{maxRetries}...");

                await Task.Delay(TimeSpan.FromSeconds(backoffDelay), ct);
                delaySeconds *= 2; // Double the delay for the next round (2s -> 4s -> 8s -> 16s)
            }
        }

        throw new InvalidOperationException("Failed after maximum API retries.");
    }

    private static bool IsTransientError(GoogleApiException ex)
    {
        // 403: rateLimitExceeded or userRateLimitExceeded
        bool isRateLimit403 = ex.HttpStatusCode == HttpStatusCode.Forbidden
            && ex.Error?.Errors?.Any(e => e.Reason == "rateLimitExceeded" || e.Reason == "userRateLimitExceeded") == true;

        // 429: Too Many Requests
        bool is429 = ex.HttpStatusCode == (HttpStatusCode)429;

        // 5xx: Temporary Google Server Issues
        bool is5xx = (int)ex.HttpStatusCode >= 500;

        return isRateLimit403 || is429 || is5xx;
    }


    public async Task<string> UploadFileAsync(
        Stream sourceStream,
        string remotePath,
        string? existingFileId = null, 
        bool assumeNew = false,
        IProgress<double>? progress = null, 
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        return await ExecuteWithRetryAsync(async () =>
        {
            if (sourceStream.CanSeek)
            {
                sourceStream.Position = 0;
            }

            // Split the remote path to get the parent folder path and the file name
            string[] segments = remotePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

            string fileName = segments.Last();
            string parentPath = string.Join("/", segments.Take(segments.Length - 1));

            string? fileId = existingFileId;

            if (!assumeNew && fileId == null)
            {
                fileId = await ResolvePathToIdAsync(remotePath);
            }

            bool remoteFileExists = fileId != null;

            string mimeType = GetMimeType(remotePath);

            long fileLength = sourceStream.CanSeek ? sourceStream.Length : 0;

            var fileMetadata = new DriveFile { Name = fileName };
            int chunkSize = fileLength < SmallFileThreshold ? SmallChunkSize : LargeChunkSize;

            ResumableUpload<DriveFile, DriveFile> uploadRequest;

            if (remoteFileExists)
            {
                uploadRequest = _service!.Files.Update(fileMetadata, fileId!, sourceStream, mimeType);
            }
            else
            {
                // Ensure the remote directory exists (creates it if it doesn't)
                string parentFolderId = await ResolvePathToIdAsync(parentPath, createIfMissing: true)
                                        ?? throw new Exception("Failed to resolve or create remote parent folder.");

                fileMetadata.Parents = [parentFolderId];
                uploadRequest = _service!.Files.Create(fileMetadata, sourceStream, mimeType);
            }

            uploadRequest.ChunkSize = chunkSize;

            // Attach progress reporter if provided
            if (progress != null)
            {
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

                if (response.Exception is GoogleApiException apiEx)
                {
                    throw apiEx;
                }

                throw new Exception($"Upload failed: {response.Exception?.Message}", response.Exception);
            }

            string uploadedId = uploadRequest.ResponseBody?.Id
                ?? fileId
                ?? throw new Exception("Failed to retrieve file ID from Google Drive.");

            progress?.Report(1.0); // 100% complete

            return uploadedId;
        });
    }

    public async Task<string> RenameRemoteFileAsync(string oldRemotePath, string newRemotePath)
    {
        EnsureConnected();

        return await ExecuteWithRetryAsync(async () =>
        {
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
            return fileId;
        });
    }

    public async Task DownloadFileAsync(string remotePath, Stream destinationStream, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        await ExecuteWithRetryAsync<object?>(async () =>
        {
            // Reset and truncate destination stream if retry happens
            if (destinationStream.CanSeek)
            {
                destinationStream.Position = 0;
                destinationStream.SetLength(0);
            }

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
            return null;
        }, cancellationToken);
    }

    public async Task DeleteRemoteFileAsync(string remotePath, bool moveToTrash = true)
    {
        EnsureConnected();

        await ExecuteWithRetryAsync<object?>(async () =>
        {
            string? remoteFileId = await ResolvePathToIdAsync(remotePath);

            if (remoteFileId == null) return null;

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
            return null;
        });
    }

    public async Task<IEnumerable<CloudFile>> ListFilesAsync(string remotePath)
    {
        EnsureConnected();

        string? folderId = await ResolvePathToIdAsync(remotePath);
        if (folderId == null) return [];

        var files = new List<CloudFile>();
        string? pageToken = null;

        string cleanRemotePath = remotePath.Replace('\\', '/').Trim('/');

        do
        {
            var request = _service!.Files.List();
            request.Q = $"'{folderId}' in parents and trashed = false";
            request.Fields = "nextPageToken, files(id, name, mimeType, size, modifiedTime)";
            request.PageSize = 1000;
            if (pageToken != null) request.PageToken = pageToken;

            var result = await ExecuteWithRetryAsync(() => request.ExecuteAsync());

            foreach (var file in result.Files)
            {
                var isFolder = file.MimeType == FolderMimeType;

                // Opportunistically populate the cache while we're here
                if (isFolder)
                {
                    var childPath = string.IsNullOrEmpty(cleanRemotePath)
                        ? file.Name
                        : $"{cleanRemotePath}/{file.Name}";

                    if (!_folderCache.ContainsKey(childPath))
                    {
                        _folderCache.TryAdd(childPath, file.Id);
                    }
                }

                files.Add(new CloudFile
                {
                    Id = file.Id,
                    Name = file.Name,
                    IsFolder = isFolder,
                    Size = file.Size,
                    ModifiedDate = file.ModifiedTimeDateTimeOffset?.DateTime
                });
            }

            pageToken = result.NextPageToken;
        }
        while (pageToken != null);

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

            Lazy<Task<string?>> lazyTask = _resolutionTasks.GetOrAdd(taskKey, _ =>
            {
                return new Lazy<Task<string?>>(() =>
                    ResolveSegmentInternalAsync(pathSnapshot, segment, parentIdSnapshot, canUseCache, createIfMissing)
                );
            });

            try
            {
                currentParentId = await lazyTask.Value;
            }
            finally
            {
                _resolutionTasks.TryRemove(taskKey, out _);
            }

            if (currentParentId == null)
            {
                return null;
            }
        }

        return currentParentId;
    }

    private async Task<string?> ResolveSegmentInternalAsync(
    string path,
    string segment,
    string parentId,
    bool canUseCache,
    bool createIfMissing)
    {
        // Double  check the cache inside task context
        if (canUseCache && _folderCache.TryGetValue(path, out string? id)) return id;

        // Attempt to locate folder on Drive
        var request = _service!.Files.List();
        string safeSegment = segment.Replace("'", "\\'");
        request.Q = $"name = '{safeSegment}' and '{parentId}' in parents and trashed = false";
        request.Fields = "files(id, mimeType)";

        var result = await ExecuteWithRetryAsync(() => request.ExecuteAsync());
        var file = result.Files.FirstOrDefault();

        if (file != null)
        {
            if (canUseCache || file.MimeType == FolderMimeType)
            {
                _folderCache[path] = file.Id;
            }
            return file.Id;
        }

        // If folder is missing, and we are in write mode, create it exclusively
        if (createIfMissing)
        {
            var folderMetadata = new DriveFile
            {
                Name = segment,
                MimeType = FolderMimeType,
                Parents = [parentId]
            };

            var createRequest = _service.Files.Create(folderMetadata);
            createRequest.Fields = "id";
            var newFolder = await ExecuteWithRetryAsync(() => createRequest.ExecuteAsync());

            _folderCache[path] = newFolder.Id;
            return newFolder.Id;
        }

        return null;
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

    private static string GetMimeType(string fileName)
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
