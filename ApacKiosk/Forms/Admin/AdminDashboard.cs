using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using ApacKiosk.Database;
using ApacKiosk.Services;
using ApacKiosk.Utils;

namespace ApacKiosk.Forms.Admin
{
    public class AdminDashboard : Form
    {
        private readonly DatabaseManager _db;
        private readonly ConfigManager _config;
        private Panel _sidebarPanel;
        private Panel _contentPanel;
        private Button[] _navButtons;
        private int _activeTabIndex;
        private Label _titleLabel;
        private UserControl _activeControl;
        private bool _isReady;

        public bool LogoutRequested { get; private set; }

        private static readonly (string Text, string Icon)[] Tabs = new[]
        {
            ("Dashboard", "📊"),
            ("Usuários", "👥"),
            ("Perfis de Acesso", "🔐"),
            ("Sites Permitidos", "🌐"),
            ("Programas", "🖥"),
            ("Monitoramento", "📸"),
            ("Sistema", "⚙"),
            ("Logs", "📋"),
        };

        public AdminDashboard(DatabaseManager db, ConfigManager config)
        {
            _db = db;
            _config = config;
            LogTrace("AdminDashboard.ctor iniciando");
            InitializeComponent();
            LogTrace("AdminDashboard.InitializeComponent concluido");
            SwitchTab(0);
            LogTrace("AdminDashboard.ctor concluido");
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _isReady = true;
            Program.Security?.SetKioskLock(false);
            LogTrace("AdminDashboard.OnShown - chamando CheckDefaultPassword");
            CheckDefaultPassword();
            LogTrace("AdminDashboard.OnShown concluido");
        }

        private void InitializeComponent()
        {
            Text = _config.DisplayName + " — Administração";
            Size = new Size(1280, 800);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.FromArgb(15, 15, 35);
            ForeColor = Color.White;
            WindowState = FormWindowState.Maximized;
            TopMost = true;

            _sidebarPanel = new Panel
            {
                Width = 240,
                Dock = DockStyle.Left,
                BackColor = Color.FromArgb(22, 22, 45),
                Padding = new Padding(0)
            };

            var logoBox = new PictureBox
            {
                Size = new Size(100, 100),
                Location = new Point(70, 20),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
            LoadAdminLogo(logoBox);

            var logoLabel = new Label
            {
                Text = "APAC Kiosk\nGuardian",
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                ForeColor = Color.FromArgb(124, 58, 237),
                Location = new Point(20, 125),
                Size = new Size(200, 60),
                TextAlign = ContentAlignment.MiddleCenter
            };

            var versionLabel = new Label
            {
                Text = "v1.0 — Administração",
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(120, 120, 140),
                Location = new Point(20, 185),
                AutoSize = false,
                Size = new Size(200, 20),
                TextAlign = ContentAlignment.MiddleCenter
            };

            var separator = new Panel
            {
                Height = 2,
                Width = 200,
                Location = new Point(20, 215),
                BackColor = Color.FromArgb(50, 50, 80)
            };

            _navButtons = new Button[Tabs.Length];
            var y = 235;
            for (int i = 0; i < Tabs.Length; i++)
            {
                var btn = new Button
                {
                    Text = $"  {Tabs[i].Icon}  {Tabs[i].Text}",
                    Font = new Font("Segoe UI", 12),
                    FlatStyle = FlatStyle.Flat,
                    Size = new Size(220, 44),
                    Location = new Point(10, y),
                    TextAlign = ContentAlignment.MiddleLeft,
                    BackColor = Color.FromArgb(22, 22, 45),
                    ForeColor = Color.FromArgb(200, 200, 220),
                    Cursor = Cursors.Hand,
                    Tag = i
                };
                btn.FlatAppearance.BorderSize = 0;
                btn.Click += NavButton_Click;
                _navButtons[i] = btn;
                _sidebarPanel.Controls.Add(btn);
                y += 50;
            }

            var logoutBtn = new Button
            {
                Text = "  ↩  Sair / Logout",
                Font = new Font("Segoe UI", 11),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(220, 44),
                Location = new Point(10, y + 10),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.FromArgb(245, 158, 11),
                ForeColor = Color.Black,
                Cursor = Cursors.Hand
            };
            logoutBtn.FlatAppearance.BorderSize = 0;
            logoutBtn.Click += LogoutBtn_Click;

            var closeBtn = new Button
            {
                Text = "  ✕  Fechar Sistema",
                Font = new Font("Segoe UI", 11),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(220, 44),
                Location = new Point(10, y + 60),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.FromArgb(200, 50, 50),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            closeBtn.FlatAppearance.BorderSize = 0;
            closeBtn.Click += (s, e) => Close();

            _sidebarPanel.Controls.AddRange(new Control[] { logoBox, logoLabel, versionLabel, separator, logoutBtn, closeBtn });

            _contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(15, 15, 35),
                Padding = new Padding(20)
            };

            _titleLabel = new Label
            {
                Font = new Font("Segoe UI", 20, FontStyle.Bold),
                ForeColor = Color.FromArgb(124, 58, 237),
                Location = new Point(20, 10),
                AutoSize = true
            };
            _contentPanel.Controls.Add(_titleLabel);

            Controls.Add(_contentPanel);
            Controls.Add(_sidebarPanel);
        }

        private void LogoutBtn_Click(object sender, EventArgs e)
        {
            var result = KioskDialog.ShowMessage(
                "Deseja sair do painel de administração e voltar à tela de login?",
                "Sair / Logout", MessageBoxButtons.YesNo, MessageBoxIcon.Question, this);

            if (result == DialogResult.Yes)
            {
                Program.Security?.SetKioskLock(true);
                _db.InsertLog(null, "admin_logout", null, "Logout do administrador");
                LogoutRequested = true;
                Close();
            }
        }

        private void NavButton_Click(object? sender, EventArgs e)
        {
            if (sender is Button btn && btn.Tag is int index)
                SwitchTab(index);
        }

        private void SwitchTab(int index)
        {
            _activeTabIndex = index;
            for (int i = 0; i < _navButtons.Length; i++)
            {
                _navButtons[i].BackColor = i == index ? Color.FromArgb(124, 58, 237) : Color.FromArgb(22, 22, 45);
                _navButtons[i].ForeColor = Color.White;
            }

            if (_activeControl != null)
            {
                _contentPanel.Controls.Remove(_activeControl);
                _activeControl.Dispose();
            }

            UserControl? control = index switch
            {
                0 => new DashboardTab(_db, _config),
                1 => new UserManagementTab(_db, _config),
                2 => new AccessProfilesTab(_db, _config),
                3 => new AllowedSitesTab(_db, _config),
                4 => new ProgramsTab(_db, _config),
                5 => new MonitoringConfigTab(_db, _config),
                6 => new SystemConfigTab(_db, _config),
                7 => new LogViewerTab(_db, _config),
                _ => null
            };

            if (control != null)
            {
                control.Location = new Point(0, 50);
                control.Size = new Size(_contentPanel.Width, _contentPanel.Height - 70);
                control.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
                _contentPanel.Controls.Add(control);
                _activeControl = control;
            }

            _titleLabel.Text = Tabs[index].Icon + "  " + Tabs[index].Text;
        }

        private void CheckDefaultPassword()
        {
            using var conn = _db.GetConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT password_hash FROM admins WHERE username = 'admin'";
            var hash = cmd.ExecuteScalar()?.ToString();
            if (hash != null && BCrypt.Net.BCrypt.Verify("APAC@Admin2024", hash))
            {
                KioskDialog.ShowMessage(
                    "ATENÇÃO: A senha de administrador ainda é a padrão (APAC@Admin2024).\n\n" +
                    "Altere-a imediatamente em Sistema > Configurações do Sistema.",
                    "Alerta de Segurança", MessageBoxButtons.OK, MessageBoxIcon.Warning, this);
            }
        }

        private static void LoadAdminLogo(PictureBox box)
        {
            var paths = new[] {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "LogoApac.png"),
                Path.Combine(Application.StartupPath, "Resources", "LogoApac.png"),
            };
            foreach (var p in paths)
            {
                if (File.Exists(p))
                {
                    try { box.Image = Image.FromFile(p); return; } catch { }
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            LogTrace($"AdminDashboard.OnFormClosing chamado (CloseReason={e.CloseReason}, isReady={_isReady}, LogoutRequested={LogoutRequested})");

            if (!_isReady)
            {
                LogTrace("AdminDashboard ainda nao esta pronto, ignorando fechamento");
                e.Cancel = true;
                return;
            }

            if (LogoutRequested)
            {
                LogTrace("AdminDashboard logout solicitado, fechando sem verificar senha");
                base.OnFormClosing(e);
                return;
            }

            var (result, pwd) = KioskDialog.ShowPasswordPrompt(
                "Digite a senha de administrador para fechar o sistema:",
                "Confirmação para Fechar", this);

            if (result == DialogResult.OK)
            {
                using var conn = _db.GetConnection();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT password_hash FROM admins WHERE username = 'admin'";
                var hash = cmd.ExecuteScalar()?.ToString();
                if (hash != null && BCrypt.Net.BCrypt.Verify(pwd, hash))
                {
                    LogTrace("AdminDashboard senha correta, fechando");
                    _db.InsertLog(null, "admin_shutdown", null, "Sistema encerrado pelo administrador");
                    base.OnFormClosing(e);
                    return;
                }
                else
                {
                    KioskDialog.ShowMessage("Senha inválida.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error, this);
                }
            }

            LogTrace("AdminDashboard fechamento cancelado");
            e.Cancel = true;
            base.OnFormClosing(e);
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
    }
}
