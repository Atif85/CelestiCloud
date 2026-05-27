using System;
using System.Threading.Tasks;
using CelestiCloud.CLI.Commands.Settings;
using CelestiCloud.Core.Config;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CelestiCloud.CLI.Commands;

public class AccountRemoveCommand : AsyncCommand<AccountRemoveSettings>
{
    protected override Task<int> ExecuteAsync(CommandContext context, AccountRemoveSettings settings, CancellationToken ct)
    {
        var configManager = new ConfigManager();

        // 1. Look up the account by Display Name (Email) first, then fallback to its GUID
        var account = configManager.LoadAccountByName(settings.Identifier)
                      ?? configManager.LoadAccount(settings.Identifier);

        if (account == null)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] No connected account found matching: [bold]'{settings.Identifier}'[/]");
            return Task.FromResult(1);
        }

        // 2. Present a confirmation prompt using Spectre
        if (!AnsiConsole.Confirm($"Are you sure you want to disconnect [yellow]{account.DisplayName}[/] ({account.Id})?"))
        {
            AnsiConsole.MarkupLine("[grey]Operation canceled by user.[/]");
            return Task.FromResult(0);
        }

        try
        {
            // 3. Remove the account config file and delete its associated token directory from AppData
            configManager.DeleteAccount(account.Id);
            AnsiConsole.MarkupLine($"[green]Successfully disconnected:[/] {account.DisplayName}");
            return Task.FromResult(0);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error removing account:[/] {ex.Message}");
            return Task.FromResult(1);
        }
    }
}