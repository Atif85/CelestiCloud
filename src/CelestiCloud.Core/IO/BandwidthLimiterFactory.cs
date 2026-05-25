using System.Threading.RateLimiting;

namespace CelestiCloud.Core.IO;

public static class BandwidthLimiterFactory
{
    /// <summary>
    /// Creates a rate limiter representing a bytes-per-second limit.
    /// </summary>
    /// <param name="bytesPerSecond">Max bandwidth allowed (e.g. 500 * 1024 for 500 KB/s)</param>
    public static TokenBucketRateLimiter CreateLimiter(int bytesPerSecond)
    {
        // Split replenishment into 10 intervals per second to keep it fluid
        int intervalsPerSecond = 10;
        int tokensPerPeriod = bytesPerSecond / intervalsPerSecond;

        var options = new TokenBucketRateLimiterOptions
        {
            TokenLimit = bytesPerSecond, // Maximum burst allowed
            QueueLimit = 10,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            ReplenishmentPeriod = TimeSpan.FromMilliseconds(1000 / intervalsPerSecond), // 100ms
            TokensPerPeriod = tokensPerPeriod,
            AutoReplenishment = true
        };

        return new TokenBucketRateLimiter(options);
    }
}