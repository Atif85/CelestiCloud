using Avalonia.Threading;
using CelestiCloud.Core.Config;
using CelestiCloud.Core.Jobs;
using CelestiCloud.Core.Logging;
using CelestiCloud.Core.Models;
using CelestiCloud.Core.Providers;
using CelestiCloud.GUI.Logging;
using CelestiCloud.GUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CelestiCloud.GUI.ViewModels;

public partial class AccountsViewModel : ViewModelBase
{
    private readonly ConfigManager _configManager;
    private readonly JobEngine _jobEngine;
    private readonly ProviderFactory _providerFactory;
    private readonly ObservableUiLogger _uiLogger;
    private readonly INotificationService _notificationService;

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

    public AccountsViewModel(ConfigManager configManager, JobEngine jobEngine, ProviderFactory providerFactory, ObservableUiLogger uiLogger, INotificationService notiService)
    {
        _configManager = configManager;
        _jobEngine = jobEngine;
        _providerFactory = providerFactory;
        _uiLogger = uiLogger;
        _notificationService = notiService;

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

            string messege = $"Linked account '{account.DisplayName}' ({account.Provider}).";
            _uiLogger.Log(LogLevel.Info, messege);

            _notificationService.Show("OAuth Success", messege, NotificationLevel.Warning);

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

        string accountId = SelectedAccountDetails.Id;
        string accountName = SelectedAccountDetails.DisplayName;

        IsDetailsOpen = false;
        IsRemoveAccountOpen = true;
        IsRemoving = true;
        HasRemoveError = false;

        try
        {
            // Stop any running jobs that are using this account
            var runningAffectedJobs = _jobEngine.GetActiveJobs()
                .Where(j => j.Config.TargetAccountId == accountId)
                .ToList();

            if (runningAffectedJobs.Count > 0)
            {
                RemoveStatusMessage = $"Stopping {runningAffectedJobs.Count} running job(s) using this account...";
                foreach (var job in runningAffectedJobs)
                {
                    await _jobEngine.StopJobAsync(job.Config.Id);
                }
            }

            RemoveStatusMessage = "Contacting provider to securely revoke access...";

            // Revoke OAuth access
            var provider = await _providerFactory.GetOrCreateProviderAsync(SelectedAccountDetails);
            await provider.RevokeAccessAsync();

            // Invalidate cache in provider factory
            _providerFactory.InvalidateProvider(accountId);

            // Delete account 
            _configManager.DeleteAccount(accountId);
            _uiLogger.Log(LogLevel.Info, $"Removed account '{accountName}'.");

            NotifyOrphanedJobs(accountId, accountName);

            LoadAccountsAsync();

            RemoveStatusMessage = "Account disconnected successfully.";
            IsRemoving = false;
        }
        catch (Exception ex)
        {
            _uiLogger.Log(LogLevel.Error, $"Failed to remove account '{accountName}': {ex.Message}");
            IsRemoving = false;
            HasRemoveError = true;
            RemoveStatusMessage = $"Could not contact the provider to revoke access. You might be offline.\n\nError: {ex.Message}\n\nWould you like to force-remove this account from the app locally?";
        }
    }

    [RelayCommand]
    private void ForceRemoveAccount()
    {
        if (SelectedAccountDetails == null) return;

        string accountId = SelectedAccountDetails.Id;
        string accountName = SelectedAccountDetails.DisplayName;

        _providerFactory.InvalidateProvider(accountId);
        _configManager.DeleteAccount(accountId);
        _uiLogger.Log(LogLevel.Info, $"Force-removed local account '{accountName}'.");

        NotifyOrphanedJobs(accountId, accountName);

        LoadAccountsAsync();
        CloseRemoveAccount();
    }

    private void NotifyOrphanedJobs(string accountId, string accountName)
    {
        var orphanedJobs = _configManager.LoadAllJobs()
            .Where(j => j.TargetAccountId == accountId)
            .ToList();

        if (orphanedJobs.Count > 0)
        {
            string jobNames = string.Join(", ", orphanedJobs.Select(j => $"'{j.Name}'"));
            _notificationService.Show(
                "Account Removed",
                $"The job(s) {jobNames} depended on '{accountName}'. Please edit them to select a new account before running.",
                NotificationLevel.Warning,
                TimeSpan.FromSeconds(8)
            );
        }
    }

    [RelayCommand]
    private void CloseRemoveAccount()
    {
        IsRemoveAccountOpen = false;
        SelectedAccountDetails = null;
    }
}