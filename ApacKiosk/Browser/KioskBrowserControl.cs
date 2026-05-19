using ApacKiosk.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ApacKiosk.Browser;

public class KioskBrowserControl : WebView2
{
    private SiteService? _siteService;
    private int? _profileId;
    private string _blockedHtml = "";
    private bool _initialized;

    public event Action<string>? UrlChanged;
    public event Action<string>? BlockedNavigation;
    public event Action? CanGoBackChanged;
    public event Action? CanGoForwardChanged;

    public KioskBrowserControl()
    {
        Dock = DockStyle.Fill;
    }

    public async Task InitializeAsync(SiteService siteService, int? profileId, string homeUrl)
    {
        _siteService = siteService;
        _profileId = profileId;
        _blockedHtml = LoadBlockedHtml();

        var env = await CoreWebView2Environment.CreateAsync();
        await EnsureCoreWebView2Async(env);

        CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        CoreWebView2.Settings.AreDevToolsEnabled = false;
        CoreWebView2.Settings.IsStatusBarEnabled = false;
        CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
        CoreWebView2.Settings.IsGeneralAutofillEnabled = false;

        CoreWebView2.NavigationStarting += OnNavigationStarting;
        CoreWebView2.NewWindowRequested += OnNewWindowRequested;
        CoreWebView2.DownloadStarting += OnDownloadStarting;
        CoreWebView2.NavigationCompleted += (s, e) =>
        {
            UrlChanged?.Invoke(CoreWebView2.Source);
            CanGoBackChanged?.Invoke();
            CanGoForwardChanged?.Invoke();
        };

        _initialized = true;
        CoreWebView2.Navigate(homeUrl);
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!_initialized || _siteService == null) return;

        if (e.Uri.StartsWith("about:") || e.Uri == "https://apac.local/blocked")
            return;

        if (!_siteService.IsUrlAllowed(e.Uri, _profileId))
        {
            e.Cancel = true;
            BlockedNavigation?.Invoke(e.Uri);
            NavigateToBlocked(e.Uri);
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        CoreWebView2.Navigate(e.Uri);
    }

    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        if (_siteService == null) { e.Cancel = true; return; }

        var uri = new Uri(e.DownloadOperation.Uri);
        var ext = Path.GetExtension(uri.AbsolutePath).TrimStart('.').ToLowerInvariant();

        var allowed = _siteService.GetAllowedExtensions();
        if (!allowed.Contains(ext))
        {
            e.Cancel = true;
            BlockedNavigation?.Invoke($"Download bloqueado: {e.DownloadOperation.Uri}");
        }
    }

    private void NavigateToBlocked(string uri)
    {
        var host = "";
        try { host = new Uri(uri).Host; } catch { host = uri; }

        var html = _blockedHtml
            .Replace("window.location.href", $"\"{host}\"")
            .Replace("APAC &mdash; Acesso Controlado", "APAC — Acesso Controlado");

        CoreWebView2.NavigateToString(html);
    }

    private string LoadBlockedHtml()
    {
        var resourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "blocked.html");
        if (File.Exists(resourcePath))
            return File.ReadAllText(resourcePath);

        return @"<!DOCTYPE html>
<html><head><meta charset=""utf-8""><style>
*{margin:0;padding:0;box-sizing:border-box}
body{font-family:'Segoe UI',sans-serif;background:#f4f4f4;display:flex;justify-content:center;align-items:center;height:100vh;text-align:center}
.container{background:white;padding:60px 80px;border-radius:12px;box-shadow:0 4px 24px rgba(0,0,0,0.1);max-width:600px}
.icon{font-size:64px;margin-bottom:20px;display:block}
h1{color:#c0392b;font-size:28px;margin-bottom:16px}
p{color:#555;font-size:16px;line-height:1.6}
.domain{font-weight:bold;color:#333;background:#fff3cd;padding:4px 12px;border-radius:4px;display:inline-block;margin:8px 0}
.logo{margin-top:40px;font-size:14px;color:#999}
</style></head><body>
<div class=""container"">
<span class=""icon"">&#128274;</span>
<h1>Acesso Bloqueado</h1>
<p>Este site não está disponível.</p>
<p>Entre em contato com o administrador.</p>
<p><span class=""domain"" id=""domain""></span></p>
<div class=""logo"">APAC — Acesso Controlado</div>
</div>
<script>document.getElementById('domain').textContent='DOMAIN';</script>
</body></html>";
    }

    public bool CanGoBack => CoreWebView2?.CanGoBack ?? false;
    public bool CanGoForward => CoreWebView2?.CanGoForward ?? false;

    public void GoBack() => CoreWebView2?.GoBack();
    public void GoForward() => CoreWebView2?.GoForward();
    public void NavigateTo(string url) => CoreWebView2?.Navigate(url);
}
