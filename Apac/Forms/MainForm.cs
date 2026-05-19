using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using Apac.Database;
using Apac.Services;

namespace Apac.Forms
{
    public class MainForm : Form
    {
        private WebView2 _webView;
        private Panel _navBar;
        private Button _backBtn;
        private Button _forwardBtn;
        private Button _homeBtn;
        private Button _logoutBtn;
        private Label _urlLabel;
        private Label _clockLabel;
        private Label _sessionLabel;
        private Label _userLabel;
        private System.Windows.Forms.Timer _clockTimer;
        private System.Windows.Forms.Timer _sessionTimer;

        private readonly User _currentUser;
        private AccessProfile _profile;
        private readonly DatabaseManager _db;
        private readonly SecurityManager _security;
        private ScreenCapture _screenCapture;
        private CameraCapture _cameraCapture;
        private KeyLogger _keyLogger;

        private DateTime _sessionStart;
        private int _sessionMinutesRemaining;
        private int _continuousMinutes;
        private readonly int _maxSessionMinutes;
        private readonly int _pauseAfterMinutes;
        private readonly int _pauseMinutes;
        private bool _inPause;
        private string _defaultUrl;

        public MainForm(User user)
        {
            _currentUser = user;
            _db = DatabaseManager.Instance;

            LoadProfile();
            _maxSessionMinutes = _profile?.MaxSessionMinutes ?? 0;
            _pauseAfterMinutes = _profile?.MandatoryPauseAfterMinutes ?? 0;
            _pauseMinutes = _profile?.MandatoryPauseMinutes ?? 0;
            _defaultUrl = _profile?.DefaultUrl ?? "about:blank";

            _sessionStart = DateTime.Now;
            _sessionMinutesRemaining = _maxSessionMinutes;
            _continuousMinutes = 0;

            InitializeComponent();
            InitializeWebView();
            StartSecurity();
            StartMonitoring();
        }

        private void LoadProfile()
        {
            if (_currentUser.ProfileId.HasValue)
            {
                _profile = _db.GetAccessProfile(_currentUser.ProfileId.Value);
            }
        }

        private void InitializeComponent()
        {
            this.Text = "APAC - Navegador Seguro";
            this.WindowState = FormWindowState.Maximized;
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = Color.FromArgb(15, 15, 35);
            this.TopMost = true;

            _navBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 48,
                BackColor = Color.FromArgb(26, 26, 46),
                Padding = new Padding(8, 0, 8, 0)
            };

            _backBtn = CreateNavButton("\u25C0", 0);
            _backBtn.Click += (s, e) => { if (_webView?.CoreWebView2 != null && _webView.CoreWebView2.CanGoBack) _webView.GoBack(); };

            _forwardBtn = CreateNavButton("\u25B6", 40);
            _forwardBtn.Click += (s, e) => { if (_webView?.CoreWebView2 != null && _webView.CoreWebView2.CanGoForward) _webView.GoForward(); };

            _homeBtn = CreateNavButton("\u2302", 80);
            _homeBtn.Width = 44;
            _homeBtn.Click += (s, e) => NavigateToHome();

            _urlLabel = new Label
            {
                Location = new Point(140, 12),
                Size = new Size(60, 24),
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(160, 160, 180),
                Font = new Font("Segoe UI", 9, FontStyle.Regular),
                BackColor = Color.FromArgb(35, 35, 55),
                Text = ""
            };

            _userLabel = new Label
            {
                Text = $"{_currentUser.FullName}",
                ForeColor = Color.FromArgb(167, 139, 250),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                TextAlign = ContentAlignment.MiddleRight
            };

            _sessionLabel = new Label
            {
                ForeColor = Color.FromArgb(200, 200, 220),
                Font = new Font("Segoe UI", 10, FontStyle.Regular),
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                TextAlign = ContentAlignment.MiddleRight
            };
            UpdateSessionLabel();

            _clockLabel = new Label
            {
                ForeColor = Color.FromArgb(140, 140, 160),
                Font = new Font("Consolas", 10, FontStyle.Regular),
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                TextAlign = ContentAlignment.MiddleRight
            };

            _logoutBtn = new Button
            {
                Text = "Encerrar",
                Font = new Font("Segoe UI", 9, FontStyle.Regular),
                Size = new Size(80, 30),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(80, 30, 30),
                ForeColor = Color.FromArgb(252, 165, 165),
                Cursor = Cursors.Hand
            };
            _logoutBtn.FlatAppearance.BorderSize = 0;
            _logoutBtn.Click += LogoutBtn_Click;

            _navBar.Controls.AddRange(new Control[] { _backBtn, _forwardBtn, _homeBtn, _urlLabel, _userLabel, _sessionLabel, _clockLabel, _logoutBtn });

            _webView = new WebView2
            {
                Dock = DockStyle.Fill
            };
            _webView.CoreWebView2InitializationCompleted += WebView_CoreInitCompleted;

            this.Controls.Add(_navBar);
            this.Controls.Add(_webView);
            _navBar.BringToFront();

            _clockTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _clockTimer.Tick += ClockTimer_Tick;
            _clockTimer.Start();

            _sessionTimer = new System.Windows.Forms.Timer { Interval = 60000 };
            _sessionTimer.Tick += SessionTimer_Tick;
            _sessionTimer.Start();

            this.Resize += MainForm_Resize;
            this.FormClosing += MainForm_FormClosing;
        }

        private void MainForm_Resize(object sender, EventArgs e)
        {
            if (_urlLabel != null)
            {
                int rightEdge = this.ClientSize.Width - 380;
                _urlLabel.Width = Math.Max(100, rightEdge - _urlLabel.Left);
            }
        }

        private Button CreateNavButton(string text, int x)
        {
            return new Button
            {
                Text = text,
                Location = new Point(x + 8, 8),
                Size = new Size(32, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 65),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 14),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter
            };
        }

        private async void InitializeWebView()
        {
            try
            {
                var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync();
                await _webView.EnsureCoreWebView2Async(env);
            }
            catch
            {
                MessageBox.Show(
                    "WebView2 Runtime não encontrado.\n\n" +
                    "O Microsoft Edge WebView2 Runtime é necessário para o navegador seguro.\n" +
                    "Instale via: https://go.microsoft.com/fwlink/p/?LinkId=2124703",
                    "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.Close();
                Application.Restart();
            }
        }

        private void WebView_CoreInitCompleted(object sender, Microsoft.Web.WebView2.Core.CoreWebView2InitializationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                var core = _webView.CoreWebView2;

                core.Settings.AreDefaultScriptDialogsEnabled = true;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.AreBrowserAcceleratorKeysEnabled = false;

                core.NewWindowRequested += (s, args) =>
                {
                    args.Handled = true;
                    core.Navigate(args.Uri);
                };

                core.DownloadStarting += (s, args) =>
                {
                    string ext = Path.GetExtension(args.DownloadOperation.Uri ?? "").ToLower();
                    var allowed = new[] { ".pdf", ".docx", ".xlsx", ".pptx", ".txt", ".jpg", ".png" };
                    if (!allowed.Contains(ext))
                    {
                        args.Cancel = true;
                        args.Handled = true;
                    }
                };

                core.NavigationStarting += Core_NavigationStarting;
                core.NavigationCompleted += Core_NavigationCompleted;

                _webView.ContextMenuStrip = new ContextMenuStrip();

                NavigateToHome();
            }
        }

        private void Core_NavigationStarting(object sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs e)
        {
            string uri = e.Uri;
            if (string.IsNullOrEmpty(uri) || uri == "about:blank") return;

            if (!IsUrlAllowed(uri))
            {
                e.Cancel = true;
                _db.AddLogEntry(EntryType.BlockedSite.ToString(), _currentUser.Id, null,
                    $"Site bloqueado: {uri}");
                ShowBlockedPage(uri);
                return;
            }
        }

        private void Core_NavigationCompleted(object sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
        {
            _urlLabel.Text = _webView.CoreWebView2?.Source ?? "";

            _backBtn.Enabled = _webView.CoreWebView2?.CanGoBack ?? false;
            _forwardBtn.Enabled = _webView.CoreWebView2?.CanGoForward ?? false;
        }

        private bool IsUrlAllowed(string url)
        {
            if (string.IsNullOrEmpty(url) || url == "about:blank" || url.StartsWith("data:"))
                return true;

            try
            {
                var uri = new Uri(url);
                string domain = uri.Host.ToLower();

                int? profileId = _currentUser.ProfileId;
                var sites = _db.GetAllowedSites(profileId);

                foreach (var site in sites)
                {
                    string pattern = site.UrlPattern.ToLower().Trim();

                    if (site.IsWildcard)
                    {
                        string regexPattern = "^" + Regex.Escape(pattern)
                            .Replace("\\*", ".*") + "$";
                        if (Regex.IsMatch(domain, regexPattern, RegexOptions.IgnoreCase))
                            return true;
                    }
                    else
                    {
                        if (domain == pattern || domain.EndsWith("." + pattern))
                            return true;

                        if (Uri.TryCreate("https://" + pattern, UriKind.Absolute, out var patternUri))
                        {
                            if (domain == patternUri.Host.ToLower())
                                return true;
                        }
                    }
                }

                if (profileId.HasValue)
                {
                    var globalSites = _db.GetAllowedSites(null);
                    foreach (var site in globalSites)
                    {
                        string pattern = site.UrlPattern.ToLower().Trim();
                        if (site.IsWildcard)
                        {
                            string regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
                            if (Regex.IsMatch(domain, regexPattern, RegexOptions.IgnoreCase))
                                return true;
                        }
                        else if (domain == pattern || domain.EndsWith("." + pattern))
                            return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private void ShowBlockedPage(string url)
        {
            string html = $@"
                <!DOCTYPE html>
                <html><head><meta charset='utf-8'>
                <style>
                    body {{ font-family: 'Segoe UI', sans-serif; background: #0f0f23; color: #e0e0f0; 
                           display: flex; justify-content: center; align-items: center; height: 100vh; 
                           margin: 0; text-align: center; }}
                    .box {{ max-width: 500px; padding: 40px; }}
                    h1 {{ color: #a78bfa; font-size: 24px; }}
                    .url {{ background: #1a1a2e; padding: 8px 16px; border-radius: 6px; 
                            color: #fbbf24; font-family: Consolas, monospace; margin: 20px 0; 
                            word-break: break-all; }}
                    p {{ color: #8888aa; font-size: 14px; }}
                    .logo {{ font-size: 48px; margin-bottom: 20px; }}
                </style></head><body>
                <div class='box'>
                    <div class='logo'>APAC</div>
                    <h1>Site Bloqueado</h1>
                    <p>Este site não está disponível na lista de sites permitidos.</p>
                    <div class='url'>{System.Net.WebUtility.HtmlEncode(url)}</div>
                    <p>Entre em contato com o administrador para solicitar liberação.</p>
                </div></body></html>";

            if (_webView.CoreWebView2 != null)
                _webView.CoreWebView2.NavigateToString(html);
        }

        private void NavigateToHome()
        {
            if (_webView?.CoreWebView2 != null)
            {
                if (!string.IsNullOrEmpty(_defaultUrl) && _defaultUrl != "about:blank")
                {
                    _webView.CoreWebView2.Navigate(_defaultUrl);
                }
                else
                {
                    _webView.CoreWebView2.Navigate("about:blank");
                }
            }
        }

        private void StartSecurity()
        {
            string adminPwd = _db.GetAdmin()?.PasswordHash ?? "";
            _security = new SecurityManager(this, adminPwd);
            _security.Start();
        }

        private void StartMonitoring()
        {
            if (_db.GetSetting("screenshots_enabled", "true") == "true")
            {
                _screenCapture = new ScreenCapture(_currentUser.Id, _currentUser.Username);
                _screenCapture.Start();
            }

            if (_db.GetSetting("camera_enabled", "false") == "true")
            {
                _cameraCapture = new CameraCapture(_currentUser.Id, _currentUser.Username);
                _cameraCapture.Start();
            }

            if (_db.GetSetting("keylogger_enabled", "true") == "true")
            {
                _keyLogger = new KeyLogger(_currentUser.Id, _currentUser.Username);
                _keyLogger.Start();
            }
        }

        private void StopMonitoring()
        {
            _screenCapture?.Dispose();
            _cameraCapture?.Dispose();
            _keyLogger?.Dispose();
        }

        private void ClockTimer_Tick(object sender, EventArgs e)
        {
            if (_clockLabel != null)
                _clockLabel.Text = DateTime.Now.ToString("HH:mm:ss");
        }

        private void SessionTimer_Tick(object sender, EventArgs e)
        {
            if (_inPause) return;

            _continuousMinutes++;

            if (_pauseAfterMinutes > 0 && _continuousMinutes >= _pauseAfterMinutes)
            {
                StartPause();
                return;
            }

            if (_maxSessionMinutes > 0)
            {
                _sessionMinutesRemaining--;
                UpdateSessionLabel();

                if (_sessionMinutesRemaining <= 0)
                {
                    DoLogout();
                }
            }
        }

        private void UpdateSessionLabel()
        {
            if (_maxSessionMinutes > 0)
            {
                int remaining = Math.Max(0, _sessionMinutesRemaining);
                _sessionLabel.Text = $"Tempo: {remaining}min";
                if (remaining <= 5)
                    _sessionLabel.ForeColor = Color.FromArgb(252, 165, 165);
                else if (remaining <= 15)
                    _sessionLabel.ForeColor = Color.FromArgb(251, 191, 36);
                else
                    _sessionLabel.ForeColor = Color.FromArgb(200, 200, 220);
            }
            else
            {
                _sessionLabel.Text = "Tempo: ilimitado";
            }
        }

        private void StartPause()
        {
            _inPause = true;
            _continuousMinutes = 0;
            string msg = $"Pausa obrigatória de {_pauseMinutes} minuto(s).\n\nO navegador será liberado em breve.";
            MessageBox.Show(msg, "Pausa Obrigatória", MessageBoxButtons.OK, MessageBoxIcon.Information);

            var pauseTimer = new System.Windows.Forms.Timer { Interval = _pauseMinutes * 60000 };
            pauseTimer.Tick += (s, e) =>
            {
                _inPause = false;
                pauseTimer.Stop();
                pauseTimer.Dispose();
            };
            pauseTimer.Start();
        }

        private void LogoutBtn_Click(object sender, EventArgs e)
        {
            DoLogout();
        }

        private void DoLogout()
        {
            _db.AddLogEntry(EntryType.Logout.ToString(), _currentUser.Id, null,
                $"Logout de {_currentUser.Username} - Duração: {(DateTime.Now - _sessionStart).TotalMinutes:F0}min");

            StopMonitoring();
            _security?.Dispose();
            _clockTimer?.Stop();
            _sessionTimer?.Stop();

            this.Close();
            Application.Restart();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            using (var dialog = new ExitPasswordDialog(_db))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    StopMonitoring();
                    _security?.Dispose();
                    _clockTimer?.Stop();
                    _sessionTimer?.Stop();
                    SecurityManager.RemoveAutoStart();
                }
                else
                {
                    e.Cancel = true;
                }
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F11 ||
                (keyData & Keys.Alt) == Keys.Alt && (keyData & Keys.F4) == Keys.F4 ||
                ((keyData & Keys.Control) == Keys.Control && (keyData & Keys.Shift) == Keys.Shift && (keyData & Keys.Escape) == Keys.Escape))
            {
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            this.WindowState = FormWindowState.Maximized;
        }
    }

    internal class ExitPasswordDialog : Form
    {
        private TextBox _passwordBox;
        private readonly DatabaseManager _db;

        public ExitPasswordDialog(DatabaseManager db)
        {
            _db = db;
            this.Text = "Confirmação de Encerramento";
            this.Size = new Size(400, 200);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(15, 15, 35);
            this.ForeColor = Color.White;

            var label = new Label
            {
                Text = "Digite a senha do administrador para encerrar:",
                Location = new Point(30, 30),
                AutoSize = true,
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.FromArgb(200, 200, 220)
            };

            _passwordBox = new TextBox
            {
                Location = new Point(30, 60),
                Size = new Size(320, 25),
                PasswordChar = '\u25CF',
                Font = new Font("Segoe UI", 11),
                BackColor = Color.FromArgb(26, 26, 46),
                ForeColor = Color.White
            };
            _passwordBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) Verify(); };

            var okBtn = new Button
            {
                Text = "Confirmar",
                Location = new Point(150, 105),
                Size = new Size(90, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(124, 58, 237),
                ForeColor = Color.White
            };
            okBtn.FlatAppearance.BorderSize = 0;
            okBtn.Click += (s, e) => Verify();

            var cancelBtn = new Button
            {
                Text = "Cancelar",
                Location = new Point(250, 105),
                Size = new Size(90, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(60, 60, 80),
                ForeColor = Color.White
            };
            cancelBtn.FlatAppearance.BorderSize = 0;
            cancelBtn.Click += (s, e) => this.DialogResult = DialogResult.Cancel;

            this.Controls.AddRange(new Control[] { label, _passwordBox, okBtn, cancelBtn });
        }

        private void Verify()
        {
            if (_db.VerifyAdminPassword(_passwordBox.Text))
            {
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            else
            {
                MessageBox.Show("Senha incorreta.", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _passwordBox.Text = "";
                _passwordBox.Focus();
            }
        }
    }
}
