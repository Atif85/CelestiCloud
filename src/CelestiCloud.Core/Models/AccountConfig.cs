using System;

namespace CelestiCloud.Core.Models;

public class AccountConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString(); // "acc-google-personal"
    public string Provider { get; set; } = "googledrive";      // "googledrive", "dropbox", etc.
    public string DisplayName { get; set; } = string.Empty;     // "alex.personal@gmail.com"
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
}