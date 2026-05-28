using Spectre.Console.Cli;
using System.ComponentModel;

namespace CelestiCloud.CLI.Commands.Start.Settings;

public class JobAutoStartSettings : CommandSettings
{
    [CommandOption("-d|--debug")]
    [Description("Output raw debug logs instead of displaying the live progress dashboard.")]
    public bool Debug { get; set; }
}