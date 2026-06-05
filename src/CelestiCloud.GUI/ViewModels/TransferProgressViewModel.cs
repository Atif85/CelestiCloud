using CelestiCloud.GUI.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Diagnostics;
using System.IO;

namespace CelestiCloud.GUI.ViewModels;

public partial class TransferProgressViewModel : ViewModelBase
{
    public string FileName { get; }
    public long FileSize { get; }
    public string FileSizeText => ByteFormatter.Format(FileSize);

    [ObservableProperty]
    private double _progress;

    public TransferProgressViewModel(string fileName, long fileSize, double progress)
    {
        FileName = fileName;
        FileSize = fileSize;
        _progress = progress;
    }

    [RelayCommand]
    private void OpenContainingFolder()
    {
        if (string.IsNullOrEmpty(FileName)) return;

        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Opens file explorer and automatically highlights the specific file
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{FileName}\"",
                    UseShellExecute = true
                });
            }
            else if (OperatingSystem.IsMacOS())
            {
                // Native macOS command to open Finder and highlight the specific file [10]
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = $"-R \"{FileName}\"",
                    UseShellExecute = true
                });
            }
            else if (OperatingSystem.IsLinux())
            {
                // Linux fallback: open the parent directory
                string? parentDir = Path.GetDirectoryName(FileName);
                if (parentDir != null && Directory.Exists(parentDir))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "xdg-open",
                        Arguments = $"\"{parentDir}\"",
                        UseShellExecute = true
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open containing folder: {ex.Message}");
        }
    }
}