namespace ApacKiosk.Models;

public class Admin
{
    public int Id { get; set; }
    public string Username { get; set; } = "admin";
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public bool MustChangePassword { get; set; }
}

public class User
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string PinHash { get; set; } = string.Empty;
    public string? PhotoPath { get; set; }
    public int? ProfileId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public class AccessProfile
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int MaxSessionMinutes { get; set; }
    public int MandatoryBreakAfterMinutes { get; set; }
    public int MandatoryBreakDurationMinutes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public class ProfileTimeSlot
{
    public int Id { get; set; }
    public int ProfileId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
}

public class AllowedSite
{
    public int Id { get; set; }
    public int? ProfileId { get; set; }
    public string UrlPattern { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public class LogEntry
{
    public int Id { get; set; }
    public string EntryType { get; set; } = string.Empty;
    public int? UserId { get; set; }
    public string? FilePath { get; set; }
    public string? Description { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

public class MonitoringConfig
{
    public string Module { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class SystemConfig
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class EmergencyOverride
{
    public int Id { get; set; }
    public DateTime StartTime { get; set; } = DateTime.Now;
    public DateTime EndTime { get; set; }
    public string Justification { get; set; } = string.Empty;
    public string AdminUsername { get; set; } = string.Empty;
}
