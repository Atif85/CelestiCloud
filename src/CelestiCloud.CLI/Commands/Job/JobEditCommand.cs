using CelestiCloud.CLI.Commands.Job.Settings;
using CelestiCloud.Core.Config;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CelestiCloud.CLI.Commands.Job;

public class JobEditCommand : AsyncCommand<JobIdentifierSettings>
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

        bool running = true;

        while (running)
        {
            AnsiConsole.Clear();
            AnsiConsole.Write(new Rule($"[yellow]Interactively Editing: {job.Name}[/]").LeftJustified());

            var selection = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select a setting to edit:")
                    .AddChoices(
                        $"Toggle Auto-Start (Current: {job.AutoStart})",
                        "Add Local Folder Path",
                        "Remove Local Folder Path",
                        "Add Ignore Pattern",
                        "Save and Exit",
                        "Cancel"
                    ));

            if (selection.StartsWith("Toggle Auto-Start"))
            {
                job.AutoStart = !job.AutoStart;
            }
            else if (selection == "Add Local Folder Path")
            {
                string path = AnsiConsole.Prompt(
                    new TextPrompt<string>("Enter the new local folder path:")
                        .Validate(p => Directory.Exists(p) ? ValidationResult.Success() : ValidationResult.Error("[red]Directory does not exist.[/]")));

                string fullPath = Path.GetFullPath(path);
                if (!job.LocalPaths.Contains(fullPath))
                {
                    job.LocalPaths.Add(fullPath);
                }
            }
            else if (selection == "Remove Local Folder Path")
            {
                if (job.LocalPaths.Count <= 1)
                {
                    AnsiConsole.MarkupLine("[red]Error:[/] You must retain at least one local path in a job! Press any key to continue.");
                    Console.ReadKey();
                }
                else
                {
                    var pathToRemove = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("Select path to remove:")
                            .AddChoices(job.LocalPaths));

                    job.LocalPaths.Remove(pathToRemove);
                }
            }
            else if (selection == "Add Ignore Pattern")
            {
                string pattern = AnsiConsole.Ask<string>("Enter new ignore pattern (e.g., 'bin/', '*.tmp'):").Trim();
                if (!string.IsNullOrWhiteSpace(pattern) && !job.IgnorePatterns.Contains(pattern))
                {
                    job.IgnorePatterns.Add(pattern);
                }
            }
            else if (selection == "Save and Exit")
            {
                configManager.SaveJob(job);
                AnsiConsole.MarkupLine("[green]Job configuration changes saved successfully![/]");
                running = false;
            }
            else if (selection == "Cancel")
            {
                running = false;
            }
        }

        return Task.FromResult(0);
    }
}