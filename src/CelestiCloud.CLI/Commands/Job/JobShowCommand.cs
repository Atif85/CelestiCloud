using CelestiCloud.CLI.Commands.Job.Settings;
using CelestiCloud.Core.Config;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.Text;

namespace CelestiCloud.CLI.Commands.Job;

public class JobShowCommand : AsyncCommand<JobIdentifierSettings>
{
    protected override Task<int> ExecuteAsync(CommandContext context, JobIdentifierSettings settings, CancellationToken ct)
    {
        var configManager = new ConfigManager();

        var job = configManager.LoadJobByName(settings.Identifier)
                  ?? configManager.LoadJob(settings.Identifier);

        if (job == null)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Job [bold]'{settings.Identifier}'[/] was not found.");
            return Task.FromResult(1);
        }

        var accountsMap = configManager.LoadAllAccounts().ToDictionary(a => a.Id, a => a.DisplayName);

        AnsiConsole.Write(new Rule($"[yellow]Job Configuration: {job.Name}[/]").LeftJustified());

        var grid = new Grid().AddColumn().AddColumn();
        grid.AddRow("[bold]Job ID:[/]", job.Id);
        grid.AddRow("[bold]Type:[/]", job.JobType.ToString());
        grid.AddRow("[bold]Auto-Start:[/]", job.AutoStart ? "Yes" : "No");
        grid.AddRow("[bold]Cloud Destination:[/]", job.RemoteRootPath);

        string accountsJoined = string.Join(", ", (accountsMap.TryGetValue(job.TargetAccountId, out var email) ? email : job.TargetAccountId));
        grid.AddRow("[bold]Linked Accounts:[/]", accountsJoined);

        AnsiConsole.Write(grid);

        AnsiConsole.WriteLine("\nLocal Folders Watched:");
        foreach (string path in job.LocalPaths)
        {
            AnsiConsole.MarkupLine($" [green]•[/] {path}");
        }

        AnsiConsole.WriteLine("\nIgnore Patterns (.gitignore syntax):");
        if (job.IgnorePatterns.Any())
        {
            foreach (string pattern in job.IgnorePatterns)
            {
                AnsiConsole.MarkupLine($" [red]•[/] {pattern}");
            }
        }
        else
        {
            AnsiConsole.MarkupLine(" [grey]No ignore rules specified.[/]");
        }

        AnsiConsole.Write(new Rule().RuleStyle("grey"));
        return Task.FromResult(0);
    }
}