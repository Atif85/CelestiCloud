using CelestiCloud.Core.Models;

namespace CelestiCloud.Core.Providers;

public interface ICloudProvider
{
    string ProviderName { get; }

    Task ConnectAsync(CancellationToken cancellationToken = default);

    Task RevokeAccessAsync(CancellationToken cancellationToken = default);

    Task<string> GetAuthenticatedUserEmailAsync(CancellationToken cancellationToken = default);

    Task UploadFileAsync(Stream sourceStream, string remotePath, 
                        string? existingFileId = null, bool assumeNew = false, 
                        IProgress<double>? progress = null,
                        CancellationToken cancellationToken = default);

    Task RenameRemoteFileAsync(string oldRemotePath, string newRemotePath);

    Task DownloadFileAsync(string remotePath, Stream destinationStream, IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    Task DeleteRemoteFileAsync(string remotePath, bool moveToTrash);

    Task<bool> FileExistsAsync(string remotePath);

    Task<IEnumerable<CloudFile>> ListFilesAsync(string remotePath);
}
