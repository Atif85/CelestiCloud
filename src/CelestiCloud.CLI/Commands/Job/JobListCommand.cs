using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CelestiCloud.Core.Config;
using CelestiCloud.Core.Jobs;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CelestiCloud.CLI.Commands.Job;

public class JobListCommand : AsyncCommand<EmptyCommandSettings>
{
    protected override Task<int> ExecuteAsync(CommandContext context, EmptyCommandSettings settings, CancellationToken ct)
    {
        var configManager = new ConfigManager();
        var jobs = configManager.LoadAllJobs().ToList();

        if (!jobs.Any())
        {
            AnsiConsole.MarkupLine("[yellow]No jobs configured. Run 'celesticloud job create' to set up a new sync or backup profile.[/]");
            return Task.FromResult(0);
        }

        // Map account IDs to display names (emails) for friendly UI rendering
        var accountsMap = configManager.LoadAllAccounts().ToDictionary(a => a.Id, a => a.DisplayName);

        var table = new Table()
            .Title("Configured Jobs")
            .Border(TableBorder.Rounded);

        table.AddColumn("[bold]Job ID[/]");
        table.AddColumn("[bold]Job Name[/]");
        table.AddColumn("[bold]Type[/]");
        table.AddColumn("[bold]Account[/]");
        table.AddColumn("[bold]Auto-Start[/]");
        table.AddColumn("[bold]Status[/]");

        foreach (var job in jobs)
        {
            // Retrieve account email or display ID fallback
            string accountDisplay = (accountsMap.TryGetValue(job.TargetAccountId, out var email) ? email : "Unknown Account");

            // Check lock files to determine active execution status [3]
            bool isRunning = JobLockManager.IsJobRunning(job.Id, configManager.GetAppDataPath());
            string status = isRunning ? "[green]Active / Running[/]" : "[grey]Idle[/]";

            table.AddRow(
                job.Id,
                job.Name,
                job.JobType.ToString(),
                accountDisplay,
                job.AutoStart ? "[green]Enabled[/]" : "[grey]Disabled[/]",
                status
            );
        }

        AnsiConsole.Write(table);
        return Task.FromResult(0);
    }
}