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
            cmd.CommandText = @"SELECT id, profile_id, url_pattern, COALESCE(icon_path,''), is_global, created_at FROM allowed_sites
                               WHERE profile_id = @pid OR is_global = 1 ORDER BY is_global DESC, url_pattern";
            cmd.Parameters.AddWithValue("@pid", profileId.Value);
        }
        else
        {
            cmd.CommandText = "SELECT id, profile_id, url_pattern, COALESCE(icon_path,''), is_global, created_at FROM allowed_sites ORDER BY is_global DESC, url_pattern";
        }
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new AllowedSite
            {
                Id = reader.GetInt32(0),
                ProfileId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                UrlPattern = reader.GetString(2),
                IconPath = reader.GetString(3),
                IsGlobal = reader.GetBoolean(4),
                CreatedAt = reader.GetDateTime(5)
            });
        }
        return list;
    }

    public int Add(string urlPattern, int? profileId, bool isGlobal, string? iconPath = null)
    {
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO allowed_sites (profile_id, url_pattern, is_global, icon_path) VALUES (@pid, @url, @g, @i);
                           SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@pid", profileId.HasValue ? (object)profileId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@url", urlPattern.ToLowerInvariant().Trim());
        cmd.Parameters.AddWithValue("@g", isGlobal ? 1 : 0);
        cmd.Parameters.AddWithValue("@i", (object?)iconPath ?? DBNull.Value);
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

    private static readonly HashSet<string> CompoundTlds = new(StringComparer.OrdinalIgnoreCase)
    {
        "com.br", "edu.br", "org.br", "gov.br", "net.br", "mil.br", "art.br", "coop.br",
        "jus.br", "leg.br", "agr.br", "esp.br", "far.br", "imb.br", "ind.br", "inf.br",
        "rec.br", "srv.br", "tmp.br", "tur.br", "tv.br", "b.br", "def.br", "mp.br",
        "co.uk", "org.uk", "ac.uk", "gov.uk"
    };

    public bool IsUrlAllowed(string url, int? profileId)
    {
        var sites = GetForProfile(profileId);
        foreach (var site in sites)
        {
            if (IsUrlAllowedByPattern(url, site.UrlPattern))
                return true;
        }

        return false;
    }

    public bool IsUrlAllowedByPattern(string url, string urlPattern)
    {
        if (!TryCreateHttpUri(url, out var uri))
            return false;

        var normalizedPattern = NormalizePatternText(urlPattern);
        if (normalizedPattern == "*")
            return true;

        if (!TryCreatePatternUri(urlPattern, out var patternUri, out _))
            return false;

        var hostNoWww = RemoveWww(uri.Host);
        var patternHost = patternUri.Host.ToLowerInvariant();
        if (patternHost.StartsWith("*."))
            patternHost = patternHost.Substring(2);

        var patternHostNoWww = RemoveWww(patternHost);

        if (hostNoWww == patternHostNoWww || hostNoWww.EndsWith("." + patternHostNoWww))
            return true;

        var baseDomain = GetRegistrableDomain(patternHostNoWww);
        if (!string.IsNullOrEmpty(baseDomain) &&
            (hostNoWww == baseDomain || hostNoWww.EndsWith("." + baseDomain)))
        {
            return true;
        }

        return false;
    }

    private static string GetRegistrableDomain(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return "";

        if (System.Net.IPAddress.TryParse(host, out _))
            return host;

        var parts = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 2)
            return host;

        var lastTwo = parts[^2] + "." + parts[^1];
        if (CompoundTlds.Contains(lastTwo))
        {
            return parts.Length >= 3
                ? $"{parts[^3]}.{parts[^2]}.{parts[^1]}"
                : host;
        }

        return $"{parts[^2]}.{parts[^1]}";
    }

    public string ToNavigableUrl(string urlPattern)
    {
        var value = NormalizePatternText(urlPattern);
        if (string.IsNullOrWhiteSpace(value))
            return "";

        if (value == "*")
            return "https://www.google.com";

        if (value.StartsWith("*."))
            value = value.Substring(2);
        else if (value.StartsWith("http://*.", StringComparison.OrdinalIgnoreCase))
            value = "http://" + value.Substring("http://*.".Length);
        else if (value.StartsWith("https://*.", StringComparison.OrdinalIgnoreCase))
            value = "https://" + value.Substring("https://*.".Length);

        if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            value = "https://" + value;
        }

        return value;
    }

    private static bool TryCreateHttpUri(string url, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(url))
            return false;

        var value = url.Trim();
        if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            value = "https://" + value;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed))
            return false;
        if (!IsHttpScheme(parsed.Scheme) || string.IsNullOrWhiteSpace(parsed.Host))
            return false;

        uri = parsed;
        return true;
    }

    private static bool TryCreatePatternUri(string urlPattern, out Uri uri, out bool isWildcard)
    {
        uri = null!;
        isWildcard = false;
        var value = NormalizePatternText(urlPattern);
        if (string.IsNullOrWhiteSpace(value) || value == "*")
            return false;

        if (value.StartsWith("*."))
        {
            isWildcard = true;
            value = value.Substring(2);
        }
        else if (value.StartsWith("http://*.", StringComparison.OrdinalIgnoreCase))
        {
            isWildcard = true;
            value = "http://" + value.Substring("http://*.".Length);
        }
        else if (value.StartsWith("https://*.", StringComparison.OrdinalIgnoreCase))
        {
            isWildcard = true;
            value = "https://" + value.Substring("https://*.".Length);
        }

        if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            value = "https://" + value;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed))
            return false;
        if (!IsHttpScheme(parsed.Scheme) || string.IsNullOrWhiteSpace(parsed.Host))
            return false;

        uri = parsed;
        return true;
    }

    private static string NormalizePatternText(string value)
    {
        return (value ?? "").Trim().TrimEnd('/');
    }

    private static bool IsHttpScheme(string scheme)
    {
        return string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    private static string RemoveWww(string host)
    {
        var value = host.ToLowerInvariant();
        return value.StartsWith("www.") ? value.Substring(4) : value;
    }

    private static bool PathMatches(Uri uri, Uri patternUri)
    {
        var patternPath = patternUri.AbsolutePath.TrimEnd('/');
        if (string.IsNullOrEmpty(patternPath))
            patternPath = "/";
        if (patternPath == "/")
            return true;

        var path = uri.AbsolutePath.TrimEnd('/');
        if (string.IsNullOrEmpty(path))
            path = "/";

        return path.Equals(patternPath, StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.StartsWith(patternPath + "/", StringComparison.OrdinalIgnoreCase);
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
