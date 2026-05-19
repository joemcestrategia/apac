using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using Apac.Models;

namespace Apac.Database
{
    public class DatabaseService
    {
        private static DatabaseService _instance;
        private static readonly object _lock = new object();
        private readonly string _connectionString;

        public static DatabaseService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                            _instance = new DatabaseService();
                    }
                }
                return _instance;
            }
        }

        private DatabaseService()
        {
            string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "apac.db");
            _connectionString = $"Data Source={dbPath};Version=3;";
        }

        public void Initialize()
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS admins (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            username TEXT NOT NULL,
                            password_hash TEXT NOT NULL,
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
                        CREATE TABLE IF NOT EXISTS access_profiles (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            name TEXT NOT NULL,
                            max_session_minutes INTEGER DEFAULT 0,
                            max_daily_minutes INTEGER DEFAULT 0,
                            mandatory_pause_after_minutes INTEGER DEFAULT 0,
                            mandatory_pause_minutes INTEGER DEFAULT 0,
                            created_at DATETIME DEFAULT CURRENT_TIMESTAMP
                        );
                        CREATE TABLE IF NOT EXISTS allowed_sites (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            profile_id INTEGER REFERENCES access_profiles(id),
                            url TEXT NOT NULL,
                            notes TEXT,
                            created_at DATETIME DEFAULT CURRENT_TIMESTAMP
                        );
                        CREATE TABLE IF NOT EXISTS time_rules (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            profile_id INTEGER REFERENCES access_profiles(id),
                            day_of_week INTEGER,
                            start_time TEXT NOT NULL,
                            end_time TEXT NOT NULL,
                            created_at DATETIME DEFAULT CURRENT_TIMESTAMP
                        );
                        CREATE TABLE IF NOT EXISTS monitoring_config (
                            id INTEGER PRIMARY KEY DEFAULT 1,
                            screenshot_enabled INTEGER DEFAULT 1,
                            screenshot_interval_seconds INTEGER DEFAULT 60,
                            screenshot_quality TEXT DEFAULT 'Media',
                            screenshot_folder TEXT,
                            camera_enabled INTEGER DEFAULT 0,
                            camera_device TEXT,
                            camera_interval_seconds INTEGER DEFAULT 120,
                            camera_quality TEXT DEFAULT 'Media',
                            camera_folder TEXT,
                            keylogger_enabled INTEGER DEFAULT 1,
                            keylogger_folder TEXT,
                            keylogger_mode TEXT DEFAULT 'per_day',
                            retention_days INTEGER DEFAULT 30,
                            max_log_size_gb REAL DEFAULT 10.0
                        );
                        CREATE TABLE IF NOT EXISTS system_config (
                            id INTEGER PRIMARY KEY DEFAULT 1,
                            display_name TEXT DEFAULT 'APAC',
                            logo_path TEXT,
                            welcome_message TEXT DEFAULT 'Bem-vindo!',
                            autostart_enabled INTEGER DEFAULT 1,
                            kiosk_emergency_minutes INTEGER DEFAULT 0,
                            default_url TEXT DEFAULT 'https://www.google.com'
                        );
                        CREATE TABLE IF NOT EXISTS log_entries (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            user_id INTEGER REFERENCES users(id),
                            type TEXT NOT NULL,
                            file_path TEXT,
                            content TEXT,
                            details TEXT,
                            created_at DATETIME DEFAULT CURRENT_TIMESTAMP
                        );
                        CREATE TABLE IF NOT EXISTS active_sessions (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            user_id INTEGER REFERENCES users(id),
                            login_time DATETIME DEFAULT CURRENT_TIMESTAMP,
                            logout_time DATETIME,
                            is_active INTEGER DEFAULT 1
                        );
                    ";
                    cmd.ExecuteNonQuery();
                }
                InsertDefaultConfigs(conn);
            }
        }

        private void InsertDefaultConfigs(SQLiteConnection conn)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT OR IGNORE INTO monitoring_config (id) VALUES (1);
                    INSERT OR IGNORE INTO system_config (id) VALUES (1);
                ";
                cmd.ExecuteNonQuery();
            }
        }

        public bool HasAdmin()
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM admins";
                    return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
                }
            }
        }

        public void CreateAdmin(string username, string passwordHash)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "INSERT INTO admins (username, password_hash) VALUES (@u, @p)";
                    cmd.Parameters.AddWithValue("@u", username);
                    cmd.Parameters.AddWithValue("@p", passwordHash);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public string GetAdminPasswordHash(string username)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT password_hash FROM admins WHERE username = @u";
                    cmd.Parameters.AddWithValue("@u", username);
                    var result = cmd.ExecuteScalar();
                    return result?.ToString();
                }
            }
        }

        public void UpdateAdminPassword(string passwordHash)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "UPDATE admins SET password_hash = @p WHERE id = 1";
                    cmd.Parameters.AddWithValue("@p", passwordHash);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public User AuthenticateUser(string username, string pinHash)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT u.*, ap.name as profile_name
                        FROM users u
                        LEFT JOIN access_profiles ap ON u.profile_id = ap.id
                        WHERE u.username = @u AND u.pin_hash = @p AND u.is_active = 1";
                    cmd.Parameters.AddWithValue("@u", username);
                    cmd.Parameters.AddWithValue("@p", pinHash);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                            return ReadUser(reader);
                    }
                }
            }
            return null;
        }

        public User GetUserById(int id)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT u.*, ap.name as profile_name
                        FROM users u
                        LEFT JOIN access_profiles ap ON u.profile_id = ap.id
                        WHERE u.id = @id";
                    cmd.Parameters.AddWithValue("@id", id);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                            return ReadUser(reader);
                    }
                }
            }
            return null;
        }

        public List<User> GetAllUsers()
        {
            var users = new List<User>();
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT u.*, ap.name as profile_name
                        FROM users u
                        LEFT JOIN access_profiles ap ON u.profile_id = ap.id
                        ORDER BY u.full_name";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                            users.Add(ReadUser(reader));
                    }
                }
            }
            return users;
        }

        public void InsertUser(User user)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        INSERT INTO users (full_name, username, pin_hash, photo_path, profile_id, is_active)
                        VALUES (@f, @u, @p, @ph, @pr, @ia)";
                    cmd.Parameters.AddWithValue("@f", user.FullName);
                    cmd.Parameters.AddWithValue("@u", user.Username);
                    cmd.Parameters.AddWithValue("@p", user.PinHash);
                    cmd.Parameters.AddWithValue("@ph", (object)user.PhotoPath ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@pr", (object)user.ProfileId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ia", user.IsActive ? 1 : 0);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void UpdateUser(User user)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        UPDATE users SET full_name=@f, username=@u, pin_hash=@p,
                            photo_path=@ph, profile_id=@pr, is_active=@ia
                        WHERE id=@id";
                    cmd.Parameters.AddWithValue("@f", user.FullName);
                    cmd.Parameters.AddWithValue("@u", user.Username);
                    cmd.Parameters.AddWithValue("@p", user.PinHash);
                    cmd.Parameters.AddWithValue("@ph", (object)user.PhotoPath ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@pr", (object)user.ProfileId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ia", user.IsActive ? 1 : 0);
                    cmd.Parameters.AddWithValue("@id", user.Id);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void DeleteUser(int id)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM users WHERE id = @id";
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public List<AccessProfile> GetAllProfiles()
        {
            var profiles = new List<AccessProfile>();
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM access_profiles ORDER BY name";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            profiles.Add(new AccessProfile
                            {
                                Id = Convert.ToInt32(reader["id"]),
                                Name = reader["name"].ToString(),
                                MaxSessionMinutes = Convert.ToInt32(reader["max_session_minutes"]),
                                MaxDailyMinutes = Convert.ToInt32(reader["max_daily_minutes"]),
                                MandatoryPauseAfterMinutes = Convert.ToInt32(reader["mandatory_pause_after_minutes"]),
                                MandatoryPauseMinutes = Convert.ToInt32(reader["mandatory_pause_minutes"]),
                                CreatedAt = Convert.ToDateTime(reader["created_at"])
                            });
                        }
                    }
                }
            }
            return profiles;
        }

        public AccessProfile GetProfileById(int id)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM access_profiles WHERE id = @id";
                    cmd.Parameters.AddWithValue("@id", id);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new AccessProfile
                            {
                                Id = Convert.ToInt32(reader["id"]),
                                Name = reader["name"].ToString(),
                                MaxSessionMinutes = Convert.ToInt32(reader["max_session_minutes"]),
                                MaxDailyMinutes = Convert.ToInt32(reader["max_daily_minutes"]),
                                MandatoryPauseAfterMinutes = Convert.ToInt32(reader["mandatory_pause_after_minutes"]),
                                MandatoryPauseMinutes = Convert.ToInt32(reader["mandatory_pause_minutes"]),
                                CreatedAt = Convert.ToDateTime(reader["created_at"])
                            };
                        }
                    }
                }
            }
            return null;
        }

        public void InsertProfile(AccessProfile profile)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        INSERT INTO access_profiles (name, max_session_minutes, max_daily_minutes, mandatory_pause_after_minutes, mandatory_pause_minutes)
                        VALUES (@n, @ms, @md, @mpa, @mp)";
                    cmd.Parameters.AddWithValue("@n", profile.Name);
                    cmd.Parameters.AddWithValue("@ms", profile.MaxSessionMinutes);
                    cmd.Parameters.AddWithValue("@md", profile.MaxDailyMinutes);
                    cmd.Parameters.AddWithValue("@mpa", profile.MandatoryPauseAfterMinutes);
                    cmd.Parameters.AddWithValue("@mp", profile.MandatoryPauseMinutes);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void UpdateProfile(AccessProfile profile)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        UPDATE access_profiles SET name=@n, max_session_minutes=@ms, max_daily_minutes=@md,
                            mandatory_pause_after_minutes=@mpa, mandatory_pause_minutes=@mp
                        WHERE id=@id";
                    cmd.Parameters.AddWithValue("@n", profile.Name);
                    cmd.Parameters.AddWithValue("@ms", profile.MaxSessionMinutes);
                    cmd.Parameters.AddWithValue("@md", profile.MaxDailyMinutes);
                    cmd.Parameters.AddWithValue("@mpa", profile.MandatoryPauseAfterMinutes);
                    cmd.Parameters.AddWithValue("@mp", profile.MandatoryPauseMinutes);
                    cmd.Parameters.AddWithValue("@id", profile.Id);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void DeleteProfile(int id)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM access_profiles WHERE id = @id";
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public List<AllowedSite> GetAllSites(int? profileId = null)
        {
            var sites = new List<AllowedSite>();
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    if (profileId.HasValue)
                        cmd.CommandText = "SELECT * FROM allowed_sites WHERE profile_id = @pid OR profile_id IS NULL ORDER BY url";
                    else
                        cmd.CommandText = "SELECT * FROM allowed_sites WHERE profile_id IS NULL ORDER BY url";
                    cmd.Parameters.AddWithValue("@pid", profileId.Value);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            sites.Add(new AllowedSite
                            {
                                Id = Convert.ToInt32(reader["id"]),
                                ProfileId = reader["profile_id"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["profile_id"]),
                                Url = reader["url"].ToString(),
                                Notes = reader["notes"]?.ToString(),
                                CreatedAt = Convert.ToDateTime(reader["created_at"])
                            });
                        }
                    }
                }
            }
            return sites;
        }

        public List<AllowedSite> GetSitesForProfile(int profileId)
        {
            var sites = GetAllSites(null);
            sites.AddRange(GetAllSites(profileId));
            return sites;
        }

        public void InsertSite(AllowedSite site)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "INSERT INTO allowed_sites (profile_id, url, notes) VALUES (@pid, @u, @n)";
                    cmd.Parameters.AddWithValue("@pid", (object)site.ProfileId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@u", site.Url);
                    cmd.Parameters.AddWithValue("@n", (object)site.Notes ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void DeleteSite(int id)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM allowed_sites WHERE id = @id";
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void DeleteSitesForProfile(int profileId)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM allowed_sites WHERE profile_id = @pid";
                    cmd.Parameters.AddWithValue("@pid", profileId);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public List<TimeRule> GetTimeRulesForProfile(int profileId)
        {
            var rules = new List<TimeRule>();
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM time_rules WHERE profile_id = @pid ORDER BY day_of_week, start_time";
                    cmd.Parameters.AddWithValue("@pid", profileId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            rules.Add(new TimeRule
                            {
                                Id = Convert.ToInt32(reader["id"]),
                                ProfileId = Convert.ToInt32(reader["profile_id"]),
                                DayOfWeek = reader["day_of_week"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["day_of_week"]),
                                StartTime = reader["start_time"].ToString(),
                                EndTime = reader["end_time"].ToString(),
                                CreatedAt = Convert.ToDateTime(reader["created_at"])
                            });
                        }
                    }
                }
            }
            return rules;
        }

        public void InsertTimeRule(TimeRule rule)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "INSERT INTO time_rules (profile_id, day_of_week, start_time, end_time) VALUES (@pid, @d, @s, @e)";
                    cmd.Parameters.AddWithValue("@pid", rule.ProfileId);
                    cmd.Parameters.AddWithValue("@d", (object)rule.DayOfWeek ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@s", rule.StartTime);
                    cmd.Parameters.AddWithValue("@e", rule.EndTime);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void DeleteTimeRule(int id)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM time_rules WHERE id = @id";
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void DeleteTimeRulesForProfile(int profileId)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM time_rules WHERE profile_id = @pid";
                    cmd.Parameters.AddWithValue("@pid", profileId);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public MonitoringConfig GetMonitoringConfig()
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM monitoring_config WHERE id = 1";
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new MonitoringConfig
                            {
                                Id = Convert.ToInt32(reader["id"]),
                                ScreenshotEnabled = Convert.ToInt32(reader["screenshot_enabled"]) == 1,
                                ScreenshotIntervalSeconds = Convert.ToInt32(reader["screenshot_interval_seconds"]),
                                ScreenshotQuality = reader["screenshot_quality"].ToString(),
                                ScreenshotFolder = reader["screenshot_folder"]?.ToString(),
                                CameraEnabled = Convert.ToInt32(reader["camera_enabled"]) == 1,
                                CameraDevice = reader["camera_device"]?.ToString(),
                                CameraIntervalSeconds = Convert.ToInt32(reader["camera_interval_seconds"]),
                                CameraQuality = reader["camera_quality"].ToString(),
                                CameraFolder = reader["camera_folder"]?.ToString(),
                                KeyloggerEnabled = Convert.ToInt32(reader["keylogger_enabled"]) == 1,
                                KeyloggerFolder = reader["keylogger_folder"]?.ToString(),
                                KeyloggerMode = reader["keylogger_mode"].ToString(),
                                RetentionDays = Convert.ToInt32(reader["retention_days"]),
                                MaxLogSizeGb = Convert.ToDouble(reader["max_log_size_gb"])
                            };
                        }
                    }
                }
            }
            return new MonitoringConfig
            {
                ScreenshotEnabled = true,
                ScreenshotIntervalSeconds = 60,
                ScreenshotQuality = "Media",
                CameraIntervalSeconds = 120,
                CameraQuality = "Media",
                KeyloggerEnabled = true,
                KeyloggerMode = "per_day",
                RetentionDays = 30,
                MaxLogSizeGb = 10.0
            };
        }

        public void SaveMonitoringConfig(MonitoringConfig config)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        INSERT OR REPLACE INTO monitoring_config
                            (id, screenshot_enabled, screenshot_interval_seconds, screenshot_quality, screenshot_folder,
                             camera_enabled, camera_device, camera_interval_seconds, camera_quality, camera_folder,
                             keylogger_enabled, keylogger_folder, keylogger_mode, retention_days, max_log_size_gb)
                        VALUES (1, @se, @si, @sq, @sf, @ce, @cd, @ci, @cq, @cf, @ke, @kf, @km, @rd, @ml)";
                    cmd.Parameters.AddWithValue("@se", config.ScreenshotEnabled ? 1 : 0);
                    cmd.Parameters.AddWithValue("@si", config.ScreenshotIntervalSeconds);
                    cmd.Parameters.AddWithValue("@sq", config.ScreenshotQuality);
                    cmd.Parameters.AddWithValue("@sf", (object)config.ScreenshotFolder ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ce", config.CameraEnabled ? 1 : 0);
                    cmd.Parameters.AddWithValue("@cd", (object)config.CameraDevice ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ci", config.CameraIntervalSeconds);
                    cmd.Parameters.AddWithValue("@cq", config.CameraQuality);
                    cmd.Parameters.AddWithValue("@cf", (object)config.CameraFolder ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ke", config.KeyloggerEnabled ? 1 : 0);
                    cmd.Parameters.AddWithValue("@kf", (object)config.KeyloggerFolder ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@km", config.KeyloggerMode);
                    cmd.Parameters.AddWithValue("@rd", config.RetentionDays);
                    cmd.Parameters.AddWithValue("@ml", config.MaxLogSizeGb);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public SystemConfig GetSystemConfig()
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM system_config WHERE id = 1";
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new SystemConfig
                            {
                                Id = Convert.ToInt32(reader["id"]),
                                DisplayName = reader["display_name"].ToString(),
                                LogoPath = reader["logo_path"]?.ToString(),
                                WelcomeMessage = reader["welcome_message"].ToString(),
                                AutostartEnabled = Convert.ToInt32(reader["autostart_enabled"]) == 1,
                                KioskEmergencyMinutes = Convert.ToInt32(reader["kiosk_emergency_minutes"]),
                                DefaultUrl = reader["default_url"].ToString()
                            };
                        }
                    }
                }
            }
            return new SystemConfig
            {
                DisplayName = "APAC",
                WelcomeMessage = "Bem-vindo!",
                AutostartEnabled = true,
                DefaultUrl = "https://www.google.com"
            };
        }

        public void SaveSystemConfig(SystemConfig config)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        INSERT OR REPLACE INTO system_config
                            (id, display_name, logo_path, welcome_message, autostart_enabled, kiosk_emergency_minutes, default_url)
                        VALUES (1, @dn, @lp, @wm, @ae, @em, @du)";
                    cmd.Parameters.AddWithValue("@dn", config.DisplayName);
                    cmd.Parameters.AddWithValue("@lp", (object)config.LogoPath ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@wm", config.WelcomeMessage);
                    cmd.Parameters.AddWithValue("@ae", config.AutostartEnabled ? 1 : 0);
                    cmd.Parameters.AddWithValue("@em", config.KioskEmergencyMinutes);
                    cmd.Parameters.AddWithValue("@du", config.DefaultUrl);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void InsertLogEntry(LogEntry entry)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        INSERT INTO log_entries (user_id, type, file_path, content, details)
                        VALUES (@uid, @t, @fp, @c, @d)";
                    cmd.Parameters.AddWithValue("@uid", (object)entry.UserId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@t", entry.Type);
                    cmd.Parameters.AddWithValue("@fp", (object)entry.FilePath ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@c", (object)entry.Content ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@d", (object)entry.Details ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public List<LogEntry> GetLogEntries(string type = null, int? userId = null, DateTime? startDate = null, DateTime? endDate = null, int limit = 500)
        {
            var entries = new List<LogEntry>();
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    var where = new List<string>();
                    if (!string.IsNullOrEmpty(type)) where.Add("le.type = @type");
                    if (userId.HasValue) where.Add("le.user_id = @uid");
                    if (startDate.HasValue) where.Add("le.created_at >= @start");
                    if (endDate.HasValue) where.Add("le.created_at <= @end");

                    string sql = @"
                        SELECT le.*, u.username
                        FROM log_entries le
                        LEFT JOIN users u ON le.user_id = u.id";
                    if (where.Count > 0) sql += " WHERE " + string.Join(" AND ", where);
                    sql += " ORDER BY le.created_at DESC LIMIT @limit";

                    cmd.CommandText = sql;
                    if (!string.IsNullOrEmpty(type)) cmd.Parameters.AddWithValue("@type", type);
                    if (userId.HasValue) cmd.Parameters.AddWithValue("@uid", userId.Value);
                    if (startDate.HasValue) cmd.Parameters.AddWithValue("@start", startDate.Value);
                    if (endDate.HasValue) cmd.Parameters.AddWithValue("@end", endDate.Value);
                    cmd.Parameters.AddWithValue("@limit", limit);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            entries.Add(ReadLogEntry(reader));
                        }
                    }
                }
            }
            return entries;
        }

        public ActiveSession StartSession(int userId)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "INSERT INTO active_sessions (user_id, is_active) VALUES (@uid, 1); SELECT last_insert_rowid();";
                    cmd.Parameters.AddWithValue("@uid", userId);
                    var id = Convert.ToInt32(cmd.ExecuteScalar());
                    return new ActiveSession { Id = id, UserId = userId, LoginTime = DateTime.Now, IsActive = true };
                }
            }
        }

        public void EndSession(int sessionId)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "UPDATE active_sessions SET logout_time = @lt, is_active = 0 WHERE id = @id";
                    cmd.Parameters.AddWithValue("@lt", DateTime.Now);
                    cmd.Parameters.AddWithValue("@id", sessionId);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public int GetActiveUserCount()
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(DISTINCT user_id) FROM active_sessions WHERE is_active = 1";
                    return Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
        }

        public double GetTotalUsageMinutesToday()
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT COALESCE(SUM(
                            CASE WHEN logout_time IS NOT NULL
                            THEN (julianday(logout_time) - julianday(login_time)) * 24 * 60
                            ELSE (julianday('now') - julianday(login_time)) * 24 * 60
                            END), 0)
                        FROM active_sessions
                        WHERE date(login_time) = date('now')";
                    return Convert.ToDouble(cmd.ExecuteScalar());
                }
            }
        }

        public int GetDailyUsageMinutes(int userId)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT COALESCE(SUM(
                            CASE WHEN logout_time IS NOT NULL
                            THEN (julianday(logout_time) - julianday(login_time)) * 24 * 60
                            ELSE (julianday('now') - julianday(login_time)) * 24 * 60
                            END), 0)
                        FROM active_sessions
                        WHERE user_id = @uid AND date(login_time) = date('now')";
                    cmd.Parameters.AddWithValue("@uid", userId);
                    return Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
        }

        public void PurgeOldLogs(int retentionDays)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM log_entries WHERE created_at < date('now', @days)";
                    cmd.Parameters.AddWithValue("@days", "-" + retentionDays + " days");
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public long GetLogFolderSize(string basePath)
        {
            if (!Directory.Exists(basePath)) return 0;
            long size = 0;
            foreach (var file in Directory.GetFiles(basePath, "*.*", SearchOption.AllDirectories))
            {
                try { size += new FileInfo(file).Length; } catch { }
            }
            return size;
        }

        private User ReadUser(SQLiteDataReader reader)
        {
            return new User
            {
                Id = Convert.ToInt32(reader["id"]),
                FullName = reader["full_name"].ToString(),
                Username = reader["username"].ToString(),
                PinHash = reader["pin_hash"].ToString(),
                PhotoPath = reader["photo_path"]?.ToString(),
                ProfileId = reader["profile_id"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["profile_id"]),
                IsActive = Convert.ToInt32(reader["is_active"]) == 1,
                CreatedAt = Convert.ToDateTime(reader["created_at"]),
                ProfileName = reader["profile_name"]?.ToString()
            };
        }

        private LogEntry ReadLogEntry(SQLiteDataReader reader)
        {
            return new LogEntry
            {
                Id = Convert.ToInt32(reader["id"]),
                UserId = reader["user_id"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["user_id"]),
                Type = reader["type"].ToString(),
                FilePath = reader["file_path"]?.ToString(),
                Content = reader["content"]?.ToString(),
                Details = reader["details"]?.ToString(),
                CreatedAt = Convert.ToDateTime(reader["created_at"]),
                Username = reader["username"]?.ToString()
            };
        }
    }
}
