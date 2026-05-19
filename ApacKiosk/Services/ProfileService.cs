using ApacKiosk.Database;
using ApacKiosk.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace ApacKiosk.Services;

public class ProfileService
{
    private readonly DatabaseManager _db;

    public ProfileService(DatabaseManager db) => _db = db;

    public List<AccessProfile> GetAll()
    {
        var list = new List<AccessProfile>();
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, max_session_minutes, mandatory_pause_minutes, pause_after_minutes, homepage_url, created_at FROM access_profiles ORDER BY name";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new AccessProfile
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                MaxSessionMinutes = reader.GetInt32(2),
                MandatoryPauseMinutes = reader.GetInt32(3),
                PauseAfterMinutes = reader.GetInt32(4),
                HomepageUrl = reader.IsDBNull(5) ? "https://www.google.com" : reader.GetString(5),
                CreatedAt = reader.GetDateTime(6)
            });
        }
        return list;
    }

    public AccessProfile? GetById(int id)
    {
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, max_session_minutes, mandatory_pause_minutes, pause_after_minutes, homepage_url, created_at FROM access_profiles WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return new AccessProfile
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                MaxSessionMinutes = reader.GetInt32(2),
                MandatoryPauseMinutes = reader.GetInt32(3),
                PauseAfterMinutes = reader.GetInt32(4),
                HomepageUrl = reader.IsDBNull(5) ? "https://www.google.com" : reader.GetString(5),
                CreatedAt = reader.GetDateTime(6)
            };
        }
        return null;
    }

    public int Create(string name, int maxSessionMinutes, int mandatoryPauseMinutes, int pauseAfterMinutes, string homepageUrl)
    {
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO access_profiles (name, max_session_minutes, mandatory_pause_minutes, pause_after_minutes, homepage_url)
                           VALUES (@n, @ms, @mp, @pa, @hp);
                           SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@n", name);
        cmd.Parameters.AddWithValue("@ms", maxSessionMinutes);
        cmd.Parameters.AddWithValue("@mp", mandatoryPauseMinutes);
        cmd.Parameters.AddWithValue("@pa", pauseAfterMinutes);
        cmd.Parameters.AddWithValue("@hp", homepageUrl);
        return Convert.ToInt32(cmd.ExecuteScalar()!);
    }

    public void Update(int id, string name, int maxSessionMinutes, int mandatoryPauseMinutes, int pauseAfterMinutes, string homepageUrl)
    {
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"UPDATE access_profiles SET name=@n, max_session_minutes=@ms, mandatory_pause_minutes=@mp,
                           pause_after_minutes=@pa, homepage_url=@hp WHERE id=@id";
        cmd.Parameters.AddWithValue("@n", name);
        cmd.Parameters.AddWithValue("@ms", maxSessionMinutes);
        cmd.Parameters.AddWithValue("@mp", mandatoryPauseMinutes);
        cmd.Parameters.AddWithValue("@pa", pauseAfterMinutes);
        cmd.Parameters.AddWithValue("@hp", homepageUrl);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public void Delete(int id)
    {
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM access_profiles WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public List<TimeRule> GetTimeRules(int profileId)
    {
        var list = new List<TimeRule>();
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, profile_id, day_of_week, start_time, end_time FROM time_rules WHERE profile_id = @pid ORDER BY day_of_week, start_time";
        cmd.Parameters.AddWithValue("@pid", profileId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new TimeRule
            {
                Id = reader.GetInt32(0),
                ProfileId = reader.GetInt32(1),
                DayOfWeek = reader.GetInt32(2),
                StartTime = TimeSpan.Parse(reader.GetString(3)),
                EndTime = TimeSpan.Parse(reader.GetString(4))
            });
        }
        return list;
    }

    public void SaveTimeRules(int profileId, List<TimeRule> rules)
    {
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM time_rules WHERE profile_id = @pid";
        cmd.Parameters.AddWithValue("@pid", profileId);
        cmd.ExecuteNonQuery();

        foreach (var rule in rules)
        {
            cmd.Parameters.Clear();
            cmd.CommandText = "INSERT INTO time_rules (profile_id, day_of_week, start_time, end_time) VALUES (@pid, @dow, @st, @et)";
            cmd.Parameters.AddWithValue("@pid", profileId);
            cmd.Parameters.AddWithValue("@dow", rule.DayOfWeek);
            cmd.Parameters.AddWithValue("@st", rule.StartTime.ToString(@"hh\:mm"));
            cmd.Parameters.AddWithValue("@et", rule.EndTime.ToString(@"hh\:mm"));
            cmd.ExecuteNonQuery();
        }
    }
}
