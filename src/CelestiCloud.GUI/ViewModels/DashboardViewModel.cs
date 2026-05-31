using Avalonia.Threading;
using CelestiCloud.Core.Config;
using CelestiCloud.Core.Jobs;
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
    private readonly DispatcherTimer _refreshTimer;

    // We expose the active jobs directly to the UI
    [ObservableProperty]
    private ObservableCollection<JobBase> _activeJobsList = [];

    [ObservableProperty]
    private string _liveLogText = string.Empty;

    [ObservableProperty]
    private bool _isLogViewerOpen;
    public ObservableCollection<LogMessage> LiveLogs { get; }

    public DashboardViewModel(ConfigManager configManager, JobEngine jobEngine, Logging.ObservableUiLogger uiLogger)
    {
        _configManager = configManager;
        _jobEngine = jobEngine;

        LiveLogs = uiLogger.LiveLogs;

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