using CelestiCloud.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CelestiCloud.GUI.ViewModels;

public partial class JobUiModel(JobConfig config, bool isRunning = false) : ObservableObject
{
    public JobConfig Config { get; } = config;

    [ObservableProperty]
    private bool _isRunning = isRunning;
}