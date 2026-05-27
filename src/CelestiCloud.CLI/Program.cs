using CelestiCloud.Core.Config;
using CelestiCloud.Core.IO;
using CelestiCloud.Core.Jobs;
using CelestiCloud.Core.Jobs.LocalToCloudJobs;
using CelestiCloud.Core.Logging;
using CelestiCloud.Core.Models;
using CelestiCloud.Core.Providers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace CelestiCloud.CLI;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== CelestiCloud Quick Sync Test ===");

        // 1. Setup the ConfigManager just to get our standard AppData paths
        var configManager = new ConfigManager();
        string appDataPath = configManager.GetAppDataPath();
        string tokensDir = configManager.GetTokensDirectory();

        // 2. Setup a test local directory
        string localTestFolder = "C:\\Users\\H.A.R\\Downloads";
        Console.WriteLine($"[Local Folder]: {localTestFolder}");
        Console.WriteLine($"[App Data]: {appDataPath}");

        // 3. Initialize Provider using a local credentials.json
        string credentialsPath = "credentials.json";
        if (!File.Exists(credentialsPath))
        {
            Console.WriteLine($"Error: {credentialsPath} not found in execution directory.");
            return;
        }

        var provider = new GoogleDriveProvider(credentialsPath, tokensDir);
        Console.WriteLine("Connecting to Google Drive...");
        await provider.ConnectAsync();
        Console.WriteLine("Connected!");

        // 4. Create a Hardcoded Sync Job Config
        var jobConfig = new JobConfig
        {
            Id = "quick-test-job-123",
            Name = "Quick Test Sync",
            JobType = JobType.Sync,
            // Forces all operations inside this folder on Google Drive
            RemoteRootPath = "/CelestiCloud/",
            LocalPaths = new List<string> { localTestFolder },
            IgnorePatterns = new List<string>
            {
                "*.tmp",        // Ignore temp files
                "steamcmd/", // Ignore node_modules folders
                "LaigterPortable-1.11.0/",
                "*.exe",
                "Assets/"
            }
        };

        var logger = new FileLogger(appDataPath, jobConfig.Id)
        {
            MinimumLevel = LogLevel.Error
        };

        // 5. Instantiate the SyncJob
        var syncJob = new SyncJob(jobConfig, provider, appDataPath, logger);

        // 6. Handle Ctrl+C gracefully
        var tcs = new TaskCompletionSource();
        Console.CancelKeyPress += async (sender, e) =>
        {
            e.Cancel = true; // Prevent sudden kill
            Console.WriteLine("\n[Ctrl+C Detected] Stopping Sync Engine gracefully...");
            await syncJob.StopAsync();
            tcs.SetResult();
        };

        // 7. Start the job in a background task
        Console.WriteLine("\nStarting SyncJob... Drop files into the local folder to test!");
        Console.WriteLine("Press Ctrl+C to stop.");

        var jobTask = syncJob.StartAsync();

        Console.CursorVisible = false;
        Console.Clear();

        while (syncJob.IsRunning)
        {
            // Move cursor to top instead of clearing
            Console.SetCursorPosition(0, 0);

            // Use PadRight to overwrite leftover characters from previous longer lines
            Console.WriteLine($"Global Progress: {syncJob.State.FilesProcessed} / {syncJob.State.FilesFound}".PadRight(50));
            Console.WriteLine(new string('-', 50));

            int lineCount = 2; // Track lines so we can blank out old removed transfers

            foreach (var transfer in syncJob.State.ActiveTransfers)
            {
                // transfer.Key is the full path. Let's just print the file name.
                string displayFileName = Path.GetFileName(transfer.Key);
                if (displayFileName.Length > 20) displayFileName = displayFileName.Substring(0, 17) + "...";

                int barLength = (int)(transfer.Value * 20);
                string bar = new string('█', barLength).PadRight(20, '-');

                // Write padded line to overwrite old text
                Console.WriteLine($"{displayFileName,-20} [{bar}] {transfer.Value:P0}".PadRight(60));
                lineCount++;
            }

            // Blank out any trailing lines left by transfers that just finished
            for (int i = 0; i < 10; i++)
            {
                Console.WriteLine(new string(' ', 60));
            }

            await Task.Delay(200); // 200ms feels much smoother than 500ms
        }

        Console.CursorVisible = true;

        // Wait until Ctrl+C is pressed
        await tcs.Task;

        // Wait for the job to finish its shutdown sequence
        await jobTask;

        Console.WriteLine("Sync Engine shut down successfully.");
    }
}