using CelestiCloud.Core.Config;
using CelestiCloud.Core.Models;
using CelestiCloud.Core.Providers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CelestiCloud.GUI.ViewModels;

public partial class AccountsViewModel : ViewModelBase
{
    private readonly ConfigManager _configManager;
    private readonly ProviderFactory _providerFactory;

    [ObservableProperty]
    private ObservableCollection<AccountConfig> _accounts = new();

    [ObservableProperty]
    private AccountConfig? _selectedAccount;

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

        // We will implement the detailed view popup hook here in our next step!
        System.Diagnostics.Debug.WriteLine($"Selected Account: {value.DisplayName}");

        // Deselect immediately so the click behavior can fire again on subsequent clicks
        SelectedAccount = null;
    }

    [RelayCommand]
    private void AddAccount()
    {
        // We will implement the Account Creation popup here in our next step!
        System.Diagnostics.Debug.WriteLine("Add Account Button Clicked");
    }
}