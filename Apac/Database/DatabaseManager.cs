using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SQLite;
using System.IO;

namespace Apac.Database
{
    public class DatabaseManager
    {
        private static DatabaseManager _instance;
        private static readonly object _lock = new object();
        private readonly string _connectionString;
        private readonly string _dbPath;

        public static DatabaseManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new DatabaseManager();
                        }
                    }
                }
                return _instance;
            }
        }

        private DatabaseManager()
        {
            _dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "apac.db");
            _connectionString = $"Data Source={_dbPath};Version=3;Foreign Keys=True;";
            InitializeDatabase();
        }

        private SQLiteConnection GetConnection()
        {
            var conn = new SQLiteConnection(_connectionString);
            conn.Open();
            ExecutePragma(conn);
            return conn;
        }

        private void ExecutePragma(SQLiteConnection conn)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
                cmd.ExecuteNonQuery();
            }
        }

        private void InitializeDatabase()
        {
            using (var conn = GetConnection())
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS admins (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            username TEXT NOT NULL,
                            password_hash TEXT NOT NULL,
                            created_at DATETIME DEFAULT CURRENT_TIMESTAMP
                        );

                        CREATE TABLE IF NOT EXISTS access_profiles (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            name TEXT NOT NULL UNIQUE,
                            max_session_minutes INTEGER DEFAULT 0,
                            mandatory_pause_after_minutes INTEGER DEFAULT 0,
                            mandatory_pause_minutes INTEGER DEFAULT 0,
                            default_url TEXT DEFAULT '',
                            created_at DATETIME DEFAULT CURRENT_TIMESTAMP
                        );

                        CREATE TABLE IF NOT EXISTS users (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            full_name TEXT NOT NULL,
                            username TEXT UNIQUE NOT NULL,
                            pin_hash TEXT NOT NULL,
                            photo_path TEXT,
                            profile_id INTEGER REFERENCES access_profiles(id),
                            is_active INTEGER DEFAULT 1,
                            created_at DATETIME DEFAULT CURRENT_TIMESTAMP
                        );

                        CREATE TABLE IF NOT EXISTS time_rules (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            profile_id INTEGER NOT NULL REFERENCES access_profiles(id) ON DELETE CASCADE,
                            day_of_week INTEGER NOT NULL,
                            start_time TEXT NOT NULL,
                            end_time TEXT NOT NULL
                        );

                        CREATE TABLE IF NOT EXISTS allowed_sites (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            profile_id INTEGER REFERENCES access_profiles(id) ON DELETE CASCADE,
                            url_pattern TEXT NOT NULL,
                            is_wildcard INTEGER DEFAULT 0,
                            created_at DATETIME DEFAULT CURRENT_TIMESTAMP
                        );

                        CREATE TABLE IF NOT EXISTS log_entries (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            entry_type TEXT NOT NULL,
                            user_id INTEGER REFERENCES users(id),
                            file_path TEXT,
                            details TEXT,
                            timestamp DATETIME DEFAULT CURRENT_TIMESTAMP
                        );

                        CREATE TABLE IF NOT EXISTS system_settings (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            key TEXT NOT NULL UNIQUE,
                            value TEXT
                        );

                        CREATE INDEX IF NOT EXISTS idx_log_entries_timestamp ON log_entries(timestamp);
                        CREATE INDEX IF NOT EXISTS idx_log_entries_type ON log_entries(entry_type);
                        CREATE INDEX IF NOT EXISTS idx_log_entries_user ON log_entries(user_id);
                    ";
                    cmd.ExecuteNonQuery();
                }
                CreateDefaultAdmin(conn);
                SeedDefaultSettings(conn);
            }
        }

        private void CreateDefaultAdmin(SQLiteConnection conn)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM admins";
                long count = (long)cmd.ExecuteScalar();
                if (count == 0)
                {
                    string defaultPassword = ConfigurationManager.AppSettings["AdminDefaultPassword"] ?? "APAC@Admin2024";
                    string hash = BCrypt.Net.BCrypt.HashPassword(defaultPassword);
                    cmd.CommandText = "INSERT INTO admins (username, password_hash) VALUES (@u, @p)";
                    cmd.Parameters.AddWithValue("@u", "admin");
                    cmd.Parameters.AddWithValue("@p", hash);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private void SeedDefaultSettings(SQLiteConnection conn)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM system_settings";
                long count = (long)cmd.ExecuteScalar();
                if (count == 0)
                {
                    var defaults = new Dictionary<string, string>
                    {
                        { "display_name", "APAC" },
                        { "welcome_message", "Bem-vindo ao Espaço de Acesso Digital" },
                        { "logo_path", "" },
                        { "autostart", "false" },
                        { "screenshots_enabled", "true" },
                        { "screenshots_interval", "60" },
                        { "screenshots_quality", "Medium" },
                        { "screenshots_path", "Logs\\Screenshots" },
                        { "camera_enabled", "false" },
                        { "camera_device", "" },
                        { "camera_interval", "120" },
                        { "camera_quality", "Medium" },
                        { "camera_path", "Logs\\Camera" },
                        { "keylogger_enabled", "true" },
                        { "keylogger_path", "Logs\\Keylogs" },
                        { "keylogger_mode", "daily" },
                        { "log_retention_days", "30" },
                        { "max_log_size_gb", "10" }
                    };
                    foreach (var kvp in defaults)
                    {
                        cmd.CommandText = "INSERT OR IGNORE INTO system_settings (key, value) VALUES (@k, @v)";
                        cmd.Parameters.Clear();
                        cmd.Parameters.AddWithValue("@k", kvp.Key);
                        cmd.Parameters.AddWithValue("@v", kvp.Value);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        public Admin GetAdmin()
        {
            using (var conn = GetConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id, username, password_hash, created_at FROM admins LIMIT 1";
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return new Admin
                        {
                            Id = reader.GetInt32(0),
                            Username = reader.GetString(1),
                            PasswordHash = reader.GetString(2),
                            CreatedAt = reader.GetDateTime(3)
                        };
                    }
                }
            }
            return null;
        }

        public bool VerifyAdminPassword(string password)
        {
            var admin = GetAdmin();
            if (admin == null) return false;
            return BCrypt.Net.BCrypt.Verify(password, admin.PasswordHash);
        }

        public void ChangeAdminPassword(string newPassword)
        {
            using (var conn = GetConnection())
            using (var cmd = conn.CreateCommand())
            {
                string hash = BCrypt.Net.BCrypt.HashPassword(newPassword);
                cmd.CommandText = "UPDATE admins SET password_hash = @p WHERE id = 1";
                cmd.Parameters.AddWithValue("@p", hash);
                cmd.ExecuteNonQuery();
            }
        }

        public bool IsDefaultAdminPassword()
        {
            string defaultPassword = ConfigurationManager.AppSettings["AdminDefaultPassword"] ?? "APAC@Admin2024";
            return VerifyAdminPassword(defaultPassword);
        }

        public List<User> GetUsers()
        {
            var users = new List<User>();
            using (var conn = GetConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"SELECT u.id, u.full_name, u.username, u.pin_hash, u.photo_path, 
                                    u.profile_id, u.is_active, u.created_at, p.name
                                    FROM users u LEFT JOIN access_profiles p ON u.profile_id = p.id
                                    ORDER BY u.full_name";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        users.Add(new User
                        {
                            Id = reader.GetInt32(0),
                            FullName = reader.GetString(1),
                            Username = reader.GetString(2),
                            PinHash = reader.GetString(3),
                            PhotoPath = reader.IsDBNull(4) ? null : reader.GetString(4),
                            ProfileId = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5),
                            IsActive = reader.GetInt32(6) == 1,
                            CreatedAt = reader.GetDateTime(7),
                            ProfileName = reader.IsDBNull(8) ? null : reader.GetString(8)
                        });
                    }
                }
            }
            return users;
        }

        public User GetUserById(int id)
        {
            using (var conn = GetConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"SELECT u.id, u.full_name, u.username, u.pin_hash, u.photo_path, 
                                    u.profile_id, u.is_active, u.created_at, p.name
                                    FROM users u LEFT JOIN access_profiles p ON u.profile_id = p.id
                                    WHERE u.id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return new User
                        {
                            Id = reader.GetInt32(0),
                            FullName = reader.GetString(1),
                            Username = reader.GetString(2),
                            PinHash = reader.GetString(3),
                            PhotoPath = reader.IsDBNull(4) ? null : reader.GetString(4),
                            ProfileId = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5),
                            IsActive = reader.GetInt32(6) == 1,
                            CreatedAt = reader.GetDateTime(7),
                            ProfileName = reader.IsDBNull(8) ? null : reader.GetString(8)
                        };
                    }
                }
            }
            return null;
        }

        public User GetUserByUsername(string username)
        {
            using (var conn = GetConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"SELECT u.id, u.full_name, u.username, u.pin_hash, u.photo_path, 
                                    u.profile_id, u.is_active, u.created_at, p.name
                                    FROM users u LEFT JOIN access_profiles p ON u.profile_id = p.id
                                    WHERE u.username = @u";
                cmd.Parameters.AddWithValue("@u", username);
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return new User
                        {
                            Id = reader.GetInt32(0),
                            FullName = reader.GetString(1),
                            Username = reader.GetString(2),
                            PinHash = reader.GetString(3),
                            PhotoPath = reader.IsDBNull(4) ? null : reader.GetString(4),
                            ProfileId = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5),
                            IsActive = reader.GetInt32(6) == 1,
                            CreatedAt = reader.GetDateTime(7),
                            ProfileName = reader.IsDBNull(8) ? null : reader.GetString(8)
                        };
                    }
                }
            }
            return null;
        }

        public bool VerifyUserPin(string username, string pin)
        {
            using (var conn = GetConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT pin_hash FROM users WHERE username = @u AND is_active = 1";
                cmd.Parameters.AddWithValue("@u", username);
                var result = cmd.ExecuteScalar();
                if (result == null) return false;
                return BCrypt.Net.BCrypt.Verify(pin, result.ToString());
            }
        }

        public User AddUser(string fullName, string username, string pin, int? profileId, string photoPath)
        {
            using (var conn = GetConnection())
            using (var cmd = conn.CreateCommand())
            {
                string pinHash = BCrypt.Net.BCrypt.HashPassword(pin);
                cmd.CommandText = @"INSERT INTO users (full_name, username, pin_hash, profile_id, photo_path, is_active) 
                                    VALUES (@f, @u, @p, @pr, @ph, 1);
                                    SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("@f", fullName);
                cmd.Parameters.AddWithValue("@u", username);
                cmd.Parameters.AddWithValue("@p", pinHash);
                cmd.Parameters.AddWithValue("@pr", (object)profileId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ph", (object)photoPath ?? DBNull.Value);
                int id = Convert.ToInt32((long)cmd.ExecuteScalar());
                return GetUserById(id);
            }
        }

        public void UpdateUser(int id, string fullName, string username, int? profileId, bool isActive, string photoPath = null)
        {
            using (var conn = GetConnection())
            using (var cmd = conn.CreateCommand())
            {
                var sql = "UPDATE users SET full_name = @f, username = @u, profile_id = @pr, is_active = @a";
                if (photoPath != null)
                    sql += ", photo_path = @ph";
                sql += " WHERE id = @id";
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@f", fullName);
                cmd.Parameters.AddWithValue("@u", username);
                cmd.Parameters.AddWithValue("@pr", (object)profileId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@a", isActive ? 1 : 0);
                cmd.Parameters.AddWithValue("@id", id);
                if (photoPath != null)
                    cmd.Parameters.AddWithValue("@ph", photoPath);
                cmd.ExecuteNonQuery();
            }
        }

        public void UpdateUserPin(int id, string newPin)
        {
            using (var conn = GetConnection())
            using (var cmd = conn.CreateCommand())
            {
                string pinHash = BCrypt.Net.BCrypt.HashPassword(newPin);
                cmd.CommandText = "UPDATE users SET pin_hash = @p WHERE id = @id";
                cmd.Parameters.AddWithValue("@p", pinHash);
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }
        }

        public void DeleteUser(int id)
        {
            using (var conn = GetConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM users WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }
        }

        public List<AccessProfile> GetAccessProfiles()
        {
            var profiles = new List<AccessProfile>();
            using (var conn = GetConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"SELECT id, name, max_session_minutes, mandatory_pause_after_minutes, 
                                    mandatory_pause_minutes, default_url, created_at FROM access_profiles ORDER BY name";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        profiles.Add(new AccessProfile
                        {
                            Id = reader.GetInt32(0),
                            Name = reader.GetString(1),
                            MaxSessionMinutes = reader.GetInt32(2),
                            MandatoryPauseAfterMinutes = reader.GetInt32(3),
                            MandatoryPauseMinutes = reader.GetInt32(4),
                            DefaultUrl = reader.IsDBNull(5) ? "" : reader.GetString(5),
                            CreatedAt = reader.GetDateTime(6)
                        });
                    }
                }
            }
            return profiles;
        }

        public AccessProfile GetAccessProfile(int id)
        {
            using (var conn = GetConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"SELECT id, name, max_session_minutes, mandatory_pause_after_minutes, 
                                    mandatory_pause_minutes, default_url, created_at FROM access_profiles WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return new AccessProfile
                        {
                            Id = reader.GetInt32(0),
                            Name = reader.GetString(1),
                            MaxSessionMinutes = reader.GetInt32(2),
                            MandatoryPauseAfterMinutes = reader.GetInt32(3),
                            MandatoryPauseMinutes = reader.GetInt32(4),
                            DefaultUrl = reader.IsDBNull(5) ? "" : reader.GetString(5),
                            CreatedAt = reader.GetDateTime(6)
                        };
                    }
                }
            }
            return null;
        }

        public AccessProfile AddAccessProfile(string name, int maxSessionMinutes, int pauseAfterMinutes, int pauseMinutes, string defaultUrl)
        {
            using (var conn = GetConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"INSERT INTO access_profiles (name, max_session_minutes, mandatory_pause_after_minutes, 
                                    mandatory_pause_minutes, default_url) 
                                    VALUES (@n, @m, @pa, @pm, @d);
                                    SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("@n", name);
                cmd.Parameters.AddWithValue("@m", maxSessionMinutes);
                cmd.Parameters.AddWithValue("@pa", pauseAfterMinutes);
                cmd.Parameters.AddWithValue("@pm", pauseMinutes);
                cmd.Parameters.AddWithValue("@d", (object)defaultUrl ?? "");
                int id = Convert.ToInt32((long)cmd.ExecuteScalar());
                return GetAccessProfile(id);
            }
        }

        public void UpdateAccessProfile(int id, string name, int maxSessionMinutes, int pauseAfterMinutes, int pauseMinutes, string defaultUrl)
        {
            using (var conn = GetConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"UPDATE access_profiles SET name = @n, max_session_minutes = @m, 
                                    mandatory_pause_after_minutes = @pa, mandatory_pause_minutes = @pm, default_url = @d
                                    WHERE id = @id";
                cmd.Parameters.AddWithValue("@n", name);
                cmd.Parameters.AddWithValue("@m", maxSessionMinutes);
                cmd.Parameters.AddWithValue("@pa", pauseAfterMinutes);
                cmd.Parameters.AddWithValue("@pm", pauseMinutes);
                cmd.Parameters.AddWithValue("@d", (object)defaultUrl ?? "");
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }
        }

        public void DeleteAccessProfile(int id)
        {
            using (var conn = GetConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM access_profiles WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }
        }

        public List<TimeRule> GetTimeRules(int profileId)
        {
            var rules = new List<TimeRule>();
            using (var conn = GetConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id, profile_id, day_of_week, start_time, end_time FROM time_rules WHERE profile_id = @pid ORDER BY day_of_week, start_time";
                cmd.Parameters.AddWithValue("@pid", profileId);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        rules.Add(new TimeRule
                        {
                            Id = reader.GetInt32(0),
                            ProfileId = reader.GetInt32(1),
                            DayOfWeek = reader.GetInt32(2),
                            StartTime = TimeSpan.Parse(reader.GetString(3)),
                            EndTime = TimeSpan.Parse(reader.GetString(4))
                        });
                    }
                }
            }
            return rules;
        }

        public void SaveTimeRules(int profileId, List<TimeRule> rules)
        {
            using (var conn = GetConnection())
            using (var trans = conn.BeginTransaction())
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = trans;
                    cmd.CommandText = "DELETE FROM time_rules WHERE profile_id = @pid";
                    cmd.Parameters.AddWithValue("@pid", profileId);
                    cmd.ExecuteNonQuery();

                    foreach (var rule in rules)
                    {
                        cmd.CommandText = @"INSERT INTO time_rules (profile_id, day_of_week, start_time, end_time) 
                                            VALUES (@pid, @dow, @st, @et)";
                        cmd.Parameters.Clear();
                        cmd.Parameters.AddWithValue("@pid", profileId);
                        cmd.Parameters.AddWithValue("@dow", rule.DayOfWeek);
                        cmd.Parameters.AddWithValue("@st", rule.StartTime.ToString(@"hh\:mm"));
                        cmd.Parameters.AddWithValue("@et", rule.EndTime.ToString(@"hh\:mm"));
                        cmd.ExecuteNonQuery();
                    }
                }
                trans.Commit();
            }
        }

        public List<AllowedSite> GetAllowedSites(int? profileId)
        {
            var sites = new List<AllowedSite>();
            using (var conn = GetConnection())
            using (var cmd = conn.CreateCommand())
            {
                if (profileId.HasValue)
                    cmd.CommandText = "SELECT id, profile_id, url_pattern, is_wildcard, created_at FROM allowed_sites WHERE profile_id = @pid OR profile_id IS NULL ORDER BY url_pattern";
                else
                    cmd.CommandText = "SELECT id, profile_id, url_pattern, is_wildcard, created_at FROM allowed_sites WHERE profile_id IS NULL ORDER BY url_pattern";
                if (profileId.HasValue)
                    cmd.Parameters.AddWithValue("@pid", profileId.Value);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        sites.Add(new AllowedSite
                        {
                            Id = reader.GetInt32(0),
                            ProfileId = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1),
                            UrlPattern = reader.GetString(2),
                            IsWildcard = reader.GetInt32(3) == 1,
                            CreatedAt = reader.GetDateTime(4)
                        });
                    }
                }
            }
            return sites;
        }

        public List<AllowedSite> GetAllAllowedSites()
        {
            var sites = new List<AllowedSite>();
            using (var conn = GetConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id, profile_id, url_pattern, is_wildcard, created_at FROM allowed_sites ORDER BY url_pattern";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        sites.Add(new AllowedSite
                        {
                            Id = reader.GetInt32(0),
                            ProfileId = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1),
                            UrlPattern = reader.GetString(2),
                            IsWildcard = reader.GetInt32(3) == 1,
                            CreatedAt = reader.GetDateTime(4)
                        });
                    }
                }
            }
            return sites;
        }

        public AllowedSite AddAllowedSite(int? profileId, string urlPattern, bool isWildcard)
        {
            using (var conn = GetConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"INSERT INTO allowed_sites (profile_id, url_pattern, is_wildcard) 
                                    VALUES (@p, @u, @w);
                                    SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("@p", (object)profileId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@u", urlPattern);
                cmd.Parameters.AddWithValue("@w", isWildcard ? 1 : 0);
                int id = Convert.ToInt32((long)cmd.ExecuteScalar());
                return new AllowedSite { Id = id, ProfileId = profileId, UrlPattern = urlPattern, IsWildcard = isWildcard };
            }
        }

        public void DeleteAllowedSite(int id)
        {
            using (var conn = GetConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM allowed_sites WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }
        }

        public void AddLogEntry(string entryType, int? userId, string filePath, string details)
        {
            using (var conn = GetConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"INSERT INTO log_entries (entry_type, user_id, file_path, details, timestamp) 
                                    VALUES (@t, @u, @f, @d, @ts)";
                cmd.Parameters.AddWithValue("@t", entryType);
                cmd.Parameters.AddWithValue("@u", (object)userId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@f", (object)filePath ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@d", (object)details ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ts", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.ExecuteNonQuery();
            }
        }

        public List<LogEntry> GetLogEntries(string entryType = null, int? userId = null, DateTime? from = null, DateTime? to = null, int limit = 500)
        {
            var entries = new List<LogEntry>();
            using (var conn = GetConnection())
            using (var cmd = conn.CreateCommand())
            {
                var sql = @"SELECT l.id, l.entry_type, l.user_id, l.file_path, l.details, l.timestamp, u.username
                            FROM log_entries l LEFT JOIN users u ON l.user_id = u.id WHERE 1=1";
                if (!string.IsNullOrEmpty(entryType))
                {
                    sql += " AND l.entry_type = @t";
                    cmd.Parameters.AddWithValue("@t", entryType);
                }
                if (userId.HasValue)
                {
                    sql += " AND l.user_id = @u";
                    cmd.Parameters.AddWithValue("@u", userId.Value);
                }
                if (from.HasValue)
                {
                    sql += " AND l.timestamp >= @f";
                    cmd.Parameters.AddWithValue("@f", from.Value.ToString("yyyy-MM-dd HH:mm:ss"));
                }
                if (to.HasValue)
                {
                    sql += " AND l.timestamp <= @t2";
                    cmd.Parameters.AddWithValue("@t2", to.Value.ToString("yyyy-MM-dd HH:mm:ss"));
                }
                sql += " ORDER BY l.timestamp DESC LIMIT @limit";
                cmd.Parameters.AddWithValue("@limit", limit);
                cmd.CommandText = sql;
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        entries.Add(new LogEntry
                        {
                            Id = reader.GetInt32(0),
                            EntryType = reader.GetString(1),
                            UserId = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2),
                            FilePath = reader.IsDBNull(3) ? null : reader.GetString(3),
                            Details = reader.IsDBNull(4) ? null : reader.GetString(4),
                            Timestamp = DateTime.Parse(reader.GetString(5)),
                            Username = reader.IsDBNull(6) ? null : reader.GetString(6)
                        });
                    }
                }
            }
            return entries;
        }

        public int GetActiveUsersCount()
        {
            using (var conn = GetConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM log_entries WHERE entry_type = 'Login' AND timestamp >= @ts";
                cmd.Parameters.AddWithValue("@ts", DateTime.Now.AddHours(-24).ToString("yyyy-MM-dd HH:mm:ss"));
                return Convert.ToInt32((long)cmd.ExecuteScalar());
            }
        }

        public int GetTotalUsageMinutesToday()
        {
            using (var conn = GetConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"SELECT COUNT(*) FROM log_entries 
                                    WHERE entry_type IN ('Screenshot','Keylog') 
                                    AND timestamp >= @ts";
                cmd.Parameters.AddWithValue("@ts", DateTime.Today.ToString("yyyy-MM-dd HH:mm:ss"));
                return Convert.ToInt32((long)cmd.ExecuteScalar());
            }
        }

        public string GetSetting(string key, string defaultValue = "")
        {
            using (var conn = GetConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT value FROM system_settings WHERE key = @k";
                cmd.Parameters.AddWithValue("@k", key);
                var result = cmd.ExecuteScalar();
                return result != null ? result.ToString() : defaultValue;
            }
        }

        public void SetSetting(string key, string value)
        {
            using (var conn = GetConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"INSERT OR REPLACE INTO system_settings (key, value) VALUES (@k, @v)";
                cmd.Parameters.AddWithValue("@k", key);
                cmd.Parameters.AddWithValue("@v", value ?? "");
                cmd.ExecuteNonQuery();
            }
        }

        public void CleanupOldLogs(int retentionDays)
        {
            using (var conn = GetConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM log_entries WHERE timestamp < @ts";
                cmd.Parameters.AddWithValue("@ts", DateTime.Now.AddDays(-retentionDays).ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.ExecuteNonQuery();
            }
        }
    }
}
