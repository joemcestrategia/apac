using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Apac.Database;
using Apac.Models;

namespace Apac.Forms
{
    public class LoginForm : Form
    {
        private TextBox _usernameBox;
        private TextBox _pinBox;
        private PictureBox _photoBox;
        private Label _statusLabel;
        private Label _nextTimeLabel;
        private Button _loginButton;
        private Button _adminButton;
        private System.Windows.Forms.Timer _clockTimer;

        public User LoggedInUser { get; private set; }
        public bool IsAdminLogin { get; private set; }

        public LoginForm()
        {
            InitializeComponent();
            LoadSystemConfig();
            _clockTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _clockTimer.Tick += (s, e) => UpdateClock();
            _clockTimer.Start();
        }

        private void InitializeComponent()
        {
            this.Text = "APAC - Login";
            this.Size = new Size(550, 500);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(15, 15, 35);
            this.ForeColor = Color.White;
            this.TopMost = true;

            var logoPanel = new Panel
            {
                Size = new Size(550, 100),
                Location = new Point(0, 0),
                BackColor = Color.FromArgb(20, 20, 45)
            };

            var logoLabel = new Label
            {
                Text = "APAC",
                Font = new Font("Segoe UI", 28, FontStyle.Bold),
                ForeColor = Color.FromArgb(124, 58, 237),
                AutoSize = true,
                Location = new Point(200, 25)
            };

            var clockLabel = new Label
            {
                Name = "clockLabel",
                Font = new Font("Segoe UI", 12),
                ForeColor = Color.FromArgb(167, 139, 250),
                AutoSize = true,
                Location = new Point(380, 45)
            };

            logoPanel.Controls.Add(logoLabel);
            logoPanel.Controls.Add(clockLabel);

            var mainPanel = new Panel
            {
                Size = new Size(550, 400),
                Location = new Point(0, 100)
            };

            var titleLabel = new Label
            {
                Text = "Acesso ao Terminal",
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                ForeColor = Color.FromArgb(167, 139, 250),
                AutoSize = true,
                Location = new Point(150, 15)
            };

            var userLabel = new Label
            {
                Text = "Usuário:",
                Font = new Font("Segoe UI", 11),
                Location = new Point(120, 70),
                AutoSize = true
            };

            _usernameBox = new TextBox
            {
                Location = new Point(120, 95),
                Size = new Size(290, 30),
                Font = new Font("Segoe UI", 13),
                BackColor = Color.FromArgb(26, 26, 46),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            var pinLabel = new Label
            {
                Text = "PIN:",
                Font = new Font("Segoe UI", 11),
                Location = new Point(120, 140),
                AutoSize = true
            };

            _pinBox = new TextBox
            {
                Location = new Point(120, 165),
                Size = new Size(290, 30),
                Font = new Font("Segoe UI", 13),
                PasswordChar = '*',
                BackColor = Color.FromArgb(26, 26, 46),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                MaxLength = 8
            };

            _photoBox = new PictureBox
            {
                Size = new Size(80, 80),
                Location = new Point(20, 65),
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.Zoom
            };

            _statusLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.FromArgb(239, 68, 68),
                Location = new Point(120, 210),
                Size = new Size(290, 25),
                TextAlign = ContentAlignment.MiddleCenter
            };

            _nextTimeLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(252, 211, 77),
                Location = new Point(120, 240),
                Size = new Size(290, 40),
                TextAlign = ContentAlignment.MiddleCenter
            };

            _loginButton = new Button
            {
                Text = "Entrar",
                Location = new Point(120, 295),
                Size = new Size(290, 40),
                Font = new Font("Segoe UI", 13, FontStyle.Bold),
                BackColor = Color.FromArgb(124, 58, 237),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _loginButton.FlatAppearance.BorderSize = 0;
            _loginButton.Click += LoginButton_Click;

            _adminButton = new Button
            {
                Text = "\u2699",
                Font = new Font("Segoe UI", 11),
                Location = new Point(10, 355),
                Size = new Size(40, 30),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.FromArgb(167, 139, 250),
                BackColor = Color.FromArgb(26, 26, 46)
            };
            _adminButton.FlatAppearance.BorderSize = 0;
            _adminButton.Click += AdminButton_Click;

            var tooltip = new ToolTip();
            tooltip.SetToolTip(_adminButton, "Administração");

            mainPanel.Controls.AddRange(new Control[] {
                titleLabel, userLabel, _usernameBox, pinLabel, _pinBox,
                _photoBox, _statusLabel, _nextTimeLabel, _loginButton, _adminButton
            });

            this.Controls.Add(logoPanel);
            this.Controls.Add(mainPanel);

            _usernameBox.TextChanged += (s, e) => LoadUserPhoto();
            _pinBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) LoginButton_Click(s, e); };
        }

        private void LoadUserPhoto()
        {
            _photoBox.Image = null;
            if (string.IsNullOrWhiteSpace(_usernameBox.Text)) return;

            var users = DatabaseService.Instance.GetAllUsers();
            var user = users.FirstOrDefault(u =>
                u.Username.Equals(_usernameBox.Text, StringComparison.OrdinalIgnoreCase));

            if (user != null && !string.IsNullOrEmpty(user.PhotoPath) && File.Exists(user.PhotoPath))
            {
                try
                {
                    _photoBox.Image = Image.FromFile(user.PhotoPath);
                }
                catch { }
            }
        }

        private void LoginButton_Click(object sender, EventArgs e)
        {
            _statusLabel.Text = "";
            _nextTimeLabel.Text = "";

            if (string.IsNullOrWhiteSpace(_usernameBox.Text) || string.IsNullOrWhiteSpace(_pinBox.Text))
            {
                _statusLabel.Text = "Preencha usuário e PIN.";
                return;
            }

            string pinHash = BCrypt.Net.BCrypt.HashPassword(_pinBox.Text, "$2a$10$" + new string('a', 22));
            var user = DatabaseService.Instance.AuthenticateUser(_usernameBox.Text, null);

            if (user == null)
            {
                _statusLabel.Text = "Usuário não encontrado ou inativo.";
                return;
            }

            if (!BCrypt.Net.BCrypt.Verify(_pinBox.Text, user.PinHash))
            {
                DatabaseService.Instance.InsertLogEntry(new LogEntry
                {
                    UserId = user.Id,
                    Type = "system_event",
                    Details = "Login failed - wrong PIN"
                });
                _statusLabel.Text = "PIN incorreto.";
                return;
            }

            var sessionMgr = new Services.SessionManager(user);
            if (!sessionMgr.IsWithinTimeRules())
            {
                var nextTime = sessionMgr.GetNextAvailableTime();
                string nextTimeStr = nextTime.HasValue ? nextTime.Value.ToString("dd/MM/yyyy HH:mm") : "consulte o administrador";
                _nextTimeLabel.Text = $"Fora do horário permitido.\nPróximo horário disponível: {nextTimeStr}";
                _statusLabel.Text = "Acesso não permitido neste horário.";
                return;
            }

            LoggedInUser = user;
            IsAdminLogin = false;

            DatabaseService.Instance.InsertLogEntry(new LogEntry
            {
                UserId = user.Id,
                Type = "system_event",
                Details = "Login successful"
            });

            DialogResult = DialogResult.OK;
            Close();
        }

        private void AdminButton_Click(object sender, EventArgs e)
        {
            using (var dialog = new Form())
            {
                dialog.Text = "Acesso Admin";
                dialog.Size = new Size(380, 200);
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.BackColor = Color.FromArgb(15, 15, 35);
                dialog.ForeColor = Color.White;

                var label = new Label
                {
                    Text = "Senha de administrador:",
                    Location = new Point(20, 25),
                    AutoSize = true,
                    Font = new Font("Segoe UI", 11)
                };

                var textBox = new TextBox
                {
                    Location = new Point(20, 60),
                    Size = new Size(320, 25),
                    PasswordChar = '*',
                    Font = new Font("Segoe UI", 13),
                    BackColor = Color.FromArgb(26, 26, 46),
                    ForeColor = Color.White,
                    BorderStyle = BorderStyle.FixedSingle
                };

                var okButton = new Button
                {
                    Text = "Entrar",
                    Location = new Point(160, 110),
                    Size = new Size(85, 30),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(124, 58, 237),
                    ForeColor = Color.White,
                    DialogResult = DialogResult.OK
                };
                okButton.FlatAppearance.BorderSize = 0;

                var cancelButton = new Button
                {
                    Text = "Cancelar",
                    Location = new Point(255, 110),
                    Size = new Size(85, 30),
                    FlatStyle = FlatStyle.Flat,
                    ForeColor = Color.FromArgb(167, 139, 250),
                    DialogResult = DialogResult.Cancel
                };

                dialog.Controls.AddRange(new Control[] { label, textBox, okButton, cancelButton });
                dialog.AcceptButton = okButton;
                dialog.CancelButton = cancelButton;

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    var hash = DatabaseService.Instance.GetAdminPasswordHash("admin");
                    if (hash != null && BCrypt.Net.BCrypt.Verify(textBox.Text, hash))
                    {
                        IsAdminLogin = true;
                        DialogResult = DialogResult.OK;
                        Close();
                    }
                    else
                    {
                        MessageBox.Show("Senha incorreta.", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void UpdateClock()
        {
            var clockLabel = this.Controls.Find("clockLabel", true).FirstOrDefault() as Label;
            if (clockLabel != null)
                clockLabel.Text = DateTime.Now.ToString("HH:mm:ss");
        }

        private void LoadSystemConfig()
        {
            var config = DatabaseService.Instance.GetSystemConfig();
            if (!string.IsNullOrEmpty(config.DisplayName))
                this.Text = $"{config.DisplayName} - Login";
            if (!string.IsNullOrEmpty(config.LogoPath) && File.Exists(config.LogoPath))
            {
                try
                {
                    var logoPanel = this.Controls[0] as Panel;
                    if (logoPanel != null)
                    {
                        var logoPic = new PictureBox
                        {
                            Image = Image.FromFile(config.LogoPath),
                            Size = new Size(80, 80),
                            Location = new Point(20, 10),
                            SizeMode = PictureBoxSizeMode.Zoom
                        };
                        logoPanel.Controls.Add(logoPic);
                    }
                }
                catch { }
            }
        }
    }
}
