using Microsoft.Data.Sqlite;
using System;
using System.IO;

namespace ApacKiosk.Database;

public static class DatabaseService
{
    private static readonly Lazy<DatabaseService> _instance = new(() => new DatabaseService());
    public static DatabaseService Instance => _instance.Value;

    private readonly string _connectionString;

    private DatabaseService()
    {
        var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "apac.db");
        _connectionString = $"Data Source={dbPath}";
    }

    public string ConnectionString => _connectionString;

    public SqliteConnection CreateConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    public void Initialize()
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS admins (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                username TEXT NOT NULL UNIQUE,
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
                homepage_url TEXT,
                created_at DATETIME DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS time_rules (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                profile_id INTEGER REFERENCES access_profiles(id) ON DELETE CASCADE,
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
                user_id INTEGER REFERENCES users(id),
                login_time DATETIME DEFAULT CURRENT_TIMESTAMP,
                logout_time DATETIME,
                duration_seconds INTEGER DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS system_settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS allowed_extensions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                extension TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_log_entries_type ON log_entries(type);
            CREATE INDEX IF NOT EXISTS idx_log_entries_timestamp ON log_entries(timestamp);
            CREATE INDEX IF NOT EXISTS idx_log_entries_user_id ON log_entries(user_id);
            CREATE INDEX IF NOT EXISTS idx_allowed_sites_profile_id ON allowed_sites(profile_id);
            CREATE INDEX IF NOT EXISTS idx_time_rules_profile_id ON time_rules(profile_id);
            CREATE INDEX IF NOT EXISTS idx_session_logs_user_id ON session_logs(user_id);
        ";
        cmd.ExecuteNonQuery();

        SeedAdmin();
    }

    private void SeedAdmin()
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM admins";
        var count = (long)cmd.ExecuteScalar()!;

        if (count == 0)
        {
            var hash = BCrypt.Net.BCrypt.HashPassword("APAC@Admin2024");
            cmd.CommandText = "INSERT INTO admins (username, password_hash) VALUES (@u, @h)";
            cmd.Parameters.AddWithValue("@u", "admin");
            cmd.Parameters.AddWithValue("@h", hash);
            cmd.ExecuteNonQuery();
        }
    }

    public void Shutdown()
    {
        SqliteConnection.ClearAllPools();
    }
}
