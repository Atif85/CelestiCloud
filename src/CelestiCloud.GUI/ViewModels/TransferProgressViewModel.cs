using CommunityToolkit.Mvvm.ComponentModel;

namespace CelestiCloud.GUI.ViewModels;

public partial class TransferProgressViewModel : ViewModelBase
{
    public string FileName { get; }

    [ObservableProperty]
    private double _progress;

    public TransferProgressViewModel(string fileName, double progress)
    {
        FileName = fileName;
        _progress = progress;
    }
}