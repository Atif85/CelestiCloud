using CelestiCloud.Core.Models;

namespace CelestiCloud.Core.Providers;

public interface ICloudProvider
{
    string ProviderName { get; }

    Task ConnectAsync(CancellationToken cancellationToken);

    Task UploadFileAsync(Stream sourceStream, string remotePath, IProgress<double>? progress = null);

    Task RenameRemoteFileAsync(string oldRemotePath, string newRemotePath);

    Task DownloadFileAsync(string remotePath, Stream destinationStream, IProgress<double>? progress = null);

    Task DeleteRemoteFileAsync(string remotePath, bool moveToTrash);

    Task<bool> FileExistsAsync(string remotePath);

    Task<IEnumerable<CloudFile>> ListFilesAsync(string remotePath);
}
