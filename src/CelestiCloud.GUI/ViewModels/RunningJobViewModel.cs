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

    public ObservableCollection<TransferProgressViewModel> ActiveTransfers { get; } = new();

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
    }
}