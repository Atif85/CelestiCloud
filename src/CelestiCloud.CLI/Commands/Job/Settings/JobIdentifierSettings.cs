using Spectre.Console.Cli;
using System.ComponentModel;

namespace CelestiCloud.CLI.Commands.Job.Settings;

public class JobIdentifierSettings : CommandSettings
{
    [CommandArgument(0, "<IDENTIFIER>")]
    [Description("The unique Job Name or Job ID.")]
    public string Identifier { get; set; } = string.Empty;
}