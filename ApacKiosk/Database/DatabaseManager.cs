using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace ApacKiosk.Database
{
    public class DatabaseManager
    {
        private readonly string _connectionString;

        public DatabaseManager(string dbPath)
        {
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            _connectionString = $"Data Source={dbPath}";
        }

        public SqliteConnection GetConnection()
        {
            var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using (var pragmaCmd = conn.CreateCommand())
            {
                pragmaCmd.CommandText = "PRAGMA journal_mode=WAL";
                pragmaCmd.ExecuteNonQuery();
                pragmaCmd.CommandText = "PRAGMA foreign_keys=ON";
                pragmaCmd.ExecuteNonQuery();
            }
            return conn;
        }

        public void Initialize()
        {
            using var conn = GetConnection();
            var cmd = conn.CreateCommand();
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
                    mandatory_pause_minutes INTEGER DEFAULT 0,
                    pause_after_minutes INTEGER DEFAULT 0,
                    homepage_url TEXT DEFAULT 'https://www.google.com',
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
                    is_global INTEGER DEFAULT 0,
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP
                );

                CREATE TABLE IF NOT EXISTS log_entries (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_id INTEGER REFERENCES users(id),
                    type TEXT NOT NULL,
                    file_path TEXT,
                    description TEXT,
                    timestamp DATETIME DEFAULT CURRENT_TIMESTAMP
                );

                CREATE TABLE IF NOT EXISTS session_logs (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_id INTEGER NOT NULL REFERENCES users(id),
                    login_time DATETIME NOT NULL,
                    logout_time DATETIME,
                    duration_seconds INTEGER DEFAULT 0
                );

                CREATE TABLE IF NOT EXISTS system_settings (
                    key TEXT PRIMARY KEY,
                    value TEXT
                );

                CREATE TABLE IF NOT EXISTS cam_devices (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    device_name TEXT NOT NULL,
                    device_moniker TEXT NOT NULL,
                    last_detected DATETIME DEFAULT CURRENT_TIMESTAMP
                );

                CREATE TABLE IF NOT EXISTS allowed_programs (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    profile_id INTEGER REFERENCES access_profiles(id) ON DELETE CASCADE,
                    name TEXT NOT NULL,
                    executable_path TEXT NOT NULL,
                    arguments TEXT,
                    icon_path TEXT,
                    is_global INTEGER DEFAULT 0,
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP
                );

                CREATE INDEX IF NOT EXISTS idx_log_entries_timestamp ON log_entries(timestamp);
                CREATE INDEX IF NOT EXISTS idx_log_entries_type ON log_entries(type);
                CREATE INDEX IF NOT EXISTS idx_log_entries_user ON log_entries(user_id);
                CREATE INDEX IF NOT EXISTS idx_session_logs_user ON session_logs(user_id);
                CREATE INDEX IF NOT EXISTS idx_time_rules_profile ON time_rules(profile_id);
                CREATE INDEX IF NOT EXISTS idx_allowed_sites_profile ON allowed_sites(profile_id);
                CREATE INDEX IF NOT EXISTS idx_allowed_programs_profile ON allowed_programs(profile_id);
            ";
            cmd.ExecuteNonQuery();

            SeedDefaultAdmin(conn);
            SeedDefaultSettings(conn);
        }

        private void SeedDefaultAdmin(SqliteConnection conn)
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM admins";
            var count = (long)cmd.ExecuteScalar();
            if (count == 0)
            {
                cmd.CommandText = "INSERT INTO admins (username, password_hash) VALUES (@u, @p)";
                cmd.Parameters.AddWithValue("@u", "admin");
                cmd.Parameters.AddWithValue("@p", BCrypt.Net.BCrypt.HashPassword("APAC@Admin2024"));
                cmd.ExecuteNonQuery();
            }
        }

        private void SeedDefaultSettings(SqliteConnection conn)
        {
            var defaults = new Dictionary<string, string>
            {
                ["display_name"] = "APAC - Acesso Controlado",
                ["welcome_message"] = "Bem-vindo ao APAC",
                ["logo_path"] = "",
                ["screenshot_enabled"] = "true",
                ["screenshot_interval_sec"] = "60",
                ["screenshot_quality"] = "Medium",
                ["screenshot_path"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "APAC", "Screenshots"),
                ["camera_enabled"] = "false",
                ["camera_interval_sec"] = "120",
                ["camera_quality"] = "Medium",
                ["camera_path"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "APAC", "Camera"),
                ["keylogger_enabled"] = "true",
                ["keylogger_path"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "APAC", "Keylogs"),
                ["keylogger_file_mode"] = "daily",
                ["log_retention_days"] = "30",
                ["log_max_size_gb"] = "5",
                ["autostart_enabled"] = "true",
                ["emergency_disable_minutes"] = "0"
            };

            foreach (var kvp in defaults)
            {
                var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT OR IGNORE INTO system_settings (key, value) VALUES (@k, @v)";
                cmd.Parameters.AddWithValue("@k", kvp.Key);
                cmd.Parameters.AddWithValue("@v", kvp.Value);
                cmd.ExecuteNonQuery();
            }
        }

        public string GetSetting(string key, string defaultValue = "")
        {
            using var conn = GetConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM system_settings WHERE key = @k";
            cmd.Parameters.AddWithValue("@k", key);
            var result = cmd.ExecuteScalar();
            return result?.ToString() ?? defaultValue;
        }

        public void SetSetting(string key, string value)
        {
            using var conn = GetConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO system_settings (key, value) VALUES (@k, @v)";
            cmd.Parameters.AddWithValue("@k", key);
            cmd.Parameters.AddWithValue("@v", value);
            cmd.ExecuteNonQuery();
        }

        public void InsertLog(int? userId, string type, string filePath, string description)
        {
            using var conn = GetConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO log_entries (user_id, type, file_path, description, timestamp) VALUES (@u, @t, @f, @d, @ts)";
            cmd.Parameters.AddWithValue("@u", userId.HasValue ? (object)userId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@t", type);
            cmd.Parameters.AddWithValue("@f", (object)filePath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@d", (object)description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ts", DateTime.Now);
            cmd.ExecuteNonQuery();
        }

        public void CleanupOldLogs()
        {
            var retentionDays = int.Parse(GetSetting("log_retention_days", "30"));
            using var conn = GetConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM log_entries WHERE timestamp < @cutoff";
            cmd.Parameters.AddWithValue("@cutoff", DateTime.Now.AddDays(-retentionDays));
            cmd.ExecuteNonQuery();
        }

        public long GetLogStorageSize()
        {
            var ssPath = GetSetting("screenshot_path");
            var camPath = GetSetting("camera_path");
            var keyPath = GetSetting("keylogger_path");
            long total = 0;
            total += GetDirectorySize(ssPath);
            total += GetDirectorySize(camPath);
            total += GetDirectorySize(keyPath);
            return total;
        }

        private long GetDirectorySize(string path)
        {
            if (!Directory.Exists(path)) return 0;
            long size = 0;
            foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { size += new FileInfo(f).Length; } catch { }
            }
            return size;
        }
    }
}
