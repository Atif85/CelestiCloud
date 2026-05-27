using CelestiCloud.Core.Logging;
using CelestiCloud.Core.Models;
using CelestiCloud.Core.Providers;
using System;
using System.Collections.Generic;
using System.Text;

namespace CelestiCloud.Core.Jobs.LocalToCloudJobs;

public class SyncJob : LocalToCloudJobBase
{
    public SyncJob(JobConfig config, ICloudProvider provider, string appDataPath, IJobLogger logger)
        : base(config, provider, appDataPath, logger) { }

    protected override bool AllowDeletions => true;
}