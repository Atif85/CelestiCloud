using CelestiCloud.CLI.Commands.Settings;
using CelestiCloud.Core.Config;
using CelestiCloud.Core.Models;
using CelestiCloud.Core.Providers;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CelestiCloud.CLI.Commands;

public class AccountAddCommand : AsyncCommand<AccountAddSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, AccountAddSettings settings, CancellationToken ct = default)
    {
        var configManager = new ConfigManager();
        var providerFactory = new ProviderFactory(configManager);

        string targetProvider = settings.Provider.ToLower();

        AnsiConsole.MarkupLine($"[cyan]Initializing connection sequence for provider: '{targetProvider}'...[/]");

        // 1. Generate a brand new Account ID
        string accountId = $"acc-{targetProvider}-{Guid.NewGuid().ToString()[..8]}";

        // Create a temporary AccountConfig to initialize our provider
        var account = new AccountConfig
        {
            Id = accountId,
            Provider = targetProvider,
            DisplayName = "Pending Authentication"
        };

        try
        {
            // 2. Connect the provider (triggers standard browser OAuth workflow)
            var provider = await providerFactory.CreateProviderAsync(account, ct);

            AnsiConsole.MarkupLine("[yellow]Authentication successful. Querying user account information...[/]");

            // 3. Pull the email address automatically (Gap fix we added to Core!)
            string userEmail = await provider.GetAuthenticatedUserEmailAsync();

            // 4. Update and write the permanent AccountConfig configuration to AppData
            account.DisplayName = userEmail;
            configManager.SaveAccount(account);

            AnsiConsole.MarkupLine($"[green]Successfully linked account:[/] [bold]{userEmail}[/] (ID: {accountId})");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error adding account:[/] {ex.Message}");
            return 1;
        }
    }
}