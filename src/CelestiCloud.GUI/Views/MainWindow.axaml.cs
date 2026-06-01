using Avalonia.Controls;
using System.ComponentModel;
using CelestiCloud.Core.Config;

namespace CelestiCloud.GUI.Views;

public partial class MainWindow : Window
{
    private bool _forceClose;
    private readonly ConfigManager _configManager;

    public MainWindow()
    {
        InitializeComponent();
        _configManager = new ConfigManager();
    }

    // Forces the window to bypass the Tray intercept and exit entirely.
    public void ForceCloseAndShutdown()
    {
        _forceClose = true;
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        var settings = _configManager.LoadSettings();

        if (!_forceClose && settings.MinimizeToTrayOnClose)
        {
            e.Cancel = true; // Prevents process termination
            this.Hide();     // Soft-hides the Window to the background tray
        }
        else
        {
            base.OnClosing(e);
        }
    }
}