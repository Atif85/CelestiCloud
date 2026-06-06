using CelestiCloud.Core.Models;

namespace CelestiCloud.Core.Jobs;

public abstract class JobBase
{
    public JobConfig Config { get; }
    public bool IsRunning { get; private set; }
    public SyncState State { get; } = new();

    public event Action<string, string, string>? NotificationRequested;

    protected readonly string AppDataPath;

    private readonly Lock _lock = new();
    private CancellationTokenSource? _cts;
    private Task? _executionTask;

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
            _executionTask = ExecuteAsync(_cts.Token);
            await _executionTask;
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
                _executionTask = null;
            }
        }
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        Task? executionTask;

        lock (_lock)
        {
            cts = _cts;
            executionTask = _executionTask;
        }

        if (cts != null)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException) { }
            catch (Exception) { }
        }

        if (executionTask != null)
        {
            try
            {
                await executionTask;
            }
            catch (OperationCanceledException) { }
            catch (Exception) { }
        }

        JobLockManager.ReleaseLock(Config.Id, AppDataPath);
    }

    /// <summary>
    /// The actual execution logic that must be overridden by concrete jobs.
    /// </summary>
    protected abstract Task ExecuteAsync(CancellationToken cancellationToken);

    protected void RequestNotification(string title, string message, string severity)
    {
        NotificationRequested?.Invoke(title, message, severity);
    }
}
