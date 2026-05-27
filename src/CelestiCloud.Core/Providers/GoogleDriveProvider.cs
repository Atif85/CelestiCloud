using CelestiCloud.Core.Models;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;

using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace CelestiCloud.Core.Providers;
public class GoogleDriveProvider : ICloudProvider
{
    public string ProviderName => "Google Drive";
    private const string APP_NAME = "CelestiCloud";
    private readonly string _credentialsFilePath;
    private readonly string _tokenDirectoryPath;

    private DriveService? _service;

    private const string FolderMimeType = "application/vnd.google-apps.folder";
    private static readonly string[] Scopes = 
    {
        DriveService.Scope.Drive
    };

    public GoogleDriveProvider(string credentialsFilePath, string tokenDirectoryPath)
    {
        _credentialsFilePath = credentialsFilePath;
        _tokenDirectoryPath = tokenDirectoryPath;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_credentialsFilePath))
        {
            throw new FileNotFoundException(
                $"credentials.json not found at {_credentialsFilePath}");
        }

        UserCredential credential;

        // Load the credentials.json
        using (var stream = new FileStream(_credentialsFilePath, FileMode.Open, FileAccess.Read))
        {
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

    public async Task UploadFileAsync(Stream sourceStream, string remotePath, IProgress<double>? progress = null)
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

        var fileMetadata = new DriveFile
        {
            Name = fileName
        };

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

        // Attach progress reporter if provided
        if (progress != null)
        {
            long fileLength = sourceStream.CanSeek ? sourceStream.Length : 0;
            uploadRequest.ProgressChanged += uploadProgress =>
            {
                if (uploadProgress.Status == UploadStatus.Uploading)
                {
                    double percentage = (double)uploadProgress.BytesSent / fileLength;
                    progress.Report(percentage);
                }
            };
        }

        var response = await uploadRequest.UploadAsync();

        if (response.Status == UploadStatus.Failed)
        {
            throw new Exception($"Upload failed: {response.Exception?.Message}", response.Exception);
        }

        progress?.Report(1.0); // 100% complete
    }

    public async Task RenameRemoteFileAsync(string oldRemotePath, string newRemotePath)
    {
        EnsureConnected();

        string? fileId = await ResolvePathToIdAsync(oldRemotePath) ?? throw new FileNotFoundException($"Cannot rename, remote file not found: {oldRemotePath}");

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
    }


    public async Task DownloadFileAsync(string remotePath, Stream destinationStream, IProgress<double>? progress = null)
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

        var response = await request.DownloadAsync(destinationStream);

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

        if (remoteFileId == null)
            return;

        if (moveToTrash)
        {
            DriveFile fileMetadata = new()
            {
                Trashed = true
            };
            var request = _service!.Files.Update(fileMetadata, remoteFileId);
            await request.ExecuteAsync();
        }
        else
        {
            var request = _service!.Files.Delete(remoteFileId);
            await request.ExecuteAsync();
        }
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
        string currentParentId = "root";

        foreach (string segment in segments)
        {
            var request = _service!.Files.List();

            string safeSegment = segment.Replace("'", "\\'");

            request.Q = $"name = '{safeSegment}' and '{currentParentId}' in parents and trashed = false";
            request.Fields = "files(id, mimeType)";

            var result = await request.ExecuteAsync();

            var file = result.Files.FirstOrDefault();

            if (file != null)
            {
                currentParentId = file.Id;
            }
            else if (createIfMissing)
            {
                // Create the missing folder
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
            }
            else
            {
                // Path segment not found, and we aren't creating it
                return null;
            }
        }

        return currentParentId;
    }

    private string GetMimeType(string fileName)
    {
        // Simple mapping. You can expand this or use a Mime mapping library later.
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
