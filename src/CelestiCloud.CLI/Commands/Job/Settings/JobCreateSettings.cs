using Spectre.Console.Cli;
using System.ComponentModel;

namespace CelestiCloud.CLI.Commands.Job.Settings;

public class JobCreateSettings : CommandSettings
{
    [CommandArgument(0, "[NAME]")]
    [Description("Optional name of the job. If omitted, you will be prompted.")]
    public string? Name { get; set; }
}