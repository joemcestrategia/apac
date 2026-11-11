using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Timers;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;
using ApacKiosk.Database;
using ApacKiosk.Services;
using ApacKiosk.Utils;
using Timer = System.Windows.Forms.Timer;

namespace ApacKiosk.Forms
{
    public class KioskBrowserForm : Form
    {
        private readonly DatabaseManager _db;
        private readonly ConfigManager _config;
        private readonly MonitorService _monitor;
        private readonly SecurityManager _security;
        private readonly Models.User _user;
        private readonly SiteService _siteService;
        private WebView2 _webView;
        private Panel _navBar;
        private Button _btnBack;
        private Button _btnForward;
        private TextBox _urlBox;
        private Button _btnHome;
        private Panel _infoPanel;
        private Label _infoDateLabel;
        private Label _infoTimeLabel;
        private Label _infoSessionLabel;
        private Label _timerLabel;
        private Label _userLabel;
        private System.Windows.Forms.PictureBox _userPhoto;
        private Timer _sessionTimer;
        private DateTime _loginTime;
        private int _elapsedSeconds;
        private int _maxSessionSeconds;
        private int _pauseAfterSeconds;
        private int _mandatoryPauseSec;
        private int _consecutiveSec;
        private DateTime _lastPauseEnd;
        private long _sessionLogId;
        private readonly string _blockedPagePath;
        private bool _isShuttingDown;

        public KioskBrowserForm(DatabaseManager db, ConfigManager config,
            MonitorService monitor, SecurityManager security, Models.User user)
        {
            _db = db;
            _siteService = new SiteService(db);
            _config = config;
            _monitor = monitor;
            _security = security;
            _user = user;
            _loginTime = DateTime.Now;
            _blockedPagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "blocked.html");

            LoadProfileSettings();
            InitializeComponent();
            StartSession();
        }

        private void LoadProfileSettings()
        {
            if (!_user.ProfileId.HasValue) return;

            using var conn = _db.GetConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT max_session_minutes, pause_after_minutes, mandatory_pause_minutes 
                               FROM access_profiles WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", _user.ProfileId.Value);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                _maxSessionSeconds = reader.GetInt32(0) * 60;
                _pauseAfterSeconds = reader.GetInt32(1) * 60;
                _mandatoryPauseSec = reader.GetInt32(2) * 60;
            }
        }

        private void InitializeComponent()
        {
            Text = _config.DisplayName;
            WindowState = FormWindowState.Maximized;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.FromArgb(15, 15, 35);
            TopMost = true;

            _navBar = new Panel
            {
                Height = 48,
                Dock = DockStyle.Top,
                BackColor = Color.FromArgb(22, 22, 45),
                Padding = new Padding(6, 4, 6, 4)
            };

            _btnBack = new Button
            {
                Text = "◀",
                Font = new Font("Segoe UI", 11),
                Size = new Size(40, 36),
                Location = new Point(6, 6),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(35, 35, 60),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            _btnBack.FlatAppearance.BorderSize = 0;
            _btnBack.Click += (s, e) => { if (_webView?.CoreWebView2 != null && _webView.CanGoBack) _webView.GoBack(); };

            _btnForward = new Button
            {
                Text = "▶",
                Font = new Font("Segoe UI", 11),
                Size = new Size(40, 36),
                Location = new Point(50, 6),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(35, 35, 60),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            _btnForward.FlatAppearance.BorderSize = 0;
            _btnForward.Click += (s, e) => { if (_webView?.CoreWebView2 != null && _webView.CanGoForward) _webView.GoForward(); };

            _urlBox = new TextBox
            {
                ReadOnly = true,
                Location = new Point(96, 8),
                Size = new Size(600, 30),
                Font = new Font("Consolas", 11),
                BackColor = Color.FromArgb(26, 26, 46),
                ForeColor = Color.FromArgb(167, 139, 250),
                BorderStyle = BorderStyle.None
            };

            _btnHome = new Button
            {
                Text = "⌂",
                Font = new Font("Segoe UI", 13),
                Size = new Size(40, 36),
                Location = new Point(702, 6),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(35, 35, 60),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            _btnHome.FlatAppearance.BorderSize = 0;
            _btnHome.Click += (s, e) => NavigateHome();

            _timerLabel = new Label
            {
                Text = "🕐 --:--",
                Font = new Font("Consolas", 12, FontStyle.Bold),
                ForeColor = Color.FromArgb(167, 139, 250),
                Location = new Point(750, 10),
                AutoSize = true
            };

            _infoPanel = new Panel
            {
                Height = 34,
                Width = 280,
                BackColor = Color.FromArgb(124, 58, 237),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            _infoTimeLabel = new Label
            {
                Text = DateTime.Now.ToString("HH:mm:ss"),
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(10, 5),
                BackColor = Color.Transparent
            };

            _infoDateLabel = new Label
            {
                Text = DateTime.Now.ToString("dd/MM/yyyy"),
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(220, 220, 255),
                AutoSize = true,
                Location = new Point(10, 19),
                BackColor = Color.Transparent
            };

            _infoSessionLabel = new Label
            {
                Text = "⏱  00:00",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(130, 5),
                BackColor = Color.Transparent
            };

            var sepLine = new Label
            {
                Text = "│",
                Font = new Font("Segoe UI", 14),
                ForeColor = Color.FromArgb(180, 140, 255),
                AutoSize = true,
                Location = new Point(120, 3),
                BackColor = Color.Transparent
            };

            _infoPanel.Controls.AddRange(new Control[] { _infoTimeLabel, _infoDateLabel, _infoSessionLabel, sepLine });

            var clockTimer = new Timer { Interval = 1000 };
            clockTimer.Tick += (s, e) =>
            {
                var now = DateTime.Now;
                _infoTimeLabel.Text = now.ToString("HH:mm:ss");
                _infoDateLabel.Text = now.ToString("dd/MM/yyyy");
            };
            clockTimer.Start();

            _userLabel = new Label
            {
                Text = _user.FullName,
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.FromArgb(200, 200, 200),
                AutoSize = true
            };

            _userPhoto = new System.Windows.Forms.PictureBox
            {
                Size = new Size(30, 30),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(35, 35, 60)
            };
            if (!string.IsNullOrEmpty(_user.PhotoPath) && File.Exists(_user.PhotoPath))
            {
                try { _userPhoto.Image = Image.FromFile(_user.PhotoPath); } catch { }
            }

            _navBar.Controls.AddRange(new Control[] { _btnBack, _btnForward, _urlBox, _btnHome, _timerLabel, _infoPanel, _userLabel, _userPhoto });

            _webView = new WebView2 { Dock = DockStyle.Fill };
            
            var userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApacKiosk", "WebView2Profile");
            _webView.CreationProperties = new CoreWebView2CreationProperties
            {
                UserDataFolder = userDataFolder,
                AdditionalBrowserArguments = "--no-sandbox --disable-gpu-sandbox"
            };

            Controls.Add(_webView);
            Controls.Add(_navBar);
        }

        private async void InitializeWebView()
        {
            try
            {
                await _webView.EnsureCoreWebView2Async();


                _webView.CoreWebView2.NewWindowRequested += (s, e) =>
                {
                    e.Handled = true;
                    _webView.CoreWebView2.Navigate(e.Uri);
                };

                _webView.CoreWebView2.DownloadStarting += (s, e) =>
                {
                    e.Cancel = true;
                    e.Handled = true;
                };

                _webView.CoreWebView2.NavigationStarting += OnNavigationStarting;
                _webView.CoreWebView2.NavigationCompleted += (s, e) =>
                {
                    if (!e.IsSuccess)
                    {
                        var logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "webview_error.log");
                        File.AppendAllText(logFile, $"{DateTime.Now}: Navigation failed. URL: {_webView.Source}, ErrorStatus: {e.WebErrorStatus}\n");
                        try { _db.InsertLog(_user.Id, "error", null, $"Navigation failed: {e.WebErrorStatus}"); } catch { }
                    }
                };

                _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                _webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
                _webView.CoreWebView2.Settings.AreHostObjectsAllowed = false;

                NavigateHome();
            }
            catch (Exception ex)
            {
                var logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "webview_error.log");
                File.AppendAllText(logFile, $"{DateTime.Now}: Critical error initializing WebView2: {ex}\n");
                KioskDialog.ShowMessage($"Erro crítico ao inicializar o navegador: {ex.Message}\n\nVerifique webview_error.log", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error, this);
                try { _db.InsertLog(_user.Id, "error", null, $"WebView2 init error: {ex.Message}"); } catch { }
            }
        }

        private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (e.Uri == null) return;

            var uriString = e.Uri;
            if (!Uri.TryCreate(uriString, UriKind.Absolute, out var uri))
            {
                e.Cancel = true;
                return;
            }

            var scheme = uri.Scheme?.ToLowerInvariant() ?? "";
            var blockedSchemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "file", "edge", "chrome", "javascript", "data", "blob",
                "about", "ms-settings", "ms-windows-store"
            };

            if (blockedSchemes.Contains(scheme))
            {
                e.Cancel = true;
                return;
            }

            if (_siteService.IsUrlAllowed(e.Uri, _user.ProfileId))
            {
                _urlBox.Text = uri.ToString();
                _db.InsertLog(_user.Id, "navigation_allowed", null, uri.ToString());
            }
            else
            {
                _db.InsertLog(_user.Id, "navigation_blocked", null, $"Bloqueado: {uri}");
                e.Cancel = true;
                NavigateToBlockedPage(uri.ToString());
            }
        }

        private void NavigateToBlockedPage(string url)
        {
            if (_webView?.CoreWebView2 == null) return;

            var html = "";
            if (File.Exists(_blockedPagePath))
                html = File.ReadAllText(_blockedPagePath);
            else
                html = $"<html><body style='font-family:sans-serif;text-align:center;padding:50px;'><h1 style='color:red;'>Acesso Bloqueado</h1><p>{url}</p><p>Este site não está disponível.</p></body></html>";

            _webView.NavigateToString(html);
        }

        private void NavigateHome()
        {
            if (_webView?.CoreWebView2 == null) return;
            var homepage = GetHomepage();
            _webView.CoreWebView2.Navigate(homepage);
        }

        private string GetHomepage()
        {
            if (_user.ProfileId.HasValue)
            {
                using var conn = _db.GetConnection();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT homepage_url FROM access_profiles WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", _user.ProfileId.Value);
                var url = cmd.ExecuteScalar()?.ToString();
                if (!string.IsNullOrEmpty(url)) return url;
            }
            return "https://www.google.com";
        }

        private void StartSession()
        {
            using var conn = _db.GetConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO session_logs (user_id, login_time) VALUES (@uid, @lt); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@uid", _user.Id);
            cmd.Parameters.AddWithValue("@lt", _loginTime);
            _sessionLogId = (long)cmd.ExecuteScalar();

            _consecutiveSec = 0;
            _lastPauseEnd = _loginTime;

            _sessionTimer = new Timer { Interval = 1000 };
            _sessionTimer.Tick += SessionTimer_Tick;
            _sessionTimer.Start();
        }

        private void SessionTimer_Tick(object sender, EventArgs e)
        {
            _elapsedSec = (int)(DateTime.Now - _loginTime).TotalSeconds;

            if (_maxSessionSeconds > 0 && _elapsedSec >= _maxSessionSeconds)
            {
                _monitor?.Keylog?.SetCurrentUser(null);
                _monitor?.Screenshot?.SetCurrentUser(null);
                _monitor?.Camera?.SetCurrentUser(null);
                KioskDialog.ShowMessage("Sua sessão expirou. Tempo máximo de uso atingido.",
                    _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information, this);
                EndSession();
                BeginInvoke(new Action(() => Close()));
                return;
            }

            if (_mandatoryPauseSec > 0 && _pauseAfterSeconds > 0)
            {
                var sinceLastPause = (int)(DateTime.Now - _lastPauseEnd).TotalSeconds;
                if (sinceLastPause >= _pauseAfterSeconds)
                {
                    ShowMandatoryPause();
                }
            }

            var remaining = _maxSessionSeconds > 0 ? _maxSessionSeconds - _elapsedSec : int.MaxValue;
            UpdateTimerDisplay(remaining);
            UpdateElapsedDisplay();
        }
        private int _elapsedSec;

        private void UpdateElapsedDisplay()
        {
            var hours = _elapsedSec / 3600;
            var minutes = (_elapsedSec % 3600) / 60;
            var seconds = _elapsedSec % 60;
            var timerText = hours > 0
                ? $"⏱  {hours:D2}:{minutes:D2}:{seconds:D2}"
                : $"⏱  {minutes:D2}:{seconds:D2}";
            if (_infoSessionLabel.InvokeRequired)
                _infoSessionLabel.BeginInvoke(new Action(() => _infoSessionLabel.Text = timerText));
            else
                _infoSessionLabel.Text = timerText;
        }

        private void UpdateTimerDisplay(int remaining)
        {
            if (_maxSessionSeconds == 0 && _timerLabel.InvokeRequired)
            {
                _timerLabel.BeginInvoke(new Action(() => _timerLabel.Text = "🕐 ∞"));
                return;
            }
            if (_timerLabel.InvokeRequired)
            {
                _timerLabel.BeginInvoke(new Action(() =>
                {
                    var ts = TimeSpan.FromSeconds(remaining);
                    _timerLabel.Text = $"🕐 {ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
                }));
            }
            else
            {
                _timerLabel.Text = _maxSessionSeconds == 0 ? "🕐 ∞" :
                    $"🕐 {TimeSpan.FromSeconds(remaining).Hours:D2}:{TimeSpan.FromSeconds(remaining).Minutes:D2}:{TimeSpan.FromSeconds(remaining).Seconds:D2}";
            }
        }

        private void ShowMandatoryPause()
        {
            _sessionTimer.Stop();

            _monitor?.Keylog?.SetCurrentUser(null);
            _monitor?.Screenshot?.SetCurrentUser(null);
            _monitor?.Camera?.SetCurrentUser(null);

            _db.InsertLog(_user.Id, "mandatory_pause", null,
                $"Pausa obrigatória iniciada: {_mandatoryPauseSec / 60}min");

            // Overlay de pausa obrigatória dentro da janela principal
            var overlay = new Panel
            {
                Size = ClientSize,
                Location = new Point(0, 0),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(1, 1, 1)
            };
            overlay.Paint += (s2, e2) =>
            {
                using var brush = new SolidBrush(Color.FromArgb(200, 0, 0, 0));
                e2.Graphics.FillRectangle(brush, overlay.ClientRectangle);
            };

            var pauseLabel = new Label
            {
                Text = $"⏸ Pausa obrigatória\nAguarde {_mandatoryPauseSec / 60} minuto(s) antes de continuar.",
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false,
                Size = new Size(600, 120),
                BackColor = Color.Transparent
            };
            pauseLabel.Location = new Point(
                (overlay.Width - pauseLabel.Width) / 2,
                (overlay.Height - pauseLabel.Height) / 2
            );
            overlay.Controls.Add(pauseLabel);

            Controls.Add(overlay);
            overlay.BringToFront();

            bool pauseDone = false;
            var pauseTimer = new Timer { Interval = 1000 };
            var pauseStart = DateTime.Now;
            pauseTimer.Tick += (s, args) =>
            {
                var elapsed = (DateTime.Now - pauseStart).TotalSeconds;
                if (elapsed >= _mandatoryPauseSec)
                {
                    pauseTimer.Stop();
                    pauseDone = true;
                    _lastPauseEnd = DateTime.Now;
                    _monitor?.Keylog?.SetCurrentUser(_user.Id);
                    _monitor?.Screenshot?.SetCurrentUser(_user.Id);
                    _monitor?.Camera?.SetCurrentUser(_user.Id);
                    _sessionTimer.Start();
                    _db.InsertLog(_user.Id, "mandatory_pause_end", null, "Pausa obrigatória finalizada");
                    Controls.Remove(overlay);
                    overlay.Dispose();
                    return;
                }
                var remaining = (int)(_mandatoryPauseSec - elapsed);
                pauseLabel.Text = $"⏸ Pausa obrigatória\nAguarde {remaining / 60:D2}:{remaining % 60:D2} antes de continuar.";
            };
            pauseTimer.Start();

            while (!pauseDone && !overlay.IsDisposed)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(10);
            }
        }

        private void EndSession()
        {
            _sessionTimer?.Stop();
            _monitor?.StopAll();

            using var conn = _db.GetConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE session_logs SET logout_time = @lt, duration_seconds = @d WHERE id = @id";
            cmd.Parameters.AddWithValue("@lt", DateTime.Now);
            cmd.Parameters.AddWithValue("@d", (int)(DateTime.Now - _loginTime).TotalSeconds);
            cmd.Parameters.AddWithValue("@id", _sessionLogId);
            cmd.ExecuteNonQuery();

            _db.InsertLog(_user.Id, "logout", null, $"Logout: {_user.FullName}");
            _db.CleanupOldLogs();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_isShuttingDown)
            {
                base.OnFormClosing(e);
                return;
            }

            e.Cancel = true;

            var (result, pwd) = KioskDialog.ShowPasswordPrompt(
                "Digite a senha de administrador para fechar:",
                "Senha para Fechar", this);

            if (result == DialogResult.OK)
            {
                using var conn = _db.GetConnection();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT password_hash FROM admins WHERE username = 'admin'";
                var hash = cmd.ExecuteScalar()?.ToString();
                if (hash != null && PasswordHash.VerifyPassword(pwd, hash))
                {
                    _isShuttingDown = true;
                    EndSession();
                    _security?.DisableAllSecurity();
                    DialogResult = DialogResult.OK;
                    Environment.Exit(0);
                }
                else
                {
                    KioskDialog.ShowMessage("Senha inválida.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error, this);
                }
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _infoPanel.Location = new Point(ClientSize.Width - _infoPanel.Width - 80, 5);
            try
            {
                _security?.SetMainWindow(Handle);
                _security?.EnableAllSecurity();
                _monitor?.SetSessionInfo(_user.Id, _user.Username);
                _monitor?.StartAll();
                
                InitializeWebView();
            }
            catch (Exception ex)
            {
                try { _db.InsertLog(_user.Id, "error", null, $"OnShown: {ex.Message}"); } catch { }
                _monitor?.StopAll();
            }
        }
    }
}
