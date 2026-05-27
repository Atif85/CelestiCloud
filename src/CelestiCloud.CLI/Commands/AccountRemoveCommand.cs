using CelestiCloud.CLI.Commands.Settings;
using CelestiCloud.Core.Config;
using CelestiCloud.Core.Providers;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.Threading.Tasks;

namespace CelestiCloud.CLI.Commands;

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

        await AnsiConsole.Status()
            .StartAsync("Contacting Google to revoke active connection tokens...", async ctx =>
            {
                try
                {
                    // Create and connect provider to allow revocation
                    var provider = await providerFactory.CreateProviderAsync(account);
                    await provider.RevokeAccessAsync();

                    AnsiConsole.MarkupLine("[green]Successfully revoked OAuth permission tokens on Google's servers.[/]");
                }
                catch (Exception)
                {
                    // If offline, we suppress the error so the user can still delete local configs
                    AnsiConsole.MarkupLine("[yellow]Warning: Could not contact Google to revoke tokens online (you might be offline).[/]");
                }
            });

        try
        {
            // 3. Remove the account config file and delete its associated token directory from AppData
            configManager.DeleteAccount(account.Id);
            AnsiConsole.MarkupLine($"[green]Successfully disconnected:[/] {account.DisplayName}");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error removing account:[/] {ex.Message}");
            return 1;
        }
    }
}