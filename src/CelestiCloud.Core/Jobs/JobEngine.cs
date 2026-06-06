using CelestiCloud.Core.Config;
using CelestiCloud.Core.IO;
using CelestiCloud.Core.Jobs.LocalToCloudJobs;
using CelestiCloud.Core.Logging;
using CelestiCloud.Core.Models;
using CelestiCloud.Core.Providers;
using System.Collections.Concurrent;
using System.Threading.RateLimiting;

namespace CelestiCloud.Core.Jobs;

public class JobEngine
{
    private readonly ConfigManager _configManager;
    private readonly ProviderFactory _providerFactory; 
    private RateLimiter? _uploadLimiter;
    private readonly IJobLogger _logger;

    public event EventHandler<string>? JobStarted;
    public event EventHandler<string>? JobStopped;

    public event Action<string, string, string>? NotificationRequested;

    // Tracks currently executing jobs in memory: JobId -> JobInstance
    private readonly ConcurrentDictionary<string, JobBase> _activeJobs = new();

    private int _safeChunkSize = 32 * 1024;

    public JobEngine(ConfigManager configManager, ProviderFactory providerFactory, RateLimiter? uploadLimiter, IJobLogger logger)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _providerFactory = providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));
        _uploadLimiter = uploadLimiter;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var appSettings = configManager.LoadSettings();
        UpdateChunkSize(appSettings.GlobalUploadLimitKbps);
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
        var provider = await _providerFactory.GetOrCreateProviderAsync(account);

        // Instantiate the correct concrete Job class
        JobBase jobInstance = config.JobType switch
        {
            JobType.Sync => new SyncJob(config, provider, _configManager.GetAppDataPath(), _uploadLimiter, _safeChunkSize, _logger),
            JobType.Backup => new BackupJob(config, provider, _configManager.GetAppDataPath(), _uploadLimiter, _safeChunkSize, _logger),
            _ => throw new NotSupportedException($"Unsupported Job Type: {config.JobType}")
        };

        jobInstance.NotificationRequested += (title, message, severity) =>
        {
            // Bubble the notification up to the GUI
            NotificationRequested?.Invoke(title, message, severity);
        };

        // Register and run the job asynchronously in the background
        if (_activeJobs.TryAdd(jobId, jobInstance))
        {
            JobStarted?.Invoke(this, jobId);

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
                    if (_activeJobs.TryRemove(jobId, out _))
                    {
                        JobStopped?.Invoke(this, jobId);
                    }
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

            if (_activeJobs.TryRemove(jobId, out _))
            {
                JobStopped?.Invoke(this, jobId);
            }
        }
    }

    public async Task StartAutoStartJobsAsync()
    {
        _logger.Log(LogLevel.Debug, "Scanning for AutoStart jobs...");

        var allJobs = _configManager.LoadAllJobs();
        var autoStartJobs = allJobs.Where(j => j.AutoStart).ToList();

        if (autoStartJobs.Count == 0)
        {
            _logger.Log(LogLevel.Debug, "No AutoStart jobs found.");
            return;
        }

        _logger.Log(LogLevel.Info, $"Booting {autoStartJobs.Count} jobs concurrently...");

        var startTasks = autoStartJobs.Select(async job =>
        {
            try
            {
                await StartJobAsync(job.Id);
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Failed to auto-start job '{job.Name}': {ex.Message}");
            }
        });

        await Task.WhenAll(startTasks);
    }

    public async Task StopAllAsync()
    {
        _logger.Log(LogLevel.Debug, "Stopping all active jobs in JobEngine...");
        var stopTasks = _activeJobs.Values.Select(job => job.StopAsync()).ToList();
        await Task.WhenAll(stopTasks);

        var stoppedIds = _activeJobs.Keys.ToList();
        _activeJobs.Clear();

        foreach (var id in stoppedIds)
        {
            JobStopped?.Invoke(this, id);
        }
    }

    public void UpdateUploadLimit(int uploadLimitKbps)
    {
        _uploadLimiter?.Dispose();
        UpdateChunkSize(uploadLimitKbps);

        if (uploadLimitKbps > 0)
        {
            _uploadLimiter = BandwidthLimiterFactory.CreateLimiter(uploadLimitKbps * 1024);
        }
        else
        {
            _uploadLimiter = null;
        }
    }

    private void UpdateChunkSize(int uploadLimitKbps)
    {
        if (uploadLimitKbps > 0)
        {
            // Set safe chunk size to 1/10th of the limit per second, ensuring it is at least 4KB [5]
            _safeChunkSize = Math.Max(4096, (uploadLimitKbps * 1024) / 10);
        }
        else
        {
            _safeChunkSize = 32 * 1024; // Default 32KB
        }
    }
}