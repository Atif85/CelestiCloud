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
using System.Threading;
using System.Threading.Tasks;

namespace CelestiCloud.GUI.ViewModels;

public partial class AccountsViewModel : ViewModelBase
{
    private readonly ConfigManager _configManager;
    private readonly ProviderFactory _providerFactory;
    private readonly ObservableUiLogger _uiLogger;

    private CancellationTokenSource? _authCts;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDimmerVisible))]
    private bool _isRemoveAccountOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRemoveSuccess))]
    private bool _isRemoving;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRemoveSuccess))]
    private bool _hasRemoveError;


    [ObservableProperty]
    private string _removeStatusMessage = string.Empty;

    public bool IsRemoveSuccess => !IsRemoving && !HasRemoveError;

    // Controls whether the background dark-overlay is shown
    public bool IsDimmerVisible => IsAddAccountOpen || IsDetailsOpen || IsRemoveAccountOpen;

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
        if (_authCts != null)
        {
            _authCts.Cancel();
            _authCts.Dispose();
            _authCts = null;
        }

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

        _authCts = new CancellationTokenSource();

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
            var provider = await _providerFactory.GetOrCreateProviderAsync(account, _authCts.Token);
            string userEmail = await provider.GetAuthenticatedUserEmailAsync(_authCts.Token);

            // Success: Update state, save to disk, and refresh view list
            account.DisplayName = userEmail;
            _configManager.SaveAccount(account);
            _uiLogger.Log(LogLevel.Info, $"Linked account '{account.DisplayName}' ({account.Provider}).");

            LoadAccountsAsync();
            IsAddAccountOpen = false;
        }
        catch (OperationCanceledException)
        {
            _uiLogger.Log(LogLevel.Info, "Account linking was canceled by the user.");
        }
        catch (Exception ex)
        {
            _uiLogger.Log(LogLevel.Error, $"Failed to link account for provider '{targetProvider}': {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Error adding account:[/] {ex.Message}");
        }
        finally
        {
            IsAuthenticating = false;

            _authCts?.Dispose();
            _authCts = null;
        }
    }

    [RelayCommand]
    private async Task RemoveAccountAsync()
    {
        if (SelectedAccountDetails == null) return;

        IsDetailsOpen = false;
        IsRemoveAccountOpen = true;
        IsRemoving = true;
        HasRemoveError = false;
        RemoveStatusMessage = "Contacting provider to securely revoke access...";

        try
        {
            var provider = await _providerFactory.GetOrCreateProviderAsync(SelectedAccountDetails);
            await provider.RevokeAccessAsync();

            _configManager.DeleteAccount(SelectedAccountDetails.Id);
            _uiLogger.Log(LogLevel.Info, $"Removed account '{SelectedAccountDetails.DisplayName}'.");

            LoadAccountsAsync();

            RemoveStatusMessage = "Account disconnected successfully.";
            IsRemoving = false;
        }
        catch (Exception ex)
        {
            _uiLogger.Log(LogLevel.Error, $"Failed to remove account '{SelectedAccountDetails.DisplayName}': {ex.Message}");
            IsRemoving = false;
            HasRemoveError = true;
            RemoveStatusMessage = $"Could not contact the provider to revoke access. You might be offline.\n\nError: {ex.Message}\n\nWould you like to force-remove this account from the app locally?";
        }
    }

    [RelayCommand]
    private void ForceRemoveAccount()
    {
        if (SelectedAccountDetails == null) return;

        _configManager.DeleteAccount(SelectedAccountDetails.Id);
        _uiLogger.Log(LogLevel.Info, $"Force-removed local account '{SelectedAccountDetails.DisplayName}'.");

        LoadAccountsAsync();
        CloseRemoveAccount();
    }

    [RelayCommand]
    private void CloseRemoveAccount()
    {
        IsRemoveAccountOpen = false;
        SelectedAccountDetails = null;
    }
}