using CelestiCloud.Core.Models;

namespace CelestiCloud.Core.Providers;

public interface ICloudProvider
{
    string ProviderName { get; }

    Task ConnectAsync(CancellationToken cancellationToken);

    Task UploadFileAsync(string localPath, string remotePath, IProgress<double>? progress = null);

    Task DownloadFileAsync(string remotePath, string localPath, IProgress<double>? progress = null);

    Task DeleteRemoteFileAsync(string remotePath, bool moveToTrash);

    Task<bool> FileExistsAsync(string remotePath);

    Task<IEnumerable<CloudFile>> ListFilesAsync(string remotePath);
}
