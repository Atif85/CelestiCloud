using Avalonia.Threading;
using CelestiCloud.Core.Config;
using CelestiCloud.Core.IO;
using CelestiCloud.Core.Jobs;
using CelestiCloud.GUI.Helpers;
using CelestiCloud.GUI.Logging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace CelestiCloud.GUI.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly JobEngine _jobEngine;
    private readonly ConfigManager _configManager;

    private readonly DispatcherTimer _jobLayoutTimer;
    private readonly DispatcherTimer _speedometerTimer;

    // We expose the active jobs directly to the UI
    [ObservableProperty]
    private ObservableCollection<RunningJobViewModel> _activeJobsList = [];

    [ObservableProperty]
    private string _currentSpeedText = "0 B/s";

    [ObservableProperty]
    private string _totalUploadedText = "0 B";

    [ObservableProperty]
    private string _liveLogText = string.Empty;

    [ObservableProperty]
    private bool _isLogViewerOpen;

    public ObservableCollection<LogMessage> LiveLogs { get; }

    public DashboardViewModel(ConfigManager configManager, JobEngine jobEngine, ObservableUiLogger uiLogger)
    {
        _configManager = configManager;
        _jobEngine = jobEngine;

        LiveLogs = uiLogger.LiveLogs;

        _jobLayoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _jobLayoutTimer.Tick += RefreshJobLayout;
        _jobLayoutTimer.Start();

        _speedometerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _speedometerTimer.Tick += RefreshDashboard;
        _speedometerTimer.Start();
    }

    private void RefreshJobLayout(object? sender, EventArgs e)
    {
        var currentActive = _jobEngine.GetActiveJobs().ToList();

        // 1. Remove jobs that stopped
        for (int i = ActiveJobsList.Count - 1; i >= 0; i--)
        {
            var existingJob = ActiveJobsList[i];
            if (!currentActive.Any(j => j.Config.Id == existingJob.JobId))
            {
                ActiveJobsList.RemoveAt(i);
            }
        }

        // 2. Add newly started jobs as UI-ready ViewModels
        foreach (var job in currentActive)
        {
            if (!ActiveJobsList.Any(j => j.JobId == job.Config.Id))
            {
                ActiveJobsList.Add(new RunningJobViewModel(job));
            }
        }
    }

    private void RefreshDashboard(object? sender, EventArgs e)
    {
        CurrentSpeedText = ByteFormatter.Format(BandwidthMonitor.CurrentSpeedBps) + "/s";
        TotalUploadedText = ByteFormatter.Format(BandwidthMonitor.TotalBytesUploaded);

        // Update progress metrics on all active card view models!
        var currentActive = _jobEngine.GetActiveJobs().ToList();
        foreach (var uiJob in ActiveJobsList)
        {
            var coreJob = currentActive.FirstOrDefault(j => j.Config.Id == uiJob.JobId);
            if (coreJob != null)
            {
                uiJob.Update(coreJob); 
            }
        }
    }

    [RelayCommand]
    private void StopAllJobs()
    {
        _ = _jobEngine.StopAllAsync();
    }

    [RelayCommand]
    private void OpenLogViewer() => IsLogViewerOpen = true;

    [RelayCommand]
    private void CloseLogViewer() => IsLogViewerOpen = false;

    [RelayCommand]
    private void OpenLogsFolder()
    {
        string logsDir = Path.Combine(_configManager.GetAppDataPath(), "logs");
        if (Directory.Exists(logsDir))
        {
            try
            {
                // Cross-platform way to open a folder in the native file explorer
                if (OperatingSystem.IsWindows())
                    Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = logsDir });
                else if (OperatingSystem.IsMacOS())
                    Process.Start("open", logsDir);
                else if (OperatingSystem.IsLinux())
                    Process.Start("xdg-open", logsDir);
            }
            catch (Exception ex)
            {
                // Handle edge-case if OS lacks default file explorer mapping
                Console.WriteLine(ex.Message);
            }
        }
    }
}