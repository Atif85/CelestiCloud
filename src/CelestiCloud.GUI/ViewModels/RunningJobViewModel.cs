using System.Collections.ObjectModel;
using System.Linq;
using CelestiCloud.Core.Jobs;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CelestiCloud.GUI.ViewModels;

public partial class RunningJobViewModel : ViewModelBase
{
    public string JobId { get; }
    public string JobName { get; }
    public string JobType { get; }
    public string RemoteRootPath { get; }

    [ObservableProperty]
    private int _filesProcessed;

    [ObservableProperty]
    private int _filesFound;

    [ObservableProperty]
    private string _statusMessage = "Starting...";

    [ObservableProperty]
    private bool _isStopping;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _hasActiveOperations;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _hasActiveTransfers;

    public bool IsIdle => !HasActiveOperations && !HasActiveTransfers;

    public ObservableCollection<TransferProgressViewModel> ActiveTransfers { get; } = [];
    public ObservableCollection<ActiveOperationViewModel> ActiveOperations { get; } = [];

    public RunningJobViewModel(JobBase job)
    {
        JobId = job.Config.Id;
        JobName = job.Config.Name;
        JobType = job.Config.JobType.ToString();
        RemoteRootPath = job.Config.RemoteRootPath;

        Update(job);
    }

    public void Update(JobBase job)
    {
        FilesProcessed = job.State.FilesProcessed;
        FilesFound = job.State.FilesFound;
        StatusMessage = job.State.CurrentStatus;

        // Sync Active Operations (Deletions / Renames)
        var currentOps = job.State.ActiveOperations.ToList();

        for (int i = ActiveOperations.Count - 1; i >= 0; i--)
        {
            var existing = ActiveOperations[i];
            if (!currentOps.Any(op => op.Key == existing.Name))
            {
                ActiveOperations.RemoveAt(i);
            }
        }

        foreach (var op in currentOps)
        {
            var existing = ActiveOperations.FirstOrDefault(o => o.Name == op.Key);
            if (existing == null)
            {
                ActiveOperations.Add(new ActiveOperationViewModel(op.Key, op.Value));
            }
            else
            {
                existing.OperationText = op.Value;
            }
        }

        HasActiveOperations = ActiveOperations.Count > 0;

        // Sync Active Upload Transfers
        var currentTransfers = job.State.ActiveTransfers.ToList();

        for (int i = ActiveTransfers.Count - 1; i >= 0; i--)
        {
            var existing = ActiveTransfers[i];
            if (!currentTransfers.Any(t => t.Key == existing.FileName))
            {
                ActiveTransfers.RemoveAt(i);
            }
        }

        foreach (var transfer in currentTransfers)
        {
            var existing = ActiveTransfers.FirstOrDefault(t => t.FileName == transfer.Key);
            if (existing == null)
            {
                long sizeOnDisk = 0;
                try
                {
                    if (System.IO.File.Exists(transfer.Key))
                    {
                        sizeOnDisk = new System.IO.FileInfo(transfer.Key).Length;
                    }
                }
                catch
                {
                    // Fallback to 0 if the file is strictly locked by the OS
                }

                ActiveTransfers.Add(new TransferProgressViewModel(transfer.Key, sizeOnDisk, transfer.Value));
            }
            else
            {
                existing.Progress = transfer.Value;
            }
        }

        HasActiveTransfers = ActiveTransfers.Count > 0;
    }
}