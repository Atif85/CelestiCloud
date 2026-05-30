using Avalonia.Controls;
using CelestiCloud.Core.Config;
using CelestiCloud.Core.Providers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CelestiCloud.GUI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    // The currently active view model showning on the content panel
    [ObservableProperty]
    private ViewModelBase _currentPage;

    // The width of the sidebar column.
    [ObservableProperty]
    private GridLength _sidebarWidth = new(220);

    [ObservableProperty]
    private double _sidebarMinWidth = 250;

    [ObservableProperty]
    private bool _isSidebarCollapsed;

    private double _preCollapseWidth = 220;

    private readonly ConfigManager _configManager;
    private readonly ProviderFactory _providerFactory;


    // Instantiated Page ViewModels
    private readonly DashboardViewModel _dashboardVm;
    private readonly JobsViewModel _jobsVm;
    private readonly AccountsViewModel _accountsVm;
    private readonly SettingsViewModel _settingsVm;

    public MainWindowViewModel(ConfigManager configManager, ProviderFactory providerFactory)
    {
        _configManager = configManager;
        _providerFactory = providerFactory;

        _dashboardVm = new DashboardViewModel();
        _jobsVm = new JobsViewModel();
        _accountsVm = new AccountsViewModel(_configManager, _providerFactory); // Injected
        _settingsVm = new SettingsViewModel();

        // Set the default startup screen
        _currentPage = _dashboardVm;
    }

    /// <summary>
    /// Swaps the active page on the right side of the main window.
    /// </summary>
    [RelayCommand]
    private void Navigate(string target)
    {
        CurrentPage = target.ToLower() switch
        {
            "dashboard" => _dashboardVm,
            "jobs" => _jobsVm,
            "accounts" => _accountsVm,
            "settings" => _settingsVm,
            _ => _dashboardVm
        };
    }

    /// <summary>
    /// Toggles the sidebar between its expanded/custom-resized size and a compact 60px icon strip.
    /// </summary>
    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarCollapsed = !IsSidebarCollapsed;

        if (IsSidebarCollapsed)
        {
            SidebarMinWidth = 60;
            _preCollapseWidth = SidebarWidth.Value;
            SidebarWidth = new GridLength(60);
        }
        else
        {
            // Restore back to the cached width, ensuring it is at least wider than compact mode
            SidebarMinWidth = 250;
            SidebarWidth = new GridLength(_preCollapseWidth > SidebarMinWidth ? _preCollapseWidth : SidebarMinWidth);
        }
    }
}