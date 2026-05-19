using ApacKiosk.Database;
using ApacKiosk.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace ApacKiosk.Services;

public class AuthService
{
    private readonly DatabaseManager _db;

    public AuthService(DatabaseManager db) => _db = db;

    public User? LoginUser(string username, string pin)
    {
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, full_name, username, pin_hash, photo_path, profile_id, is_active FROM users WHERE username = @u AND is_active = 1";
        cmd.Parameters.AddWithValue("@u", username);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        var hash = reader.GetString(3);
        if (!BCrypt.Net.BCrypt.Verify(pin, hash)) return null;
        return new User
        {
            Id = reader.GetInt32(0),
            FullName = reader.GetString(1),
            Username = reader.GetString(2),
            PinHash = hash,
            PhotoPath = reader.IsDBNull(4) ? null : reader.GetString(4),
            ProfileId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
            IsActive = reader.GetBoolean(6)
        };
    }

    public bool LoginAdmin(string password)
    {
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT password_hash FROM admins WHERE username = 'admin'";
        var hash = cmd.ExecuteScalar()?.ToString();
        if (hash == null) return false;
        return BCrypt.Net.BCrypt.Verify(password, hash);
    }

    public bool IsDefaultPassword()
    {
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT password_hash FROM admins WHERE username = 'admin'";
        var hash = cmd.ExecuteScalar()?.ToString();
        if (hash == null) return false;
        return BCrypt.Net.BCrypt.Verify("APAC@Admin2024", hash);
    }

    public void ChangeAdminPassword(string current, string newPass)
    {
        if (!LoginAdmin(current))
            throw new UnauthorizedAccessException("Senha atual incorreta");
        var hash = BCrypt.Net.BCrypt.HashPassword(newPass);
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE admins SET password_hash = @h WHERE username = 'admin'";
        cmd.Parameters.AddWithValue("@h", hash);
        cmd.ExecuteNonQuery();
    }

    public int CreateUser(string fullName, string username, string pin, int? profileId, string? photoPath)
    {
        var hash = BCrypt.Net.BCrypt.HashPassword(pin);
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO users (full_name, username, pin_hash, profile_id, photo_path, is_active)
                           VALUES (@fn, @un, @ph, @pi, @pp, 1);
                           SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@fn", fullName);
        cmd.Parameters.AddWithValue("@un", username);
        cmd.Parameters.AddWithValue("@ph", hash);
        cmd.Parameters.AddWithValue("@pi", profileId.HasValue ? (object)profileId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@pp", (object?)photoPath ?? DBNull.Value);
        return Convert.ToInt32(cmd.ExecuteScalar()!);
    }
}
