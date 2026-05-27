using CelestiCloud.Core.Models;
using CelestiCloud.Core.Providers;
using System;
using System.Collections.Generic;
using System.Text;

namespace CelestiCloud.Core.Jobs.LocalToCloudJobs;

public class SyncJob : LocalToCloudJobBase
{
    public SyncJob(JobConfig config, ICloudProvider provider, string appDataPath)
        : base(config, provider, appDataPath) { }

    protected override bool AllowDeletions => true;
}