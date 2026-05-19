using System;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using Microsoft.Data.Sqlite;
using ApacKiosk.Database;
using ApacKiosk.Utils;

namespace ApacKiosk.Forms
{
    public class LoginForm : Form
    {
        private readonly DatabaseManager _db;
        private readonly ConfigManager _config;

        private PictureBox _logoBox;
        private Label _appNameLabel;
        private Label _welcomeLabel;
        private Panel _cardPanel;
        private Label _cardTitle;
        private Label _cardSubtitle;

        private ToggleButton _userModeBtn;
        private ToggleButton _adminModeBtn;

        private Panel _userPanel;
        private ComboBox _userCombo;
        private TextBox _pinBox;
        private TableLayoutPanel _pinPad;
        private Button _userLoginBtn;
        private PictureBox _photoBox;
        private Label _timeInfoLabel;

        private Panel _adminPanel;
        private TextBox _adminUsernameBox;
        private TextBox _adminPasswordBox;
        private Button _adminLoginBtn;

        private bool _isAdminMode;
        public Models.User LoggedInUser { get; private set; }
        public bool IsAdmin { get; private set; }

        public LoginForm(DatabaseManager db, ConfigManager config)
        {
            _db = db;
            _config = config;
            InitializeComponent();
            LoadUsers();
            SwitchToUserMode();
        }

        private void InitializeComponent()
        {
            Text = _config.DisplayName;
            Size = new Size(900, 640);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.FromArgb(10, 10, 25);
            ForeColor = Color.White;
            WindowState = FormWindowState.Maximized;
            TopMost = true;
            DoubleBuffered = true;

            var bgPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(10, 10, 25) };

            _logoBox = new PictureBox
            {
                Size = new Size(100, 100),
                Location = new Point((900 - 100) / 2, 25),
                SizeMode = PictureBoxSizeMode.Zoom
            };
            LoadLogo();

            _appNameLabel = new Label
            {
                Text = _config.DisplayName,
                Font = new Font("Segoe UI", 26, FontStyle.Bold),
                ForeColor = Color.FromArgb(220, 220, 240),
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false,
                Size = new Size(500, 45),
                Location = new Point(200, 130)
            };

            _welcomeLabel = new Label
            {
                Text = _config.WelcomeMessage,
                Font = new Font("Segoe UI", 12),
                ForeColor = Color.FromArgb(150, 150, 175),
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false,
                Size = new Size(500, 25),
                Location = new Point(200, 175)
            };

            var tabPanel = new Panel
            {
                Size = new Size(340, 44),
                Location = new Point(280, 220),
                BackColor = Color.FromArgb(22, 22, 45)
            };
            var tabRadius = 10;
            using (var path = GetRoundRect(0, 0, 340, 44, tabRadius))
            {
                tabPanel.Region = new Region(path);
            }

            _userModeBtn = new ToggleButton
            {
                Text = "Usuário",
                Size = new Size(168, 40),
                Location = new Point(1, 2),
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Selected = true
            };
            _userModeBtn.OnToggle += () => SwitchToUserMode();

            _adminModeBtn = new ToggleButton
            {
                Text = "Administrador",
                Size = new Size(168, 40),
                Location = new Point(171, 2),
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Selected = false
            };
            _adminModeBtn.OnToggle += () => SwitchToAdminMode();

            tabPanel.Controls.Add(_userModeBtn);
            tabPanel.Controls.Add(_adminModeBtn);

            _cardPanel = new Panel
            {
                Size = new Size(420, 340),
                Location = new Point(240, 280),
                BackColor = Color.FromArgb(18, 18, 40)
            };
            using (var path = GetRoundRect(0, 0, 420, 340, 16))
            {
                _cardPanel.Region = new Region(path);
            }

            _cardTitle = new Label
            {
                Font = new Font("Segoe UI", 15, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(30, 20),
                AutoSize = true
            };
            _cardSubtitle = new Label
            {
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.FromArgb(140, 140, 160),
                Location = new Point(30, 48),
                AutoSize = true
            };
            _cardPanel.Controls.Add(_cardTitle);
            _cardPanel.Controls.Add(_cardSubtitle);

            BuildUserPanel();
            BuildAdminPanel();
            _cardPanel.Controls.Add(_userPanel);
            _cardPanel.Controls.Add(_adminPanel);

            var closeLabel = new Label
            {
                Text = "✕",
                Font = new Font("Segoe UI", 16),
                ForeColor = Color.FromArgb(150, 150, 170),
                Location = new Point(870, 10),
                AutoSize = true,
                Cursor = Cursors.Hand
            };
            closeLabel.Click += (s, e) => Application.Exit();

            bgPanel.Controls.AddRange(new Control[] { _logoBox, _appNameLabel, _welcomeLabel, tabPanel, _cardPanel, closeLabel });
            Controls.Add(bgPanel);
        }

        private void BuildUserPanel()
        {
            _userPanel = new Panel { Size = new Size(420, 340), Location = new Point(0, 0), BackColor = Color.Transparent };

            var lblUser = new Label { Text = "Selecionar Usuário", Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Color.FromArgb(180, 180, 200), Location = new Point(30, 80), AutoSize = true };
            _userCombo = new ComboBox
            {
                Location = new Point(30, 104),
                Size = new Size(360, 36),
                Font = new Font("Segoe UI", 13),
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(30, 30, 55),
                ForeColor = Color.White
            };
            _userCombo.SelectedIndexChanged += UserCombo_SelectedIndexChanged;

            var lblPin = new Label { Text = "PIN de Acesso", Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Color.FromArgb(180, 180, 200), Location = new Point(30, 155), AutoSize = true };
            _pinBox = new TextBox
            {
                Location = new Point(30, 179),
                Size = new Size(200, 36),
                Font = new Font("Segoe UI", 16),
                PasswordChar = '●',
                TextAlign = HorizontalAlignment.Center,
                MaxLength = 8,
                BackColor = Color.FromArgb(30, 30, 55),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            _pinBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) DoUserLogin(); };

            _userLoginBtn = new Button
            {
                Text = "ENTRAR",
                Location = new Point(245, 179),
                Size = new Size(145, 36),
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                BackColor = Color.FromArgb(108, 50, 210),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _userLoginBtn.FlatAppearance.BorderSize = 0;
            _userLoginBtn.Click += (s, e) => DoUserLogin();

            _pinPad = new TableLayoutPanel { Location = new Point(30, 228), Size = new Size(360, 100), ColumnCount = 3, RowCount = 4, BackColor = Color.Transparent };
            for (int i = 0; i < 3; i++) _pinPad.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            for (int i = 0; i < 4; i++) _pinPad.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
            BuildPinPad();

            _photoBox = new PictureBox { Location = new Point(315, 10), Size = new Size(75, 75), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(30, 30, 55) };
            using (var path = GetRoundRect(0, 0, 75, 75, 37))
            {
                _photoBox.Region = new Region(path);
            }

            _timeInfoLabel = new Label { Font = new Font("Segoe UI", 9), ForeColor = Color.FromArgb(130, 130, 160), Location = new Point(10, 10), AutoSize = true, MaximumSize = new Size(220, 60) };

            _userPanel.Controls.AddRange(new Control[] { lblUser, _userCombo, lblPin, _pinBox, _userLoginBtn, _pinPad, _photoBox, _timeInfoLabel });
        }

        private void BuildAdminPanel()
        {
            _adminPanel = new Panel { Size = new Size(420, 340), Location = new Point(0, 0), BackColor = Color.Transparent, Visible = false };

            var lblAdminInfo = new Label
            {
                Text = "Acesso restrito a administradores do sistema",
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.FromArgb(140, 140, 170),
                Location = new Point(30, 70),
                AutoSize = true
            };

            var lblUsername = new Label { Text = "Usuário", Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Color.FromArgb(180, 180, 200), Location = new Point(30, 110), AutoSize = true };
            _adminUsernameBox = new TextBox
            {
                Text = "admin",
                Location = new Point(30, 134),
                Size = new Size(360, 36),
                Font = new Font("Segoe UI", 13),
                BackColor = Color.FromArgb(30, 30, 55),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            var lblPassword = new Label { Text = "Senha", Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Color.FromArgb(180, 180, 200), Location = new Point(30, 185), AutoSize = true };
            _adminPasswordBox = new TextBox
            {
                Location = new Point(30, 209),
                Size = new Size(360, 36),
                Font = new Font("Segoe UI", 13),
                PasswordChar = '●',
                BackColor = Color.FromArgb(30, 30, 55),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            _adminPasswordBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) DoAdminLogin(); };

            _adminLoginBtn = new Button
            {
                Text = "ACESSAR PAINEL DE ADMINISTRAÇÃO",
                Location = new Point(30, 270),
                Size = new Size(360, 42),
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                BackColor = Color.FromArgb(108, 50, 210),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _adminLoginBtn.FlatAppearance.BorderSize = 0;
            _adminLoginBtn.Click += (s, e) => DoAdminLogin();

            _adminPanel.Controls.AddRange(new Control[] { lblAdminInfo, lblUsername, _adminUsernameBox, lblPassword, _adminPasswordBox, _adminLoginBtn });
        }

        private GraphicsPath GetRoundRect(int x, int y, int w, int h, int r)
        {
            var path = new GraphicsPath();
            path.AddArc(x, y, r * 2, r * 2, 180, 90);
            path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
            path.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void SwitchToUserMode()
        {
            _isAdminMode = false;
            _userModeBtn.Selected = true;
            _adminModeBtn.Selected = false;
            _userPanel.Visible = true;
            _adminPanel.Visible = false;
            _cardTitle.Text = "Acesso do Usuário";
            _cardSubtitle.Text = "Selecione seu nome e digite seu PIN";
            _pinBox.Clear();
        }

        private void SwitchToAdminMode()
        {
            _isAdminMode = true;
            _userModeBtn.Selected = false;
            _adminModeBtn.Selected = true;
            _userPanel.Visible = false;
            _adminPanel.Visible = true;
            _cardTitle.Text = "Administração";
            _cardSubtitle.Text = "Digite suas credenciais de administrador";
            _adminPasswordBox.Clear();
            _adminPasswordBox.Focus();
        }

        private void BuildPinPad()
        {
            _pinPad.Controls.Clear();
            var digits = new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "⌫", "0", "OK" };
            foreach (var d in digits)
            {
                var btn = new Button
                {
                    Text = d,
                    Font = new Font("Segoe UI", 14, FontStyle.Bold),
                    Dock = DockStyle.Fill,
                    Margin = new Padding(4),
                    BackColor = d == "OK" ? Color.FromArgb(108, 50, 210) : Color.FromArgb(42, 42, 70),
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand
                };
                btn.FlatAppearance.BorderSize = 0;
                btn.Click += (s, e) =>
                {
                    if (d == "⌫")
                    {
                        if (_pinBox.Text.Length > 0) _pinBox.Text = _pinBox.Text.Substring(0, _pinBox.Text.Length - 1);
                    }
                    else if (d == "OK") DoUserLogin();
                    else _pinBox.Text += d;
                };
                _pinPad.Controls.Add(btn);
            }
        }

        private void LoadLogo()
        {
            _logoBox.Image = null;
            string[] paths = {
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

            var configPath = _config.LogoPath;
            if (!string.IsNullOrEmpty(configPath) && File.Exists(configPath))
            {
                try { _logoBox.Image = Image.FromFile(configPath); return; } catch { }
            }

            try { _logoBox.Image = CreateDefaultLogo(); } catch { }
        }

        private Bitmap CreateDefaultLogo()
        {
            var bmp = new Bitmap(100, 100);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(10, 10, 25));
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(Color.FromArgb(124, 58, 237)))
                using (var font = new Font("Segoe UI", 36, FontStyle.Bold))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString("APAC", font, brush, new RectangleF(0, 0, 100, 100), sf);
                }
            }
            return bmp;
        }

        private void LoadUsers()
        {
            _userCombo.Items.Clear();
            using var conn = _db.GetConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, full_name, username, photo_path, profile_id, is_active FROM users WHERE is_active = 1 ORDER BY full_name";
            using var reader = cmd.ExecuteReader();
            var dt = new DataTable();
            dt.Load(reader);
            foreach (DataRow row in dt.Rows)
            {
                _userCombo.Items.Add(new UserListItem
                {
                    Id = Convert.ToInt32(row["id"]),
                    FullName = row["full_name"].ToString(),
                    Username = row["username"].ToString(),
                    PhotoPath = row["photo_path"]?.ToString(),
                    ProfileId = row["profile_id"] as int?,
                    IsActive = Convert.ToBoolean(row["is_active"])
                });
            }
        }

        private void UserCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            _photoBox.Image = null;
            _timeInfoLabel.Text = "";
            if (_userCombo.SelectedItem is UserListItem user)
            {
                if (!string.IsNullOrEmpty(user.PhotoPath) && File.Exists(user.PhotoPath))
                {
                    try { _photoBox.Image = Image.FromFile(user.PhotoPath); } catch { }
                }
                _timeInfoLabel.Text = CheckUserTimeAllowed(user);
                _pinBox.Focus();
            }
        }

        private string CheckUserTimeAllowed(UserListItem user)
        {
            if (!user.ProfileId.HasValue) return "";
            using var conn = _db.GetConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT day_of_week, start_time, end_time FROM time_rules WHERE profile_id = @pid";
            cmd.Parameters.AddWithValue("@pid", user.ProfileId.Value);
            using var reader = cmd.ExecuteReader();
            var dt = new DataTable();
            dt.Load(reader);
            if (dt.Rows.Count == 0) return "";

            var now = DateTime.Now;
            var currentDay = ((int)now.DayOfWeek == 0) ? 7 : (int)now.DayOfWeek;

            foreach (DataRow row in dt.Rows)
            {
                if (Convert.ToInt32(row["day_of_week"]) != currentDay) continue;
                if (TimeSpan.TryParse(row["start_time"].ToString(), out var start) &&
                    TimeSpan.TryParse(row["end_time"].ToString(), out var end))
                {
                    if (now.TimeOfDay >= start && now.TimeOfDay <= end)
                        return $"Disponível até {end:h\\:mm} ✓";
                    if (now.TimeOfDay < start)
                        return $"Disponível a partir das {start:h\\:mm}";
                }
            }
            return "Fora do horário permitido";
        }

        private void DoUserLogin()
        {
            if (!(_userCombo.SelectedItem is UserListItem user))
            {
                MessageBox.Show("Selecione um usuário.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(_pinBox.Text))
            {
                MessageBox.Show("Digite o PIN.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using var conn = _db.GetConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT pin_hash FROM users WHERE id = @id AND is_active = 1";
            cmd.Parameters.AddWithValue("@id", user.Id);
            var pinHash = cmd.ExecuteScalar()?.ToString();

            if (pinHash == null || !PasswordHash.VerifyPassword(_pinBox.Text, pinHash))
            {
                MessageBox.Show("PIN inválido.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                _pinBox.Clear();
                return;
            }

            _db.InsertLog(user.Id, "login", null, $"Login: {user.FullName}");

            LoggedInUser = new Models.User
            {
                Id = user.Id, FullName = user.FullName, Username = user.Username,
                PhotoPath = user.PhotoPath, ProfileId = user.ProfileId, IsActive = user.IsActive
            };
            DialogResult = DialogResult.OK;
            Close();
        }

        private void DoAdminLogin()
        {
            var username = _adminUsernameBox.Text.Trim();
            var password = _adminPasswordBox.Text;

            if (string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("Digite a senha.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using var conn = _db.GetConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT password_hash FROM admins WHERE username = @u";
            cmd.Parameters.AddWithValue("@u", username);
            var hash = cmd.ExecuteScalar()?.ToString();

            if (hash != null && PasswordHash.VerifyPassword(password, hash))
            {
                _db.InsertLog(null, "admin_login", null, $"Login admin: {username}");

                if (PasswordHash.VerifyPassword("APAC@Admin2024", hash))
                {
                    MessageBox.Show("ATENÇÃO: A senha de administrador ainda é a padrão.\nAltere-a no Painel Admin > Configurações do Sistema.",
                        "Alerta de Segurança", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                IsAdmin = true;
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                MessageBox.Show("Usuário ou senha inválidos.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                _adminPasswordBox.Clear();
                _adminPasswordBox.Focus();
            }
        }

        private class ToggleButton : Button
        {
            private bool _selected;
            public bool Selected
            {
                get => _selected;
                set { _selected = value; UpdateStyle(); }
            }

            public event Action OnToggle;

            public ToggleButton()
            {
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                Cursor = Cursors.Hand;
                Click += (s, e) => { Selected = true; OnToggle?.Invoke(); };
                UpdateStyle();
            }

            private void UpdateStyle()
            {
                BackColor = _selected ? Color.FromArgb(108, 50, 210) : Color.FromArgb(22, 22, 45);
                ForeColor = _selected ? Color.White : Color.FromArgb(160, 160, 180);
            }
        }

        private class UserListItem
        {
            public int Id { get; set; }
            public string FullName { get; set; }
            public string Username { get; set; }
            public string PhotoPath { get; set; }
            public int? ProfileId { get; set; }
            public bool IsActive { get; set; }
            public override string ToString() => $"{FullName} ({Username})";
        }
    }
}
