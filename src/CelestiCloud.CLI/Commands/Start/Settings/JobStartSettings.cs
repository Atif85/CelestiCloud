using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace CelestiCloud.CLI.Commands.Start.Settings;

public class JobStartSettings : CommandSettings
{
    [CommandArgument(0, "<IDENTIFIER>")]
    [Description("The unique Job Name or Job ID to start.")]
    public string Identifier { get; set; } = string.Empty;

    [CommandOption("-d|--debug")]
    [Description("Output raw debug logs instead of displaying the live progress dashboard.")]
    public bool Debug { get; set; }
}