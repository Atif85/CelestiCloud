using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.SymbolStore;
using System.Text;

namespace CelestiCloud.Core.Jobs;

public static class JobLockManager
{
    public static bool IsJobRunning(string jobId, string appDataPath)
    {
        string lockFilePath = Path.Combine(appDataPath, "jobs", $"{jobId}.lock");

        try
        {
            if (!File.Exists(lockFilePath))
            {
                return false;
            }

            string fileContent = File.ReadAllText(lockFilePath).Trim();
            if (int.TryParse(fileContent, out int existingPID))
            {
                try
                {
                    Process.GetProcessById(existingPID);
                    return true;
                }
                catch (ArgumentException)
                {
                    try { File.Delete(lockFilePath); } catch { }
                    return false;
                }
            }

            try { File.Delete(lockFilePath); } catch { }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryAcquireLock(string jobId, string appDataPath, out string? errorMessege)
    {
        errorMessege = null;
        string lockFilePath = Path.Combine(appDataPath, "jobs", $"{jobId}.lock");

        try
        {
            // Use our new helper to check if the job is actually alive
            if (IsJobRunning(jobId, appDataPath))
            {
                string fileContent = File.Exists(lockFilePath) ? File.ReadAllText(lockFilePath).Trim() : "Unknown";
                errorMessege = $"Process is still alive with PID: {fileContent}";
                return false;
            }

            // Create directories if missing and write our current PID
            Directory.CreateDirectory(Path.GetDirectoryName(lockFilePath)!);
            File.WriteAllText(lockFilePath, Environment.ProcessId.ToString());
            return true;
        }
        catch (Exception ex)
        {
            errorMessege = $"Failed to manage lock file: {ex.Message}";
            return false;
        }
    }

    public static void ReleaseLock(string jobId, string appDataPath)
    {
        string lockFilePath = Path.Combine(appDataPath, "jobs", $"{jobId}.lock");

        try
        {
            if (File.Exists(lockFilePath))
            {
                File.Delete(lockFilePath);
            }
        }
        catch
        {
            return;
        }
    }
}
