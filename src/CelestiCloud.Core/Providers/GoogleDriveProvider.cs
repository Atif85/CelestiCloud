using CelestiCloud.Core.Models;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;

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

    public async Task ConnectAsync()
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
                CancellationToken.None,
                new FileDataStore(_tokenDirectoryPath, true));
        }

        // Create Drive API service.
        _service = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = APP_NAME
        });
    }

    public async Task UploadFileAsync(string localPath, string remotePath, IProgress<double>? progress = null)
    {
        EnsureConnected();
        throw new NotImplementedException();
    }

    public async Task DownloadFileAsync(string remotePath, string localPath, IProgress<double>? progress = null)
    {
        EnsureConnected();
        throw new NotImplementedException();
    }

    public async Task DeleteRemoteFileAsync(string remotePath)
    {
        EnsureConnected();
        throw new NotImplementedException();
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
                var folderMetadata = new Google.Apis.Drive.v3.Data.File
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

    private void EnsureConnected()
    {
        if (_service == null)
            throw new InvalidOperationException("Not connected to Google Drive. Call ConnectAsync() first.");
    }
}
