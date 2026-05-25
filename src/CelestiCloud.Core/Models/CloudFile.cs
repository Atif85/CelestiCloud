using System;
using System.Collections.Generic;
using System.Text;

namespace CelestiCloud.Core.Models;

public class CloudFile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public bool IsFolder { get; init; }
    public long? Size { get; init; }
    public DateTime? ModifiedDate { get; init; }

    public override string ToString()
    {
        return $"ID: {Id}, Name: {Name}, IsFolder: {IsFolder}, Size: {Size}";
    }
}
