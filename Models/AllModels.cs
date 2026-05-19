using System;

namespace Apac.Models
{
    public class Admin
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string PasswordHash { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class User
    {
        public int Id { get; set; }
        public string FullName { get; set; }
        public string Username { get; set; }
        public string PinHash { get; set; }
        public string PhotoPath { get; set; }
        public int? ProfileId { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public string ProfileName { get; set; }
        public string PhotoBase64 { get; set; }
    }

    public class AccessProfile
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int MaxSessionMinutes { get; set; }
        public int MaxDailyMinutes { get; set; }
        public int MandatoryPauseAfterMinutes { get; set; }
        public int MandatoryPauseMinutes { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class AllowedSite
    {
        public int Id { get; set; }
        public int? ProfileId { get; set; }
        public string Url { get; set; }
        public string Notes { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class TimeRule
    {
        public int Id { get; set; }
        public int ProfileId { get; set; }
        public int? DayOfWeek { get; set; }
        public string StartTime { get; set; }
        public string EndTime { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class MonitoringConfig
    {
        public int Id { get; set; }
        public bool ScreenshotEnabled { get; set; }
        public int ScreenshotIntervalSeconds { get; set; }
        public string ScreenshotQuality { get; set; }
        public string ScreenshotFolder { get; set; }
        public bool CameraEnabled { get; set; }
        public string CameraDevice { get; set; }
        public int CameraIntervalSeconds { get; set; }
        public string CameraQuality { get; set; }
        public string CameraFolder { get; set; }
        public bool KeyloggerEnabled { get; set; }
        public string KeyloggerFolder { get; set; }
        public string KeyloggerMode { get; set; }
        public int RetentionDays { get; set; }
        public double MaxLogSizeGb { get; set; }
    }

    public class SystemConfig
    {
        public int Id { get; set; }
        public string DisplayName { get; set; }
        public string LogoPath { get; set; }
        public string WelcomeMessage { get; set; }
        public bool AutostartEnabled { get; set; }
        public int KioskEmergencyMinutes { get; set; }
        public string DefaultUrl { get; set; }
    }

    public class LogEntry
    {
        public int Id { get; set; }
        public int? UserId { get; set; }
        public string Type { get; set; }
        public string FilePath { get; set; }
        public string Content { get; set; }
        public string Details { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Username { get; set; }
    }

    public class ActiveSession
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public DateTime LoginTime { get; set; }
        public DateTime? LogoutTime { get; set; }
        public bool IsActive { get; set; }
    }
}
