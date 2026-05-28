using CelestiCloud.CLI.Commands.Job.Settings;
using CelestiCloud.Core.Config;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CelestiCloud.CLI.Commands.Job;

public class JobDeleteCommand : AsyncCommand<JobIdentifierSettings>
{
    protected override Task<int> ExecuteAsync(CommandContext context, JobIdentifierSettings settings, CancellationToken ct)
    {
        var configManager = new ConfigManager();

        var job = configManager.LoadJobByName(settings.Identifier)
                  ?? configManager.LoadJob(settings.Identifier);

        if (job == null)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] No job config found matching [bold]'{settings.Identifier}'[/]");
            return Task.FromResult(1);
        }

        if (!AnsiConsole.Confirm($"Are you sure you want to delete the job [red]'{job.Name}'[/]?"))
        {
            AnsiConsole.MarkupLine("[grey]Operation cancelled.[/]");
            return Task.FromResult(0);
        }

        try
        {
            configManager.DeleteJob(job.Id);
            AnsiConsole.MarkupLine($"[green]Successfully deleted job:[/] {job.Name}");
            return Task.FromResult(0);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error deleting job:[/] {ex.Message}");
            return Task.FromResult(1);
        }
    }
}