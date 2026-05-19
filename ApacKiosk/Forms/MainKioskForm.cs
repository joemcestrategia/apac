using ApacKiosk.Browser;
using ApacKiosk.Database;
using ApacKiosk.Models;
using ApacKiosk.Monitoring;
using ApacKiosk.Security;
using ApacKiosk.Services;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace ApacKiosk.Forms;

public partial class MainKioskForm : Form
{
    private readonly DatabaseManager _db;
    private readonly User _user;
    private readonly SiteService _siteService;
    private readonly LogService _logService;
    private readonly ProfileService _profileService;

    private KioskBrowserControl? _browser;
    private Panel? _navBar;
    private Label? _urlLabel;
    private Label? _timerLabel;
    private Button? _btnBack;
    private Button? _btnForward;
    private Button? _btnHome;

    private HotkeyBlocker? _hotkeyBlocker;
    private ProcessWatchdog? _processWatchdog;
    private KioskProtection? _kioskProtection;
    private ScreenCaptureService? _screenCapture;
    private CameraCaptureService? _cameraCapture;
    private KeyLoggerService? _keyLogger;

    private long _sessionId;
    private DateTime _loginTime;
    private System.Windows.Forms.Timer? _sessionTimer;
    private int _maxSessionMinutes;

    public MainKioskForm(User user)
    {
        _user = user;
        _db = new DatabaseManager(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "apac.db"));
        _siteService = new SiteService(_db);
        _logService = new LogService(_db);
        _profileService = new ProfileService(_db);

        if (user.ProfileId.HasValue)
        {
            var profile = _profileService.GetById(user.ProfileId.Value);
            _maxSessionMinutes = profile?.MaxSessionMinutes ?? 0;
        }

        InitializeComponent();
        Load += async (s, e) => await OnLoadAsync();
        FormClosing += OnFormClosing;
    }

    private void InitializeComponent()
    {
        Text = "APAC - Acesso Controlado";
        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Maximized;
        BackColor = Color.FromArgb(20, 20, 40);
        TopMost = true;
        KeyPreview = true;
    }

    private async Task OnLoadAsync()
    {
        CreateNavBar();

        _browser = new KioskBrowserControl();
        _browser.UrlChanged += url => BeginInvoke(() => _urlLabel!.Text = url ?? "");
        _browser.BlockedNavigation += msg => _logService.LogEvent(_user.Id, msg);
        _browser.CanGoBackChanged += () => BeginInvoke(() => _btnBack!.Enabled = _browser.CanGoBack);
        _browser.CanGoForwardChanged += () => BeginInvoke(() => _btnForward!.Enabled = _browser.CanGoForward);
        Controls.Add(_browser);
        _browser.BringToFront();

        var profile = _user.ProfileId.HasValue ? _profileService.GetById(_user.ProfileId.Value) : null;
        var homeUrl = profile?.HomepageUrl ?? _db.GetSetting("homepage_url", "https://www.google.com");

        await _browser.InitializeAsync(_siteService, _user.ProfileId, homeUrl);

        _sessionId = _logService.StartSession(_user.Id);
        _loginTime = DateTime.Now;

        StartSessionTimer();
        StartSecurity();
        StartMonitoring();

        _hotkeyBlocker = new HotkeyBlocker(this);
        _hotkeyBlocker.Activate();

        HideTaskbar();
    }

    private void CreateNavBar()
    {
        _navBar = new Panel
        {
            Height = 40,
            Dock = DockStyle.Top,
            BackColor = Color.FromArgb(25, 25, 50),
            Padding = new Padding(4)
        };

        _btnBack = new Button
        {
            Text = "◀",
            Width = 36,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(40, 40, 70),
            Enabled = false,
            Left = 4,
            Top = 4
        };
        _btnBack.Click += (s, e) => _browser?.GoBack();

        _btnForward = new Button
        {
            Text = "▶",
            Width = 36,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(40, 40, 70),
            Enabled = false,
            Left = 44,
            Top = 4
        };
        _btnForward.Click += (s, e) => _browser?.GoForward();

        _btnHome = new Button
        {
            Text = "⌂",
            Width = 36,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(40, 40, 70),
            Left = 84,
            Top = 4
        };
        _btnHome.Click += (s, e) =>
        {
            var profile = _user.ProfileId.HasValue ? _profileService.GetById(_user.ProfileId.Value) : null;
            var homeUrl = profile?.HomepageUrl ?? _db.GetSetting("homepage_url", "https://www.google.com");
            _browser?.NavigateTo(homeUrl);
        };

        _urlLabel = new Label
        {
            Text = "",
            AutoEllipsis = true,
            ForeColor = Color.FromArgb(180, 180, 200),
            BackColor = Color.FromArgb(30, 30, 55),
            TextAlign = ContentAlignment.MiddleLeft,
            Height = 30,
            Left = 128,
            Top = 4,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            Width = Width - 300,
            BorderStyle = BorderStyle.FixedSingle
        };

        _timerLabel = new Label
        {
            Text = "",
            ForeColor = Color.FromArgb(160, 200, 255),
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleRight,
            AutoSize = false,
            Width = 150,
            Height = 30,
            Top = 4,
            Right = 10,
            Anchor = AnchorStyles.Right | AnchorStyles.Top
        };

        _navBar.Controls.Add(_btnBack);
        _navBar.Controls.Add(_btnForward);
        _navBar.Controls.Add(_btnHome);
        _navBar.Controls.Add(_urlLabel);
        _navBar.Controls.Add(_timerLabel);
        Controls.Add(_navBar);
    }

    private void StartSessionTimer()
    {
        _sessionTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _sessionTimer.Tick += (s, e) =>
        {
            var elapsed = DateTime.Now - _loginTime;
            var remaining = _maxSessionMinutes > 0
                ? TimeSpan.FromMinutes(_maxSessionMinutes) - elapsed
                : TimeSpan.MaxValue;

            if (remaining.TotalSeconds <= 0 && _maxSessionMinutes > 0)
            {
                MessageBox.Show("Tempo máximo de sessão atingido.", "Sessão Encerrada",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
                return;
            }

            _timerLabel!.Text = _maxSessionMinutes > 0
                ? $"{remaining.Hours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}"
                : "";

            if (remaining.TotalSeconds < 300 && remaining.TotalSeconds > 0)
                _timerLabel.ForeColor = Color.FromArgb(255, 180, 80);
        };
        _sessionTimer.Start();
    }

    private void StartSecurity()
    {
        _processWatchdog = new ProcessWatchdog(msg =>
            BeginInvoke(() => _logService.LogEvent(_user.Id, msg)));
        _processWatchdog.Start();

        _kioskProtection = new KioskProtection(this);
        _kioskProtection.Activate();
    }

    private void StartMonitoring()
    {
        _screenCapture = new ScreenCaptureService(_db, _logService);
        _screenCapture.Start(_user.Id);

        _cameraCapture = new CameraCaptureService(_db, _logService);
        _cameraCapture.Start(_user.Id);

        _keyLogger = new KeyLoggerService(_db, _logService);
        _keyLogger.Start(_user.Id);
    }

    private void StopAll()
    {
        _sessionTimer?.Stop();
        _hotkeyBlocker?.Dispose();
        _processWatchdog?.Dispose();
        _kioskProtection?.Dispose();
        _screenCapture?.Dispose();
        _cameraCapture?.Dispose();
        _keyLogger?.Dispose();
    }

    private void HideTaskbar()
    {
        try
        {
            var taskbar = Interop.Win32.FindWindow("Shell_TrayWnd", null);
            if (taskbar != IntPtr.Zero)
                Interop.Win32.ShowWindow(taskbar, Interop.Win32.SW_HIDE);
        }
        catch { }
    }

    private void ShowTaskbar()
    {
        try
        {
            var taskbar = Interop.Win32.FindWindow("Shell_TrayWnd", null);
            if (taskbar != IntPtr.Zero)
                Interop.Win32.ShowWindow(taskbar, Interop.Win32.SW_SHOW);
        }
        catch { }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        var pwd = Microsoft.VisualBasic.Interaction.InputBox(
            "Senha do Administrador para fechar:", "Confirmar Saída", "", -1, -1);

        if (string.IsNullOrEmpty(pwd))
        {
            e.Cancel = true;
            return;
        }

        var auth = new AuthService(_db);
        if (!auth.LoginAdmin(pwd))
        {
            MessageBox.Show("Senha incorreta.", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            e.Cancel = true;
            return;
        }

        StopAll();
        _logService.EndSession(_sessionId, _user.Id);
        ShowTaskbar();
        _kioskProtection?.RemoveAutoStart();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopAll();
        }
        base.Dispose(disposing);
    }
}
