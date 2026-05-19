using System;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
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
        private Label _titleLabel;
        private Label _welcomeLabel;
        private ComboBox _userCombo;
        private TextBox _pinBox;
        private PictureBox _photoBox;
        private Button _loginButton;
        private Button _adminButton;
        private Label _timeInfoLabel;
        private TableLayoutPanel _pinPad;
        private bool _isAdminMode;

        public Models.User LoggedInUser { get; private set; }
        public bool IsAdmin { get; private set; }

        public LoginForm(DatabaseManager db, ConfigManager config)
        {
            _db = db;
            _config = config;
            InitializeComponent();
            LoadUsers();
        }

        private void InitializeComponent()
        {
            Text = _config.DisplayName;
            Size = new Size(800, 700);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.FromArgb(15, 15, 35);
            ForeColor = Color.White;
            WindowState = FormWindowState.Maximized;
            TopMost = true;

            var mainPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(40),
                BackColor = Color.FromArgb(15, 15, 35)
            };
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));

            var leftPanel = new Panel { Dock = DockStyle.Fill };
            var rightPanel = new Panel { Dock = DockStyle.Fill };

            _logoBox = new PictureBox
            {
                Size = new Size(140, 140),
                Location = new Point(100, 60),
                SizeMode = PictureBoxSizeMode.Zoom
            };
            LoadLogo();

            _titleLabel = new Label
            {
                Text = _config.DisplayName,
                Font = new Font("Segoe UI", 26, FontStyle.Bold),
                ForeColor = Color.FromArgb(124, 58, 237),
                Location = new Point(100, 220),
                AutoSize = true
            };

            _welcomeLabel = new Label
            {
                Text = _config.WelcomeMessage,
                Font = new Font("Segoe UI", 14),
                ForeColor = Color.FromArgb(167, 139, 250),
                Location = new Point(100, 280),
                AutoSize = true
            };

            _timeInfoLabel = new Label
            {
                Font = new Font("Segoe UI", 11),
                ForeColor = Color.FromArgb(200, 200, 200),
                Location = new Point(100, 320),
                AutoSize = true,
                MaximumSize = new Size(300, 200)
            };

            leftPanel.Controls.AddRange(new Control[] { _logoBox, _titleLabel, _welcomeLabel, _timeInfoLabel });

            var lblUser = new Label
            {
                Text = "Usuário",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 200, 220),
                Location = new Point(30, 60),
                AutoSize = true
            };

            _userCombo = new ComboBox
            {
                Location = new Point(30, 90),
                Size = new Size(320, 35),
                Font = new Font("Segoe UI", 13),
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(26, 26, 46),
                ForeColor = Color.White
            };
            _userCombo.SelectedIndexChanged += UserCombo_SelectedIndexChanged;

            var lblPin = new Label
            {
                Text = "PIN",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 200, 220),
                Location = new Point(30, 145),
                AutoSize = true
            };

            _pinBox = new TextBox
            {
                Location = new Point(30, 175),
                Size = new Size(320, 35),
                Font = new Font("Segoe UI", 14),
                PasswordChar = '●',
                TextAlign = HorizontalAlignment.Center,
                MaxLength = 8,
                BackColor = Color.FromArgb(26, 26, 46),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            _pinBox.KeyDown += PinBox_KeyDown;

            _loginButton = new Button
            {
                Text = "Entrar",
                Location = new Point(30, 230),
                Size = new Size(320, 45),
                Font = new Font("Segoe UI", 13, FontStyle.Bold),
                BackColor = Color.FromArgb(124, 58, 237),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _loginButton.FlatAppearance.BorderSize = 0;
            _loginButton.Click += LoginButton_Click;

            _photoBox = new PictureBox
            {
                Location = new Point(120, 310),
                Size = new Size(120, 120),
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(26, 26, 46)
            };

            _pinPad = new TableLayoutPanel
            {
                Location = new Point(30, 450),
                Size = new Size(320, 200),
                ColumnCount = 3,
                RowCount = 4,
                BackColor = Color.Transparent
            };
            for (int i = 0; i < 3; i++) _pinPad.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            for (int i = 0; i < 4; i++) _pinPad.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
            BuildPinPad();

            _adminButton = new Button
            {
                Text = "⚙",
                Font = new Font("Segoe UI", 16),
                Size = new Size(45, 45),
                Location = new Point(310, 670),
                BackColor = Color.FromArgb(26, 26, 46),
                ForeColor = Color.FromArgb(150, 150, 170),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            _adminButton.FlatAppearance.BorderSize = 0;
            _adminButton.Click += AdminButton_Click;

            rightPanel.Controls.AddRange(new Control[] { lblUser, _userCombo, lblPin, _pinBox, _loginButton, _photoBox, _pinPad, _adminButton });

            mainPanel.Controls.Add(leftPanel, 0, 0);
            mainPanel.Controls.Add(rightPanel, 1, 0);
            Controls.Add(mainPanel);
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
                    BackColor = d == "OK" ? Color.FromArgb(124, 58, 237) : Color.FromArgb(42, 42, 70),
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand
                };
                btn.FlatAppearance.BorderSize = 0;
                btn.Click += (s, e) =>
                {
                    if (d == "⌫")
                    {
                        if (_pinBox.Text.Length > 0)
                            _pinBox.Text = _pinBox.Text.Substring(0, _pinBox.Text.Length - 1);
                    }
                    else if (d == "OK")
                    {
                        DoLogin();
                    }
                    else
                    {
                        _pinBox.Text += d;
                    }
                };
                _pinPad.Controls.Add(btn);
            }
        }

        private void LoadLogo()
        {
            var logoPath = _config.LogoPath;
            if (!string.IsNullOrEmpty(logoPath) && File.Exists(logoPath))
            {
                try { _logoBox.Image = Image.FromFile(logoPath); return; } catch { }
            }
            try
            {
                _logoBox.Image = CreateDefaultLogo();
            }
            catch { }
        }

        private Bitmap CreateDefaultLogo()
        {
            var bmp = new Bitmap(140, 140);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(15, 15, 35));
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(Color.FromArgb(124, 58, 237)))
                using (var font = new Font("Segoe UI", 48, FontStyle.Bold))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString("APAC", font, brush, new RectangleF(0, 0, 140, 140), sf);
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

                var timeInfo = CheckUserTimeAllowed(user);
                _timeInfoLabel.Text = timeInfo;

                if (_isAdminMode)
                {
                    _isAdminMode = false;
                }
            }
        }

        private string CheckUserTimeAllowed(UserListItem user)
        {
            if (!user.ProfileId.HasValue)
                return "";

            using var conn = _db.GetConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT day_of_week, start_time, end_time FROM time_rules WHERE profile_id = @pid";
            cmd.Parameters.AddWithValue("@pid", user.ProfileId.Value);
            using var reader = cmd.ExecuteReader();
            var dt = new DataTable();
            dt.Load(reader);

            if (dt.Rows.Count == 0)
                return "";

            var now = DateTime.Now;
            var currentDay = ((int)now.DayOfWeek) % 7; // 0=Sunday, ... 6=Saturday
            if (currentDay == 0) currentDay = 7; // Convert to 1=Monday...7=Sunday format

            foreach (DataRow row in dt.Rows)
            {
                var dayOfWeek = Convert.ToInt32(row["day_of_week"]);
                if (dayOfWeek != currentDay) continue;

                var startStr = row["start_time"].ToString();
                var endStr = row["end_time"].ToString();

                if (TimeSpan.TryParse(startStr, out var start) &&
                    TimeSpan.TryParse(endStr, out var end))
                {
                    if (now.TimeOfDay >= start && now.TimeOfDay <= end)
                        return $"Horário permitido: {start:h\\:mm} – {end:h\\:mm} ✓";

                    if (now.TimeOfDay < start)
                        return $"Próximo horário disponível: hoje às {start:h\\:mm}";
                }
            }

            var next = GetNextAvailableTime(currentDay, dt);
            return next ?? "Fora do horário permitido";
        }

        private string GetNextAvailableTime(int currentDay, DataTable rules)
        {
            if (rules.Rows.Count == 0) return null;

            var now = DateTime.Now;
            for (int offset = 0; offset < 7; offset++)
            {
                var checkDay = ((currentDay + offset) % 7);
                if (checkDay == 0) checkDay = 7;
                foreach (DataRow row in rules.Rows)
                {
                    var dayOfWeek = Convert.ToInt32(row["day_of_week"]);
                    if (dayOfWeek != checkDay) continue;
                    if (TimeSpan.TryParse(row["start_time"].ToString(), out var start))
                    {
                        if (offset == 0 && now.TimeOfDay >= start) continue;
                        var dayName = checkDay switch
                        {
                            1 => "segunda", 2 => "terça", 3 => "quarta", 4 => "quinta",
                            5 => "sexta", 6 => "sábado", 7 => "domingo", _ => ""
                        };
                        if (offset == 0)
                            return $"Próximo horário: hoje às {start:h\\:mm}";
                        if (offset == 1)
                            return $"Próximo horário: amanhã ({dayName}) às {start:h\\:mm}";
                        return $"Próximo horário: {dayName} às {start:h\\:mm}";
                    }
                }
            }
            return "Sem horários disponíveis";
        }

        private void PinBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
                DoLogin();
        }

        private void LoginButton_Click(object sender, EventArgs e)
        {
            DoLogin();
        }

        private void DoLogin()
        {
            if (_isAdminMode)
            {
                VerifyAdminPassword();
                return;
            }

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

            _db.InsertLog(user.Id, "login", null, $"Login do usuário: {user.FullName}");

            LoggedInUser = new Models.User
            {
                Id = user.Id,
                FullName = user.FullName,
                Username = user.Username,
                PhotoPath = user.PhotoPath,
                ProfileId = user.ProfileId,
                IsActive = user.IsActive
            };

            DialogResult = DialogResult.OK;
            Close();
        }

        private void VerifyAdminPassword()
        {
            var pin = _pinBox.Text;
            if (string.IsNullOrWhiteSpace(pin))
            {
                MessageBox.Show("Digite a senha de administrador.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using var conn = _db.GetConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT password_hash FROM admins WHERE username = 'admin'";
            var hash = cmd.ExecuteScalar()?.ToString();

            if (hash != null && PasswordHash.VerifyPassword(pin, hash))
            {
                _db.InsertLog(null, "admin_login", null, "Login de administrador");

                if (PasswordHash.VerifyPassword("APAC@Admin2024", hash))
                {
                    MessageBox.Show("ATENÇÃO: A senha de administrador ainda é a padrão.\nAltere-a no Painel de Administração > Configurações do Sistema.",
                        "Alerta de Segurança", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                IsAdmin = true;
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                MessageBox.Show("Senha de administrador inválida.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                _pinBox.Clear();
            }
        }

        private void AdminButton_Click(object sender, EventArgs e)
        {
            _isAdminMode = true;
            _pinBox.Clear();
            _pinBox.PasswordChar = '●';
            _userCombo.Enabled = false;

            var result = MessageBox.Show("Modo Administrador — digite a senha no campo PIN e pressione Enter.",
                "Admin", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
            if (result == DialogResult.Cancel)
            {
                _isAdminMode = false;
                _userCombo.Enabled = true;
            }
        }

        public new void Show()
        {
            base.Show();
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
