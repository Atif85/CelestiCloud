using CelestiCloud.Core.Providers;

string appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
string celestiCloudFolder = Path.Combine(appDataFolder, "CelestiCloud");
string tokensFolder = Path.Combine(celestiCloudFolder, "tokens");

Directory.CreateDirectory(tokensFolder);

var provider = new GoogleDriveProvider(
    credentialsFilePath: "credentials.json",
    tokenDirectoryPath: tokensFolder
);

Console.WriteLine("Connecting to Google Drive...");

await provider.ConnectAsync();

Console.WriteLine("Successfully authenticated!");
Console.WriteLine($"Token saved to: {tokensFolder}");

string remotePath = "My Folders/College/slides";

bool exists = await provider.FileExistsAsync(remotePath);

Console.WriteLine(exists ? "Exists" : "Doesnt Exist");