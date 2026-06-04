using Avalonia.Threading;
using CelestiCloud.Core.Config;
using CelestiCloud.Core.Logging;
using CelestiCloud.Core.Models;
using CelestiCloud.Core.Providers;
using CelestiCloud.GUI.Logging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Data.Common;
using System.Security.Principal;
using System.Threading.Tasks;

namespace CelestiCloud.GUI.ViewModels;

public partial class AccountsViewModel : ViewModelBase
{
    private readonly ConfigManager _configManager;
    private readonly ProviderFactory _providerFactory;
    private readonly ObservableUiLogger _uiLogger;

    [ObservableProperty]
    private ObservableCollection<AccountConfig> _accounts = [];


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDimmerVisible))]
    private bool _isAddAccountOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDimmerVisible))]
    private bool _isDetailsOpen;

    [ObservableProperty]
    private bool _isAuthenticating;

    // Controls whether the background dark-overlay is shown
    public bool IsDimmerVisible => IsAddAccountOpen || IsDetailsOpen;

    // Holds the currently inspected account for the detail view
    [ObservableProperty]
    private AccountConfig? _selectedAccountDetails;

    public AccountsViewModel(ConfigManager configManager, ProviderFactory providerFactory, ObservableUiLogger uiLogger)
    {
        _configManager = configManager;
        _providerFactory = providerFactory;
        _uiLogger = uiLogger;

        LoadAccountsAsync();
    }

    public void LoadAccountsAsync()
    {
        Task.Run(() =>
        {
            var loadedAccounts = _configManager.LoadAllAccounts();

            // Use the UI Dispatcher to safely update the ObservableCollection on the main thread
            Dispatcher.UIThread.Post(() =>
            {
                Accounts.Clear();
                foreach (var account in loadedAccounts)
                {
                    Accounts.Add(account);
                }
            });
        });
    }

    [RelayCommand]
    private void OpenAccountDetails(AccountConfig account)
    {
        if (account == null) return;

        SelectedAccountDetails = account;
        IsDetailsOpen = true;
    }

    [RelayCommand]
    private void OpenAddAccount()
    {
        IsAddAccountOpen = true;
    }

    [RelayCommand]
    private void CloseAddAccount()
    {
        IsAddAccountOpen = false;
        IsAuthenticating = false;
    }

    [RelayCommand]
    private void CloseDetails()
    {
        IsDetailsOpen = false;
        SelectedAccountDetails = null;
    }

    [RelayCommand]
    private async Task LinkAccountAsync(string targetProvider)
    {
        IsAuthenticating = true;
        try
        {
            if (targetProvider == "google")
                targetProvider = "googledrive";

            string accountId = $"acc-{targetProvider.ToLower()}-{Guid.NewGuid().ToString()[..8]}";

            var account = new AccountConfig
            {
                Id = accountId,
                Provider = targetProvider.ToLower(),
                DisplayName = "Authenticating..."
            };

            // This fires off standard browser consent sequence
            var provider = await _providerFactory.GetOrCreateProviderAsync(account);
            string userEmail = await provider.GetAuthenticatedUserEmailAsync();

            // Success: Update state, save to disk, and refresh view list
            account.DisplayName = userEmail;
            _configManager.SaveAccount(account);
            _uiLogger.Log(LogLevel.Info, $"Linked account '{account.DisplayName}' ({account.Provider}).");

            LoadAccountsAsync();
            IsAddAccountOpen = false;
        }
        catch (Exception ex)
        {
            _uiLogger.Log(LogLevel.Error, $"Failed to link account for provider '{targetProvider}': {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Error adding account:[/] {ex.Message}");
        }
        finally
        {
            IsAuthenticating = false;
        }
    }

    /// <summary>
    /// Disconnects and deletes an account from disk
    /// </summary>
    [RelayCommand]
    private async Task RemoveAccount()
    {
        if (SelectedAccountDetails == null) return;

        try
        {
            var provider = await _providerFactory.GetOrCreateProviderAsync(SelectedAccountDetails);
            await provider.RevokeAccessAsync();

            _configManager.DeleteAccount(SelectedAccountDetails.Id);
            _uiLogger.Log(LogLevel.Info, $"Removed account '{SelectedAccountDetails.DisplayName}'.");

            LoadAccountsAsync();
            CloseDetails();
        }
        catch (Exception ex)
        {
            _uiLogger.Log(LogLevel.Error, $"Failed to remove account '{SelectedAccountDetails.DisplayName}': {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Error removing account: {ex.Message}");
            return;
        }
    }
}