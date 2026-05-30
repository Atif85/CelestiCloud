using Avalonia.Controls;
using CelestiCloud.Core.Config;
using CelestiCloud.Core.Providers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CelestiCloud.GUI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private const float SIDEBAR_MIN_WIDTH = 230;

    // The width of the sidebar column.
    [ObservableProperty]
    private GridLength _sidebarWidth = new(SIDEBAR_MIN_WIDTH);

    [ObservableProperty]
    private double _sidebarMinWidth = SIDEBAR_MIN_WIDTH;

    [ObservableProperty]
    private bool _isSidebarCollapsed;

    private double _preCollapseWidth = SIDEBAR_MIN_WIDTH;

    private readonly ConfigManager _configManager;
    private readonly ProviderFactory _providerFactory;

    [ObservableProperty] private bool _isDashboardActive = true;
    [ObservableProperty] private bool _isJobsActive = false;
    [ObservableProperty] private bool _isAccountsActive = false;
    [ObservableProperty] private bool _isSettingsActive = false;

    // Instantiated Page ViewModels
    public DashboardViewModel DashboardVm { get; }
    public JobsViewModel JobsVm { get; }
    public AccountsViewModel AccountsVm { get; }
    public SettingsViewModel SettingsVm { get; }

    public MainWindowViewModel(ConfigManager configManager, ProviderFactory providerFactory)
    {
        _configManager = configManager;
        _providerFactory = providerFactory;

        DashboardVm = new DashboardViewModel();
        JobsVm = new JobsViewModel();
        AccountsVm = new AccountsViewModel(configManager, providerFactory);
        SettingsVm = new SettingsViewModel(configManager);
    }

    /// <summary>
    /// Swaps the active page on the right side of the main window.
    /// </summary>
    [RelayCommand]
    private void Navigate(string target)
    {
        // Instantly toggle visibility (0ms layout cost)
        string targetLower = target.ToLower();

        IsDashboardActive = targetLower == "dashboard";
        IsJobsActive = targetLower == "jobs";
        IsAccountsActive = targetLower == "accounts";
        IsSettingsActive = targetLower == "settings";
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
            SidebarMinWidth = SIDEBAR_MIN_WIDTH;
            SidebarWidth = new GridLength(_preCollapseWidth > SidebarMinWidth ? _preCollapseWidth : SidebarMinWidth);
        }
    }
}