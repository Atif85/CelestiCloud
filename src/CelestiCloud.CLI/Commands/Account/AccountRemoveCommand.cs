using CelestiCloud.CLI.Commands.Account.Settings;
using CelestiCloud.Core.Config;
using CelestiCloud.Core.Providers;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CelestiCloud.CLI.Commands.Account;

public class AccountRemoveCommand : AsyncCommand<AccountRemoveSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, AccountRemoveSettings settings, CancellationToken ct)
    {
        var configManager = new ConfigManager();
        var providerFactory = new ProviderFactory(configManager);

        // Look up the account by Display Name (Email) first, then fallback to its GUID
        var account = configManager.LoadAccountByName(settings.Identifier)
                      ?? configManager.LoadAccount(settings.Identifier);

        if (account == null)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] No connected account found matching: [bold]'{settings.Identifier}'[/]");
            return 1;
        }

        // Present a confirmation prompt using Spectre
        if (!AnsiConsole.Confirm($"Are you sure you want to disconnect [yellow]{account.DisplayName}[/] ({account.Id})?"))
        {
            AnsiConsole.MarkupLine("[grey]Operation canceled by user.[/]");
            return 0;
        }

        bool proceedWithLocalDeletion = false;

        try
        {
            // Run the network request inside the spinner
            await AnsiConsole.Status()
                .StartAsync($"Contacting {account.Provider} to revoke active connection tokens...", async ctx =>
                {
                    var provider = await providerFactory.GetOrCreateProviderAsync(account);
                    await provider.RevokeAccessAsync();
                });

            AnsiConsole.MarkupLine($"[green]Successfully revoked OAuth permission tokens on {account.Provider}'s servers.[/]");
            proceedWithLocalDeletion = true; // Network succeeded, proceed normally
        }
        catch (Exception ex)
        {
            // Network failed. Break out of the spinner and ask the user how to handle it.
            AnsiConsole.MarkupLine($"[red]Network Error:[/] {ex.Message}");
            AnsiConsole.MarkupLine("[yellow]Could not contact the provider to revoke access. You might be offline.[/]");

            // Ask for fallback permission
            if (AnsiConsole.Confirm("Would you like to force-remove this account from the app locally anyway?", defaultValue: false))
            {
                proceedWithLocalDeletion = true;
            }
            else
            {
                AnsiConsole.MarkupLine("[grey]Account removal aborted.[/]");
                return 1;
            }   
        }

        if (proceedWithLocalDeletion)
        {
            try
            {
                // Remove the account config file and delete its associated token directory from AppData
                configManager.DeleteAccount(account.Id);
                AnsiConsole.MarkupLine($"[green]Successfully removed local account profile:[/] {account.DisplayName}");
                return 0;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error removing local files:[/] {ex.Message}");
                return 1;
            }
        }

        return 0;
    }
}