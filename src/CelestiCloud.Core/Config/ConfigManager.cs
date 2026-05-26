using System.Text.Json;
using CelestiCloud.Core.Models;

namespace CelestiCloud.Core.Config;

public class ConfigManager
{
    private readonly string _appDataPath;
    private readonly string _jobsDirectory;

    private readonly JsonSerializerOptions _options;

    public ConfigManager()
    {
        string systemFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _appDataPath = Path.Combine(systemFolder, "CelestiCloud");
        _jobsDirectory = Path.Combine(_appDataPath, "jobs");

        // Ensure directories exist on startup
        Directory.CreateDirectory(_jobsDirectory);

        _options = new JsonSerializerOptions { WriteIndented = true };
    }

    public string GetAppDataPath() => _appDataPath;

    public string GetTokensDirectory() => Path.Combine(_appDataPath, "tokens");

    /// <summary>
    /// Saves a single JobConfig to disk.
    /// </summary>
    public void SaveJob(JobConfig job)
    {
        if (string.IsNullOrWhiteSpace(job.Id))
            throw new ArgumentException("Job ID cannot be empty.", nameof(job));

        string filePath = Path.Combine(_jobsDirectory, $"{job.Id}.json");
        string json = JsonSerializer.Serialize(job, _options);

        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// Loads a single JobConfig by ID. Returns null if not found.
    /// </summary>
    public JobConfig? LoadJob(string jobId)
    {
        string filePath = Path.Combine(_jobsDirectory, $"{jobId}.json");
        if (!File.Exists(filePath)) return null;

        try
        {
            string json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<JobConfig>(json);
        }
        catch
        {
            return null; // Corrupted JSON or read failure
        }
    }

    /// <summary>
    /// Loads all configured jobs from the AppData directory.
    /// </summary>
    public IEnumerable<JobConfig> LoadAllJobs()
    {
        var jobs = new List<JobConfig>();
        if (!Directory.Exists(_jobsDirectory)) return jobs;

        string[] files = Directory.GetFiles(_jobsDirectory, "*.json");
        foreach (string file in files)
        {
            // Skip lock files
            if (file.EndsWith(".lock")) continue;

            try
            {
                string json = File.ReadAllText(file);
                var job = JsonSerializer.Deserialize<JobConfig>(json);
                if (job != null)
                {
                    jobs.Add(job);
                }
            }
            catch
            {
                // Log or ignore corrupted individual configs
            }
        }

        return jobs;
    }

    /// <summary>
    /// Deletes a job configuration from disk.
    /// </summary>
    public void DeleteJob(string jobId)
    {
        string filePath = Path.Combine(_jobsDirectory, $"{jobId}.json");
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        // Clean up any stray locks
        string lockPath = Path.Combine(_jobsDirectory, $"{jobId}.lock");
        if (File.Exists(lockPath))
        {
            File.Delete(lockPath);
        }
    }
}