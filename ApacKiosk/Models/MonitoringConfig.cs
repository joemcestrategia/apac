namespace ApacKiosk.Models;

public class MonitoringConfig
{
    public bool ScreenshotEnabled { get; set; } = true;
    public int ScreenshotIntervalSeconds { get; set; } = 60;
    public string ScreenshotQuality { get; set; } = "Media";
    public string ScreenshotFolder { get; set; } = "Logs\\Screenshots";

    public bool CameraEnabled { get; set; } = false;
    public int CameraIntervalSeconds { get; set; } = 120;
    public string CameraQuality { get; set; } = "Media";
    public string CameraFolder { get; set; } = "Logs\\Camera";
    public string CameraDevice { get; set; } = "";

    public bool KeyloggerEnabled { get; set; } = true;
    public string KeyloggerFolder { get; set; } = "Logs\\Keylogs";
    public string KeyloggerFileMode { get; set; } = "daily";

    public int LogRetentionDays { get; set; } = 30;
    public double MaxLogFolderSizeGB { get; set; } = 5.0;
}
