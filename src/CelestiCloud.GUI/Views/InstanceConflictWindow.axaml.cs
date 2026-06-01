using Avalonia.Controls;
using System.ComponentModel;
using System.Threading.Tasks;

namespace CelestiCloud.GUI.Views;

public enum InstanceDecision
{
    CloseOther,
    Exit
}

public partial class InstanceConflictWindow : Window
{
    private readonly TaskCompletionSource<InstanceDecision> _tcs = new();

    public InstanceConflictWindow()
    {
        InitializeComponent();
    }

    public Task<InstanceDecision> GetDecisionAsync() => _tcs.Task;

    public void OnCloseOtherClick(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _tcs.TrySetResult(InstanceDecision.CloseOther);
        Close();
    }

    public void OnExitClick(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _tcs.TrySetResult(InstanceDecision.Exit);
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // If the window is closed without clicking a button (e.g. system X button), default to exiting
        _tcs.TrySetResult(InstanceDecision.Exit);
        base.OnClosing(e);
    }
}