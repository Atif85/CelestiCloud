using CelestiCloud.Core.Jobs;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace CelestiCloud.CLI.Util;

public static class ProgressRenderer
{
    public static IRenderable RenderDashboard(SyncEngine engine)
    {
        var activeJobs = engine.GetActiveJobs().ToList();

        if (activeJobs.Count == 0)
        {
            return new Markup("[yellow]No active jobs running in the engine.[/]");
        }

        // Create a root layout grid
        var grid = new Grid().AddColumn();

        grid.AddRow(new Rule("[yellow]CelestiCloud Active Monitor[/] (Press Ctrl+C to Stop)").LeftJustified());
        grid.AddRow(new Text(""));

        foreach (var job in activeJobs)
        {
            var jobGrid = new Grid().AddColumn().AddColumn();

            // Format Job Header
            string typeBadge = job.Config.JobType == Core.Models.JobType.Sync
                ? "[[[deepskyblue1]Sync[/]]]"
                : "[[[springgreen3_1]Backup[/]]]";

            jobGrid.AddRow(
                new Markup($"[bold cyan]{job.Config.Name.EscapeMarkup()}[/]  {typeBadge}"),
                new Markup($"[bold green]Processed:[/] {job.State.FilesProcessed} / {job.State.FilesFound} files")
            );

            // Display Active Concurrent File Transfers
            if (!job.State.ActiveTransfers.IsEmpty)
            {
                var transferTable = new Table().NoBorder().HideHeaders().AddColumns("", "");

                foreach (var transfer in job.State.ActiveTransfers.ToList())
                {
                    double percent = transfer.Value;
                    int barLength = (int)(percent * 20);

                    // Render a clean progress bar block [4]
                    string filledBar = new('█', barLength);
                    string emptyBar = new('░', 20 - barLength);

                    transferTable.AddRow(
                        new Markup($"  [grey]•[/] [white]{transfer.Key.EscapeMarkup()}[/]"),
                        new Markup($"[green]{filledBar}[/][grey]{emptyBar}[/] [bold green]{percent:P0}[/]")
                    );
                }

                jobGrid.AddRow(transferTable, new Text(""));
            }
            else
            {
                jobGrid.AddRow(new Markup("  [grey]No active file transfers (idle/watching)...[/]"), new Text(""));
            }

            grid.AddRow(jobGrid);
            grid.AddRow(new Rule().RuleStyle("grey23"));
        }

        return grid;
    }
}