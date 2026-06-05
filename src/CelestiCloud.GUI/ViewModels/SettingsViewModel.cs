using Avalonia;
using Avalonia.Styling;
using CelestiCloud.Core.Config;
using CelestiCloud.Core.Jobs;
using CelestiCloud.Core.Logging;
using CelestiCloud.Core.Models;
using CelestiCloud.GUI.Helpers;
using CelestiCloud.GUI.Logging;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace CelestiCloud.GUI.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ConfigManager _configManager;
    private readonly JobEngine _jobEngine;
    private readonly ObservableUiLogger _uiLogger;
    private readonly AppSettings _currentSettings;

    public ObservableCollection<string> AvailableThemes { get; } = ["System", "Light", "Dark"];

    [ObservableProperty]
    private bool _startOnStartup;

    [ObservableProperty]
    private bool _minimizeToTrayOnClose;

    [ObservableProperty]
    private string _selectedTheme;

    [ObservableProperty]
    private string _uploadLimitInput = string.Empty;

    private int _uploadLimitKbps;

    public SettingsViewModel(ConfigManager configManager, JobEngine jobEngine, ObservableUiLogger uiLogger)
    {
        _configManager = configManager;
        _jobEngine = jobEngine;
        _uiLogger = uiLogger;
        _currentSettings = _configManager.LoadSettings();

        _startOnStartup = _currentSettings.StartOnStartup;
        _minimizeToTrayOnClose = _currentSettings.MinimizeToTrayOnClose;

        // Initialize UI properties with saved values (bypassing the On...Changed triggers temporarily)
        _selectedTheme = _currentSettings.Theme;

        _uploadLimitKbps = _currentSettings.GlobalUploadLimitKbps;
        _uploadLimitInput = ByteFormatter.FormatKbps(_uploadLimitKbps);

        // Apply the loaded theme immediately on startup
        ApplyTheme(_selectedTheme);
    }

    // CommunityToolkit automatically calls this when SelectedTheme changes!
    partial void OnSelectedThemeChanged(string value)
    {
        _currentSettings.Theme = value;
        _configManager.SaveSettings(_currentSettings);
        _uiLogger.Log(LogLevel.Info, $"Theme changed to '{value}'.");
        ApplyTheme(value);
    }

    partial void OnUploadLimitInputChanged(string value)
    {
        int newLimitKbps = ByteFormatter.ParseToKbps(value);

        if (newLimitKbps != _uploadLimitKbps)
        {
            _uploadLimitKbps = newLimitKbps;
            _currentSettings.GlobalUploadLimitKbps = newLimitKbps;
            _configManager.SaveSettings(_currentSettings);

            _uiLogger.Log(LogLevel.Info, $"Upload limit updated to: {ByteFormatter.FormatKbps(newLimitKbps)}");
            _jobEngine.UpdateUploadLimit(newLimitKbps);
        }
    }

    public void FormatInputOnLostFocus()
    {
        UploadLimitInput = ByteFormatter.FormatKbps(_uploadLimitKbps);
    }

    private void ApplyTheme(string themeStr)
    {
        if (Application.Current == null) return;

        // Avalonia uses RequestedThemeVariant to dynamically swap Light/Dark dictionaries!
        Application.Current.RequestedThemeVariant = themeStr switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default // Follows OS System theme
        };
    }

    partial void OnStartOnStartupChanged(bool value)
    {
        _currentSettings.StartOnStartup = value;
        _configManager.SaveSettings(_currentSettings);
        _uiLogger.Log(LogLevel.Info, value ? "Enabled start on startup." : "Disabled start on startup.");

        // Configures the launch-on-startup registries/files across platforms!
        StartupManager.SetStartup(value);
    }

    partial void OnMinimizeToTrayOnCloseChanged(bool value)
    {
        _currentSettings.MinimizeToTrayOnClose = value;
        _configManager.SaveSettings(_currentSettings);
        _uiLogger.Log(LogLevel.Info, value ? "Minimize to tray on close enabled." : "Minimize to tray on close disabled.");
    }
}