using System.Text.RegularExpressions;
using Apac.App.Models;

namespace Apac.App.Services;

public class SiteChecker
{
    public bool IsAllowed(string url, List<AllowedSite> allowedSites)
    {
        if (url.StartsWith("about:")) return true;
        if (url == "apac://blocked") return true;
        if (allowedSites.Count == 0) return true;

        Uri? uri = null;
        try { uri = new Uri(url); } catch { return false; }

        var host = uri.Host.ToLowerInvariant();
        var fullUrl = url.ToLowerInvariant();

        foreach (var site in allowedSites)
        {
            var pattern = site.Pattern.ToLowerInvariant().Trim();
            if (string.IsNullOrEmpty(pattern)) continue;

            if (pattern.Contains('*'))
            {
                var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
                if (Regex.IsMatch(host, regexPattern) || Regex.IsMatch(fullUrl, regexPattern))
                    return true;
            }
            else
            {
                if (host == pattern || host.EndsWith("." + pattern) || fullUrl.Contains(pattern))
                    return true;
            }
        }
        return false;
    }
}
