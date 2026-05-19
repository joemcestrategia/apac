namespace Apac.App.Models;

public class Admin
{
    public int Id { get; set; }
    public string Username { get; set; } = "admin";
    public string PasswordHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class User
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Username { get; set; } = "";
    public string PinHash { get; set; } = "";
    public string? PhotoPath { get; set; }
    public int? ProfileId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public AccessProfile? Profile { get; set; }
}

public class AccessProfile
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int MaxSessionMinutes { get; set; } = 0;
    public int MandatoryPauseAfterMinutes { get; set; } = 0;
    public int MandatoryPauseDurationMinutes { get; set; } = 0;
    public string AllowedHoursJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; }
}

public class AllowedSchedule
{
    public DayOfWeek Day { get; set; }
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }
}

public class AllowedSite
{
    public int Id { get; set; }
    public string Pattern { get; set; } = "";
    public int? ProfileId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class LogEntry
{
    public int Id { get; set; }
    public string EntryType { get; set; } = "";
    public string? FilePath { get; set; }
    public int? UserId { get; set; }
    public string? Username { get; set; }
    public string? Description { get; set; }
    public DateTime Timestamp { get; set; }
}

public class SystemConfig
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

public class DashboardData
{
    public int ActiveUsersNow { get; set; }
    public long TotalUsageMinutesToday { get; set; }
    public List<LogEntry> RecentActivities { get; set; } = new();
    public List<LogEntry> RecentAlerts { get; set; } = new();
}
