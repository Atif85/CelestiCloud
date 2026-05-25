using CelestiCloud.Core.Providers;

namespace CelestiCloud.CLI;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== CelestiCloud Google Drive Provider Integration Test ===");

        // Set up paths for credentials and token store as defined in specs
        string appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string celestiCloudFolder = Path.Combine(appDataFolder, "CelestiCloud");
        string tokensFolder = Path.Combine(celestiCloudFolder, "tokens");

        string credentialsPath = "credentials.json";

        if (!File.Exists(credentialsPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {credentialsPath} not found in the execution directory.");
            Console.WriteLine("Ensure you've set 'Copy to Output Directory' to 'Copy if newer' on the file.");
            Console.ResetColor();
            return;
        }

        Directory.CreateDirectory(tokensFolder);

        // Initialize provider
        var provider = new GoogleDriveProvider(credentialsPath, tokensFolder);

        try
        {
            // 1. Connection & OAuth Consent
            Console.WriteLine("\n[1/6] Connecting and Authenticating...");
            await provider.ConnectAsync();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Connected successfully!");
            Console.ResetColor();

            // Create a temporary dummy local file
            string testFileName = "test_upload.txt";
            string localTempPath = Path.Combine(Path.GetTempPath(), testFileName);
            await File.WriteAllTextAsync(localTempPath, "Hello CelestiCloud! This is a test file for upload and download validation.");
            Console.WriteLine($"Created temporary local file: {localTempPath}");

            // Remote pathways
            string remoteFolder = "/CelestiCloudTest/SubFolder";
            string remoteFilePath = $"{remoteFolder}/{testFileName}";

            // 2. Upload File with Progress
            Console.WriteLine($"\n[2/6] Uploading local file to remote path: {remoteFilePath}");
            var uploadProgress = new Progress<double>(p =>
            {
                Console.Write($"\rUploading progress: {p * 100:F0}%");
            });
            await provider.UploadFileAsync(localTempPath, remoteFilePath, uploadProgress);
            Console.WriteLine("\nUpload complete.");

            // 3. File Existence Check
            Console.WriteLine("\n[3/6] Verifying file exists in cloud...");
            bool exists = await provider.FileExistsAsync(remoteFilePath);
            Console.WriteLine($"File exists verification: {exists} (Expected: True)");

            // 4. List Files
            Console.WriteLine($"\n[4/6] Listing files under remote folder: {remoteFolder}");
            var cloudFiles = await provider.ListFilesAsync(remoteFolder);
            foreach (var file in cloudFiles)
            {
                Console.WriteLine($" - {file.Name} (ID: {file.Id}, Size: {file.Size} bytes, Folder: {file.IsFolder})");
            }

            // 5. Download File with Progress
            string localDownloadedPath = Path.Combine(Path.GetTempPath(), "test_downloaded.txt");
            Console.WriteLine($"\n[5/6] Downloading file from cloud to: {localDownloadedPath}");
            var downloadProgress = new Progress<double>(p =>
            {
                Console.Write($"\rDownloading progress: {p * 100:F0}%");
            });
            await provider.DownloadFileAsync(remoteFilePath, localDownloadedPath, downloadProgress);
            Console.WriteLine("\nDownload complete.");

            // Compare local and downloaded content
            string originalContent = await File.ReadAllTextAsync(localTempPath);
            string downloadedContent = await File.ReadAllTextAsync(localDownloadedPath);
            bool contentsMatch = originalContent == downloadedContent;
            Console.WriteLine($"Original vs Downloaded content match: {contentsMatch} (Expected: True)");

            // Clean up temporary local files
            if (File.Exists(localDownloadedPath)) File.Delete(localDownloadedPath);
            if (File.Exists(localTempPath)) File.Delete(localTempPath);

            // 6. Delete Remote File (Trash)
            Console.WriteLine($"\n[6/6] Deleting remote file (moving to trash)...");
            await provider.DeleteRemoteFileAsync(remoteFilePath, moveToTrash: true);
            Console.WriteLine("Deletion complete.");

            // Verify remote file is gone
            bool stillExists = await provider.FileExistsAsync(remoteFilePath);
            Console.WriteLine($"File still exists verification: {stillExists} (Expected: False)");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\nAll integration tests passed successfully!");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nAn error occurred during verification: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Console.ResetColor();
        }
    }
}