using Avalonia;
using Avalonia.Styling;
using CelestiCloud.Core.Config;
using CelestiCloud.Core.Jobs;
using CelestiCloud.Core.Models;
using CelestiCloud.GUI.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace CelestiCloud.GUI.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ConfigManager _configManager;
    private readonly JobEngine _jobEngine;
    private readonly AppSettings _currentSettings;

    public ObservableCollection<string> AvailableThemes { get; } = ["System", "Light", "Dark"];

    [ObservableProperty]
    private bool _startOnStartup;

    [ObservableProperty]
    private bool _minimizeToTrayOnClose;

    [ObservableProperty]
    private string _selectedTheme;

    [ObservableProperty]
    private decimal? _uploadLimitKbps;

    public SettingsViewModel(ConfigManager configManager, JobEngine jobEngine)
    {
        _configManager = configManager;
        _jobEngine = jobEngine;
        _currentSettings = _configManager.LoadSettings();

        _startOnStartup = _currentSettings.StartOnStartup;
        _minimizeToTrayOnClose = _currentSettings.MinimizeToTrayOnClose;

        // Initialize UI properties with saved values (bypassing the On...Changed triggers temporarily)
        _selectedTheme = _currentSettings.Theme;
        _uploadLimitKbps = _currentSettings.GlobalUploadLimitKbps;

        // Apply the loaded theme immediately on startup
        ApplyTheme(_selectedTheme);
    }

    // CommunityToolkit automatically calls this when SelectedTheme changes!
    partial void OnSelectedThemeChanged(string value)
    {
        _currentSettings.Theme = value;
        _configManager.SaveSettings(_currentSettings);
        ApplyTheme(value);
    }

    partial void OnUploadLimitKbpsChanged(decimal? value)
    {
        int intValue = (int)(value ?? 0);
        _currentSettings.GlobalUploadLimitKbps = intValue;
        _configManager.SaveSettings(_currentSettings);

        _jobEngine.UpdateUploadLimit(intValue);
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

        // Configures the launch-on-startup registries/files across platforms!
        StartupManager.SetStartup(value);
    }

    partial void OnMinimizeToTrayOnCloseChanged(bool value)
    {
        _currentSettings.MinimizeToTrayOnClose = value;
        _configManager.SaveSettings(_currentSettings);
    }
}