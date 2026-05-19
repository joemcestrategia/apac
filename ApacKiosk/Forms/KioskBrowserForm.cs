using System;
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
        private WebView2 _webView;
        private Panel _navBar;
        private Button _btnBack;
        private Button _btnForward;
        private TextBox _urlBox;
        private Button _btnHome;
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
            _config = config;
            _monitor = monitor;
            _security = security;
            _user = user;
            _loginTime = DateTime.Now;
            _blockedPagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "blocked.html");

            LoadProfileSettings();
            InitializeComponent();
            InitializeWebView();
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

            _navBar.Controls.AddRange(new Control[] { _btnBack, _btnForward, _urlBox, _btnHome, _timerLabel, _userLabel, _userPhoto });

            _webView = new WebView2 { Dock = DockStyle.Fill };

            Controls.Add(_webView);
            Controls.Add(_navBar);
        }

        private async void InitializeWebView()
        {
            await _webView.EnsureCoreWebView2Async(null);
            var env = await CoreWebView2Environment.CreateAsync();

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
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _webView.CoreWebView2.Settings.IsZoomControlEnabled = false;

            NavigateHome();
        }

        private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (e.Uri == null) return;

            var uri = new Uri(e.Uri);

            if (uri.ToString().StartsWith("about:"))
            {
                _db.InsertLog(_user.Id, "navigation_blocked", null, $"Bloqueado: {uri}");
                e.Cancel = true;
                NavigateToBlockedPage(uri.ToString());
                return;
            }

            if (IsUrlAllowed(uri))
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

        private bool IsUrlAllowed(Uri uri)
        {
            using var conn = _db.GetConnection();
            var cmd = conn.CreateCommand();
            var host = uri.Host;

            if (_user.ProfileId.HasValue)
            {
                cmd.CommandText = @"SELECT url_pattern FROM allowed_sites WHERE profile_id = @pid OR is_global = 1";
                cmd.Parameters.AddWithValue("@pid", _user.ProfileId.Value);
            }
            else
            {
                cmd.CommandText = @"SELECT url_pattern FROM allowed_sites WHERE is_global = 1";
            }

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var pattern = reader.GetString(0).Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(pattern)) continue;

                if (pattern.Contains("*"))
                {
                    var regexPattern = ConvertWildcardToRegex(pattern);
                    if (Regex.IsMatch(host, regexPattern, RegexOptions.IgnoreCase))
                        return true;
                    if (Regex.IsMatch(uri.ToString().ToLowerInvariant(), regexPattern, RegexOptions.IgnoreCase))
                        return true;
                }
                else
                {
                    if (host.Contains(pattern) || uri.ToString().ToLowerInvariant().Contains(pattern.ToLowerInvariant()))
                        return true;
                }
            }
            return false;
        }

        private static string ConvertWildcardToRegex(string pattern)
        {
            pattern = pattern.Replace(".", "\\.");
            pattern = pattern.Replace("*", ".*");
            return "^" + pattern + "$";
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
                MessageBox.Show("Sua sessão expirou. Tempo máximo de uso atingido.",
                    _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        }
        private int _elapsedSec;

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

            var pauseForm = new Form
            {
                Text = "Pausa Obrigatória",
                Size = new Size(500, 300),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                BackColor = Color.FromArgb(15, 15, 35),
                TopMost = true,
                ControlBox = false
            };

            var label = new Label
            {
                Text = $"Pausa obrigatória\nAguarde {_mandatoryPauseSec / 60} minuto(s) antes de continuar.",
                Font = new Font("Segoe UI", 14),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };
            pauseForm.Controls.Add(label);

            var pauseTimer = new Timer { Interval = 1000 };
            var pauseStart = DateTime.Now;
            pauseTimer.Tick += (s, args) =>
            {
                var elapsed = (DateTime.Now - pauseStart).TotalSeconds;
                if (elapsed >= _mandatoryPauseSec)
                {
                    pauseTimer.Stop();
                    pauseForm.Close();
                    _lastPauseEnd = DateTime.Now;
                    _monitor?.Keylog?.SetCurrentUser(_user.Id);
                    _monitor?.Screenshot?.SetCurrentUser(_user.Id);
                    _monitor?.Camera?.SetCurrentUser(_user.Id);
                    _sessionTimer.Start();
                    _db.InsertLog(_user.Id, "mandatory_pause_end", null, "Pausa obrigatória finalizada");
                    return;
                }
                var remaining = (int)(_mandatoryPauseSec - elapsed);
                label.Text = $"Pausa obrigatória\nAguarde {remaining / 60:D2}:{remaining % 60:D2} antes de continuar.";
            };
            pauseForm.Shown += (s, args) => pauseTimer.Start();
            pauseForm.ShowDialog();
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

            var pwdPrompt = new Form
            {
                Text = "Senha para Fechar",
                Size = new Size(400, 200),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                BackColor = Color.FromArgb(15, 15, 35),
                TopMost = true
            };

            var lbl = new Label
            {
                Text = "Digite a senha de administrador para fechar:",
                Font = new Font("Segoe UI", 11),
                ForeColor = Color.White,
                Location = new Point(20, 20),
                AutoSize = true
            };

            var txt = new TextBox
            {
                Location = new Point(20, 60),
                Size = new Size(340, 30),
                PasswordChar = '●',
                Font = new Font("Segoe UI", 12),
                BackColor = Color.FromArgb(26, 26, 46),
                ForeColor = Color.White
            };

            var btnOk = new Button
            {
                Text = "Fechar",
                Location = new Point(150, 110),
                Size = new Size(100, 35),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(200, 50, 50),
                ForeColor = Color.White
            };
            btnOk.FlatAppearance.BorderSize = 0;

            var btnCancel = new Button
            {
                Text = "Cancelar",
                Location = new Point(260, 110),
                Size = new Size(100, 35),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(42, 42, 70),
                ForeColor = Color.White,
                DialogResult = DialogResult.Cancel
            };
            btnCancel.FlatAppearance.BorderSize = 0;

            btnOk.Click += (s, args) =>
            {
                using var conn = _db.GetConnection();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT password_hash FROM admins WHERE username = 'admin'";
                var hash = cmd.ExecuteScalar()?.ToString();
                if (hash != null && PasswordHash.VerifyPassword(txt.Text, hash))
                {
                    pwdPrompt.DialogResult = DialogResult.OK;
                    pwdPrompt.Close();
                }
                else
                {
                    MessageBox.Show("Senha inválida.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            pwdPrompt.Controls.AddRange(new Control[] { lbl, txt, btnOk, btnCancel });
            pwdPrompt.AcceptButton = btnOk;
            pwdPrompt.CancelButton = btnCancel;

            if (pwdPrompt.ShowDialog() == DialogResult.OK)
            {
                _isShuttingDown = true;
                EndSession();
                _security?.DisableAllSecurity();
                DialogResult = DialogResult.OK;
                Environment.Exit(0);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _security?.SetMainWindow(Handle);
            _monitor?.SetCurrentUser(_user.Id);
            _monitor?.StartAll();
        }
    }
}
