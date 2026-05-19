using System;

namespace Apac.Database
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
    }

    public class AccessProfile
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int MaxSessionMinutes { get; set; }
        public int MandatoryPauseAfterMinutes { get; set; }
        public int MandatoryPauseMinutes { get; set; }
        public string DefaultUrl { get; set; }
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
        public bool IsWildcard { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class LogEntry
    {
        public int Id { get; set; }
        public string EntryType { get; set; }
        public int? UserId { get; set; }
        public string Username { get; set; }
        public string FilePath { get; set; }
        public string Details { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class SystemSetting
    {
        public int Id { get; set; }
        public string Key { get; set; }
        public string Value { get; set; }
    }

    public enum EntryType
    {
        Screenshot,
        Camera,
        Keylog,
        SystemEvent,
        Login,
        Logout,
        BlockedProcess,
        BlockedSite,
        SettingChange,
        KioskExit
    }

    public class BlockedProcess
    {
        public string Name { get; set; }
        public BlockedProcess(string name)
        {
            Name = name;
        }
    }
}
