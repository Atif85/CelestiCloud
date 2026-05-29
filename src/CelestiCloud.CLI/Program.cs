using CelestiCloud.CLI.Commands.Account;
using CelestiCloud.CLI.Commands.Job;
using CelestiCloud.CLI.Commands.Start;
using CelestiCloud.CLI.Util;
using Spectre.Console.Cli;
using System.Reflection;

namespace CelestiCloud.CLI;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (OperatingSystem.IsWindows())
        {
            WindowsConsoleHelper.EnableAnsiSupport();
        }

        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var app = new CommandApp();

        app.Configure(config =>
        {
            config.SetApplicationName("celesticloud");
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
            config.SetApplicationVersion($"{version}-alpha");
    
            // Help configuration: Enables both "celesticloud account --help" and "celesticloud account add -h"
            config.ValidateExamples();

            config.AddCommand<JobStartCommand>("start")
                .WithAlias("s")
                .WithDescription("Start a specific sync or backup job in the foreground.")
                .WithExample(["start", "Work Docs"]);


            config.AddCommand<JobAutoStartCommand>("autostart")
                .WithAlias("as")
                .WithDescription("Concurrently start all jobs configured with AutoStart enabled in the foreground.")
                .WithExample(["autostart"]);

            // Set up our main branch for Accounts
            config.AddBranch("account", account =>
            {
                account.SetDescription("Link, view, and disconnect cloud storage accounts.");

                account.AddCommand<AccountAddCommand>("add")
                    .WithAlias("a")
                    .WithDescription("Connect and authenticate a new cloud storage account.")
                    .WithExample(["account", "add", "googledrive"]);

                account.AddCommand<AccountListCommand>("list")
                    .WithAlias("l")
                    .WithDescription("Show a table of all connected accounts.")
                    .WithExample(["account", "list"]);

                account.AddCommand<AccountRemoveCommand>("remove")
                    .WithAlias("r")
                    .WithDescription("Disconnect and delete an account configuration.")
                    .WithExample(["account", "remove", "alex.personal@gmail.com"]);
            }).WithAlias("a");

            // Set up our main branch for Jobs
            config.AddBranch("job", job =>
            {
                job.SetDescription("Configure, view, edit, and delete sync/backup jobs.");

                job.AddCommand<JobCreateCommand>("create")
                    .WithAlias("c")
                    .WithDescription("Create a new sync or backup job via interactive prompts.")
                    .WithExample(["job", "create"]);

                job.AddCommand<JobListCommand>("list")
                    .WithAlias("l")
                    .WithDescription("List all configured sync and backup jobs.")
                    .WithExample(["job", "list"]);

                job.AddCommand<JobShowCommand>("show")
                    .WithAlias("s")
                    .WithDescription("Show detailed configuration for a specific job.")
                    .WithExample(["job", "show", "\"Work Docs\""]);

                job.AddCommand<JobDeleteCommand>("delete")
                    .WithAlias("d")
                    .WithDescription("Delete a job configuration.")
                    .WithExample(["job", "delete", "\"Work Docs\""]);

                job.AddCommand<JobEditCommand>("edit")
                    .WithAlias("e")
                    .WithDescription("Interactively edit configuration settings for a specific job.")
                    .WithExample(["job", "edit", "\"Work Docs\""]);
            }).WithAlias("j"); ;
        });

        return await app.RunAsync(args);
    }
}