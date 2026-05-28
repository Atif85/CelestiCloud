using System.Linq;
using System.Threading.Tasks;
using CelestiCloud.Core.Config;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CelestiCloud.CLI.Commands.Account;

public class AccountListCommand : AsyncCommand<EmptyCommandSettings>
{
    protected override Task<int> ExecuteAsync(CommandContext context, EmptyCommandSettings settings, CancellationToken ct)
    {
        var configManager = new ConfigManager();
        var accounts = configManager.LoadAllAccounts().ToList();

        if (accounts.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No accounts connected yet. Link an account using 'celesticloud account add google'[/]");
            return Task.FromResult(0);
        }

        var table = new Table()
            .Title("Connected Storage Accounts")
            .Border(TableBorder.Rounded);

        table.AddColumn("[bold]Account ID[/]");
        table.AddColumn("[bold]Provider[/]");
        table.AddColumn("[bold]Display Name / Email[/]");
        table.AddColumn("[bold]Connected At[/]");

        foreach (var account in accounts)
        {
            table.AddRow(
                account.Id,
                account.Provider,
                account.DisplayName,
                account.ConnectedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            );
        }

        AnsiConsole.Write(table);
        return Task.FromResult(0);
    }
}