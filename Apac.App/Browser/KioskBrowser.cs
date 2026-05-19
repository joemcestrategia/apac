using Microsoft.Web.WebView2.WinForms;
using Apac.App.Services;
using Apac.App.Models;

namespace Apac.App.Browser;

public class KioskBrowser : Panel
{
    private WebView2 _webView;
    private Panel _navBar;
    private Button _btnBack;
    private Button _btnForward;
    private Button _btnHome;
    private TextBox _urlBar;
    private Label _lblTime;
    private readonly string _defaultHomepage;
    private readonly SiteChecker _siteChecker;
    private List<AllowedSite> _allowedSites = new();
    private readonly string _blockedPageHtml;
    private bool _initialized;

    public event Action<string>? BlockedNavigation;

    public KioskBrowser(string defaultHomepage)
    {
        _defaultHomepage = defaultHomepage;
        _siteChecker = new SiteChecker();

        _blockedPageHtml = @"<!DOCTYPE html>
<html><head><meta charset='utf-8'><style>
body{font-family:Arial,sans-serif;text-align:center;padding:80px 20px;background:#f5f5f5;}
.icon{font-size:64px;color:#e74c3c;}.title{font-size:24px;color:#2c3e50;margin:20px 0;}
.msg{font-size:16px;color:#7f8c8d;max-width:500px;margin:0 auto 30px;}
.logo{font-size:48px;font-weight:bold;color:#3498db;margin-top:40px;}
.logo span{color:#e74c3c;}
.footer{position:fixed;bottom:20px;left:0;right:0;font-size:12px;color:#bdc3c7;}
</style></head><body>
<div class='icon'>&#128274;</div>
<div class='title'>Acesso Bloqueado</div>
<div class='msg'>Este site não está disponível. Entre em contato com o administrador.</div>
<div class='logo'>A<span>P</span>AC</div>
<div class='footer'>Acesso Público Assistido por Computador</div>
</body></html>";

        InitializeComponent();
    }

    private void InitializeComponent()
    {
        _navBar = new Panel
        {
            Height = 40,
            Dock = DockStyle.Top,
            BackColor = Color.FromArgb(44, 62, 80)
        };

        _btnBack = new Button
        {
            Text = "\u25C0",
            Width = 36,
            Height = 30,
            Top = 5,
            Left = 5,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(52, 73, 94),
            Font = new Font("Segoe UI", 12, FontStyle.Bold)
        };
        _btnBack.FlatAppearance.BorderSize = 0;
        _btnBack.Click += (s, e) => _webView?.GoBack();

        _btnForward = new Button
        {
            Text = "\u25B6",
            Width = 36,
            Height = 30,
            Top = 5,
            Left = 44,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(52, 73, 94),
            Font = new Font("Segoe UI", 12, FontStyle.Bold)
        };
        _btnForward.FlatAppearance.BorderSize = 0;
        _btnForward.Click += (s, e) => _webView?.GoForward();

        _urlBar = new TextBox
        {
            Height = 28,
            Top = 6,
            Left = 90,
            Width = 400,
            ReadOnly = true,
            BackColor = Color.FromArgb(52, 73, 94),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 10)
        };

        _btnHome = new Button
        {
            Text = "\u2302",
            Width = 36,
            Height = 30,
            Top = 5,
            Left = 498,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(41, 128, 185),
            Font = new Font("Segoe UI", 14, FontStyle.Bold)
        };
        _btnHome.FlatAppearance.BorderSize = 0;
        _btnHome.Click += (s, e) => Navigate(_defaultHomepage);

        _lblTime = new Label
        {
            Text = "Tempo: Ilimitado",
            AutoSize = false,
            Width = 140,
            Height = 30,
            Top = 5,
            Left = 545,
            ForeColor = Color.FromArgb(46, 204, 113),
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            BackColor = Color.Transparent
        };

        _navBar.Controls.AddRange(new Control[] { _btnBack, _btnForward, _urlBar, _btnHome, _lblTime });

        _webView = new WebView2
        {
            Dock = DockStyle.Fill
        };
        _webView.NavigationStarting += WebView_NavigationStarting;
        _webView.NavigationCompleted += WebView_NavigationCompleted;
        _webView.DownloadStarting += WebView_DownloadStarting;
        _webView.NewWindowRequested += WebView_NewWindowRequested;

        Controls.Add(_webView);
        Controls.Add(_navBar);
    }

    public void SetAllowedSites(List<AllowedSite> sites)
    {
        _allowedSites = sites;
    }

    public async Task InitializeAsync()
    {
        var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment
            .CreateAsync(null, Path.Combine(Path.GetTempPath(), "APAC_Browser"));
        await _webView.EnsureCoreWebView2Async(env);

        _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
        _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        _webView.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
        _webView.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;

        _initialized = true;
    }

    public void Navigate(string url)
    {
        if (_webView?.CoreWebView2 == null) return;
        if (_webView.CoreWebView2.Source == url) return;

        if (!_siteChecker.IsAllowed(url, _allowedSites))
        {
            ShowBlockedPage();
            return;
        }

        _webView.CoreWebView2.Navigate(url);
    }

    private void WebView_NavigationStarting(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs e)
    {
        if (!_initialized) return;
        if (!_siteChecker.IsAllowed(e.Uri, _allowedSites))
        {
            e.Cancel = true;
            ShowBlockedPage();
            BlockedNavigation?.Invoke(e.Uri);
        }
    }

    private void WebView_NavigationCompleted(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_webView?.CoreWebView2 != null)
        {
            _urlBar.Text = _webView.CoreWebView2.Source;
        }
    }

    private void WebView_DownloadStarting(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2DownloadStartingEventArgs e)
    {
        e.Cancel = true;
        e.Handled = true;
    }

    private void WebView_NewWindowRequested(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (!_siteChecker.IsAllowed(e.Uri, _allowedSites))
        {
            ShowBlockedPage();
            BlockedNavigation?.Invoke(e.Uri);
        }
        else
        {
            Navigate(e.Uri);
        }
    }

    private void ShowBlockedPage()
    {
        if (_webView?.CoreWebView2 != null)
        {
            _webView.CoreWebView2.NavigateToString(_blockedPageHtml);
        }
    }

    public void SetTimeDisplay(string timeText)
    {
        if (_lblTime.InvokeRequired)
            _lblTime.Invoke(() => _lblTime.Text = $"Tempo: {timeText}");
        else
            _lblTime.Text = $"Tempo: {timeText}";
    }

    public void GoHome() => Navigate(_defaultHomepage);
}
