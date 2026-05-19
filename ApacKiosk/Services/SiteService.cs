using ApacKiosk.Database;
using ApacKiosk.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace ApacKiosk.Services;

public class SiteService
{
    private readonly DatabaseManager _db;

    public SiteService(DatabaseManager db) => _db = db;

    public List<AllowedSite> GetForProfile(int? profileId = null)
    {
        var list = new List<AllowedSite>();
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        if (profileId.HasValue)
        {
            cmd.CommandText = @"SELECT id, profile_id, url_pattern, is_global, created_at FROM allowed_sites
                               WHERE profile_id = @pid OR is_global = 1 ORDER BY is_global DESC, url_pattern";
            cmd.Parameters.AddWithValue("@pid", profileId.Value);
        }
        else
        {
            cmd.CommandText = "SELECT id, profile_id, url_pattern, is_global, created_at FROM allowed_sites ORDER BY is_global DESC, url_pattern";
        }
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new AllowedSite
            {
                Id = reader.GetInt32(0),
                ProfileId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                UrlPattern = reader.GetString(2),
                IsGlobal = reader.GetBoolean(3),
                CreatedAt = reader.GetDateTime(4)
            });
        }
        return list;
    }

    public int Add(string urlPattern, int? profileId, bool isGlobal)
    {
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO allowed_sites (profile_id, url_pattern, is_global) VALUES (@pid, @url, @g);
                           SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@pid", profileId.HasValue ? (object)profileId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@url", urlPattern.ToLowerInvariant().Trim());
        cmd.Parameters.AddWithValue("@g", isGlobal ? 1 : 0);
        return Convert.ToInt32(cmd.ExecuteScalar()!);
    }

    public void Delete(int id)
    {
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM allowed_sites WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public bool IsUrlAllowed(string url, int? profileId)
    {
        var uri = new Uri(url);
        var host = uri.Host.ToLowerInvariant();

        var sites = GetForProfile(profileId);

        foreach (var site in sites)
        {
            var pattern = site.UrlPattern.ToLowerInvariant();
            if (pattern.StartsWith("*."))
            {
                var domain = pattern.Substring(2);
                if (host == domain || host.EndsWith("." + domain))
                    return true;
            }
            else if (host == pattern || host.EndsWith("." + pattern))
            {
                return true;
            }
            else if (pattern.StartsWith("http") && url.ToLowerInvariant().StartsWith(pattern))
            {
                return true;
            }
        }
        return false;
    }

    public List<string> GetAllowedExtensions()
    {
        var list = new List<string>();
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT extension FROM allowed_extensions";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(reader.GetString(0));
        return list;
    }

    public void AddAllowedExtension(string ext)
    {
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO allowed_extensions (extension) VALUES (@e)";
        cmd.Parameters.AddWithValue("@e", ext.TrimStart('.').ToLowerInvariant());
        cmd.ExecuteNonQuery();
    }

    public void RemoveAllowedExtension(string ext)
    {
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM allowed_extensions WHERE extension = @e";
        cmd.Parameters.AddWithValue("@e", ext.TrimStart('.').ToLowerInvariant());
        cmd.ExecuteNonQuery();
    }

    public void ImportFromFile(string filePath, int? profileId, bool isGlobal)
    {
        foreach (var line in File.ReadAllLines(filePath))
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("#"))
            {
                try { Add(trimmed, profileId, isGlobal); } catch { }
            }
        }
    }

    public void ExportToFile(string filePath)
    {
        var lines = new List<string>();
        foreach (var site in GetForProfile(null))
        {
            lines.Add(site.UrlPattern);
        }
        File.WriteAllLines(filePath, lines);
    }
}
