using Microsoft.Data.Sqlite;
using Dapper;
using BCrypt.Net;
using ApacKiosk.Models;

namespace ApacKiosk.Data;

public static class DatabaseHelper
{
    public const string DefaultAdminPassword = "APAC@Admin2024";
    private static string _connectionString = "Data Source=apac_kiosk.db";

    public static string ConnectionString
    {
        get => _connectionString;
        set => _connectionString = value;
    }

    public static void Initialize()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        conn.Execute(@"
            CREATE TABLE IF NOT EXISTS admins (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                username TEXT NOT NULL,
                password_hash TEXT NOT NULL,
                created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                must_change_password INTEGER DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS access_profiles (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                max_session_minutes INTEGER DEFAULT 0,
                mandatory_break_after_minutes INTEGER DEFAULT 0,
                mandatory_break_duration_minutes INTEGER DEFAULT 0,
                created_at DATETIME DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS profile_time_slots (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                profile_id INTEGER NOT NULL REFERENCES access_profiles(id) ON DELETE CASCADE,
                day_of_week INTEGER NOT NULL,
                start_time TEXT NOT NULL,
                end_time TEXT NOT NULL
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

            CREATE TABLE IF NOT EXISTS allowed_sites (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                profile_id INTEGER REFERENCES access_profiles(id) ON DELETE CASCADE,
                url_pattern TEXT NOT NULL,
                created_at DATETIME DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS log_entries (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                entry_type TEXT NOT NULL,
                user_id INTEGER REFERENCES users(id),
                file_path TEXT,
                description TEXT,
                timestamp DATETIME DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS monitoring_config (
                module TEXT NOT NULL,
                key TEXT NOT NULL,
                value TEXT NOT NULL,
                PRIMARY KEY (module, key)
            );

            CREATE TABLE IF NOT EXISTS system_config (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS emergency_overrides (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                start_time DATETIME NOT NULL,
                end_time DATETIME NOT NULL,
                justification TEXT NOT NULL,
                admin_username TEXT NOT NULL
            );
        ");

        EnsureDefaultAdmin(conn);
        EnsureDefaultConfig(conn);
    }

    private static void EnsureDefaultAdmin(SqliteConnection conn)
    {
        var count = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM admins");
        if (count == 0)
        {
            var hash = BCrypt.Net.BCrypt.HashPassword(DefaultAdminPassword);
            conn.Execute(
                "INSERT INTO admins (username, password_hash, must_change_password) VALUES (@u, @h, 1)",
                new { u = "admin", h = hash });
        }
    }

    private static void EnsureDefaultConfig(SqliteConnection conn)
    {
        var defaults = new[]
        {
            ("login_title", "APAC - Acesso Digital"),
            ("welcome_message", "Bem-vindo ao sistema APAC"),
            ("logo_path", ""),
            ("default_homepage", "https://www.google.com"),
            ("log_retention_days", "30"),
            ("max_log_size_gb", "10")
        };

        foreach (var (key, value) in defaults)
        {
            conn.Execute(
                "INSERT OR IGNORE INTO system_config (key, value) VALUES (@k, @v)",
                new { k = key, v = value });
        }

        var monitorDefaults = new[]
        {
            ("screenshots", "enabled", "true"),
            ("screenshots", "interval_seconds", "60"),
            ("screenshots", "quality", "medium"),
            ("screenshots", "folder_path", "Logs\\Screenshots"),
            ("camera", "enabled", "false"),
            ("camera", "interval_seconds", "120"),
            ("camera", "quality", "medium"),
            ("camera", "device_name", ""),
            ("camera", "folder_path", "Logs\\Camera"),
            ("keylogger", "enabled", "false"),
            ("keylogger", "folder_path", "Logs\\Keylogs"),
            ("keylogger", "file_mode", "per_day")
        };

        foreach (var (module, key, value) in monitorDefaults)
        {
            conn.Execute(
                "INSERT OR IGNORE INTO monitoring_config (module, key, value) VALUES (@m, @k, @v)",
                new { m = module, k = key, v = value });
        }
    }

    public static SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        conn.Execute("PRAGMA foreign_keys = ON");
        return conn;
    }

    public static void InsertLog(string entryType, int? userId, string? filePath, string? description)
    {
        using var conn = OpenConnection();
        conn.Execute(
            "INSERT INTO log_entries (entry_type, user_id, file_path, description, timestamp) VALUES (@t, @u, @f, @d, @ts)",
            new { t = entryType, u = userId, f = filePath, d = description, ts = DateTime.Now });
    }

    public static string GetConfig(string key, string defaultValue = "")
    {
        using var conn = OpenConnection();
        return conn.ExecuteScalar<string>(
            "SELECT value FROM system_config WHERE key = @k", new { k = key }) ?? defaultValue;
    }

    public static void SetConfig(string key, string value)
    {
        using var conn = OpenConnection();
        conn.Execute(
            "INSERT OR REPLACE INTO system_config (key, value) VALUES (@k, @v)",
            new { k = key, v = value });
    }

    public static string GetMonitoringConfig(string module, string key, string defaultValue = "")
    {
        using var conn = OpenConnection();
        return conn.ExecuteScalar<string>(
            "SELECT value FROM monitoring_config WHERE module = @m AND key = @k",
            new { m = module, k = key }) ?? defaultValue;
    }

    public static void SetMonitoringConfig(string module, string key, string value)
    {
        using var conn = OpenConnection();
        conn.Execute(
            "INSERT OR REPLACE INTO monitoring_config (module, key, value) VALUES (@m, @k, @v)",
            new { m = module, k = key, v = value });
    }

    public static List<T> Query<T>(string sql, object? param = null)
    {
        using var conn = OpenConnection();
        return conn.Query<T>(sql, param).AsList();
    }

    public static T? QueryFirstOrDefault<T>(string sql, object? param = null)
    {
        using var conn = OpenConnection();
        return conn.QueryFirstOrDefault<T>(sql, param);
    }

    public static int Execute(string sql, object? param = null)
    {
        using var conn = OpenConnection();
        return conn.Execute(sql, param);
    }

    public static T ExecuteScalar<T>(string sql, object? param = null)
    {
        using var conn = OpenConnection();
        return conn.ExecuteScalar<T>(sql, param);
    }
}
