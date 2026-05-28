using System.Threading.Tasks;
using CelestiCloud.CLI.Commands.Account;
using CelestiCloud.CLI.Commands.Job;
using CelestiCloud.CLI.Commands.Start;
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

            config.AddCommand<JobStartCommand>("start")
                .WithDescription("Start a specific sync or backup job in the foreground.")
                .WithExample(["start", "Work Docs"]);


            config.AddCommand<JobAutoStartCommand>("autostart")
                .WithDescription("Concurrently start all jobs configured with AutoStart enabled in the foreground.")
                .WithExample(["autostart"]);

            // Set up our main branch for Accounts
            config.AddBranch("account", account =>
            {
                account.SetDescription("Link, view, and disconnect cloud storage accounts.");

                account.AddCommand<AccountAddCommand>("add")
                    .WithDescription("Connect and authenticate a new cloud storage account.")
                    .WithExample(["account", "add", "google"]);

                account.AddCommand<AccountListCommand>("list")
                    .WithDescription("Show a table of all connected accounts.")
                    .WithExample(["account", "list"]);

                account.AddCommand<AccountRemoveCommand>("remove")
                    .WithDescription("Disconnect and delete an account configuration.")
                    .WithExample(["account", "remove", "alex.personal@gmail.com"]);
            });

            // Set up our main branch for Jobs
            config.AddBranch("job", job =>
            {
                job.SetDescription("Configure, view, edit, and delete sync/backup jobs.");

                job.AddCommand<JobCreateCommand>("create")
                    .WithDescription("Create a new sync or backup job via interactive prompts.")
                    .WithExample(["job", "create"]);

                job.AddCommand<JobListCommand>("list")
                    .WithDescription("List all configured sync and backup jobs.")
                    .WithExample(["job", "list"]);

                job.AddCommand<JobShowCommand>("show")
                    .WithDescription("Show detailed configuration for a specific job.")
                    .WithExample(["job", "show", "\"Work Docs\""]);

                job.AddCommand<JobDeleteCommand>("delete")
                    .WithDescription("Delete a job configuration.")
                    .WithExample(["job", "delete", "\"Work Docs\""]);

                job.AddCommand<JobEditCommand>("edit")
                    .WithDescription("Interactively edit configuration settings for a specific job.")
                    .WithExample(["job", "edit", "\"Work Docs\""]);
            });
        });

        return await app.RunAsync(args);
    }
}