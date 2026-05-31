using CelestiCloud.CLI.Commands.Start.Settings;
using CelestiCloud.CLI.Util;
using CelestiCloud.Core.Config;
using CelestiCloud.Core.IO;
using CelestiCloud.Core.Jobs;
using CelestiCloud.Core.Logging;
using CelestiCloud.Core.Providers;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Threading.RateLimiting;

namespace CelestiCloud.CLI.Commands.Start;

public class JobAutoStartCommand : AsyncCommand<JobAutoStartSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, JobAutoStartSettings settings, CancellationToken ct)
    {
        var configManager = new ConfigManager();
        var providerFactory = new ProviderFactory(configManager);

        IJobLogger logger = settings.Debug
            ? new ConsoleLogger { MinimumLevel = LogLevel.Debug }
            : new FileLogger(configManager.GetAppDataPath(), "GlobalAutoStart");

        var appSettings = configManager.LoadSettings();

        int uploadLimit = appSettings.GlobalUploadLimitKbps;
        RateLimiter? uploadLimiter = (uploadLimit > 0) ? BandwidthLimiterFactory.CreateLimiter(uploadLimit * 1024) : null;

        var engine = new JobEngine(configManager, providerFactory, uploadLimiter, logger);

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += async (sender, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            await engine.StopAllAsync();
        };

        try
        {
            // Start all auto-start jobs concurrently
            await engine.StartAutoStartJobsAsync();

            if (!engine.GetActiveJobs().Any())
            {
                AnsiConsole.MarkupLine("[yellow]No jobs are configured with Auto-Start enabled.[/]");
                return 0;
            }

            if (settings.Debug)
            {
                await Task.Delay(Timeout.Infinite, cts.Token);
            }
            else
            {
                // Render a live-updating multi-progress dashboard
                await AnsiConsole.Live(new Text("Initializing multi-progress render..."))
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
            AnsiConsole.MarkupLine($"[red]Engine Error:[/] {ex.Message}");
            return 1;
        }

        AnsiConsole.MarkupLine("[green]JobEngine stopped cleanly.[/]");
        return 0;
    }
}