namespace CelestiCloud.Core.Models;

public class AppSettings
{
    // "System", "Light", or "Dark"
    public string Theme { get; set; } = "System";

    // 0 means unlimited. Let's store it in KB/s for the user UI.
    public int GlobalUploadLimitKbps { get; set; } = 0;
    public int GlobalDownloadLimitKbps { get; set; } = 0;
}