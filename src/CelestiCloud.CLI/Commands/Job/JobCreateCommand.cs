
using CelestiCloud.CLI.Commands.Job.Settings;
using CelestiCloud.Core.Config;
using CelestiCloud.Core.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CelestiCloud.CLI.Commands.Job;

public class JobCreateCommand : AsyncCommand<JobCreateSettings>
{
    protected override Task<int> ExecuteAsync(CommandContext context, JobCreateSettings settings, CancellationToken ct)
    {
        var configManager = new ConfigManager();

        // Ensure at least one account exists before creating a job
        var accounts = configManager.LoadAllAccounts().ToList();
        if (accounts.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] You must connect at least one storage account first using [yellow]celesticloud account add[/].");
            return Task.FromResult(1);
        }

        AnsiConsole.Write(new Rule("[yellow]CelestiCloud Job Wizard[/]").RuleStyle("yellow").LeftJustified());

        // Resolve or Prompt for Name (Ensuring uniqueness)
        string jobName = settings.Name ?? string.Empty;
        while (string.IsNullOrWhiteSpace(jobName) || configManager.LoadJobByName(jobName) != null)
        {
            if (!string.IsNullOrWhiteSpace(jobName))
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] A job named [bold]'{jobName}'[/] already exists.");
            }

            jobName = AnsiConsole.Ask<string>("Enter a unique name for this job (e.g., 'Work Documents'):").Trim();
        }

        // Prompt for Job Type
        var jobType = AnsiConsole.Prompt(
            new SelectionPrompt<JobType>()
                .Title("Select the [green]Job Type[/]:")
                .AddChoices(JobType.Sync, JobType.Backup));

        // Prompt for Account
        var selectedAccountDisplayName = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select the [green]Target Storage Account[/]:")
                .AddChoices(accounts.Select(a => a.DisplayName)));

        var targetAccount = accounts.First(a => a.DisplayName == selectedAccountDisplayName);

        // Prompt for Local Paths (support multiple local directories)
        var localPaths = new List<string>();
        bool addMore = true;

        while (addMore)
        {
            string path = AnsiConsole.Prompt(
                new TextPrompt<string>("Enter a [green]local folder path[/] to watch:")
                    .Validate(p => Directory.Exists(p) ? ValidationResult.Success() : ValidationResult.Error("[red]Directory does not exist.[/]")));

            localPaths.Add(Path.GetFullPath(path));

            addMore = AnsiConsole.Confirm("Would you like to add another local folder to this job?");
        }

        // Prompt for Remote Path
        string remotePath = AnsiConsole.Prompt(
            new TextPrompt<string>("Enter the [green]remote cloud path[/] target (defaults to '/'):")
                .DefaultValue("/")
                .Validate(p => p.StartsWith('/') ? ValidationResult.Success() : ValidationResult.Error("[red]Remote paths must start with a '/'[/]")));

        // Prompt for AutoStart
        bool autoStart = AnsiConsole.Confirm("Should this job automatically run on system startup?");

        // Prompt for ignore patterns
        var ignorePatterns = new List<string>();
        bool addIgnorePatterns = AnsiConsole.Confirm("Would you like to add custom ignore patterns to this job? (e.g., 'node_modules/', '*.tmp')");

        while (addIgnorePatterns)
        {
            string pattern = AnsiConsole.Prompt(
                new TextPrompt<string>("Enter ignore pattern (Gitignore-style):")
                    .Validate(p => !string.IsNullOrWhiteSpace(p) ? ValidationResult.Success() : ValidationResult.Error("[red]Pattern cannot be empty.[/]")));

            ignorePatterns.Add(pattern.Trim());

            addIgnorePatterns = AnsiConsole.Confirm("Would you like to add another ignore pattern?");
        }


        // Create and Save JobConfig
        var jobConfig = new JobConfig
        {
            Id = Guid.NewGuid().ToString(),
            Name = jobName,
            JobType = jobType,
            TargetAccountId = targetAccount.Id,
            RemoteRootPath = remotePath,
            LocalPaths = localPaths,
            AutoStart = autoStart,
            IgnorePatterns = ignorePatterns
        };

        configManager.SaveJob(jobConfig);

        AnsiConsole.MarkupLine($"\n[green]Success![/] Configured job [bold]'{jobName}'[/] (Type: {jobType}).");
        return Task.FromResult(0);
    }
}