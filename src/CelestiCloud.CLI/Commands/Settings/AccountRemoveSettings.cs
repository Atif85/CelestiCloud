using System.ComponentModel;
using Spectre.Console.Cli;

namespace CelestiCloud.CLI.Commands.Settings;

public class AccountRemoveSettings : CommandSettings
{
    [CommandArgument(0, "<IDENTIFIER>")]
    [Description("The unique Account ID or Display Name (email address) of the account to remove.")]
    public string Identifier { get; set; } = string.Empty;
}