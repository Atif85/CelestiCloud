using CelestiCloud.Core.Config;
using CelestiCloud.Core.Models;
using CelestiCloud.Core.Providers;

namespace CelestiCloud.CLI;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== CelestiCloud ===");

        ConfigManager configManager = new();

        string tokensFolder = configManager.GetTokensDirectory();

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

        var provider = new GoogleDriveProvider(credentialsPath, tokensFolder);

        var jobConfig = new JobConfig
        {
            Name = "Test Job",
        };

        foreach (var job in configManager.LoadAllJobs())
        {
            Console.WriteLine(job);
        }
    }
}