using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace CelestiCloud.Core.Jobs;

internal static class JobLockManager
{
    public static bool TryAcquireLock(string jobId, string appDataPath, out string? errorMessege)
    {
        errorMessege = null;

        string lockFilePath = Path.Combine(appDataPath, "jobs", $"{jobId}.lock");

        try
        {
            if (File.Exists(lockFilePath))
            {
                string fileContent = File.ReadAllText(lockFilePath);
                if (int.TryParse(fileContent, out int existingPID))
                {
                    // We check if the process is still alive
                    try
                    {
                        Process process = Process.GetProcessById(existingPID);

                        errorMessege = $"Process is still alive with PID: {existingPID}";
                        return false;
                    }
                    catch (ArgumentException)
                    {
                        File.Delete(lockFilePath);
                    }
                }
            }

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
