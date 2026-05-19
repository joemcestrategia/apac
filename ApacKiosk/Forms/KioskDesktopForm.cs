using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;
using ApacKiosk.Database;
using ApacKiosk.Services;
using ApacKiosk.Utils;
using Timer = System.Windows.Forms.Timer;

namespace ApacKiosk.Forms
{
    public class KioskDesktopForm : Form
    {
        private readonly DatabaseManager _db;
        private readonly ConfigManager _config;
        private readonly MonitorService _monitor;
        private readonly SecurityManager _security;
        private readonly Models.User _user;
        private readonly SiteService _siteService;
        private readonly ProgramService _programService;
        private readonly ProfileService _profileService;

        private NotifyIcon _trayIcon;
        private Panel _topBar;
        private Label _userNameLabel;
        private Label _clockLabel;
        private PictureBox _photoBox;
        private PictureBox _logoBox;
        private Panel _shortcutsPanel;
        private WebView2 _webView;
        private Panel _browserPanel;
        private Button _btnBack;
        private Button _btnHome;
        private TextBox _urlBox;
        private Button _btnGo;
        private Button _btnCloseBrowser;
        private Button _btnMinimize;

        private int _sessionSeconds;
        private Timer _sessionTimer;
        private bool _isShuttingDown;
        private bool _isMinimized;

        public KioskDesktopForm(DatabaseManager db, ConfigManager config,
            MonitorService monitor, SecurityManager security, Models.User user)
        {
            _db = db;
            _config = config;
            _monitor = monitor;
            _security = security;
            _user = user;
            _siteService = new SiteService(db);
            _programService = new ProgramService(db);
            _profileService = new ProfileService(db);

            InitializeComponent();
            SetupTrayIcon();
            LoadShortcuts();
            _sessionTimer = new Timer { Interval = 1000 };
            _sessionTimer.Tick += SessionTimer_Tick;
            _sessionTimer.Start();
        }

        private void InitializeComponent()
        {
            Text = _config.DisplayName;
            Size = new Size(1280, 800);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.FromArgb(10, 10, 25);
            WindowState = FormWindowState.Maximized;
            TopMost = true;
            DoubleBuffered = true;

            _topBar = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Color.FromArgb(18, 18, 40) };

            _logoBox = new PictureBox { Size = new Size(36, 36), Location = new Point(12, 10), SizeMode = PictureBoxSizeMode.Zoom };
            LoadLogo();

            _userNameLabel = new Label
            {
                Text = _user.FullName,
                Font = new Font("Segoe UI", 13, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(55, 14),
                AutoSize = true
            };

            _photoBox = new PictureBox
            {
                Size = new Size(32, 32), Location = new Point(5, 12), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(30, 30, 55)
            };
            using (var path = GetRoundRect(0, 0, 32, 32, 16)) _photoBox.Region = new Region(path);
            LoadUserPhoto();

            _btnCloseBrowser = new Button
            {
                Text = "✕", Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Size = new Size(30, 30), Location = new Point(2, 4), FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(200, 50, 50), ForeColor = Color.White, Cursor = Cursors.Hand, Visible = false
            };
            _btnCloseBrowser.FlatAppearance.BorderSize = 0;
            _btnCloseBrowser.Click += (s, e) => CloseBrowser();

            _btnBack = new Button
            {
                Text = "«", Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Size = new Size(30, 30), Location = new Point(37, 4), FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(40, 40, 65), ForeColor = Color.White, Cursor = Cursors.Hand, Visible = false
            };
            _btnBack.FlatAppearance.BorderSize = 0;
            _btnBack.Click += (s, e) => { if (_webView?.CanGoBack == true) _webView.GoBack(); };

            _urlBox = new TextBox
            {
                Size = new Size(400, 28), Location = new Point(75, 4),
                Font = new Font("Segoe UI", 10), BackColor = Color.FromArgb(26, 26, 46),
                ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, ReadOnly = true, Visible = false
            };

            _btnGo = new Button
            {
                Text = "Ir", Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Size = new Size(30, 30), Location = new Point(480, 4), FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(108, 50, 210), ForeColor = Color.White, Cursor = Cursors.Hand, Visible = false
            };
            _btnGo.FlatAppearance.BorderSize = 0;
            _btnGo.Click += (s, e) => NavigateToUrl(_urlBox.Text);

            _btnHome = new Button
            {
                Text = "⌂", Font = new Font("Segoe UI", 12),
                Size = new Size(30, 30), Location = new Point(515, 4), FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(40, 40, 65), ForeColor = Color.White, Cursor = Cursors.Hand, Visible = false
            };
            _btnHome.FlatAppearance.BorderSize = 0;
            _btnHome.Click += (s, e) => GoHome();

            _btnMinimize = new Button
            {
                Text = "─", Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Size = new Size(44, 36), FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(18, 18, 40), ForeColor = Color.FromArgb(180, 180, 200),
                Cursor = Cursors.Hand, Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _btnMinimize.FlatAppearance.BorderSize = 0;
            _btnMinimize.Click += (s, e) => MinimizeToTray();
            _btnMinimize.Location = new Point(1224, 10);

            _clockLabel = new Label
            {
                Font = new Font("Segoe UI", 11), ForeColor = Color.FromArgb(150, 150, 175),
                Location = new Point(1080, 16), AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Text = DateTime.Now.ToString("HH:mm") + "  " + DateTime.Now.ToString("dd/MM/yyyy")
            };

            var clockTimer = new Timer { Interval = 30000 };
            clockTimer.Tick += (s, e) =>
            {
                _clockLabel.Text = DateTime.Now.ToString("HH:mm") + "  " + DateTime.Now.ToString("dd/MM/yyyy");
            };
            clockTimer.Start();

            _topBar.Controls.AddRange(new Control[] { _logoBox, _userNameLabel, _photoBox,
                _btnCloseBrowser, _btnBack, _urlBox, _btnGo, _btnHome, _clockLabel, _btnMinimize });

            _shortcutsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(20),
                BackColor = Color.FromArgb(10, 10, 25)
            };

            _browserPanel = new Panel { Dock = DockStyle.Fill, Visible = false, BackColor = Color.FromArgb(10, 10, 25) };

            var browserTopBar = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = Color.FromArgb(18, 18, 40) };
            browserTopBar.Controls.AddRange(new Control[] { _btnCloseBrowser, _btnBack, _urlBox, _btnGo, _btnHome });
            // Clone-like controls - original buttons are in topBar, let me place a copy in the browser panel
            // Actually, let me just position controls correctly: in topBar for normal, in browserPanel for browser

            _webView = new WebView2 { Dock = DockStyle.Fill };
            _webView.NavigationStarting += OnNavigationStarting;

            _browserPanel.Controls.Add(browserTopBar);
            _browserPanel.Controls.Add(_webView);

            Controls.Add(_browserPanel);
            Controls.Add(_shortcutsPanel);
            Controls.Add(_topBar);

            Shown += async (s, e) =>
            {
                await _webView.EnsureCoreWebView2Async(null);
                _webView.CoreWebView2.DOMContentLoaded += (_, _) =>
                {
                    _urlBox.Text = _webView.Source?.ToString() ?? "";
                };
            };
        }

        private void SetupTrayIcon()
        {
            _trayIcon = new NotifyIcon
            {
                Text = _config.DisplayName + " — " + _user.FullName,
                Visible = true
            };

            try
            {
                var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "LogoApac.png");
                if (File.Exists(iconPath))
                {
                    using var bmp = new Bitmap(iconPath);
                    _trayIcon.Icon = Icon.FromHandle(new Bitmap(bmp, 32, 32).GetHicon());
                }
            }
            catch { }

            var menu = new ContextMenuStrip();

            var showItem = new ToolStripMenuItem("Mostrar Janela");
            showItem.Click += (s, e) => RestoreFromTray();
            menu.Items.Add(showItem);

            menu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem("Fechar (Requer Admin)");
            exitItem.Click += (s, e) => { RestoreFromTray(); Close(); };
            menu.Items.Add(exitItem);

            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += (s, e) => RestoreFromTray();
        }

        private void MinimizeToTray()
        {
            _isMinimized = true;
            Hide();
            _trayIcon.ShowBalloonTip(2000, _config.DisplayName,
                "O sistema continua ativo na bandeja. Clique duas vezes para restaurar.", ToolTipIcon.Info);
        }

        private void RestoreFromTray()
        {
            _isMinimized = false;
            Show();
            WindowState = FormWindowState.Maximized;
            Activate();
        }

        private void LoadLogo()
        {
            var paths = new[] {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "LogoApac.png"),
                Path.Combine(Application.StartupPath, "Resources", "LogoApac.png"),
            };
            foreach (var p in paths)
            {
                if (File.Exists(p))
                {
                    try { _logoBox.Image = Image.FromFile(p); return; } catch { }
                }
            }
        }

        private void LoadUserPhoto()
        {
            if (!string.IsNullOrEmpty(_user.PhotoPath) && File.Exists(_user.PhotoPath))
            {
                try { _photoBox.Image = Image.FromFile(_user.PhotoPath); } catch { }
            }
        }

        private void LoadShortcuts()
        {
            _shortcutsPanel.Controls.Clear();

            var sites = _siteService.GetForProfile(_user.ProfileId);
            var programs = _programService.GetForProfile(_user.ProfileId);
            var profile = _user.ProfileId.HasValue ? _profileService.GetById(_user.ProfileId.Value) : null;
            var homeUrl = profile?.HomepageUrl ?? "https://www.google.com";

            foreach (var site in sites)
            {
                var card = CreateShortcutCard(site.UrlPattern, GetDomainName(site.UrlPattern), "🌐", Color.FromArgb(59, 130, 246));
                card.Click += (s, e) => OpenWebsite(site.UrlPattern);
                _shortcutsPanel.Controls.Add(card);
            }

            foreach (var prog in programs)
            {
                var card = CreateShortcutCard(prog.Name, prog.Name, "💻", Color.FromArgb(16, 185, 129));
                card.Click += (s, e) => LaunchProgram(prog.ExecutablePath, prog.Arguments);
                _shortcutsPanel.Controls.Add(card);
            }
        }

        private Panel CreateShortcutCard(string id, string name, string icon, Color accent)
        {
            var card = new Panel
            {
                Size = new Size(150, 140),
                BackColor = Color.FromArgb(22, 22, 45),
                Margin = new Padding(8, 6, 8, 6),
                Cursor = Cursors.Hand
            };
            using (var path = GetRoundRect(0, 0, 150, 140, 12))
                card.Region = new Region(path);

            var iconLabel = new Label
            {
                Text = icon,
                Font = new Font("Segoe UI", 28),
                Location = new Point(0, 15),
                Size = new Size(150, 45),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };

            var accentBar = new Panel
            {
                Size = new Size(40, 4),
                Location = new Point(55, 60),
                BackColor = accent
            };

            var nameLabel = new Label
            {
                Text = name.Length > 18 ? name.Substring(0, 16) + ".." : name,
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.FromArgb(220, 220, 240),
                Location = new Point(5, 72),
                Size = new Size(140, 60),
                TextAlign = ContentAlignment.TopCenter,
                BackColor = Color.Transparent
            };

            card.Controls.AddRange(new Control[] { iconLabel, accentBar, nameLabel });
            card.MouseEnter += (s, e) => card.BackColor = Color.FromArgb(35, 35, 60);
            card.MouseLeave += (s, e) => card.BackColor = Color.FromArgb(22, 22, 45);

            return card;
        }

        private void OpenWebsite(string url)
        {
            if (!url.StartsWith("http")) url = "https://" + url;
            _shortcutsPanel.Visible = false;
            _browserPanel.Visible = true;
            _btnCloseBrowser.Visible = true;
            _btnBack.Visible = true;
            _urlBox.Visible = true;
            _btnGo.Visible = true;
            _btnHome.Visible = true;
            _btnMinimize.Visible = false;
            NavigateToUrl(url);
        }

        private void CloseBrowser()
        {
            _shortcutsPanel.Visible = true;
            _browserPanel.Visible = false;
            _btnCloseBrowser.Visible = false;
            _btnBack.Visible = false;
            _urlBox.Visible = false;
            _btnGo.Visible = false;
            _btnHome.Visible = false;
            _btnMinimize.Visible = true;
            _webView?.CoreWebView2?.Navigate("about:blank");
            LoadShortcuts();
        }

        private void GoHome()
        {
            var profile = _user.ProfileId.HasValue ? _profileService.GetById(_user.ProfileId.Value) : null;
            var homeUrl = profile?.HomepageUrl ?? "https://www.google.com";
            NavigateToUrl(homeUrl);
        }

        private void NavigateToUrl(string url)
        {
            if (!url.StartsWith("http")) url = "https://" + url;
            if (_webView?.CoreWebView2 != null)
            {
                _webView.CoreWebView2.Navigate(url);
            }
        }

        private void LaunchProgram(string path, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(path)
                {
                    Arguments = args ?? "",
                    UseShellExecute = true
                };
                Process.Start(psi);
                _db.InsertLog(_user.Id, "program_launch", null, $"Programa aberto: {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Não foi possível abrir o programa:\n{ex.Message}", _config.DisplayName,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string GetDomainName(string url)
        {
            try
            {
                if (!url.StartsWith("http")) url = "https://" + url;
                var uri = new Uri(url);
                var domain = uri.Host.Replace("www.", "");
                if (domain.Length > 22) domain = domain.Substring(0, 20) + "..";
                return domain;
            }
            catch { return url; }
        }

        private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            var uri = e.Uri;
            if (_siteService.IsUrlAllowed(uri, _user.ProfileId))
            {
                _urlBox.Text = uri;
            }
            else
            {
                e.Cancel = true;
                _db.InsertLog(_user.Id, "blocked_site", uri, $"Site bloqueado: {uri}");
                _webView.CoreWebView2.NavigateToString(@"
                    <html><body style='background:#0a0a19;color:#fff;font-family:Segoe UI;display:flex;justify-content:center;align-items:center;height:100vh;text-align:center'>
                    <div><h1 style='color:#ef4444'>🚫 Site Bloqueado</h1><p>Apenas sites autorizados podem ser acessados.</p></div></body></html>");
            }
        }

        private void SessionTimer_Tick(object sender, EventArgs e)
        {
            _sessionSeconds++;

            var profile = _user.ProfileId.HasValue ? _profileService.GetById(_user.ProfileId.Value) : null;
            if (profile != null && profile.MaxSessionMinutes > 0)
            {
                var maxSeconds = profile.MaxSessionMinutes * 60;
                if (_sessionSeconds >= maxSeconds)
                {
                    _sessionTimer.Stop();
                    MessageBox.Show("Tempo máximo de sessão atingido. Sua sessão será encerrada.",
                        _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    DoShutdown();
                    return;
                }

                if (_sessionSeconds % 60 == 0)
                {
                    var remaining = maxSeconds - _sessionSeconds;
                    var remainingMin = remaining / 60;
                    if (remainingMin <= 5 && remainingMin > 0)
                    {
                        _trayIcon.ShowBalloonTip(3000, _config.DisplayName,
                            $"Atenção: {remainingMin} minuto(s) restante(s) de sessão.", ToolTipIcon.Warning);
                    }
                }
            }
        }

        private void DoShutdown()
        {
            _isShuttingDown = true;
            _monitor?.StopAll();
            _security?.DisableAllSecurity();
            EndSession();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            DialogResult = DialogResult.OK;
            Environment.Exit(0);
        }

        private void EndSession()
        {
            try
            {
                using var conn = _db.GetConnection();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE session_logs SET logout_time = datetime('now'), duration_seconds = @d WHERE user_id = @u AND logout_time IS NULL";
                cmd.Parameters.AddWithValue("@d", _sessionSeconds);
                cmd.Parameters.AddWithValue("@u", _user.Id);
                cmd.ExecuteNonQuery();

                _db.InsertLog(_user.Id, "logout", null, $"Logout: {_user.FullName} ({_sessionSeconds / 60} min)");

                cmd.CommandText = "INSERT INTO session_logs (user_id, login_time, logout_time, duration_seconds) VALUES (@u, datetime('now', @offset), datetime('now'), @d)";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@u", _user.Id);
                cmd.Parameters.AddWithValue("@offset", $"-{_sessionSeconds} seconds");
                cmd.Parameters.AddWithValue("@d", _sessionSeconds);
                cmd.ExecuteNonQuery();
            }
            catch { }
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
                BackColor = Color.FromArgb(22, 22, 45),
                TopMost = true
            };

            var lbl = new Label { Text = "Digite a senha de administrador para fechar:", Font = new Font("Segoe UI", 11), ForeColor = Color.White, Location = new Point(20, 20), AutoSize = true };
            var txt = new TextBox { Location = new Point(20, 60), Size = new Size(340, 30), PasswordChar = '\u25CF', Font = new Font("Segoe UI", 12), BackColor = Color.FromArgb(26, 26, 46), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
            var btnOk = new Button { Text = "Fechar", Location = new Point(150, 110), Size = new Size(100, 35), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(200, 50, 50), ForeColor = Color.White, Cursor = Cursors.Hand };
            btnOk.FlatAppearance.BorderSize = 0;
            var btnCancel = new Button { Text = "Cancelar", Location = new Point(260, 110), Size = new Size(100, 35), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(42, 42, 70), ForeColor = Color.White, Cursor = Cursors.Hand, DialogResult = DialogResult.Cancel };
            btnCancel.FlatAppearance.BorderSize = 0;

            btnOk.Click += (s, args) =>
            {
                using var conn = _db.GetConnection();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT password_hash FROM admins WHERE username = 'admin'";
                var hash = cmd.ExecuteScalar()?.ToString();
                if (hash != null && BCrypt.Net.BCrypt.Verify(txt.Text, hash))
                { pwdPrompt.DialogResult = DialogResult.OK; pwdPrompt.Close(); }
                else MessageBox.Show("Senha inválida.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            pwdPrompt.Controls.AddRange(new Control[] { lbl, txt, btnOk, btnCancel });
            pwdPrompt.AcceptButton = btnOk;
            pwdPrompt.CancelButton = btnCancel;

            if (pwdPrompt.ShowDialog() == DialogResult.OK)
            {
                _isShuttingDown = true;
                EndSession();
                _monitor?.StopAll();
                _security?.DisableAllSecurity();
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
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

            try
            {
                using var conn = _db.GetConnection();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO session_logs (user_id, login_time) VALUES (@u, datetime('now'))";
                cmd.Parameters.AddWithValue("@u", _user.Id);
                cmd.ExecuteNonQuery();
            }
            catch { }
        }

        private GraphicsPath GetRoundRect(int x, int y, int w, int h, int r)
        {
            var g = new GraphicsPath();
            g.AddArc(x, y, r * 2, r * 2, 180, 90);
            g.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
            g.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
            g.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
            g.CloseFigure();
            return g;
        }
    }
}
