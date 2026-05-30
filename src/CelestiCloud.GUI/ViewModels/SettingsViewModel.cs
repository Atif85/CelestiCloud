using Avalonia;
using Avalonia.Styling;
using CelestiCloud.Core.Config;
using CelestiCloud.Core.Jobs;
using CelestiCloud.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace CelestiCloud.GUI.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ConfigManager _configManager;
    private readonly JobEngine _syncEngine;
    private readonly AppSettings _currentSettings;

    public ObservableCollection<string> AvailableThemes { get; } = ["System", "Light", "Dark"];

    [ObservableProperty]
    private string _selectedTheme;

    [ObservableProperty]
    private decimal? _uploadLimitKbps;

    public SettingsViewModel(ConfigManager configManager, JobEngine syncEngine)
    {
        _configManager = configManager;
        _syncEngine = syncEngine;
        _currentSettings = _configManager.LoadSettings();

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

        _syncEngine.UpdateUploadLimit(intValue);


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
}