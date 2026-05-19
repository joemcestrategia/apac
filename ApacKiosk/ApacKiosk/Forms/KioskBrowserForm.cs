using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;
using ApacKiosk.Data;
using ApacKiosk.Models;

namespace ApacKiosk.Forms;

public class KioskBrowserForm : Form
{
    private readonly User _user;
    private WebView2 _webView;
    private Panel _navBar;
    private Button _backButton;
    private Button _forwardButton;
    private TextBox _urlDisplay;
    private Button _homeButton;
    private Label _timerLabel;
    private Label _userLabel;
    private DateTime _sessionStart;
    private CancellationTokenSource? _sessionCts;
    private int _sessionMinutes;
    private int _breakAfterMinutes;
    private int _breakDurationMinutes;
    private string _blockedPagePath;

    public KioskBrowserForm(User user)
    {
        _user = user;
        _blockedPagePath = Path.Combine(AppContext.BaseDirectory, "Resources", "blocked.html");
        InitializeComponent();
        LoadSessionConfig();
    }

    private void InitializeComponent()
    {
        WindowState = FormWindowState.Maximized;
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Color.FromArgb(15, 15, 35);
        TopMost = true;

        _navBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 48,
            BackColor = Color.FromArgb(26, 26, 46),
            Padding = new Padding(8, 6, 8, 6)
        };

        _backButton = new Button
        {
            Text = "\u25C0",
            Font = new Font("Segoe UI", 14),
            Size = new Size(44, 36),
            Location = new Point(8, 6),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(42, 42, 74),
            ForeColor = Color.FromArgb(224, 224, 224),
            Cursor = Cursors.Hand
        };
        _backButton.FlatAppearance.BorderSize = 0;
        _backButton.Click += (s, e) => _webView?.GoBack();

        _forwardButton = new Button
        {
            Text = "\u25B6",
            Font = new Font("Segoe UI", 14),
            Size = new Size(44, 36),
            Location = new Point(58, 6),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(42, 42, 74),
            ForeColor = Color.FromArgb(224, 224, 224),
            Cursor = Cursors.Hand
        };
        _forwardButton.FlatAppearance.BorderSize = 0;
        _forwardButton.Click += (s, e) => _webView?.GoForward();

        _homeButton = new Button
        {
            Text = "\u2302",
            Font = new Font("Segoe UI", 16),
            Size = new Size(44, 36),
            Location = new Point(108, 6),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(42, 42, 74),
            ForeColor = Color.FromArgb(224, 224, 224),
            Cursor = Cursors.Hand
        };
        _homeButton.FlatAppearance.BorderSize = 0;
        _homeButton.Click += NavigateHome;

        _urlDisplay = new TextBox
        {
            ReadOnly = true,
            Font = new Font("Segoe UI", 11),
            BackColor = Color.FromArgb(15, 15, 35),
            ForeColor = Color.FromArgb(156, 163, 175),
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(160, 8),
            Size = new Size(Width - 450, 32),
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };

        _timerLabel = new Label
        {
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            ForeColor = Color.FromArgb(167, 139, 250),
            TextAlign = ContentAlignment.MiddleRight,
            Size = new Size(150, 36),
            Location = new Point(Width - 310, 6),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };

        _userLabel = new Label
        {
            Text = _user.FullName,
            Font = new Font("Segoe UI", 11),
            ForeColor = Color.FromArgb(156, 163, 175),
            TextAlign = ContentAlignment.MiddleRight,
            Size = new Size(140, 36),
            Location = new Point(Width - 160, 6),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };

        _navBar.Controls.Add(_backButton);
        _navBar.Controls.Add(_forwardButton);
        _navBar.Controls.Add(_homeButton);
        _navBar.Controls.Add(_urlDisplay);
        _navBar.Controls.Add(_timerLabel);
        _navBar.Controls.Add(_userLabel);

        _webView = new WebView2
        {
            Dock = DockStyle.Fill
        };

        Controls.Add(_webView);
        Controls.Add(_navBar);

        FormClosing += KioskBrowserForm_FormClosing;
        Resize += (s, e) =>
        {
            _urlDisplay.Width = Width - 450;
            _timerLabel.Left = Width - 310;
            _userLabel.Left = Width - 160;
        };
    }

    private void LoadSessionConfig()
    {
        _sessionMinutes = 0;
        _breakAfterMinutes = 0;
        _breakDurationMinutes = 0;

        if (_user.ProfileId != null)
        {
            var profile = DatabaseHelper.QueryFirstOrDefault<AccessProfile>(
                "SELECT * FROM access_profiles WHERE id = @id", new { id = _user.ProfileId });
            if (profile != null)
            {
                _sessionMinutes = profile.MaxSessionMinutes;
                _breakAfterMinutes = profile.MandatoryBreakAfterMinutes;
                _breakDurationMinutes = profile.MandatoryBreakDurationMinutes;
            }
        }
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        try
        {
            var env = await CoreWebView2Environment.CreateAsync();
            await _webView.EnsureCoreWebView2Async(env);

            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _webView.CoreWebView2.Settings.IsBuiltInErrorPageEnabled = false;

            _webView.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
            _webView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
            _webView.CoreWebView2.DownloadStarting += CoreWebView2_DownloadStarting;

            _webView.NavigationCompleted += (s, ev) =>
            {
                _urlDisplay.Text = _webView.Source?.ToString() ?? "";
            };

            NavigateHome(null, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao inicializar WebView2: {ex.Message}\n\nVerifique se o WebView2 Runtime está instalado.",
                "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void NavigateHome(object? sender, EventArgs e)
    {
        var home = DatabaseHelper.GetConfig("default_homepage", "https://www.google.com");
        _webView?.CoreWebView2?.Navigate(home);
    }

    private void CoreWebView2_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        try
        {
            var uri = new Uri(e.Uri);

            if (uri.Scheme == "about")
            {
                if (uri.AbsolutePath == "blank") return;
                e.Cancel = true;
                return;
            }
        }
        catch { }

        if (!IsUrlAllowed(e.Uri))
        {
            e.Cancel = true;
            ShowBlockedPage();
        }
    }

    private bool IsUrlAllowed(string url)
    {
        try
        {
            var uri = new Uri(url);
            var host = uri.Host.ToLowerInvariant();

            var allowedUrls = DatabaseHelper.Query<string>(
                @"SELECT url_pattern FROM allowed_sites
                  WHERE profile_id IS NULL OR profile_id = @p
                  UNION
                  SELECT url_pattern FROM allowed_sites WHERE profile_id IS NULL
                  ORDER BY url_pattern",
                new { p = _user.ProfileId ?? -1 });

            foreach (var pattern in allowedUrls)
            {
                if (string.IsNullOrWhiteSpace(pattern)) continue;

                try
                {
                    if (pattern.StartsWith("*."))
                    {
                        var domainPart = pattern[2..];
                        if (host == domainPart || host.EndsWith("." + domainPart))
                            return true;
                    }
                    else if (pattern.Contains("*"))
                    {
                        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$";
                        if (System.Text.RegularExpressions.Regex.IsMatch(host, regex))
                            return true;
                    }
                    else
                    {
                        var patternUri = new Uri(pattern.StartsWith("http") ? pattern : "https://" + pattern);
                        if (host == patternUri.Host)
                            return true;
                    }
                }
                catch { }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private void ShowBlockedPage()
    {
        if (File.Exists(_blockedPagePath))
        {
            _webView?.CoreWebView2?.Navigate("file:///" + _blockedPagePath.Replace("\\", "/"));
        }
        else
        {
            _webView?.CoreWebView2?.NavigateToString(@"
<html><body style='background:#0f0f23;color:#e0e0e0;display:flex;align-items:center;justify-content:center;height:100vh;font-family:sans-serif'>
<div style='text-align:center'><h1 style='color:#a78bfa'>Acesso Bloqueado</h1><p>Este site não está disponível.</p></div></body></html>");
        }
    }

    private void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        _webView?.CoreWebView2?.Navigate(e.Uri);
    }

    private void CoreWebView2_DownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        e.Cancel = true;
    }

    private void KioskBrowserForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            var dialog = new AdminLoginDialog();
            dialog.Text = "Senha para Fechar";
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                e.Cancel = true;
                return;
            }
        }

        _sessionCts?.Cancel();
        _webView?.Dispose();

        Data.DatabaseHelper.InsertLog("user_event", _user.Id, null, "Logout");
    }
}
