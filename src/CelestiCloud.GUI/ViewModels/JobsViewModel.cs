using Avalonia.Threading;
using CelestiCloud.Core.Config;
using CelestiCloud.Core.Models;
using CelestiCloud.Core.Jobs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace CelestiCloud.GUI.ViewModels;

public partial class JobsViewModel : ViewModelBase
{
    private readonly ConfigManager _configManager;
    private readonly JobEngine _jobEngine;

    [ObservableProperty]
    private ObservableCollection<JobConfig> _jobs = [];

    [ObservableProperty]
    private ObservableCollection<AccountConfig> _availableAccounts = [];

    // The job currently being created or edited
    [ObservableProperty]
    private JobConfig? _editingJob;

    // Temporary collections for the UI to bind to while editing
    [ObservableProperty]
    private ObservableCollection<string> _editingLocalPaths = [];

    [ObservableProperty]
    private ObservableCollection<string> _editingIgnorePatterns = [];

    [ObservableProperty]
    private string _newLocalPathInput = string.Empty;

    [ObservableProperty]
    private string _newIgnorePatternInput = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDimmerVisible))]
    private bool _isModalOpen;

    public bool IsDimmerVisible => IsModalOpen;

    // A flag so the UI knows whether to show "Create" or "Save Changes"
    [ObservableProperty]
    private bool _isCreatingNew;

    public JobsViewModel(ConfigManager configManager, JobEngine jobEngine)
    {
        _configManager = configManager;
        _jobEngine = jobEngine;

        LoadData();
    }

    private void LoadData()
    {
        var loadedJobs = _configManager.LoadAllJobs();
        var loadedAccounts = _configManager.LoadAllAccounts();

        Dispatcher.UIThread.Post(() =>
        {
            Jobs.Clear();
            foreach (var job in loadedJobs) Jobs.Add(job);

            AvailableAccounts.Clear();
            foreach (var acc in loadedAccounts) AvailableAccounts.Add(acc);
        });
    }

    [RelayCommand]
    private void OpenAddJob()
    {
        IsCreatingNew = true;
        EditingJob = new JobConfig
        {
            TargetAccountId = AvailableAccounts.FirstOrDefault()?.Id ?? string.Empty
        };

        EditingLocalPaths.Clear();
        EditingIgnorePatterns.Clear();

        // Add defaults
        EditingIgnorePatterns.Add("*.tmp");

        IsModalOpen = true;
    }

    [RelayCommand]
    private void OpenEditJob(JobConfig job)
    {
        IsCreatingNew = false;

        // Clone the job so we don't modify the list directly until "Save" is clicked
        EditingJob = new JobConfig
        {
            Id = job.Id,
            Name = job.Name,
            JobType = job.JobType,
            TargetAccountId = job.TargetAccountId,
            RemoteRootPath = job.RemoteRootPath,
            AutoStart = job.AutoStart,
            MaxConcurrentTransfers = job.MaxConcurrentTransfers,
            LocalPaths = [.. job.LocalPaths],
            IgnorePatterns = [.. job.IgnorePatterns]
        };

        EditingLocalPaths = new ObservableCollection<string>(job.LocalPaths);
        EditingIgnorePatterns = new ObservableCollection<string>(job.IgnorePatterns);

        IsModalOpen = true;
    }

    [RelayCommand]
    private void CloseModal()
    {
        IsModalOpen = false;
        EditingJob = null;
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

        _configManager.SaveJob(EditingJob);

        LoadData();
        CloseModal();
    }

    [RelayCommand]
    private async Task DeleteJob()
    {
        if (EditingJob == null) return;

        // Ensure we stop it if it's running
        await _jobEngine.StopJobAsync(EditingJob.Id);

        _configManager.DeleteJob(EditingJob.Id);

        LoadData();
        CloseModal();
    }
}