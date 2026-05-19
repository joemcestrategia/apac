using System;
using System.Drawing;
using System.Windows.Forms;
using ApacKiosk.Database;
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

        private static readonly (string Text, string Icon)[] Tabs = new[]
        {
            ("Dashboard", "📊"),
            ("Usuários", "👥"),
            ("Perfis de Acesso", "🔐"),
            ("Sites Permitidos", "🌐"),
            ("Monitoramento", "📸"),
            ("Sistema", "⚙"),
            ("Logs", "📋"),
        };

        public AdminDashboard(DatabaseManager db, ConfigManager config)
        {
            _db = db;
            _config = config;
            InitializeComponent();
            CheckDefaultPassword();
            SwitchTab(0);
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

            var logoLabel = new Label
            {
                Text = "APAC Kiosk\nGuardian",
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                ForeColor = Color.FromArgb(124, 58, 237),
                Location = new Point(20, 20),
                Size = new Size(200, 50),
                TextAlign = ContentAlignment.MiddleLeft
            };

            var versionLabel = new Label
            {
                Text = "v1.0 — Administração",
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(120, 120, 140),
                Location = new Point(20, 70),
                AutoSize = true
            };

            var separator = new Panel
            {
                Height = 2,
                Width = 200,
                Location = new Point(20, 100),
                BackColor = Color.FromArgb(50, 50, 80)
            };

            _navButtons = new Button[Tabs.Length];
            var y = 120;
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

            var closeBtn = new Button
            {
                Text = "  ✕  Fechar Painel Admin",
                Font = new Font("Segoe UI", 11),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(220, 44),
                Location = new Point(10, y + 20),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.FromArgb(200, 50, 50),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            closeBtn.FlatAppearance.BorderSize = 0;
            closeBtn.Click += (s, e) => Close();

            _sidebarPanel.Controls.AddRange(new Control[] { logoLabel, versionLabel, separator, closeBtn });

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
                4 => new MonitoringConfigTab(_db, _config),
                5 => new SystemConfigTab(_db, _config),
                6 => new LogViewerTab(_db, _config),
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
                MessageBox.Show(
                    "ATENÇÃO: A senha de administrador ainda é a padrão (APAC@Admin2024).\n\n" +
                    "Altere-a imediatamente em Sistema > Configurações do Sistema.",
                    "Alerta de Segurança", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            var pwdPrompt = new Form
            {
                Text = "Confirmação para Fechar",
                Size = new Size(400, 200),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                BackColor = Color.FromArgb(22, 22, 45),
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
                PasswordChar = '\u25CF',
                Font = new Font("Segoe UI", 12),
                BackColor = Color.FromArgb(26, 26, 46),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            var btnOk = new Button
            {
                Text = "Fechar",
                Location = new Point(150, 110),
                Size = new Size(100, 35),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(200, 50, 50),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
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
                Cursor = Cursors.Hand,
                DialogResult = DialogResult.Cancel
            };
            btnCancel.FlatAppearance.BorderSize = 0;

            btnOk.Click += (s, args) =>
            {
                using var conn = _db.GetConnection();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT password_hash FROM admins WHERE username = 'admin'";
                var hash = cmd.ExecuteScalar()?.ToString();
                if (hash != null && BCrypt.Net.BCrypt.Verify(txt.Text, hash))
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

            if (pwdPrompt.ShowDialog() != DialogResult.OK)
            {
                e.Cancel = true;
            }

            base.OnFormClosing(e);
        }
    }
}
