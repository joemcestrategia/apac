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

        public bool IsFirstRun
        {
            get => _db.GetSetting("is_first_run", "true") == "true";
            set => _db.SetSetting("is_first_run", value ? "true" : "false");
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

        public string BaseLogPath
        {
            get => _db.GetSetting("base_log_path", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "APAC", "Logs"));
            set => _db.SetSetting("base_log_path", value);
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



        public bool KeyloggerEnabled
        {
            get => _db.GetSetting("keylogger_enabled", "true") == "true";
            set => _db.SetSetting("keylogger_enabled", value ? "true" : "false");
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

        public string CameraDeviceName
        {
            get => _db.GetSetting("camera_device_name", "");
            set => _db.SetSetting("camera_device_name", value);
        }

        public int EmergencyDisableMinutes
        {
            get => int.TryParse(_db.GetSetting("emergency_disable_minutes", "0"), out var v) ? v : 0;
            set => _db.SetSetting("emergency_disable_minutes", value.ToString());
        }

        public string KioskExePath
        {
            get => _db.GetSetting("kiosk_exe_path",
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ApacKiosk.exe"));
            set => _db.SetSetting("kiosk_exe_path", value);
        }

        public int MonitorIntervalSec
        {
            get => int.TryParse(_db.GetSetting("monitor_interval_sec", "3"), out var v) ? v : 3;
            set => _db.SetSetting("monitor_interval_sec", value.ToString());
        }

        public string LicenseSignature
        {
            get => _db.GetSetting("license_signature", "");
            set => _db.SetSetting("license_signature", value);
        }

        public string TrialStartDate
        {
            get => _db.GetSetting("trial_start_date", "");
            set => _db.SetSetting("trial_start_date", value);
        }

        public string LastCloudCheckin
        {
            get => _db.GetSetting("last_cloud_checkin", "");
            set => _db.SetSetting("last_cloud_checkin", value);
        }

        public string UnitState
        {
            get => _db.GetSetting("unit_state", "");
            set => _db.SetSetting("unit_state", value);
        }

        public string UnitCity
        {
            get => _db.GetSetting("unit_city", "");
            set => _db.SetSetting("unit_city", value);
        }

        public string UnitGender
        {
            get => _db.GetSetting("unit_gender", "");
            set => _db.SetSetting("unit_gender", value);
        }

        public string ContactEmail
        {
            get => _db.GetSetting("contact_email", "");
            set => _db.SetSetting("contact_email", value);
        }

        public string ContactName
        {
            get => _db.GetSetting("contact_name", "");
            set => _db.SetSetting("contact_name", value);
        }

        public string GetSetting(string key, string defaultValue) => _db.GetSetting(key, defaultValue);

        public void SetSetting(string key, string value) => _db.SetSetting(key, value);
    }
}
