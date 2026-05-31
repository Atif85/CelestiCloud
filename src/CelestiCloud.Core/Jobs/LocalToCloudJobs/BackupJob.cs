using CelestiCloud.Core.Logging;
using CelestiCloud.Core.Models;
using CelestiCloud.Core.Providers;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.RateLimiting;

namespace CelestiCloud.Core.Jobs.LocalToCloudJobs;

public class BackupJob : LocalToCloudJobBase
{
    public BackupJob(JobConfig config, ICloudProvider provider, string appDataPath, RateLimiter? limiter, int safeChunkSize, IJobLogger logger)
        : base(config, provider, appDataPath, limiter, safeChunkSize, logger) { }

    protected override bool AllowDeletions => false;
}