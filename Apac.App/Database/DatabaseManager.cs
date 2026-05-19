using Microsoft.Data.Sqlite;
using Apac.App.Models;
using System.Text.Json;

namespace Apac.App.Database;

public class DatabaseManager
{
    private readonly string _connectionString;

    public DatabaseManager(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        Initialize();
    }

    public SqliteConnection GetConnection() => new(_connectionString);

    private void Initialize()
    {
        using var conn = GetConnection();
        conn.Open();

        var cmds = new[]
        {
            @"CREATE TABLE IF NOT EXISTS admins (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                username TEXT NOT NULL DEFAULT 'admin',
                password_hash TEXT NOT NULL,
                created_at DATETIME DEFAULT CURRENT_TIMESTAMP
            );",
            @"CREATE TABLE IF NOT EXISTS users (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                full_name TEXT NOT NULL,
                username TEXT UNIQUE NOT NULL,
                pin_hash TEXT NOT NULL,
                photo_path TEXT,
                profile_id INTEGER REFERENCES access_profiles(id),
                is_active INTEGER DEFAULT 1,
                created_at DATETIME DEFAULT CURRENT_TIMESTAMP
            );",
            @"CREATE TABLE IF NOT EXISTS access_profiles (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                max_session_minutes INTEGER DEFAULT 0,
                mandatory_pause_after_minutes INTEGER DEFAULT 0,
                mandatory_pause_duration_minutes INTEGER DEFAULT 0,
                allowed_hours_json TEXT DEFAULT '[]',
                created_at DATETIME DEFAULT CURRENT_TIMESTAMP
            );",
            @"CREATE TABLE IF NOT EXISTS allowed_sites (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                pattern TEXT NOT NULL,
                profile_id INTEGER REFERENCES access_profiles(id),
                created_at DATETIME DEFAULT CURRENT_TIMESTAMP
            );",
            @"CREATE TABLE IF NOT EXISTS log_entries (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                entry_type TEXT NOT NULL,
                file_path TEXT,
                user_id INTEGER REFERENCES users(id),
                username TEXT,
                description TEXT,
                timestamp DATETIME DEFAULT CURRENT_TIMESTAMP
            );",
            @"CREATE TABLE IF NOT EXISTS system_config (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                key TEXT UNIQUE NOT NULL,
                value TEXT NOT NULL
            );",
            @"CREATE TABLE IF NOT EXISTS active_sessions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER REFERENCES users(id) NOT NULL,
                username TEXT NOT NULL,
                started_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                paused_at DATETIME,
                is_paused INTEGER DEFAULT 0
            );"
        };

        foreach (var cmd in cmds)
        {
            using var c = conn.CreateCommand();
            c.CommandText = cmd;
            c.ExecuteNonQuery();
        }

        EnsureDefaultAdmin(conn);
        EnsureDefaultConfig(conn);
    }

    private void EnsureDefaultAdmin(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM admins";
        var count = (long)cmd.ExecuteScalar()!;
        if (count == 0)
        {
            var hash = BCrypt.Net.BCrypt.HashPassword("APAC@Admin2024");
            using var insert = conn.CreateCommand();
            insert.CommandText = "INSERT INTO admins (username, password_hash) VALUES ('admin', @hash)";
            insert.Parameters.AddWithValue("@hash", hash);
            insert.ExecuteNonQuery();
        }
    }

    private void EnsureDefaultConfig(SqliteConnection conn)
    {
        SetDefaultIfMissing(conn, "app_name", "APAC");
        SetDefaultIfMissing(conn, "welcome_message", "Bem-vindo ao Acesso Público Assistido por Computador");
        SetDefaultIfMissing(conn, "screenshot_enabled", "true");
        SetDefaultIfMissing(conn, "screenshot_interval_sec", "60");
        SetDefaultIfMissing(conn, "screenshot_quality", "Medium");
        SetDefaultIfMissing(conn, "screenshot_path", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "Screenshots"));
        SetDefaultIfMissing(conn, "camera_enabled", "false");
        SetDefaultIfMissing(conn, "camera_interval_sec", "120");
        SetDefaultIfMissing(conn, "camera_quality", "Medium");
        SetDefaultIfMissing(conn, "camera_path", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "Camera"));
        SetDefaultIfMissing(conn, "keylogger_enabled", "true");
        SetDefaultIfMissing(conn, "keylogger_path", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "Keylogs"));
        SetDefaultIfMissing(conn, "retention_days", "30");
        SetDefaultIfMissing(conn, "max_log_size_gb", "5");
        SetDefaultIfMissing(conn, "autostart_enabled", "true");
        SetDefaultIfMissing(conn, "logo_path", "");
        SetDefaultIfMissing(conn, "default_homepage", "https://www.google.com");
    }

    private void SetDefaultIfMissing(SqliteConnection conn, string key, string value)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO system_config (key, value) VALUES (@k, @v)";
        cmd.Parameters.AddWithValue("@k", key);
        cmd.Parameters.AddWithValue("@v", value);
        cmd.ExecuteNonQuery();
    }

    public string GetConfig(string key, string defaultValue = "")
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM system_config WHERE key = @k";
        cmd.Parameters.AddWithValue("@k", key);
        var result = cmd.ExecuteScalar();
        return result?.ToString() ?? defaultValue;
    }

    public void SetConfig(string key, string value)
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO system_config (key, value) VALUES (@k, @v)";
        cmd.Parameters.AddWithValue("@k", key);
        cmd.Parameters.AddWithValue("@v", value);
        cmd.ExecuteNonQuery();
    }

    public string? GetAdminPasswordHash()
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT password_hash FROM admins WHERE username = 'admin' LIMIT 1";
        return cmd.ExecuteScalar()?.ToString();
    }

    public bool UpdateAdminPassword(string newPasswordHash)
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE admins SET password_hash = @h WHERE username = 'admin'";
        cmd.Parameters.AddWithValue("@h", newPasswordHash);
        return cmd.ExecuteNonQuery() > 0;
    }

    public List<User> GetAllUsers()
    {
        var users = new List<User>();
        using var conn = GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT u.id, u.full_name, u.username, u.pin_hash, u.photo_path, u.profile_id, u.is_active, u.created_at
                          FROM users u ORDER BY u.username";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            users.Add(new User
            {
                Id = reader.GetInt32(0),
                FullName = reader.GetString(1),
                Username = reader.GetString(2),
                PinHash = reader.GetString(3),
                PhotoPath = reader.IsDBNull(4) ? null : reader.GetString(4),
                ProfileId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                IsActive = reader.GetInt32(6) == 1,
                CreatedAt = reader.GetDateTime(7)
            });
        }
        return users;
    }

    public User? GetUserById(int id)
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, full_name, username, pin_hash, photo_path, profile_id, is_active, created_at FROM users WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return new User
            {
                Id = reader.GetInt32(0),
                FullName = reader.GetString(1),
                Username = reader.GetString(2),
                PinHash = reader.GetString(3),
                PhotoPath = reader.IsDBNull(4) ? null : reader.GetString(4),
                ProfileId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                IsActive = reader.GetInt32(6) == 1,
                CreatedAt = reader.GetDateTime(7)
            };
        }
        return null;
    }

    public User? GetUserByUsername(string username)
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, full_name, username, pin_hash, photo_path, profile_id, is_active, created_at FROM users WHERE username = @u";
        cmd.Parameters.AddWithValue("@u", username);
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return new User
            {
                Id = reader.GetInt32(0),
                FullName = reader.GetString(1),
                Username = reader.GetString(2),
                PinHash = reader.GetString(3),
                PhotoPath = reader.IsDBNull(4) ? null : reader.GetString(4),
                ProfileId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                IsActive = reader.GetInt32(6) == 1,
                CreatedAt = reader.GetDateTime(7)
            };
        }
        return null;
    }

    public int InsertUser(User user)
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO users (full_name, username, pin_hash, photo_path, profile_id, is_active)
                          VALUES (@name, @uname, @pin, @photo, @prof, @active);
                          SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@name", user.FullName);
        cmd.Parameters.AddWithValue("@uname", user.Username);
        cmd.Parameters.AddWithValue("@pin", user.PinHash);
        cmd.Parameters.AddWithValue("@photo", (object?)user.PhotoPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@prof", (object?)user.ProfileId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@active", user.IsActive ? 1 : 0);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public bool UpdateUser(User user)
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"UPDATE users SET full_name=@name, username=@uname, pin_hash=@pin,
                          photo_path=@photo, profile_id=@prof, is_active=@active WHERE id=@id";
        cmd.Parameters.AddWithValue("@name", user.FullName);
        cmd.Parameters.AddWithValue("@uname", user.Username);
        cmd.Parameters.AddWithValue("@pin", user.PinHash);
        cmd.Parameters.AddWithValue("@photo", (object?)user.PhotoPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@prof", (object?)user.ProfileId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@active", user.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", user.Id);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool DeleteUser(int id)
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM users WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteNonQuery() > 0;
    }

    public List<AccessProfile> GetAllProfiles()
    {
        var profiles = new List<AccessProfile>();
        using var conn = GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, max_session_minutes, mandatory_pause_after_minutes, mandatory_pause_duration_minutes, allowed_hours_json, created_at FROM access_profiles ORDER BY name";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            profiles.Add(new AccessProfile
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                MaxSessionMinutes = reader.GetInt32(2),
                MandatoryPauseAfterMinutes = reader.GetInt32(3),
                MandatoryPauseDurationMinutes = reader.GetInt32(4),
                AllowedHoursJson = reader.GetString(5),
                CreatedAt = reader.GetDateTime(6)
            });
        }
        return profiles;
    }

    public AccessProfile? GetProfile(int id)
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, max_session_minutes, mandatory_pause_after_minutes, mandatory_pause_duration_minutes, allowed_hours_json, created_at FROM access_profiles WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return new AccessProfile
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                MaxSessionMinutes = reader.GetInt32(2),
                MandatoryPauseAfterMinutes = reader.GetInt32(3),
                MandatoryPauseDurationMinutes = reader.GetInt32(4),
                AllowedHoursJson = reader.GetString(5),
                CreatedAt = reader.GetDateTime(6)
            };
        }
        return null;
    }

    public int InsertProfile(AccessProfile profile)
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO access_profiles (name, max_session_minutes, mandatory_pause_after_minutes, mandatory_pause_duration_minutes, allowed_hours_json)
                          VALUES (@name, @max, @pauseAfter, @pauseDur, @hours);
                          SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@name", profile.Name);
        cmd.Parameters.AddWithValue("@max", profile.MaxSessionMinutes);
        cmd.Parameters.AddWithValue("@pauseAfter", profile.MandatoryPauseAfterMinutes);
        cmd.Parameters.AddWithValue("@pauseDur", profile.MandatoryPauseDurationMinutes);
        cmd.Parameters.AddWithValue("@hours", profile.AllowedHoursJson);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public bool UpdateProfile(AccessProfile profile)
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"UPDATE access_profiles SET name=@name, max_session_minutes=@max,
                          mandatory_pause_after_minutes=@pauseAfter, mandatory_pause_duration_minutes=@pauseDur,
                          allowed_hours_json=@hours WHERE id=@id";
        cmd.Parameters.AddWithValue("@name", profile.Name);
        cmd.Parameters.AddWithValue("@max", profile.MaxSessionMinutes);
        cmd.Parameters.AddWithValue("@pauseAfter", profile.MandatoryPauseAfterMinutes);
        cmd.Parameters.AddWithValue("@pauseDur", profile.MandatoryPauseDurationMinutes);
        cmd.Parameters.AddWithValue("@hours", profile.AllowedHoursJson);
        cmd.Parameters.AddWithValue("@id", profile.Id);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool DeleteProfile(int id)
    {
        using var conn = GetConnection();
        conn.Open();

        using var unlink = conn.CreateCommand();
        unlink.CommandText = "UPDATE users SET profile_id = NULL WHERE profile_id = @id";
        unlink.Parameters.AddWithValue("@id", id);
        unlink.ExecuteNonQuery();

        using var delSites = conn.CreateCommand();
        delSites.CommandText = "DELETE FROM allowed_sites WHERE profile_id = @id";
        delSites.Parameters.AddWithValue("@id", id);
        delSites.ExecuteNonQuery();

        using var del = conn.CreateCommand();
        del.CommandText = "DELETE FROM access_profiles WHERE id = @id";
        del.Parameters.AddWithValue("@id", id);
        return del.ExecuteNonQuery() > 0;
    }

    public List<AllowedSite> GetAllSites(int? profileId = null)
    {
        var sites = new List<AllowedSite>();
        using var conn = GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        if (profileId.HasValue)
        {
            cmd.CommandText = "SELECT id, pattern, profile_id, created_at FROM allowed_sites WHERE profile_id = @pid ORDER BY pattern";
            cmd.Parameters.AddWithValue("@pid", profileId.Value);
        }
        else
        {
            cmd.CommandText = "SELECT id, pattern, profile_id, created_at FROM allowed_sites ORDER BY pattern";
        }
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            sites.Add(new AllowedSite
            {
                Id = reader.GetInt32(0),
                Pattern = reader.GetString(1),
                ProfileId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                CreatedAt = reader.GetDateTime(3)
            });
        }
        return sites;
    }

    public int InsertSite(AllowedSite site)
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO allowed_sites (pattern, profile_id) VALUES (@p, @pid);
                          SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@p", site.Pattern);
        cmd.Parameters.AddWithValue("@pid", (object?)site.ProfileId ?? DBNull.Value);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public bool DeleteSite(int id)
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM allowed_sites WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteNonQuery() > 0;
    }

    public void InsertLog(string type, string? filePath, int? userId, string? username, string? description)
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO log_entries (entry_type, file_path, user_id, username, description)
                          VALUES (@t, @fp, @uid, @un, @desc)";
        cmd.Parameters.AddWithValue("@t", type);
        cmd.Parameters.AddWithValue("@fp", (object?)filePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@uid", (object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@un", (object?)username ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@desc", (object?)description ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public List<LogEntry> GetLogs(string? type = null, int? userId = null, DateTime? from = null, DateTime? to = null, int limit = 500)
    {
        var logs = new List<LogEntry>();
        using var conn = GetConnection();
        conn.Open();
        var where = new List<string>();
        using var cmd = conn.CreateCommand();

        if (!string.IsNullOrEmpty(type))
        {
            where.Add("entry_type = @type");
            cmd.Parameters.AddWithValue("@type", type);
        }
        if (userId.HasValue)
        {
            where.Add("user_id = @uid");
            cmd.Parameters.AddWithValue("@uid", userId.Value);
        }
        if (from.HasValue)
        {
            where.Add("timestamp >= @from");
            cmd.Parameters.AddWithValue("@from", from.Value.ToString("yyyy-MM-dd HH:mm:ss"));
        }
        if (to.HasValue)
        {
            where.Add("timestamp <= @to");
            cmd.Parameters.AddWithValue("@to", to.Value.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";
        cmd.CommandText = $"SELECT id, entry_type, file_path, user_id, username, description, timestamp FROM log_entries {whereClause} ORDER BY timestamp DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            logs.Add(new LogEntry
            {
                Id = reader.GetInt32(0),
                EntryType = reader.GetString(1),
                FilePath = reader.IsDBNull(2) ? null : reader.GetString(2),
                UserId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                Username = reader.IsDBNull(4) ? null : reader.GetString(4),
                Description = reader.IsDBNull(5) ? null : reader.GetString(5),
                Timestamp = reader.GetDateTime(6)
            });
        }
        return logs;
    }

    public DashboardData GetDashboardData()
    {
        var data = new DashboardData();
        using var conn = GetConnection();
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM active_sessions WHERE is_paused = 0";
            data.ActiveUsersNow = Convert.ToInt32(cmd.ExecuteScalar());
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT COUNT(*) * 2 FROM active_sessions
                              WHERE date(started_at) = date('now')";
            data.TotalUsageMinutesToday = Convert.ToInt64(cmd.ExecuteScalar());
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, entry_type, file_path, user_id, username, description, timestamp FROM log_entries ORDER BY timestamp DESC LIMIT 20";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                data.RecentActivities.Add(new LogEntry
                {
                    Id = reader.GetInt32(0),
                    EntryType = reader.GetString(1),
                    FilePath = reader.IsDBNull(2) ? null : reader.GetString(2),
                    UserId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    Username = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Description = reader.IsDBNull(5) ? null : reader.GetString(5),
                    Timestamp = reader.GetDateTime(6)
                });
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT id, entry_type, file_path, user_id, username, description, timestamp
                              FROM log_entries WHERE entry_type IN ('process_blocked', 'window_blocked', 'hotkey_blocked', 'close_attempt')
                              ORDER BY timestamp DESC LIMIT 10";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                data.RecentAlerts.Add(new LogEntry
                {
                    Id = reader.GetInt32(0),
                    EntryType = reader.GetString(1),
                    FilePath = reader.IsDBNull(2) ? null : reader.GetString(2),
                    UserId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    Username = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Description = reader.IsDBNull(5) ? null : reader.GetString(5),
                    Timestamp = reader.GetDateTime(6)
                });
            }
        }

        return data;
    }

    public void StartSession(int userId, string username)
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO active_sessions (user_id, username) VALUES (@uid, @un)";
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@un", username);
        cmd.ExecuteNonQuery();
    }

    public void EndSession(int userId)
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM active_sessions WHERE user_id = @uid";
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.ExecuteNonQuery();
    }

    public void EndAllSessions()
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM active_sessions";
        cmd.ExecuteNonQuery();
    }

    public void CleanupOldLogs(int retentionDays)
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM log_entries WHERE timestamp < datetime('now', @days)";
        cmd.Parameters.AddWithValue("@days", $"-{retentionDays} days");
        cmd.ExecuteNonQuery();
    }
}
