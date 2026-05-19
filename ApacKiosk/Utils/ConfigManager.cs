using System;
using System.IO;
using System.Text.Json;

namespace ApacKiosk.Utils
{
    public class ConfigManager
    {
        private readonly Database.DatabaseManager _db;

        public ConfigManager(Database.DatabaseManager db)
        {
            _db = db;
        }

        public string DisplayName
        {
            get => _db.GetSetting("display_name", "APAC - Acesso Controlado");
            set => _db.SetSetting("display_name", value);
        }

        public string WelcomeMessage
        {
            get => _db.GetSetting("welcome_message", "Bem-vindo ao APAC");
            set => _db.SetSetting("welcome_message", value);
        }

        public string LogoPath
        {
            get => _db.GetSetting("logo_path", "");
            set => _db.SetSetting("logo_path", value);
        }

        public bool ScreenshotEnabled
        {
            get => _db.GetSetting("screenshot_enabled", "true") == "true";
            set => _db.SetSetting("screenshot_enabled", value ? "true" : "false");
        }

        public int ScreenshotIntervalSec
        {
            get => int.TryParse(_db.GetSetting("screenshot_interval_sec", "60"), out var v) ? v : 60;
            set => _db.SetSetting("screenshot_interval_sec", value.ToString());
        }

        public string ScreenshotQuality
        {
            get => _db.GetSetting("screenshot_quality", "Medium");
            set => _db.SetSetting("screenshot_quality", value);
        }

        public string ScreenshotPath
        {
            get => _db.GetSetting("screenshot_path", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "APAC", "Screenshots"));
            set => _db.SetSetting("screenshot_path", value);
        }

        public bool CameraEnabled
        {
            get => _db.GetSetting("camera_enabled", "false") == "true";
            set => _db.SetSetting("camera_enabled", value ? "true" : "false");
        }

        public int CameraIntervalSec
        {
            get => int.TryParse(_db.GetSetting("camera_interval_sec", "120"), out var v) ? v : 120;
            set => _db.SetSetting("camera_interval_sec", value.ToString());
        }

        public string CameraQuality
        {
            get => _db.GetSetting("camera_quality", "Medium");
            set => _db.SetSetting("camera_quality", value);
        }

        public string CameraPath
        {
            get => _db.GetSetting("camera_path", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "APAC", "Camera"));
            set => _db.SetSetting("camera_path", value);
        }

        public bool KeyloggerEnabled
        {
            get => _db.GetSetting("keylogger_enabled", "true") == "true";
            set => _db.SetSetting("keylogger_enabled", value ? "true" : "false");
        }

        public string KeyloggerPath
        {
            get => _db.GetSetting("keylogger_path", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "APAC", "Keylogs"));
            set => _db.SetSetting("keylogger_path", value);
        }

        public string KeyloggerFileMode
        {
            get => _db.GetSetting("keylogger_file_mode", "daily");
            set => _db.SetSetting("keylogger_file_mode", value);
        }

        public int LogRetentionDays
        {
            get => int.TryParse(_db.GetSetting("log_retention_days", "30"), out var v) ? v : 30;
            set => _db.SetSetting("log_retention_days", value.ToString());
        }

        public double LogMaxSizeGb
        {
            get => double.TryParse(_db.GetSetting("log_max_size_gb", "5"), out var v) ? v : 5.0;
            set => _db.SetSetting("log_max_size_gb", value.ToString());
        }

        public bool AutostartEnabled
        {
            get => _db.GetSetting("autostart_enabled", "true") == "true";
            set => _db.SetSetting("autostart_enabled", value ? "true" : "false");
        }

        public int EmergencyDisableMinutes
        {
            get => int.TryParse(_db.GetSetting("emergency_disable_minutes", "0"), out var v) ? v : 0;
            set => _db.SetSetting("emergency_disable_minutes", value.ToString());
        }
    }
}
