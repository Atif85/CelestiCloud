using CelestiCloud.Core.Config;
using CelestiCloud.Core.Jobs.LocalToCloudJobs;
using CelestiCloud.Core.Logging;
using CelestiCloud.Core.Models;
using CelestiCloud.Core.Providers;
using System.Collections.Concurrent;

namespace CelestiCloud.Core.Jobs;

public class SyncEngine
{
    private readonly ConfigManager _configManager;
    private readonly ProviderFactory _providerFactory;
    private readonly IJobLogger _logger;

    // Tracks currently executing jobs in memory: JobId -> JobInstance
    private readonly ConcurrentDictionary<string, JobBase> _activeJobs = new();

    public SyncEngine(ConfigManager configManager, ProviderFactory providerFactory, IJobLogger logger)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _providerFactory = providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IEnumerable<JobBase> GetActiveJobs() => _activeJobs.Values;

    public async Task StartJobAsync(string identifier)
    {
        var config = _configManager.LoadJobByName(identifier);

        config ??= _configManager.LoadJob(identifier);

        if (config == null)
            throw new FileNotFoundException($"No job found with name or ID: {identifier}");

        string jobId = config.Id;

        if (_activeJobs.ContainsKey(jobId))
        {
            _logger.Log(LogLevel.Info, $"Job '{jobId}' is already running.");
            return;
        }

        // Load the target Account
        string targetAccountId = config.TargetAccountId
            ?? throw new InvalidOperationException($"Job '{config.Name}' has no target accounts.");

        var account = _configManager.LoadAccount(targetAccountId)
            ?? throw new FileNotFoundException($"Account not found: {targetAccountId}");

        // Connect the Provider using the Factory
        _logger.Log(LogLevel.Debug, $"Connecting account '{account.DisplayName}' for job '{config.Name}'...");
        var provider = await _providerFactory.CreateProviderAsync(account);

        // Instantiate the correct concrete Job class
        JobBase jobInstance = config.JobType switch
        {
            JobType.Sync => new SyncJob(config, provider, _configManager.GetAppDataPath(), _logger),
            JobType.Backup => new BackupJob(config, provider, _configManager.GetAppDataPath(), _logger),
            _ => throw new NotSupportedException($"Unsupported Job Type: {config.JobType}")
        };

        // Register and run the job asynchronously in the background
        if (_activeJobs.TryAdd(jobId, jobInstance))
        {
            // Fire and forget task execution, but monitor errors
            _ = Task.Run(async () =>
            {
                try
                {
                    await jobInstance.StartAsync();
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Error, $"Job '{config.Name}' crashed: {ex.Message}");
                }
                finally
                {
                    _activeJobs.TryRemove(jobId, out _);
                }
            });
        }
    }

    public async Task StopJobAsync(string jobId)
    {
        if (_activeJobs.TryGetValue(jobId, out var job))
        {
            _logger.Log(LogLevel.Info, $"Stopping job '{job.Config.Name}'...");
            await job.StopAsync();
        }
    }

    public async Task StartAutoStartJobsAsync()
    {
        _logger.Log(LogLevel.Debug, "Scanning for AutoStart jobs...");
        var allJobs = _configManager.LoadAllJobs();
        var autoStartJobs = allJobs.Where(j => j.AutoStart);

        foreach (var job in autoStartJobs)
        {
            try
            {
                await StartJobAsync(job.Id);
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Failed to auto-start job '{job.Name}': {ex.Message}");
            }
        }
    }

    public async Task StopAllAsync()
    {
        _logger.Log(LogLevel.Debug, "Stopping all active jobs in SyncEngine...");
        var stopTasks = _activeJobs.Values.Select(job => job.StopAsync()).ToList();
        await Task.WhenAll(stopTasks);
        _activeJobs.Clear();
    }
}