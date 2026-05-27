using System.Threading.Tasks;
using CelestiCloud.CLI.Commands;
using Spectre.Console.Cli;

namespace CelestiCloud.CLI;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var app = new CommandApp();

        app.Configure(config =>
        {
            config.SetApplicationName("celesticloud");
            config.SetApplicationVersion("1.0.0");

            // Help configuration: Enables both "celesticloud account --help" and "celesticloud account add -h"
            config.ValidateExamples();

            // Set up our main branch for Accounts
            config.AddBranch("account", account =>
            {
                account.SetDescription("Link, view, and disconnect cloud storage accounts.");

                account.AddCommand<AccountAddCommand>("add")
                    .WithDescription("Connect and authenticate a new cloud storage account.")
                    .WithExample(new[] { "account", "add", "google" });

                account.AddCommand<AccountListCommand>("list")
                    .WithDescription("Show a table of all connected accounts.")
                    .WithExample(new[] { "account", "list" });

                account.AddCommand<AccountRemoveCommand>("remove")
                    .WithDescription("Disconnect and delete an account configuration.")
                    .WithExample(new[] { "account", "remove", "alex.personal@gmail.com" });
            });
        });

        return await app.RunAsync(args);
    }
}