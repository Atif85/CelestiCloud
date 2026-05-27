using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CelestiCloud.Core.Config;
using CelestiCloud.Core.Models;

namespace CelestiCloud.Core.Providers;

public class ProviderFactory
{
    private readonly ConfigManager _configManager;

    public ProviderFactory(ConfigManager configManager)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
    }

    /// <summary>
    /// Instantiates and connects the appropriate Cloud Provider based on the Account configuration.
    /// </summary>
    public async Task<ICloudProvider> CreateProviderAsync(AccountConfig account, CancellationToken cancellationToken = default)
    {
        if (account.Provider.Equals("googledrive", StringComparison.OrdinalIgnoreCase) ||
            account.Provider.Equals("google", StringComparison.OrdinalIgnoreCase))
        {
            string credentialsPath = "credentials.json";

            // Map the token store to a subfolder specifically named after this Account ID
            string tokenDirectory = Path.Combine(_configManager.GetTokensDirectory(), account.Id);

            var provider = new GoogleDriveProvider(credentialsPath, tokenDirectory);
            await provider.ConnectAsync(cancellationToken);
            return provider;
        }

        // Future expansions (e.g., dropbox, onedrive) will go here:
        // else if (account.Provider == "dropbox") { ... }

        throw new NotSupportedException($"Cloud Provider '{account.Provider}' is not supported.");
    }
}