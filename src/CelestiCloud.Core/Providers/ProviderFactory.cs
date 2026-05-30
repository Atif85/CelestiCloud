using CelestiCloud.Core.Config;
using CelestiCloud.Core.Models;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CelestiCloud.Core.Providers;

public class ProviderFactory
{
    private readonly ConfigManager _configManager;

    private readonly ConcurrentDictionary<string, ICloudProvider> _providerCache = new();
    private readonly SemaphoreSlim _factoryLock = new(1, 1);

    public ProviderFactory(ConfigManager configManager)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
    }

    public async Task<ICloudProvider> GetOrCreateProviderAsync(AccountConfig account, CancellationToken cancellationToken = default)
    {
        // Check if we already have an active, initialized provider for this account
        if (_providerCache.TryGetValue(account.Id, out var existingProvider))
        {
            return existingProvider;
        }

        // Acquire lock to prevent duplicate initialization
        await _factoryLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check cache inside the lock
            if (_providerCache.TryGetValue(account.Id, out existingProvider))
            {
                return existingProvider;
            }

            ICloudProvider newProvider;

            if (account.Provider.Equals("googledrive", StringComparison.OrdinalIgnoreCase))
            {
                // Map the token store specifically to this account
                string tokenDirectory = Path.Combine(_configManager.GetTokensDirectory(), account.Id);

                var gDriveProvider = new GoogleDriveProvider(tokenDirectory);
                await gDriveProvider.ConnectAsync(cancellationToken);

                newProvider = gDriveProvider;
            }
            else
            {
                throw new NotSupportedException($"Cloud Provider '{account.Provider}' is not supported.");
            }

            // Cache the connected provider for subsequent jobs to reuse
            _providerCache[account.Id] = newProvider;
            return newProvider;
        }
        finally
        {
            _factoryLock.Release();
        }
    }
}