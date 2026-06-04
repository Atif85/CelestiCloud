using Avalonia.Threading;
using CelestiCloud.Core.Config;
using CelestiCloud.Core.Jobs;
using CelestiCloud.Core.Logging;
using CelestiCloud.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CelestiCloud.GUI.Logging;
using CelestiCloud.GUI.Services;
using System.IO;

namespace CelestiCloud.GUI.ViewModels;

public partial class JobsViewModel : ViewModelBase
{
    private readonly ConfigManager _configManager;
    private readonly JobEngine _jobEngine;
    private readonly ObservableUiLogger _uiLogger;
    private readonly INotificationService _notificationService;

    private readonly DispatcherTimer _statusTimer;

    [ObservableProperty]
    private ObservableCollection<JobUiModel> _jobs = [];

    [ObservableProperty]
    private ObservableCollection<AccountConfig> _availableAccounts = [];

    // --- VIEW STATE ---
    [ObservableProperty]
    private JobConfig? _selectedJobDetails;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStartButtonVisible))]
    [NotifyPropertyChangedFor(nameof(IsStopButtonVisible))]
    private bool _isSelectedJobRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStartButtonVisible))]
    [NotifyPropertyChangedFor(nameof(IsStopButtonVisible))]
    [NotifyPropertyChangedFor(nameof(IsProcessingButtonVisible))]
    private bool _isTogglingState;

    public bool IsStartButtonVisible => !IsSelectedJobRunning && !IsTogglingState;
    public bool IsStopButtonVisible => IsSelectedJobRunning && !IsTogglingState;
    public bool IsProcessingButtonVisible => IsTogglingState;

    // --- EDIT STATE ---
    [ObservableProperty]
    private JobConfig? _editingJob;

    [ObservableProperty]
    private AccountConfig? _editingTargetAccount;

    [ObservableProperty]
    private ObservableCollection<string> _editingLocalPaths = [];

    [ObservableProperty]
    private ObservableCollection<string> _editingIgnorePatterns = [];

    [ObservableProperty]
    private string _newLocalPathInput = string.Empty;

    [ObservableProperty]
    private string _newIgnorePatternInput = string.Empty;

    [ObservableProperty]
    private decimal? _editingMaxConcurrentTransfers; // decimal? matches NumericUpDown perfectly

    // --- MODAL VISIBILITY ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDimmerVisible))]
    private bool _isViewModalOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDimmerVisible))]
    private bool _isEditModalOpen;

    [ObservableProperty]
    private bool _isCreatingNew;

    public bool IsDimmerVisible => IsViewModalOpen || IsEditModalOpen;

    public JobsViewModel(ConfigManager configManager, JobEngine jobEngine, ObservableUiLogger uiLogger, INotificationService notiService)
    {
        _configManager = configManager;
        _jobEngine = jobEngine;
        _uiLogger = uiLogger;
        _notificationService = notiService;

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusTimer.Tick += (s, e) =>
        {
            if (SelectedJobDetails != null)
            {
                IsSelectedJobRunning = _jobEngine.GetActiveJobs().Any(j => j.Config.Id == SelectedJobDetails.Id);
            }

            var activeJobIds = _jobEngine.GetActiveJobs().Select(j => j.Config.Id).ToHashSet();
            foreach (var uiJob in Jobs)
            {
                uiJob.IsRunning = activeJobIds.Contains(uiJob.Config.Id);
            }
        };
        _statusTimer.Start();

        LoadData();
    }

    private void LoadData()
    {
        var loadedJobs = _configManager.LoadAllJobs();
        var loadedAccounts = _configManager.LoadAllAccounts();

        Dispatcher.UIThread.Post(() =>
        {
            Jobs.Clear();
            var activeJobIds = _jobEngine.GetActiveJobs().Select(j => j.Config.Id).ToHashSet();
            foreach (var job in loadedJobs)
            {
                bool isRunning = activeJobIds.Contains(job.Id);
                Jobs.Add(new JobUiModel(job, isRunning));
            }

            AvailableAccounts.Clear();
            foreach (var acc in loadedAccounts) AvailableAccounts.Add(acc);

            if (SelectedJobDetails != null)
            {
                SelectedJobDetails = loadedJobs.FirstOrDefault(j => j.Id == SelectedJobDetails.Id) ?? SelectedJobDetails;
            }
        });
    }

    [RelayCommand]
    private void OpenJobDetails(JobConfig job)
    {
        if (job == null) return;
        SelectedJobDetails = job;
        IsSelectedJobRunning = _jobEngine.GetActiveJobs().Any(j => j.Config.Id == job.Id);
        IsViewModalOpen = true;
    }

    [RelayCommand]
    private void CloseJobDetails()
    {
        IsViewModalOpen = false;
        SelectedJobDetails = null;
    }

    [RelayCommand]
    private async Task ToggleSelectedJobState()
    {
        if (SelectedJobDetails == null || IsTogglingState) return;

        IsTogglingState = true;

        try
        {
            if (IsSelectedJobRunning)
            {
                await _jobEngine.StopJobAsync(SelectedJobDetails.Id);
            }
            else
            {
                await _jobEngine.StartJobAsync(SelectedJobDetails.Id);
            }

            IsSelectedJobRunning = !IsSelectedJobRunning;
        }
        finally
        {
            IsTogglingState = false;
        }
    }


    [RelayCommand]
    private void OpenAddJob()
    {
        IsCreatingNew = true;
        EditingJob = new JobConfig();

        EditingTargetAccount = AvailableAccounts.FirstOrDefault();

        EditingLocalPaths.Clear();
        EditingIgnorePatterns.Clear();

        // Add defaults
        EditingIgnorePatterns.Add("*.tmp");

        EditingMaxConcurrentTransfers = 4;

        IsEditModalOpen = true;
    }

    [RelayCommand]
    private void OpenEditJob()
    {
        if (SelectedJobDetails == null) return;

        IsCreatingNew = false;

        // Clone the job for editing
        EditingJob = new JobConfig
        {
            Id = SelectedJobDetails.Id,
            Name = SelectedJobDetails.Name,
            JobType = SelectedJobDetails.JobType,
            TargetAccountId = SelectedJobDetails.TargetAccountId,
            RemoteRootPath = SelectedJobDetails.RemoteRootPath,
            AutoStart = SelectedJobDetails.AutoStart,
            MaxConcurrentTransfers = SelectedJobDetails.MaxConcurrentTransfers,
            LocalPaths = [.. SelectedJobDetails.LocalPaths],
            IgnorePatterns = [.. SelectedJobDetails.IgnorePatterns]
        };

        EditingTargetAccount = AvailableAccounts.FirstOrDefault(a => a.Id == SelectedJobDetails.TargetAccountId);

        EditingLocalPaths = new ObservableCollection<string>(SelectedJobDetails.LocalPaths);
        EditingIgnorePatterns = new ObservableCollection<string>(SelectedJobDetails.IgnorePatterns);
        EditingMaxConcurrentTransfers = SelectedJobDetails.MaxConcurrentTransfers;

        // Transition modals
        IsViewModalOpen = false;
        IsEditModalOpen = true;
    }

    [RelayCommand]
    private void CloseEditModal()
    {
        IsEditModalOpen = false;
        EditingJob = null;

        if (!IsCreatingNew)
        {
            IsViewModalOpen = true;
        }
    }

    partial void OnEditingTargetAccountChanged(AccountConfig? value)
{
    if (EditingJob != null && value != null)
    {
        EditingJob.TargetAccountId = value.Id;
    }
}

    [RelayCommand]
    private void AddLocalPath()
    {
        if (!string.IsNullOrWhiteSpace(NewLocalPathInput) && !EditingLocalPaths.Contains(NewLocalPathInput))
        {
            EditingLocalPaths.Add(NewLocalPathInput.Trim());
            NewLocalPathInput = string.Empty;
        }
    }

    [RelayCommand]
    private void RemoveLocalPath(string path) => EditingLocalPaths.Remove(path);

    [RelayCommand]
    private void AddIgnorePattern()
    {
        if (!string.IsNullOrWhiteSpace(NewIgnorePatternInput) && !EditingIgnorePatterns.Contains(NewIgnorePatternInput))
        {
            EditingIgnorePatterns.Add(NewIgnorePatternInput.Trim());
            NewIgnorePatternInput = string.Empty;
        }
    }

    [RelayCommand]
    private void RemoveIgnorePattern(string pattern) => EditingIgnorePatterns.Remove(pattern);

    [RelayCommand]
    private void SaveJob()
    {
        if (EditingJob == null || string.IsNullOrWhiteSpace(EditingJob.Name)) return;

        // Sync the temporary collections back to the model
        EditingJob.LocalPaths = [.. EditingLocalPaths];
        EditingJob.IgnorePatterns = [.. EditingIgnorePatterns];
        EditingJob.MaxConcurrentTransfers = (int)(EditingMaxConcurrentTransfers ?? 4);

        var allOtherJobs = _configManager.LoadAllJobs().Where(j => j.Id != EditingJob.Id);

        foreach (var path in EditingLocalPaths)
        {
            var conflictingJob = allOtherJobs.FirstOrDefault(j => j.LocalPaths.Any(p => p.Equals(path, StringComparison.OrdinalIgnoreCase)));
            if (conflictingJob != null)
            {
                _notificationService.Show(
                    "Directory Conflict",
                    $"'{Path.GetFileName(path)}' is already being watched by the job '{conflictingJob.Name}'. Running both concurrently may cause transfer conflicts.",
                    NotificationLevel.Warning
                );
            }
        }

        _configManager.SaveJob(EditingJob);

        if (EditingJob.LocalPaths.Count == 0)
        {
            string messege = $"Job '{EditingJob.Name}' was saved but has no local paths. No directories will be watched.";
            _notificationService.Show(
                title: "Empty Folder Warning",
                message: messege,
                level: NotificationLevel.Warning
            );

            _uiLogger.Log(LogLevel.Warning, messege);
        }

        bool ignoresEverything = EditingJob.IgnorePatterns.Contains("*") &&
                             !EditingJob.IgnorePatterns.Any(static p => p.StartsWith('!'));

        if (EditingJob.IgnorePatterns.Contains("*"))
        {
            string messege = $"Job '{EditingJob.Name}' is configured to ignore everything (*) with no negation rules (!). No files will be uploaded.";
            _notificationService.Show(
                title: "Ignore Policy Notice",
                message: messege,
                level: NotificationLevel.Warning,
                duration: TimeSpan.FromSeconds(5)
            );

            _uiLogger.Log(LogLevel.Warning, messege);
        }

        if (IsCreatingNew)
        {
            _uiLogger.Log(LogLevel.Info, $"Created job '{EditingJob.Name}'.");
        }
        else
        {
            _uiLogger.Log(LogLevel.Info, $"Updated job '{EditingJob.Name}'.");
        }

        LoadData();
        IsEditModalOpen = false;
        if (!IsCreatingNew) IsViewModalOpen = true;
    }

    [RelayCommand]
    private async Task DeleteJob()
    {
        if (EditingJob == null) return;

        // Ensure we stop it if it's running
        await _jobEngine.StopJobAsync(EditingJob.Id);

        _configManager.DeleteJob(EditingJob.Id);
        _uiLogger.Log(LogLevel.Info, $"Deleted job '{EditingJob.Name}'.");

        LoadData();
        IsEditModalOpen = false;
        SelectedJobDetails = null;
    }

    [RelayCommand]
    private async Task BrowseLocalPathAsync()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var topLevel = TopLevel.GetTopLevel(desktop.MainWindow);

            if (topLevel != null)
            {
                // Open the native operating system's folder selection dialog
                var result = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select Folder to Sync",
                    AllowMultiple = false
                });

                if (result != null && result.Count > 0)
                {
                    // Convert the storage path URI to a standard, absolute local OS path 
                    string selectedPath = result[0].Path.LocalPath;

                    NewLocalPathInput = selectedPath;
                }
            }
        }
    }
}