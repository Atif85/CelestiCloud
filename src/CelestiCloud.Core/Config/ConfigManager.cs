using System.Text.Json;
using CelestiCloud.Core.Models;

namespace CelestiCloud.Core.Config;

public class ConfigManager
{
    private readonly string _appDataPath;
    private readonly string _jobsDirectory;
    private readonly string _accountsDirectory;

    private readonly JsonSerializerOptions _options;

    public ConfigManager()
    {
        string systemFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _appDataPath = Path.Combine(systemFolder, "CelestiCloud");
        _jobsDirectory = Path.Combine(_appDataPath, "jobs");
        _accountsDirectory = Path.Combine(_appDataPath, "accounts");

        // Ensure directories exist on startup
        Directory.CreateDirectory(_jobsDirectory);
        Directory.CreateDirectory(_accountsDirectory);

        _options = new JsonSerializerOptions { WriteIndented = true };
    }

    public string GetAppDataPath() => _appDataPath;

    public string GetTokensDirectory() => Path.Combine(_appDataPath, "tokens");

    #region Jobs

    public void SaveJob(JobConfig job)
    {
        if (string.IsNullOrWhiteSpace(job.Id))
            throw new ArgumentException("Job ID cannot be empty.", nameof(job));

        string filePath = Path.Combine(_jobsDirectory, $"{job.Id}.json");
        string json = JsonSerializer.Serialize(job, _options);

        File.WriteAllText(filePath, json);
    }

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

    public JobConfig? LoadJobByName(string jobName)
    {
        var allJobs = LoadAllJobs();
        return allJobs.FirstOrDefault(j => j.Name.Equals(jobName, StringComparison.OrdinalIgnoreCase));
    }

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

    #endregion

    #region Accounts

    public void SaveAccount(AccountConfig account)
    {
        if (string.IsNullOrWhiteSpace(account.Id))
            throw new ArgumentException("Account ID cannot be empty.", nameof(account));

        string filePath = Path.Combine(_accountsDirectory, $"{account.Id}.json");
        string json = JsonSerializer.Serialize(account, _options);

        File.WriteAllText(filePath, json);
    }

    public AccountConfig? LoadAccount(string accountId)
    {
        string filePath = Path.Combine(_accountsDirectory, $"{accountId}.json");
        if (!File.Exists(filePath)) return null;

        try
        {
            string json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<AccountConfig>(json);
        }
        catch
        {
            return null;
        }
    }

    public AccountConfig? LoadAccountByName(string displayName)
    {
        var allAccounts = LoadAllAccounts();
        return allAccounts.FirstOrDefault(a => a.DisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase));
    }

    public IEnumerable<AccountConfig> LoadAllAccounts()
    {
        var accounts = new List<AccountConfig>();
        if (!Directory.Exists(_accountsDirectory)) return accounts;

        string[] files = Directory.GetFiles(_accountsDirectory, "*.json");
        foreach (string file in files)
        {
            try
            {
                string json = File.ReadAllText(file);
                var account = JsonSerializer.Deserialize<AccountConfig>(json);
                if (account != null)
                {
                    accounts.Add(account);
                }
            }
            catch { /* skip corrupted configs */ }
        }

        return accounts;
    }

    public void DeleteAccount(string accountId)
    {
        string filePath = Path.Combine(_accountsDirectory, $"{accountId}.json");
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        // Also clean up associated OAuth token directories
        string tokenPath = Path.Combine(GetTokensDirectory(), accountId);
        if (Directory.Exists(tokenPath))
        {
            Directory.Delete(tokenPath, recursive: true);
        }
    }

    #endregion
}