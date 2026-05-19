using ApacKiosk.Database;
using ApacKiosk.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace ApacKiosk.Services;

public class UserService
{
    private readonly DatabaseManager _db;

    public UserService(DatabaseManager db) => _db = db;

    public List<User> GetAll()
    {
        var list = new List<User>();
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT id, full_name, username, pin_hash, photo_path, profile_id, is_active, created_at
                           FROM users ORDER BY full_name";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(MapUser(reader));
        }
        return list;
    }

    public User? GetById(int id)
    {
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT id, full_name, username, pin_hash, photo_path, profile_id, is_active, created_at
                           FROM users WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
            return MapUser(reader);
        return null;
    }

    public void Update(int id, string fullName, string username, int? profileId, bool isActive, string? photoPath)
    {
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"UPDATE users SET full_name=@fn, username=@un, profile_id=@pi, is_active=@ia, photo_path=@pp WHERE id=@id";
        cmd.Parameters.AddWithValue("@fn", fullName);
        cmd.Parameters.AddWithValue("@un", username);
        cmd.Parameters.AddWithValue("@pi", profileId.HasValue ? (object)profileId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@ia", isActive ? 1 : 0);
        cmd.Parameters.AddWithValue("@pp", (object?)photoPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public void Delete(int id)
    {
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM users WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public void ToggleActive(int userId, bool active)
    {
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE users SET is_active = @a WHERE id = @id";
        cmd.Parameters.AddWithValue("@a", active ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", userId);
        cmd.ExecuteNonQuery();
    }

    private static User MapUser(SqliteDataReader reader)
    {
        return new User
        {
            Id = reader.GetInt32(0),
            FullName = reader.GetString(1),
            Username = reader.GetString(2),
            PinHash = reader.GetString(3),
            PhotoPath = reader.IsDBNull(4) ? null : reader.GetString(4),
            ProfileId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
            IsActive = reader.GetBoolean(6),
            CreatedAt = reader.GetDateTime(7)
        };
    }
}
