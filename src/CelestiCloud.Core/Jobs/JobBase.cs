using CelestiCloud.Core.Models;

namespace CelestiCloud.Core.Jobs;

public abstract class JobBase
{
    public JobConfig Config { get; }
    public bool IsRunning { get; private set; }
    public SyncState State { get; } = new();

    protected readonly string AppDataPath;

    private readonly Lock _lock = new();
    private CancellationTokenSource? _cts;

    protected JobBase(JobConfig config, string appDataPath)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
        AppDataPath = appDataPath;
    }

    /// <summary>
    /// Starts the job execution in the background.
    /// </summary>
    public async Task StartAsync()
    {
        lock (_lock)
        {
            if (IsRunning) return;
            IsRunning = true;
            _cts = new CancellationTokenSource();
        }

        if (!JobLockManager.TryAcquireLock(Config.Id, AppDataPath, out string? errorMessage))
        {
            lock (_lock)
            {
                IsRunning = false;
                _cts?.Dispose();
                _cts = null;
            }
            throw new InvalidOperationException($"Cannot start job: {errorMessage}");
        }

        try
        {
            // Execute the actual job implemented by subclasses
            await ExecuteAsync(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Clean exit when cancelled
        }
        finally
        {
            JobLockManager.ReleaseLock(Config.Id, AppDataPath);

            lock (_lock)
            {
                IsRunning = false;
                _cts?.Dispose();
                _cts = null;
            }
        }
    }

    public async Task StopAsync()
    {
        JobLockManager.ReleaseLock(Config.Id, AppDataPath);

        CancellationTokenSource? cts;
        lock (_lock)
        {
            cts = _cts;
        }

        if (cts != null)
        {
            await cts.CancelAsync();
        }
    }

    /// <summary>
    /// The actual execution logic that must be overridden by concrete jobs.
    /// </summary>
    protected abstract Task ExecuteAsync(CancellationToken cancellationToken);
}
