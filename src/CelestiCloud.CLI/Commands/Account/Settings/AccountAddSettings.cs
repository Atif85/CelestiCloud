using Spectre.Console.Cli;
using System.ComponentModel;

namespace CelestiCloud.CLI.Commands.Account.Settings;

public class AccountAddSettings : CommandSettings
{
    [CommandArgument(0, "<PROVIDER>")]
    [Description("The cloud provider to authenticate with (e.g., 'google')")]
    public string Provider { get; set; } = string.Empty;
}