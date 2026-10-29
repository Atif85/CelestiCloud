using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CelestiCloud.GUI.ViewModels;

public partial class ActiveOperationViewModel : ObservableObject
{
    public string Name { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BadgeBackground))]
    [NotifyPropertyChangedFor(nameof(BadgeForeground))]
    private string _operationText;

    private bool IsDelete => OperationText.Contains("Delet", StringComparison.OrdinalIgnoreCase);

    public string BadgeBackground => IsDelete ? "#3D1414" : "#14283D";
    public string BadgeForeground => IsDelete ? "#FF6B6B" : "#64B5F6";

    public ActiveOperationViewModel(string name, string operationText)
    {
        Name = name;
        _operationText = operationText;
    }
}