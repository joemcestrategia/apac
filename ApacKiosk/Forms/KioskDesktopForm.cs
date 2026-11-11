using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;
using ApacKiosk;
using ApacKiosk.Database;
using ApacKiosk.Interop;
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
        private Panel _infoPanel;
        private Label _infoDateLabel;
        private Label _infoTimeLabel;
        private Label _infoSessionLabel;
        private Panel _browserInfoPanel;
        private Label _browserInfoDateLabel;
        private Label _browserInfoTimeLabel;
        private Label _browserInfoSessionLabel;
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
        private Button _btnLogout;
        private Button _btnLogoutBrowser;
        private Panel _welcomeOverlay;

        private int _sessionSeconds;
        private Timer _sessionTimer;
        private bool _isShuttingDown;
        private bool _isMinimized;
        private Process _launchedProcess;
        private IntPtr _launchedWindowHandle = IntPtr.Zero;
        private Timer _focusEnforcementTimer;
        private Timer _safetyTimer;
        private IntPtr _minimizeEventHook;
        private NativeMethods.WinEventDelegate _minimizeEventCallback;
        private readonly Dictionary<string, Process> _runningPrograms = new();
        private string? _pendingUrl;
        private string? _activeSitePattern;
        private string? _activeSiteUrl;

        public bool LogoutRequested { get; private set; }

        public KioskDesktopForm(DatabaseManager db, ConfigManager config,
            MonitorService monitor, SecurityManager security, Models.User user)
        {
            LogTrace("KioskDesktopForm.ctor iniciando");
            _db = db;
            _config = config;
            _monitor = monitor;
            _security = security;
            _user = user;
            _siteService = new SiteService(db);
            _programService = new ProgramService(db);
            _profileService = new ProfileService(db);

            LogTrace("KioskDesktopForm.InitializeComponent...");
            InitializeComponent();
            LogTrace("KioskDesktopForm.SetupTrayIcon...");
            SetupTrayIcon();
            LogTrace("KioskDesktopForm.LoadShortcuts...");
            LoadShortcuts();
            _sessionTimer = new Timer { Interval = 1000 };
            _sessionTimer.Tick += SessionTimer_Tick;
            _sessionTimer.Start();
            _safetyTimer = new Timer { Interval = 3000 };
            _safetyTimer.Tick += SafetyTimer_Tick;
            _safetyTimer.Start();
            LogTrace("KioskDesktopForm.ctor concluido");
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

            _logoBox = new PictureBox { Size = new Size(36, 36), Location = new Point(12, 10), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
            LoadLogo();

            _userNameLabel = new Label
            {
                Text = _user.FullName,
                Font = new Font("Segoe UI", 13, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(98, 14),
                AutoSize = true
            };

            _photoBox = new PictureBox
            {
                Size = new Size(36, 36), Location = new Point(55, 10), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(30, 30, 55)
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
                ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle,
                ReadOnly = true, Visible = false, Enabled = false
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

            _btnLogout = new Button
            {
                Text = "  ⏻  Sair",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Size = new Size(90, 36), FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(200, 55, 55), ForeColor = Color.White,
                Cursor = Cursors.Hand, Anchor = AnchorStyles.Top | AnchorStyles.Right,
                TextAlign = ContentAlignment.MiddleCenter
            };
            _btnLogout.FlatAppearance.BorderSize = 0;
            _btnLogout.Click += BtnLogout_Click;
            _btnLogout.MouseEnter += (s, e) => _btnLogout.BackColor = Color.FromArgb(239, 68, 68);
            _btnLogout.MouseLeave += (s, e) => _btnLogout.BackColor = Color.FromArgb(200, 55, 55);
            _btnLogout.Location = new Point(1100, 10);

            _btnMinimize = new Button
            {
                Text = "─", Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Size = new Size(44, 36), FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(18, 18, 40), ForeColor = Color.FromArgb(180, 180, 200),
                Cursor = Cursors.Hand, Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Visible = false
            };
            _btnMinimize.FlatAppearance.BorderSize = 0;
            _btnMinimize.Click += (s, e) => MinimizeToTray();
            _btnMinimize.Location = new Point(1202, 10);

            _btnLogoutBrowser = new Button
            {
                Text = "  ⏻  Sair",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Size = new Size(90, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(200, 55, 55),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                TextAlign = ContentAlignment.MiddleCenter,
                Visible = false
            };
            _btnLogoutBrowser.FlatAppearance.BorderSize = 0;
            _btnLogoutBrowser.Click += (s, e) => BtnLogout_Click(s, e);
            _btnLogoutBrowser.MouseEnter += (s, e) => _btnLogoutBrowser.BackColor = Color.FromArgb(239, 68, 68);
            _btnLogoutBrowser.MouseLeave += (s, e) => _btnLogoutBrowser.BackColor = Color.FromArgb(200, 55, 55);
            _btnLogoutBrowser.Location = new Point(1200, 6);

            _infoPanel = new Panel
            {
                Height = 40,
                Width = 380,
                BackColor = Color.FromArgb(124, 58, 237),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            _infoTimeLabel = new Label
            {
                Text = DateTime.Now.ToString("HH:mm:ss"),
                Font = new Font("Consolas", 14, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(15, 8),
                BackColor = Color.Transparent
            };

            _infoDateLabel = new Label
            {
                Text = DateTime.Now.ToString("dd/MM/yyyy"),
                Font = new Font("Consolas", 12),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(125, 10),
                BackColor = Color.Transparent
            };

            _infoSessionLabel = new Label
            {
                Text = "⏱ 00:00",
                Font = new Font("Consolas", 14, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(255, 8),
                BackColor = Color.Transparent
            };

            _infoPanel.Controls.AddRange(new Control[] { _infoTimeLabel, _infoDateLabel, _infoSessionLabel });

            _browserInfoPanel = new Panel
            {
                Height = 34,
                Width = 380,
                BackColor = Color.FromArgb(124, 58, 237),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            _browserInfoTimeLabel = new Label
            {
                Text = DateTime.Now.ToString("HH:mm:ss"),
                Font = new Font("Consolas", 14, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(15, 5),
                BackColor = Color.Transparent
            };

            _browserInfoDateLabel = new Label
            {
                Text = DateTime.Now.ToString("dd/MM/yyyy"),
                Font = new Font("Consolas", 12),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(125, 7),
                BackColor = Color.Transparent
            };

            _browserInfoSessionLabel = new Label
            {
                Text = "⏱ 00:00",
                Font = new Font("Consolas", 14, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(255, 5),
                BackColor = Color.Transparent
            };

            _browserInfoPanel.Controls.AddRange(new Control[] { _browserInfoTimeLabel, _browserInfoDateLabel, _browserInfoSessionLabel });

            var clockTimer = new Timer { Interval = 1000 };
            clockTimer.Tick += (s, e) =>
            {
                var now = DateTime.Now;
                var timeText = now.ToString("HH:mm:ss");
                var dateText = now.ToString("dd/MM/yyyy");
                _infoTimeLabel.Text = timeText;
                _infoDateLabel.Text = dateText;
                _browserInfoTimeLabel.Text = timeText;
                _browserInfoDateLabel.Text = dateText;
            };
            clockTimer.Start();

            _topBar.Controls.AddRange(new Control[] { _logoBox, _photoBox, _userNameLabel,
                _btnCloseBrowser, _btnBack, _urlBox, _btnGo, _btnHome, _infoPanel, _btnLogout, _btnMinimize });

            _shortcutsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = false,
                Padding = new Padding(20),
                BackColor = Color.FromArgb(10, 10, 25),
                WrapContents = true
            };

            _browserPanel = new Panel { Dock = DockStyle.Fill, Visible = false, BackColor = Color.FromArgb(10, 10, 25) };

            var browserTopBar = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = Color.FromArgb(18, 18, 40) };
            browserTopBar.Controls.AddRange(new Control[] { _btnCloseBrowser, _btnBack, _urlBox, _btnGo, _btnHome, _browserInfoPanel, _btnLogoutBrowser });

            _webView = new WebView2 { Dock = DockStyle.Fill };
            
            var userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApacKiosk", "WebView2Profile");
            Directory.CreateDirectory(userDataFolder);
            _webView.CreationProperties = new CoreWebView2CreationProperties
            {
                UserDataFolder = userDataFolder,
                AdditionalBrowserArguments = "--no-sandbox --disable-gpu-sandbox"
            };

            _browserPanel.Controls.Add(browserTopBar);
            _browserPanel.Controls.Add(_webView);

            Controls.Add(_browserPanel);
            Controls.Add(_shortcutsPanel);
            Controls.Add(_topBar);

            Shown += (s, e) =>
            {
                PositionTopRightButtons();

                _browserPanel.Visible = false;
                
                if (string.IsNullOrEmpty(_urlBox.Text) || _urlBox.Text == "about:blank")
                {
                    _browserPanel.Visible = false;
                }
                else
                {
                    _browserPanel.BringToFront();
                }
            };

            Resize += (s, e) => PositionTopRightButtons();
        }

        private bool _isWebViewInitialized = false;
        private bool _isWebViewInitializing = false;

        private async Task<bool> InitializeWebViewAsync()
        {
            if (_isWebViewInitialized) return true;
            if (_isWebViewInitializing)
            {
                while (_isWebViewInitializing) await Task.Delay(50);
                if (_isWebViewInitialized) return true;
            }
            
            _isWebViewInitializing = true;
            try
            {
                await _webView.EnsureCoreWebView2Async();

                _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                _webView.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
                _webView.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
                _webView.CoreWebView2.Settings.AreHostObjectsAllowed = false;
                _webView.CoreWebView2.NewWindowRequested += (_, args) =>
                {
                    args.Handled = true;
                    if (IsNavigationAllowedForActiveSite(args.Uri))
                    {
                        _webView.CoreWebView2.Navigate(args.Uri);
                    }
                    else
                    {
                        BlockNavigation(args.Uri);
                    }
                };
                _webView.CoreWebView2.DownloadStarting += (_, args) => args.Cancel = true;
                _webView.CoreWebView2.NavigationStarting += OnNavigationStarting;
                _webView.CoreWebView2.NavigationCompleted += (_, args) =>
                {
                    if (!args.IsSuccess)
                    {
                        var logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "webview_error.log");
                        try { File.AppendAllText(logFile, $"{DateTime.Now}: DesktopForm Navigation failed. URL: {_webView.Source}, ErrorStatus: {args.WebErrorStatus}\n"); } catch { }
                    }
                    _urlBox.BeginInvoke((Action)(() =>
                    {
                        _urlBox.Text = _webView.Source?.ToString() ?? "";
                    }));
                };
                _isWebViewInitialized = true;
                return true;
            }
            finally
            {
                _isWebViewInitializing = false;
            }
        }

        private void PositionTopRightButtons()
        {
            _btnLogout.Location = new Point(ClientSize.Width - 100, 10);
            _infoPanel.Location = new Point(ClientSize.Width - 500, 8);
            _browserInfoPanel.Location = new Point(ClientSize.Width - 500, 4);
        }

        private void BtnLogout_Click(object sender, EventArgs e)
        {
            var result = KioskDialog.ShowMessage(
                "Deseja realmente encerrar sua sessão?\nVocê será redirecionado à tela de login.",
                "Encerrar Sessão", MessageBoxButtons.YesNo, MessageBoxIcon.Question, this);

            if (result == DialogResult.Yes)
            {
                ShowLogoutOverlay();
            }
        }

        private void ShowLogoutOverlay()
        {
            var overlay = new Panel
            {
                Size = ClientSize,
                Location = new Point(0, 0),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(1, 1, 1)
            };
            overlay.Paint += (s, ev) =>
            {
                using var brush = new SolidBrush(Color.FromArgb(230, 10, 10, 25));
                ev.Graphics.FillRectangle(brush, overlay.ClientRectangle);
            };

            var spinnerSize = 60;
            var spinner = new Panel
            {
                Size = new Size(spinnerSize, spinnerSize),
                Location = new Point((ClientSize.Width - spinnerSize) / 2, (ClientSize.Height - spinnerSize) / 2 - 60),
                BackColor = Color.Transparent
            };

            var spinnerTimer = new Timer { Interval = 50 };
            var angle = 0f;
            spinner.Paint += (s, ev) =>
            {
                angle = (angle + 6f) % 360f;
                ev.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var cx = spinnerSize / 2f;
                var cy = spinnerSize / 2f;
                var radius = spinnerSize / 2f - 4f;

                for (int i = 0; i < 12; i++)
                {
                    var a = (i * 30f + angle) * Math.PI / 180f;
                    var alpha = 255 - i * 20;
                    var x = cx + (float)(radius * 0.6 * Math.Cos(a));
                    var y = cy + (float)(radius * 0.6 * Math.Sin(a));
                    using var brush = new SolidBrush(Color.FromArgb(alpha, 124, 58, 237));
                    ev.Graphics.FillEllipse(brush, x - 4, y - 4, 8, 8);
                }

                var outerPen = new Pen(Color.FromArgb(80, 124, 58, 237), 3);
                ev.Graphics.DrawArc(outerPen, 4, 4, spinnerSize - 8, spinnerSize - 8, angle, 120);
            };
            spinnerTimer.Tick += (s, ev) => spinner.Invalidate();
            spinnerTimer.Start();

            var loadingLabel = new Label
            {
                Text = "Encerrando sessão...",
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(400, 40),
                Location = new Point((ClientSize.Width - 400) / 2, spinner.Bottom + 30),
                BackColor = Color.Transparent
            };

            var subLabel = new Label
            {
                Text = "Finalizando monitoramento e salvando logs",
                Font = new Font("Segoe UI", 11),
                ForeColor = Color.FromArgb(180, 180, 200),
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(400, 25),
                Location = new Point((ClientSize.Width - 400) / 2, loadingLabel.Bottom + 5),
                BackColor = Color.Transparent
            };

            overlay.Controls.AddRange(new Control[] { spinner, loadingLabel, subLabel });
            Controls.Add(overlay);
            overlay.BringToFront();

            Task.Run(() =>
            {
                Thread.Sleep(800);
                if (!IsDisposed)
                {
                    BeginInvoke((Action)(() =>
                    {
                        spinnerTimer.Stop();
                        spinnerTimer.Dispose();
                        DoLogout();
                    }));
                }
            });
        }

        private void DoLogout()
        {
            LogoutRequested = true;
            _isShuttingDown = true;
            _safetyTimer?.Stop();
            _monitor?.StopAll();
            _security?.PauseWindowProtector();
            EndSession();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            DialogResult = DialogResult.OK;
            Close();
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

            var logoutItem = new ToolStripMenuItem("Encerrar Sessão (Logout)");
            logoutItem.Click += (s, e) => { RestoreFromTray(); BeginInvoke((Action)(() => ShowLogoutOverlay())); };
            menu.Items.Add(logoutItem);

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
            TopMost = true;
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
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ApacKiosk.Resources.LogoApac.png");
                if (stream != null) { _logoBox.Image = Image.FromStream(stream); return; }
            }
            catch { }
            try { _logoBox.Image = CreateDefaultLogo(); } catch { }
        }

        private Bitmap CreateDefaultLogo()
        {
            var bmp = new Bitmap(36, 36);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using var brush = new SolidBrush(Color.White);
                using var font = new Font("Segoe UI", 14, FontStyle.Bold);
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString("APAC", font, brush, new RectangleF(0, 0, 36, 36), sf);
            }
            return bmp;
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

            foreach (var site in sites)
            {
                var card = CreateShortcutCard(site.UrlPattern, GetDomainName(site.UrlPattern), "🌐", Color.FromArgb(59, 130, 246), site.IconPath);
                var url = site.UrlPattern;
                EventHandler siteHandler = (s, e) => OpenWebsite(url);
                card.Click += siteHandler;
                foreach (Control child in card.Controls)
                    child.Click += siteHandler;
                _shortcutsPanel.Controls.Add(card);
            }

            foreach (var prog in programs)
            {
                var card = CreateShortcutCard(prog.Name, prog.Name, "💻", Color.FromArgb(16, 185, 129), prog.IconPath, prog.ExecutablePath);
                var exePath = prog.ExecutablePath;
                var args = prog.Arguments;
                EventHandler progHandler = (s, e) => LaunchProgram(exePath, args);
                card.Click += progHandler;
                foreach (Control child in card.Controls)
                    child.Click += progHandler;
                _shortcutsPanel.Controls.Add(card);
            }
        }

        private Panel CreateShortcutCard(string id, string name, string emojiIcon, Color accent, string? iconPath = null, string? exePath = null)
        {
            var card = new Panel
            {
                Size = new Size(160, 150),
                BackColor = Color.FromArgb(22, 22, 45),
                Margin = new Padding(10, 8, 10, 8),
                Cursor = Cursors.Hand
            };
            using (var path = GetRoundRect(0, 0, 160, 150, 14))
                card.Region = new Region(path);

            Image? iconImage = TryLoadIcon(iconPath, exePath);
            Control iconControl;
            if (iconImage != null)
            {
                var picBox = new PictureBox
                {
                    Image = iconImage,
                    Size = new Size(50, 50),
                    Location = new Point(55, 10),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = Color.Transparent
                };
                iconControl = picBox;
            }
            else
            {
                iconControl = new Label
                {
                    Text = emojiIcon,
                    Font = new Font("Segoe UI", 30),
                    Location = new Point(0, 15),
                    Size = new Size(160, 50),
                    TextAlign = ContentAlignment.MiddleCenter,
                    BackColor = Color.Transparent
                };
            }

            var accentBar = new Panel
            {
                Size = new Size(44, 4),
                Location = new Point(58, 66),
                BackColor = accent
            };

            var nameLabel = new Label
            {
                Text = name.Length > 20 ? name.Substring(0, 18) + ".." : name,
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.FromArgb(220, 220, 240),
                Location = new Point(5, 76),
                Size = new Size(150, 65),
                TextAlign = ContentAlignment.TopCenter,
                BackColor = Color.Transparent
            };

            card.Controls.AddRange(new Control[] { iconControl, accentBar, nameLabel });

            EventHandler hoverEnter = (s, e) => card.BackColor = Color.FromArgb(35, 35, 60);
            EventHandler hoverLeave = (s, e) => card.BackColor = Color.FromArgb(22, 22, 45);

            card.MouseEnter += hoverEnter;
            card.MouseLeave += hoverLeave;

            foreach (Control c in card.Controls)
            {
                c.MouseEnter += hoverEnter;
                c.MouseLeave += hoverLeave;
            }

            return card;
        }

        private Image? TryLoadIcon(string? iconPath, string? exePath)
        {
            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
            {
                try { return Image.FromFile(iconPath); } catch { }
            }
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                try
                {
                    var ico = Icon.ExtractAssociatedIcon(exePath);
                    if (ico != null) return ico.ToBitmap();
                }
                catch { }
            }
            return null;
        }

        private async void OpenWebsite(string url)
        {
            var sitePattern = url;
            var navigationUrl = _siteService.ToNavigableUrl(sitePattern);
            if (!IsHttpUrl(navigationUrl))
            {
                KioskDialog.ShowMessage("Não foi possível abrir este site. Verifique a URL cadastrada pelo administrador.",
                    _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning, this);
                return;
            }

            _activeSitePattern = sitePattern;
            _activeSiteUrl = navigationUrl;
            _shortcutsPanel.Visible = false;
            _btnLogout.Visible = false;

            await InitializeWebViewAsync();

            _browserPanel.Visible = true;
            _browserPanel.BringToFront();
            _browserPanel.PerformLayout();
            _webView.Update();
            _btnCloseBrowser.Visible = true;
            _btnBack.Visible = true;
            _urlBox.Text = navigationUrl;
            _urlBox.Visible = true;
            _btnGo.Visible = true;
            _btnHome.Visible = true;
            _btnLogoutBrowser.Visible = true;
            NavigateToUrl(navigationUrl);
        }

        private void CloseBrowser()
        {
            _activeSitePattern = null;
            _activeSiteUrl = null;
            _pendingUrl = null;
            _browserPanel.Visible = false;
            _webView?.CoreWebView2?.Navigate("about:blank");
            _shortcutsPanel.Visible = true;
            _shortcutsPanel.BringToFront();
            _btnLogout.Visible = true;
            _btnCloseBrowser.Visible = false;
            _btnBack.Visible = false;
            _urlBox.Visible = false;
            _btnGo.Visible = false;
            _btnHome.Visible = false;
            _btnLogoutBrowser.Visible = false;
            LoadShortcuts();
        }

        private void GoHome()
        {
            var homeUrl = _activeSiteUrl;
            if (string.IsNullOrWhiteSpace(homeUrl))
            {
                var profile = _user.ProfileId.HasValue ? _profileService.GetById(_user.ProfileId.Value) : null;
                homeUrl = profile?.HomepageUrl ?? "https://www.google.com";
            }

            _shortcutsPanel.Visible = false;
            _btnLogout.Visible = false;
            _browserPanel.Visible = true;
            _browserPanel.BringToFront();
            _browserPanel.PerformLayout();
            _webView.Update();
            _btnCloseBrowser.Visible = true;
            _btnBack.Visible = true;
            _urlBox.Visible = true;
            _btnGo.Visible = true;
            _btnHome.Visible = true;
            _btnLogoutBrowser.Visible = true;
            NavigateToUrl(homeUrl);
        }

        private async void NavigateToUrl(string url)
        {
            url = _siteService.ToNavigableUrl(url);
            if (!IsHttpUrl(url))
            {
                KioskDialog.ShowMessage("URL inválida.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning, this);
                return;
            }

            if (!IsNavigationAllowedForActiveSite(url))
            {
                BlockNavigation(url);
                return;
            }

            if (_webView?.CoreWebView2 != null)
            {
                try
                {
                    _webView.CoreWebView2.Navigate(url);
                }
                catch (Exception ex)
                {
                    LogTrace($"Navigation error: {ex.Message}");
                    try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "webview_error.log"), $"{DateTime.Now}: Navigate error: {ex}\n"); } catch { }
                    KioskDialog.ShowMessage("Não foi possível abrir este site.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error, this);
                }
                return;
            }

            _pendingUrl = url;
            try
            {
                var ok = await InitializeWebViewAsync();
                if (ok && _webView?.CoreWebView2 != null && !string.IsNullOrEmpty(_pendingUrl))
                {
                    var u = _pendingUrl;
                    _pendingUrl = null;
                    _webView.CoreWebView2.Navigate(u);
                }
                else
                {
                    _pendingUrl = null;
                    LogTrace("WebView2 init failed, cannot navigate");
                    var logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "webview_error.log");
                    try { File.AppendAllText(logFile, $"{DateTime.Now}: DesktopForm init failed, cannot navigate to {url}\n"); } catch { }
                    KioskDialog.ShowMessage("Não foi possível inicializar o navegador.\n\nVerifique se o WebView2 Runtime está instalado e tente novamente.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error, this);
                }
            }
            catch (Exception ex)
            {
                _pendingUrl = null;
                LogTrace($"WebView2 delayed init error: {ex.Message}");
                var logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "webview_error.log");
                try { File.AppendAllText(logFile, $"{DateTime.Now}: DesktopForm Delayed Critical error initializing WebView2: {ex}\n"); } catch { }
                KioskDialog.ShowMessage($"Erro crítico ao inicializar o navegador: {ex.Message}\n\nVerifique webview_error.log", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error, this);
            }
        }

        private bool IsNavigationAllowedForActiveSite(string url)
        {
            if (!IsHttpUrl(url))
                return false;

            if (!string.IsNullOrWhiteSpace(_activeSitePattern))
                return _siteService.IsUrlAllowedByPattern(url, _activeSitePattern);

            return _siteService.IsUrlAllowed(url, _user.ProfileId);
        }

        private static bool IsHttpUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;

            return uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                   uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        }

        private void BlockNavigation(string url)
        {
            try { _db.InsertLog(_user.Id, "blocked_site", url, $"Site bloqueado: {url}"); } catch { }

            if (_webView?.CoreWebView2 == null)
                return;

            _webView.CoreWebView2.NavigateToString(@"
                <html><body style='background:#0a0a19;color:#fff;font-family:Segoe UI;display:flex;justify-content:center;align-items:center;height:100vh;text-align:center'>
                <div><h1 style='color:#ef4444'>Site Bloqueado</h1><p>Apenas o site selecionado pode ser acessado.</p></div></body></html>");
        }

        private void LaunchProgram(string path, string args)
        {
            try
            {
                if (_runningPrograms.TryGetValue(path, out var existingProc))
                {
                    if (!existingProc.HasExited)
                    {
                        SwitchToProgram(existingProc);
                        return;
                    }
                    _runningPrograms.Remove(path);
                }

                if (_launchedProcess != null && !_launchedProcess.HasExited)
                {
                    UnhookMinimizeHook();
                    StopFocusEnforcement();
                    if (_launchedWindowHandle != IntPtr.Zero && IsWindow(_launchedWindowHandle) && !IsIconic(_launchedWindowHandle))
                        ShowWindow(_launchedWindowHandle, 6);
                }

                StopFocusEnforcement();

                var psi = new ProcessStartInfo(path)
                {
                    Arguments = args ?? "",
                    UseShellExecute = true
                };
                var proc = Process.Start(psi);
                if (proc != null)
                {
                    _launchedProcess = proc;
                    _launchedWindowHandle = IntPtr.Zero;

                    string launchedProcessName = null;
                    try { launchedProcessName = proc.ProcessName; } catch { }

                    _security.RegisterAllowedProcess(proc.Id);
                    if (!string.IsNullOrEmpty(launchedProcessName))
                    {
                        _security.RegisterAllowedProcessName(launchedProcessName);
                    }
                    
                    Program.ProcessWatcher?.RegisterAllowedProcess(proc.Id);
                    Program.FileDialogBlocker?.RegisterAllowedProcess(proc.Id);
                    Program.ShellGuard?.RegisterAllowedProcess(proc.Id);

                    _security.PauseWindowProtector();

                    TopMost = false;

                    _db.InsertLog(_user.Id, "program_launch", null, $"Programa aberto: {Path.GetFileName(path)}");

                    proc.EnableRaisingEvents = true;
                    proc.Exited += async (s, ev) =>
                    {
                        try
                        {
                            _security.UnregisterAllowedProcess(proc.Id);
                            Program.ProcessWatcher?.UnregisterAllowedProcess(proc.Id);
                            Program.FileDialogBlocker?.UnregisterAllowedProcess(proc.Id);
                            Program.ShellGuard?.UnregisterAllowedProcess(proc.Id);
                            
                            if (!string.IsNullOrEmpty(launchedProcessName))
                            {
                                await Task.Run(async () =>
                                {
                                    for (int i = 0; i < 3600; i++)
                                    {
                                        var procs = Process.GetProcessesByName(launchedProcessName);
                                        if (procs.Length == 0) break;
                                        
                                        bool hasWindow = false;
                                        foreach (var p in procs)
                                        {
                                            try { if (p.MainWindowHandle != IntPtr.Zero) { hasWindow = true; break; } } catch { }
                                        }
                                        if (!hasWindow) break;
                                        
                                        await Task.Delay(1000);
                                    }
                                });
                                _security.UnregisterAllowedProcessName(launchedProcessName);
                            }

                            _runningPrograms.Remove(path);

                            if (_launchedProcess != null && _launchedProcess.Id == proc.Id)
                            {
                                StopFocusEnforcement();
                                _launchedProcess = null;
                                _launchedWindowHandle = IntPtr.Zero;
                                ReclaimKioskFromProgram();
                            }
                        }
                        catch { }
                    };

                    _runningPrograms[path] = proc;

                    Task.Run(() => FindAndFocusLaunchWindowAsync(proc));
                    SetupMinimizeHook(proc);
                }
            }
            catch (Exception ex)
            {
                KioskDialog.ShowMessage($"Não foi possível abrir o programa:\n{ex.Message}", _config.DisplayName,
                    MessageBoxButtons.OK, MessageBoxIcon.Error, this);
            }
        }

        private async Task FindAndFocusLaunchWindowAsync(Process proc)
        {
            var startedEnforcement = false;
            var stableCount = 0;

            for (var attempt = 0; attempt < 150; attempt++)
            {
                if (proc.HasExited) return;
                await Task.Delay(100);

                try
                {
                    proc.WaitForInputIdle(100);
                }
                catch { }

                IntPtr foundHwnd = IntPtr.Zero;

                EnumWindows((hWnd, lParam) =>
                {
                    if (foundHwnd != IntPtr.Zero) return false;

                    if (!IsWindowVisible(hWnd)) return true;
                    if (GetParent(hWnd) != IntPtr.Zero) return true;
                    if (GetWindowTextLength(hWnd) == 0) return true;

                    GetWindowThreadProcessId(hWnd, out uint pid);
                    if ((int)pid != proc.Id) return true;

                    foundHwnd = hWnd;
                    return false;
                }, IntPtr.Zero);

                if (foundHwnd != IntPtr.Zero)
                {
                    if (_launchedWindowHandle == foundHwnd)
                    {
                        stableCount++;
                    }
                    else
                    {
                        stableCount = 0;
                    }

                    if (!startedEnforcement && stableCount >= 8)
                    {
                        startedEnforcement = true;
                        StartFocusEnforcement(proc);
                    }

                    if (_launchedWindowHandle != foundHwnd || !IsWindow(_launchedWindowHandle))
                    {
                        _launchedWindowHandle = foundHwnd;

                        this.Invoke((Action)(() =>
                        {
                            if (!IsWindow(foundHwnd)) return;

                            if (IsIconic(foundHwnd))
                                ShowWindow(foundHwnd, SW_RESTORE);

                            SetForegroundWindow(foundHwnd);
                            BringWindowToTop(foundHwnd);

                            SetWindowPos(foundHwnd, new IntPtr(NativeMethods.HWND_TOPMOST),
                                0, 0, 0, 0,
                                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
                        }));
                    }
                }
                else
                {
                    stableCount = 0;
                }
            }

            if (!startedEnforcement && !proc.HasExited && _launchedWindowHandle != IntPtr.Zero && IsWindow(_launchedWindowHandle))
            {
                StartFocusEnforcement(proc);
            }
        }

        private void StartFocusEnforcement(Process proc)
        {
            StopFocusEnforcement();
            _focusEnforcementTimer = new Timer { Interval = 500 };
            _focusEnforcementTimer.Tick += (s, e) =>
            {
                try
                {
                    if (proc.HasExited)
                    {
                        StopFocusEnforcement();
                        ReclaimKioskFromProgram();
                        return;
                    }

                    if (_launchedWindowHandle != IntPtr.Zero && IsWindow(_launchedWindowHandle) && IsIconic(_launchedWindowHandle))
                    {
                        StopFocusEnforcement();
                        ReclaimKioskFromProgram();
                        return;
                    }

                    if (_launchedWindowHandle == IntPtr.Zero || !IsWindow(_launchedWindowHandle) || !IsWindowVisible(_launchedWindowHandle))
                    {
                        IntPtr bestHwnd = IntPtr.Zero;

                        EnumWindows((hWnd, lParam) =>
                        {
                            if (bestHwnd != IntPtr.Zero) return false;
                            if (!IsWindowVisible(hWnd)) return true;
                            if (GetParent(hWnd) != IntPtr.Zero) return true;
                            if (GetWindowTextLength(hWnd) == 0) return true;
                            GetWindowThreadProcessId(hWnd, out uint pid);
                            if ((int)pid != proc.Id) return true;
                            bestHwnd = hWnd;
                            return false;
                        }, IntPtr.Zero);

                        if (bestHwnd != IntPtr.Zero)
                        {
                            if (IsIconic(bestHwnd))
                            {
                                StopFocusEnforcement();
                                ReclaimKioskFromProgram();
                                return;
                            }

                            _launchedWindowHandle = bestHwnd;
                            SetForegroundWindow(bestHwnd);
                            BringWindowToTop(bestHwnd);
                            SetWindowPos(bestHwnd, new IntPtr(NativeMethods.HWND_TOPMOST),
                                0, 0, 0, 0,
                                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
                        }
                        return;
                    }

                    var fgWnd = GetForegroundWindow();
                    if (fgWnd != _launchedWindowHandle && fgWnd != IntPtr.Zero && fgWnd != Handle)
                    {
                        GetWindowThreadProcessId(fgWnd, out uint fgPid);
                        if ((int)fgPid != proc.Id)
                        {
                            SetForegroundWindow(_launchedWindowHandle);
                            BringWindowToTop(_launchedWindowHandle);
                        }
                    }
                }
                catch { }
            };
            _focusEnforcementTimer.Start();
        }

        private void ReclaimKioskFromProgram()
        {
            if (IsDisposed || LogoutRequested) return;
            UnhookMinimizeHook();
            Invoke((Action)(() =>
            {
                TopMost = true;
                Show();
                WindowState = FormWindowState.Maximized;
                Activate();
            }));
            _security?.ResumeWindowProtector();
        }

        private void StopFocusEnforcement()
        {
            try
            {
                _focusEnforcementTimer?.Stop();
                _focusEnforcementTimer?.Dispose();
                _focusEnforcementTimer = null;
            }
            catch { }
        }

        private void SwitchToProgram(Process proc)
        {
            try
            {
                if (proc == null || proc.HasExited) return;

                StopFocusEnforcement();
                UnhookMinimizeHook();
                _security.PauseWindowProtector();
                _launchedProcess = proc;
                _launchedWindowHandle = IntPtr.Zero;

                IntPtr foundHwnd = IntPtr.Zero;
                var procId = proc.Id;
                EnumWindows((hWnd, lParam) =>
                {
                    if (!IsWindowVisible(hWnd)) return true;
                    if (GetParent(hWnd) != IntPtr.Zero) return true;
                    if (GetWindowTextLength(hWnd) == 0) return true;
                    GetWindowThreadProcessId(hWnd, out uint pid);
                    if ((int)pid != procId) return true;
                    foundHwnd = hWnd;
                    return false;
                }, IntPtr.Zero);

                if (foundHwnd == IntPtr.Zero) return;

                _launchedWindowHandle = foundHwnd;

                if (IsIconic(foundHwnd))
                    ShowWindow(foundHwnd, SW_RESTORE);

                SetWindowPos(foundHwnd, new IntPtr(NativeMethods.HWND_TOPMOST),
                    0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
                SetForegroundWindow(foundHwnd);
                BringWindowToTop(foundHwnd);

                TopMost = false;

                SetupMinimizeHook(proc);
                StartFocusEnforcement(proc);
            }
            catch { }
        }

        private void SetupMinimizeHook(Process proc)
        {
            UnhookMinimizeHook();
            if (proc == null || proc.HasExited) return;
            _minimizeEventCallback = OnMinimizeEvent;
            _minimizeEventHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_MINIMIZESTART,
                NativeMethods.EVENT_SYSTEM_MINIMIZESTART,
                IntPtr.Zero,
                _minimizeEventCallback,
                (uint)proc.Id,
                0,
                NativeMethods.WINEVENT_OUTOFCONTEXT);
        }

        private void OnMinimizeEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (idObject != 0 || idChild != 0) return;
            if (GetParent(hwnd) != IntPtr.Zero) return;

            var proc = _launchedProcess;
            if (proc == null || proc.HasExited) return;

            bool hasOtherVisibleWindows = false;
            var procId = proc.Id;
            EnumWindows((h, lParam) =>
            {
                if (h == hwnd) return true;
                if (!IsWindowVisible(h)) return true;
                if (GetParent(h) != IntPtr.Zero) return true;
                if (IsIconic(h)) return true;
                GetWindowThreadProcessId(h, out uint pid);
                if ((int)pid != procId) return true;
                if (GetWindowTextLength(h) == 0) return true;
                hasOtherVisibleWindows = true;
                return false;
            }, IntPtr.Zero);

            if (hasOtherVisibleWindows) return;

            BeginInvoke((Action)(() =>
            {
                StopFocusEnforcement();
                ReclaimKioskFromProgram();
                UnhookMinimizeHook();
            }));
        }

        private void UnhookMinimizeHook()
        {
            if (_minimizeEventHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_minimizeEventHook);
                _minimizeEventHook = IntPtr.Zero;
            }
            _minimizeEventCallback = null;
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
            if (string.Equals(uri, "about:blank", StringComparison.OrdinalIgnoreCase))
                return;

            var scheme = "";
            try
            {
                if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsedUri))
                {
                    e.Cancel = true;
                    return;
                }
                scheme = parsedUri.Scheme?.ToLowerInvariant() ?? "";
            }
            catch { }
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

            try
            {
                if (IsNavigationAllowedForActiveSite(uri))
                {
                    _urlBox.Text = uri;
                }
                else
                {
                    e.Cancel = true;
                    BlockNavigation(uri);
                }
            }
            catch
            {
                e.Cancel = true;
                BlockNavigation(uri);
            }
        }

        private void SessionTimer_Tick(object sender, EventArgs e)
        {
            _sessionSeconds++;

            var hours = _sessionSeconds / 3600;
            var minutes = (_sessionSeconds % 3600) / 60;
            var seconds = _sessionSeconds % 60;
            var timerText = hours > 0
                ? $"⏱  {hours:D2}:{minutes:D2}:{seconds:D2}"
                : $"⏱  {minutes:D2}:{seconds:D2}";
            _infoSessionLabel.Text = timerText;
            _browserInfoSessionLabel.Text = timerText;

            var profile = _user.ProfileId.HasValue ? _profileService.GetById(_user.ProfileId.Value) : null;
            if (profile != null && profile.MaxSessionMinutes > 0)
            {
                var maxSeconds = profile.MaxSessionMinutes * 60;
                if (_sessionSeconds >= maxSeconds)
                {
                    _sessionTimer.Stop();
                    KioskDialog.ShowMessage("Tempo máximo de sessão atingido. Sua sessão será encerrada.",
                        _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information, this);
                    DoLogout();
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

        private void SafetyTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                if (_isShuttingDown || LogoutRequested || IsDisposed) return;

                if (WindowState == FormWindowState.Minimized && _launchedProcess == null && !_isMinimized)
                {
                    _safetyTimer?.Stop();
                    BeginInvoke((Action)(() =>
                    {
                        TopMost = true;
                        Show();
                        WindowState = FormWindowState.Maximized;
                        Activate();
                    }));
                    _security?.ResumeWindowProtector();
                    _safetyTimer?.Start();
                }
            }
            catch { }
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

            var (result, pwd) = KioskDialog.ShowPasswordPrompt(
                "Digite a senha de administrador para fechar o sistema:",
                "Senha para Fechar", this);

            if (result == DialogResult.OK)
            {
                using var conn = _db.GetConnection();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT password_hash FROM admins WHERE username = 'admin'";
                var hash = cmd.ExecuteScalar()?.ToString();
                if (hash != null && BCrypt.Net.BCrypt.Verify(pwd, hash))
                {
                    _isShuttingDown = true;
                    _safetyTimer?.Stop();
                    EndSession();
                    _monitor?.StopAll();
                    _security?.DisableAllSecurity();
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
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
            LogTrace("OnShown iniciando");
            base.OnShown(e);

            ShowWelcomeOverlay();

            try
            {
                LogTrace("OnShown: SetMainWindow...");
                _security?.SetMainWindow(Handle);
                _security?.SetKioskLock(true);
                _security?.EnableAllSecurity();
                _security?.ResumeWindowProtector();
                LogTrace("OnShown: SetSessionInfo...");
                _monitor?.SetSessionInfo(_user.Id, _user.Username);
                LogTrace("OnShown: StartAll...");
                _monitor?.StartAll();
                LogTrace("OnShown: monitor iniciado com sucesso");
            }
            catch (Exception ex)
            {
                LogTrace($"OnShown ERRO: {ex}");
                try { _db.InsertLog(_user.Id, "error", null, $"OnShown: {ex.Message}"); } catch { }
                _monitor?.StopAll();
            }

            // Tira foto imediatamente ao acessar
            Task.Run(() =>
            {
                try { _monitor?.Camera?.CaptureNow(); } catch { }
            });

            try
            {
                using var conn = _db.GetConnection();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO session_logs (user_id, login_time) VALUES (@u, datetime('now'))";
                cmd.Parameters.AddWithValue("@u", _user.Id);
                cmd.ExecuteNonQuery();
            }
            catch { }
            LogTrace("OnShown concluido");
        }

        private void ShowWelcomeOverlay()
        {
            _welcomeOverlay = new Panel
            {
                Size = ClientSize,
                Location = new Point(0, 0),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(1, 1, 1)
            };

            _welcomeOverlay.Paint += (s, ev) =>
            {
                using var brush = new SolidBrush(Color.FromArgb(230, 10, 10, 25));
                ev.Graphics.FillRectangle(brush, _welcomeOverlay.ClientRectangle);
            };
            _welcomeOverlay.Click += (s, ev) => { };

            var card = new Panel
            {
                Size = new Size(640, 540),
                BackColor = Color.FromArgb(22, 22, 45)
            };
            using (var path = GetRoundRect(0, 0, 640, 540, 16))
                card.Region = new Region(path);

            card.Paint += (s, ev) =>
            {
                using var pen = new Pen(Color.FromArgb(60, 60, 100), 1);
                ev.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
            };

            var iconLabel = new Label
            {
                Text = "🔒",
                Font = new Font("Segoe UI", 32),
                ForeColor = Color.FromArgb(124, 58, 237),
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(640, 55),
                Location = new Point(0, 25),
                BackColor = Color.Transparent
            };

            var titleLabel = new Label
            {
                Text = $"Bem-vindo(a), {_user.FullName}!",
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(580, 35),
                Location = new Point(30, 85),
                BackColor = Color.Transparent
            };

            var warnTitle = new Label
            {
                Text = "⚠️  Importante:",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.FromArgb(251, 191, 36),
                Location = new Point(40, 130),
                AutoSize = true,
                BackColor = Color.Transparent
            };

            var regularFont = new Font("Segoe UI", 11);
            var highlightFont = new Font("Segoe UI", 11, FontStyle.Bold);
            var regularColor = Color.FromArgb(180, 180, 200);
            var highlightColor = Color.FromArgb(251, 191, 36);

            var infoPanel = new Panel
            {
                Size = new Size(560, 250),
                Location = new Point(40, 160),
                BackColor = Color.Transparent,
                AutoScroll = false
            };

            var lineY = 0;
            var lineHeight = 22;

            var line1 = BuildInfoLine(
                new[] { "Todas as suas ", "ATIVIDADES", " serão ", "REGISTRADAS", " e revisadas" },
                new[] { false, true, false, true, false },
                regularFont, highlightFont, regularColor, highlightColor, lineY);
            infoPanel.Controls.Add(line1);
            lineY += lineHeight;

            var line2 = BuildInfoLine(
                new[] { "pelo administrador do sistema." },
                new[] { false },
                regularFont, highlightFont, regularColor, highlightColor, lineY);
            infoPanel.Controls.Add(line2);
            lineY += lineHeight + 8;

            var line3 = BuildInfoLine(
                new[] { "Capturas de ", "TELA", ", registros de ", "TECLAS", " e " },
                new[] { false, true, false, true, false },
                regularFont, highlightFont, regularColor, highlightColor, lineY);
            infoPanel.Controls.Add(line3);
            lineY += lineHeight;

            var line4 = BuildInfoLine(
                new[] { "FOTOS", " periódicas são coletadas na sessão." },
                new[] { true, false },
                regularFont, highlightFont, regularColor, highlightColor, lineY);
            infoPanel.Controls.Add(line4);
            lineY += lineHeight + 8;

            var line5 = BuildInfoLine(
                new[] { "Apenas ", "SITES", " e ", "PROGRAMAS", " autorizados são" },
                new[] { false, true, false, true, false },
                regularFont, highlightFont, regularColor, highlightColor, lineY);
            infoPanel.Controls.Add(line5);
            lineY += lineHeight;

            var line6 = BuildInfoLine(
                new[] { "permitidos pelo seu perfil de acesso." },
                new[] { false },
                regularFont, highlightFont, regularColor, highlightColor, lineY);
            infoPanel.Controls.Add(line6);
            lineY += lineHeight + 8;

            var line7 = BuildInfoLine(
                new[] { "O ", "TEMPO", " de sessão é ", "LIMITADO", " pelo perfil." },
                new[] { false, true, false, true, false },
                regularFont, highlightFont, regularColor, highlightColor, lineY);
            infoPanel.Controls.Add(line7);
            lineY += lineHeight + 16;

            var consentLabel = new Label
            {
                Text = "Ao continuar, você declara estar CIENTE e de acordo\ncom estas condições de monitoramento.",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(200, 200, 210),
                TextAlign = ContentAlignment.MiddleLeft,
                Size = new Size(560, 36),
                Location = new Point(0, lineY),
                BackColor = Color.Transparent
            };
            infoPanel.Controls.Add(consentLabel);

            var continueBtn = new Button
            {
                Text = "AGUARDE 10s...",
                Font = new Font("Segoe UI", 13, FontStyle.Bold),
                Size = new Size(560, 48),
                Location = new Point(40, 460),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(75, 75, 100),
                ForeColor = Color.FromArgb(180, 180, 200),
                Cursor = Cursors.Default,
                Enabled = false
            };
            continueBtn.FlatAppearance.BorderSize = 0;
            continueBtn.Click += (s, ev) =>
            {
                Controls.Remove(_welcomeOverlay);
                _welcomeOverlay.Dispose();
                _welcomeOverlay = null;
            };

            var countdownSeconds = 10;
            var countdownTimer = new Timer { Interval = 1000 };
            countdownTimer.Tick += (s, ev) =>
            {
                countdownSeconds--;
                if (countdownSeconds <= 0)
                {
                    countdownTimer.Stop();
                    countdownTimer.Dispose();
                    continueBtn.Text = "ESTOU CIENTE — CONTINUAR";
                    continueBtn.BackColor = Color.FromArgb(16, 185, 129);
                    continueBtn.ForeColor = Color.White;
                    continueBtn.Enabled = true;
                    continueBtn.Cursor = Cursors.Hand;
                }
                else
                {
                    continueBtn.Text = $"AGUARDE {countdownSeconds}s...";
                }
            };
            countdownTimer.Start();

            card.Controls.AddRange(new Control[] { iconLabel, titleLabel, warnTitle, infoPanel, continueBtn });

            card.Location = new Point(
                (_welcomeOverlay.Width - 640) / 2,
                (_welcomeOverlay.Height - 540) / 2
            );

            _welcomeOverlay.Controls.Add(card);
            card.BringToFront();

            Controls.Add(_welcomeOverlay);
            _welcomeOverlay.BringToFront();
        }

        private static Panel BuildInfoLine(string[] segments, bool[] isHighlight,
            Font regularFont, Font highlightFont, Color regularColor, Color highlightColor, int y)
        {
            var panel = new Panel
            {
                Location = new Point(0, y),
                Size = new Size(560, 24),
                BackColor = Color.Transparent
            };
            var x = 0;
            for (var i = 0; i < segments.Length; i++)
            {
                var lbl = new Label
                {
                    Text = segments[i],
                    Location = new Point(x, 2),
                    AutoSize = true,
                    BackColor = Color.Transparent,
                    Font = isHighlight[i] ? highlightFont : regularFont,
                    ForeColor = isHighlight[i] ? highlightColor : regularColor
                };
                panel.Controls.Add(lbl);
                x += TextRenderer.MeasureText(segments[i], lbl.Font).Width;
            }
            return panel;
        }

        private static void LogTrace(string msg)
        {
            try
            {
                var logPath = Path.Combine(Path.GetTempPath(), "ApacKiosk_Trace.log");
                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
            }
            catch { }
        }

        #region Win32

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        private const int SW_RESTORE = 9;

        #endregion

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
