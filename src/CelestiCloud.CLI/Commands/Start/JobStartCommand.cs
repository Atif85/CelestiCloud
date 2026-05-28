using CelestiCloud.CLI.Commands.Start.Settings;
using CelestiCloud.CLI.Util;
using CelestiCloud.Core.Config;
using CelestiCloud.Core.IO;
using CelestiCloud.Core.Jobs;
using CelestiCloud.Core.Logging;
using CelestiCloud.Core.Providers;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.Text;

namespace CelestiCloud.CLI.Commands.Start;

public class JobStartCommand : AsyncCommand<JobStartSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, JobStartSettings settings, CancellationToken ct)
    {
        var configManager = new ConfigManager();
        var providerFactory = new ProviderFactory(configManager);

        // Configure the Logger based on flags
        IJobLogger logger = settings.Debug
            ? new ConsoleLogger { MinimumLevel = LogLevel.Debug } // Print raw scrolling debug outputs
            : new FileLogger(configManager.GetAppDataPath(), settings.Identifier); // Silence console, log to file [5]

        var engine = new SyncEngine(configManager, providerFactory, logger);

        // Setup termination hooks
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += async (sender, e) =>
        {
            e.Cancel = true; // Prevent abrupt terminal exit
            cts.Cancel();
            await engine.StopAllAsync();
        };

        try
        {
            // Start the job
            await engine.StartJobAsync(settings.Identifier);

            if (settings.Debug)
            {
                // In Debug mode, simply hold the terminal open and let raw logs print
                await Task.Delay(Timeout.Infinite, cts.Token);
            }
            else
            {
                // In Standard mode, run the live updating progress bar dashboard
                await AnsiConsole.Live(new Text("Initializing progress render..."))
                    .AutoClear(false)
                    .StartAsync(async ctx =>
                    {
                        while (!cts.Token.IsCancellationRequested && engine.GetActiveJobs().Any())
                        {
                            ctx.UpdateTarget(ProgressRenderer.RenderDashboard(engine));
                            await Task.Delay(250, cts.Token);
                        }
                    });
            }
        }
        catch (OperationCanceledException)
        {
            // Normal exit
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error executing job:[/] {ex.Message}");
            return 1;
        }

        AnsiConsole.MarkupLine("[green]Job completed or stopped successfully.[/]");
        return 0;
    }
}