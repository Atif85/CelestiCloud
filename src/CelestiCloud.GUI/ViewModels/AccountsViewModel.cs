using Avalonia.Threading;
using CelestiCloud.Core.Config;
using CelestiCloud.Core.Models;
using CelestiCloud.Core.Providers;
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

    [ObservableProperty]
    private ObservableCollection<AccountConfig> _accounts = [];

    [ObservableProperty]
    private AccountConfig? _selectedAccount;

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

    public AccountsViewModel(ConfigManager configManager, ProviderFactory providerFactory)
    {
        _configManager = configManager;
        _providerFactory = providerFactory;

        LoadAccounts();
    }

    public void LoadAccounts()
    {
        Accounts.Clear();
        foreach (var account in _configManager.LoadAllAccounts())
        {
            Accounts.Add(account);
        }
    }

    partial void OnSelectedAccountChanged(AccountConfig? value)
    {
        if (value == null) return;

        SelectedAccountDetails = value;
        IsDetailsOpen = true;

        //Dispatcher.UIThread.Post(() =>
        //{
        //    SelectedAccount = null;
        //});
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
        SelectedAccount = null;
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

            LoadAccounts();
            IsAddAccountOpen = false;
        }
        catch (Exception ex)
        {
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
            System.Diagnostics.Debug.WriteLine($"Got Provider Succesfully");
            await provider.RevokeAccessAsync();

            _configManager.DeleteAccount(SelectedAccountDetails.Id);

            LoadAccounts();
            CloseDetails();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error removing account: {ex.Message}");
            return;
        }
    }
}