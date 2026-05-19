using System;

namespace ApacKiosk.Models
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
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
    }

    public class AccessProfile
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int MaxSessionMinutes { get; set; }
        public int MandatoryPauseMinutes { get; set; }
        public int PauseAfterMinutes { get; set; }
        public string HomepageUrl { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class TimeRule
    {
        public int Id { get; set; }
        public int ProfileId { get; set; }
        public int DayOfWeek { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
    }

    public class AllowedSite
    {
        public int Id { get; set; }
        public int? ProfileId { get; set; }
        public string UrlPattern { get; set; }
        public bool IsGlobal { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class LogEntry
    {
        public long Id { get; set; }
        public int? UserId { get; set; }
        public string Type { get; set; }
        public string FilePath { get; set; }
        public string Description { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class SessionLog
    {
        public long Id { get; set; }
        public int UserId { get; set; }
        public DateTime LoginTime { get; set; }
        public DateTime? LogoutTime { get; set; }
        public int DurationSeconds { get; set; }
    }

    public class SystemSetting
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }
}
