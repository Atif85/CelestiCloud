namespace CelestiCloud.Core.Models;

public class AppSettings
{
    // "System", "Light", or "Dark"
    public string Theme { get; set; } = "System";

    // 0 means unlimited.
    public int GlobalUploadLimitKbps { get; set; } = 0;
    public bool StartOnStartup { get; set; } = false;
    public bool MinimizeToTrayOnClose { get; set; } = true;
}