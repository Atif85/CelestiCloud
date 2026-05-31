using Avalonia.Threading;
using CelestiCloud.Core.Config;
using CelestiCloud.Core.Jobs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace CelestiCloud.GUI.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly JobEngine _jobEngine;
    private readonly ConfigManager _configManager;
    private readonly DispatcherTimer _refreshTimer;

    // We expose the active jobs directly to the UI
    [ObservableProperty]
    private ObservableCollection<JobBase> _activeJobsList = [];

    [ObservableProperty]
    private string _liveLogText = string.Empty;

    [ObservableProperty]
    private bool _isLogViewerOpen;

    public DashboardViewModel(ConfigManager configManager, JobEngine jobEngine)
    {
        _configManager = configManager;
        _jobEngine = jobEngine;

        // Refresh the dashboard every 500ms
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _refreshTimer.Tick += RefreshDashboard;
        _refreshTimer.Start();
    }

    private void RefreshDashboard(object? sender, EventArgs e)
    {
        var currentActive = _jobEngine.GetActiveJobs().ToList();

        // Simple UI refresh
        ActiveJobsList.Clear();
        foreach (var job in currentActive)
        {
            ActiveJobsList.Add(job);
        }
    }

    [RelayCommand]
    private void StopAllJobs()
    {
        _ = _jobEngine.StopAllAsync();
    }

    [RelayCommand]
    private void OpenLogViewer()
    {
        string logsDir = Path.Combine(_configManager.GetAppDataPath(), "logs");
        string logFile = Path.Combine(logsDir, $"CelestiCloud_GUI.log");

        if (File.Exists(logFile))
        {
            try
            {
                // We MUST use FileShare.ReadWrite because the FileLogger is actively writing to it!
                using var fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);

                // Read the end of the log file (e.g., last few kilobytes) so we don't freeze the UI on a huge log
                string fullLog = sr.ReadToEnd();

                // Take the last 10,000 characters just to keep the UI snappy
                LiveLogText = fullLog.Length > 10000 ? "..." + fullLog[^10000..] : fullLog;
            }
            catch (Exception ex)
            {
                LiveLogText = $"Error reading log: {ex.Message}";
            }
        }
        else
        {
            LiveLogText = "No logs generated for today yet.";
        }

        IsLogViewerOpen = true;
    }

    [RelayCommand]
    private void CloseLogViewer()
    {
        IsLogViewerOpen = false;
        LiveLogText = string.Empty;
    }
}