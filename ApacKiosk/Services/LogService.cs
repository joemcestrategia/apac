using ApacKiosk.Database;
using ApacKiosk.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ApacKiosk.Services;

public class LogService
{
    private readonly DatabaseManager _db;

    public LogService(DatabaseManager db) => _db = db;

    public void Log(int? userId, string type, string? filePath, string? description)
    {
        _db.InsertLog(userId, type, filePath, description);
    }

    public void LogEvent(int? userId, string description)
    {
        Log(userId, "event", null, description);
    }

    public int StartSession(int userId)
    {
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO session_logs (user_id, login_time) VALUES (@u, @t);
                           SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@u", userId);
        cmd.Parameters.AddWithValue("@t", DateTime.Now);
        var id = Convert.ToInt32(cmd.ExecuteScalar()!);
        LogEvent(userId, "Login");
        return id;
    }

    public void EndSession(long sessionId, int userId)
    {
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"UPDATE session_logs SET logout_time = @t, duration_seconds = @d WHERE id = @id";
        var now = DateTime.Now;
        cmd.Parameters.AddWithValue("@t", now);
        cmd.Parameters.AddWithValue("@id", sessionId);

        var loginCmd = conn.CreateCommand();
        loginCmd.CommandText = "SELECT login_time FROM session_logs WHERE id = @id";
        loginCmd.Parameters.AddWithValue("@id", sessionId);
        var loginTime = Convert.ToDateTime(loginCmd.ExecuteScalar()!);
        cmd.Parameters.AddWithValue("@d", (int)(now - loginTime).TotalSeconds);
        cmd.ExecuteNonQuery();
        LogEvent(userId, "Logout");
    }

    public List<LogEntry> Query(int? userId, string? type, DateTime? from, DateTime? to, int limit = 500)
    {
        var list = new List<LogEntry>();
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        var conditions = new List<string>();
        if (userId.HasValue) conditions.Add("user_id = @uid");
        if (!string.IsNullOrEmpty(type)) conditions.Add("type = @type");
        if (from.HasValue) conditions.Add("timestamp >= @from");
        if (to.HasValue) conditions.Add("timestamp <= @to");

        var where = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "";
        cmd.CommandText = $"SELECT id, user_id, type, file_path, description, timestamp FROM log_entries{where} ORDER BY timestamp DESC LIMIT @lim";

        if (userId.HasValue) cmd.Parameters.AddWithValue("@uid", userId.Value);
        if (!string.IsNullOrEmpty(type)) cmd.Parameters.AddWithValue("@type", type);
        if (from.HasValue) cmd.Parameters.AddWithValue("@from", from.Value);
        if (to.HasValue) cmd.Parameters.AddWithValue("@to", to.Value);
        cmd.Parameters.AddWithValue("@lim", limit);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new LogEntry
            {
                Id = reader.GetInt64(0),
                UserId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                Type = reader.GetString(2),
                FilePath = reader.IsDBNull(3) ? null : reader.GetString(3),
                Description = reader.IsDBNull(4) ? null : reader.GetString(4),
                Timestamp = reader.GetDateTime(5)
            });
        }
        return list;
    }

    public List<SessionLog> GetRecentSessions(int limit = 20)
    {
        var list = new List<SessionLog>();
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, user_id, login_time, logout_time, duration_seconds FROM session_logs ORDER BY login_time DESC LIMIT @l";
        cmd.Parameters.AddWithValue("@l", limit);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new SessionLog
            {
                Id = reader.GetInt64(0),
                UserId = reader.GetInt32(1),
                LoginTime = reader.GetDateTime(2),
                LogoutTime = reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                DurationSeconds = reader.GetInt32(4)
            });
        }
        return list;
    }

    public int GetActiveUserCount()
    {
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM session_logs WHERE logout_time IS NULL";
        return Convert.ToInt32(cmd.ExecuteScalar()!);
    }

    public long GetTotalUsageSecondsToday()
    {
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT COALESCE(SUM(duration_seconds), 0) FROM session_logs
                           WHERE date(login_time) = date('now')";
        return (long)cmd.ExecuteScalar()!;
    }

    public void CleanupOldLogs()
    {
        _db.CleanupOldLogs();
    }
}
