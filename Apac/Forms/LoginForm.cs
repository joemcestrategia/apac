using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Apac.Database;
using Apac.Services;

namespace Apac.Forms
{
    public class LoginForm : Form
    {
        private ComboBox _userCombo;
        private TextBox _pinBox;
        private Button _loginBtn;
        private Button _bioBtn;
        private Button _adminBtn;
        private PictureBox _photoBox;
        private Label _titleLabel;
        private Label _subtitleLabel;
        private Label _timeWarningLabel;
        private Panel _mainPanel;
        private Panel _footerPanel;

        private List<User> _users;
        private readonly DatabaseManager _db;
        private readonly IBiometricProvider _biometric;

        public LoginForm()
        {
            _db = DatabaseManager.Instance;
            _biometric = BiometricProviderFactory.Create();

            InitializeComponent();
            LoadUsers();

            if (_db.IsDefaultAdminPassword())
            {
                MessageBox.Show(
                    "ATENÇÃO: A senha padrão do administrador (APAC@Admin2024) está em uso.\n\n" +
                    "Por segurança, troque a senha no primeiro acesso ao Painel de Administração.",
                    "Alerta de Segurança", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void InitializeComponent()
        {
            this.Text = "APAC - Espaço de Acesso Digital";
            this.Size = new Size(900, 650);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = Color.FromArgb(15, 15, 35);
            this.KeyPreview = true;

            _mainPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(15, 15, 35)
            };

            _titleLabel = new Label
            {
                Text = _db.GetSetting("display_name", "APAC"),
                Font = new Font("Segoe UI", 32, FontStyle.Bold),
                ForeColor = Color.FromArgb(167, 139, 250),
                AutoSize = true,
                Location = new Point(0, 60)
            };

            _subtitleLabel = new Label
            {
                Text = _db.GetSetting("welcome_message", "Bem-vindo ao Espaço de Acesso Digital"),
                Font = new Font("Segoe UI", 13, FontStyle.Regular),
                ForeColor = Color.FromArgb(180, 180, 200),
                AutoSize = true,
                Location = new Point(0, 110)
            };

            _photoBox = new PictureBox
            {
                Size = new Size(120, 120),
                Location = new Point(0, 170),
                BackColor = Color.FromArgb(26, 26, 46),
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle
            };

            var userLabel = new Label
            {
                Text = "Usuário",
                Font = new Font("Segoe UI", 10, FontStyle.Regular),
                ForeColor = Color.FromArgb(167, 139, 250),
                Location = new Point(0, 310),
                AutoSize = true
            };

            _userCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 14, FontStyle.Regular),
                Size = new Size(300, 36),
                Location = new Point(0, 335),
                BackColor = Color.FromArgb(26, 26, 46),
                ForeColor = Color.White
            };
            _userCombo.SelectedIndexChanged += UserCombo_SelectedIndexChanged;

            var pinLabel = new Label
            {
                Text = "PIN (4 a 8 dígitos)",
                Font = new Font("Segoe UI", 10, FontStyle.Regular),
                ForeColor = Color.FromArgb(167, 139, 250),
                Location = new Point(0, 385),
                AutoSize = true
            };

            _pinBox = new TextBox
            {
                Font = new Font("Segoe UI", 14, FontStyle.Regular),
                Size = new Size(300, 36),
                Location = new Point(0, 410),
                PasswordChar = '\u25CF',
                MaxLength = 8,
                BackColor = Color.FromArgb(26, 26, 46),
                ForeColor = Color.White
            };
            _pinBox.KeyPress += PinBox_KeyPress;
            _pinBox.KeyDown += PinBox_KeyDown;

            _timeWarningLabel = new Label
            {
                Font = new Font("Segoe UI", 10, FontStyle.Regular),
                ForeColor = Color.FromArgb(251, 191, 36),
                Location = new Point(0, 455),
                AutoSize = true,
                Visible = false
            };

            _loginBtn = new Button
            {
                Text = "Entrar",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                Size = new Size(300, 45),
                Location = new Point(0, 490),
                BackColor = Color.FromArgb(124, 58, 237),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _loginBtn.FlatAppearance.BorderSize = 0;
            _loginBtn.Click += LoginBtn_Click;

            _bioBtn = new Button
            {
                Text = "Verificação Biométrica",
                Font = new Font("Segoe UI", 12, FontStyle.Regular),
                Size = new Size(300, 40),
                Location = new Point(0, 545),
                BackColor = Color.FromArgb(59, 130, 246),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Enabled = _biometric.IsAvailable,
                Visible = _biometric.IsAvailable
            };
            _bioBtn.FlatAppearance.BorderSize = 0;
            _bioBtn.Click += BioBtn_Click;

            _footerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                BackColor = Color.FromArgb(10, 10, 25)
            };

            _adminBtn = new Button
            {
                Text = "\u2699",
                Font = new Font("Segoe UI", 14, FontStyle.Regular),
                Size = new Size(40, 30),
                Location = new Point(840, 5),
                BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(100, 100, 120),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _adminBtn.FlatAppearance.BorderSize = 0;
            _adminBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 60, 80);
            _adminBtn.Click += AdminBtn_Click;

            _footerPanel.Controls.Add(_adminBtn);
            _mainPanel.Controls.AddRange(new Control[] {
                _titleLabel, _subtitleLabel, _photoBox,
                userLabel, _userCombo, pinLabel, _pinBox,
                _timeWarningLabel, _loginBtn, _bioBtn
            });
            this.Controls.Add(_mainPanel);
            this.Controls.Add(_footerPanel);

            CenterControls();
            this.Resize += (s, e) => CenterControls();
        }

        private void CenterControls()
        {
            int centerX = this.ClientSize.Width / 2;
            _titleLabel.Location = new Point(centerX - _titleLabel.Width / 2, 60);
            _subtitleLabel.Location = new Point(centerX - _subtitleLabel.Width / 2, 110);
            _photoBox.Location = new Point(centerX - _photoBox.Width / 2, 170);
            _userCombo.Location = new Point(centerX - _userCombo.Width / 2, 335);
            _pinBox.Location = new Point(centerX - _pinBox.Width / 2, 410);
            _timeWarningLabel.Location = new Point(centerX - _timeWarningLabel.Width / 2, 455);
            _loginBtn.Location = new Point(centerX - _loginBtn.Width / 2, 490);
            _bioBtn.Location = new Point(centerX - _bioBtn.Width / 2, 545);

            if (_userCombo.Left < 20)
            {
                foreach (Control c in _mainPanel.Controls)
                    c.Left = Math.Max(c.Left, 20);
            }
        }

        private void LoadUsers()
        {
            _users = _db.GetUsers().Where(u => u.IsActive).ToList();
            _userCombo.Items.Clear();
            foreach (var user in _users)
            {
                _userCombo.Items.Add($"{user.FullName} ({user.Username})");
            }
            if (_userCombo.Items.Count > 0)
                _userCombo.SelectedIndex = 0;
        }

        private void UserCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_userCombo.SelectedIndex >= 0 && _userCombo.SelectedIndex < _users.Count)
            {
                var user = _users[_userCombo.SelectedIndex];
                LoadPhoto(user);
                CheckTimeAvailability(user);
            }
        }

        private void LoadPhoto(User user)
        {
            if (!string.IsNullOrEmpty(user.PhotoPath) && File.Exists(user.PhotoPath))
            {
                try
                {
                    _photoBox.Image = Image.FromFile(user.PhotoPath);
                    return;
                }
                catch { }
            }
            _photoBox.Image = null;
            using (var bmp = new Bitmap(120, 120))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(26, 26, 46));
                using (var font = new Font("Segoe UI", 40, FontStyle.Regular))
                using (var brush = new SolidBrush(Color.FromArgb(60, 60, 80)))
                {
                    string initial = string.IsNullOrEmpty(user.FullName) ? "?" : user.FullName[0].ToString().ToUpper();
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(initial, font, brush, new RectangleF(0, 0, 120, 120), sf);
                }
            }
            _photoBox.Image = new Bitmap(120, 120);
            using (var g2 = Graphics.FromImage(_photoBox.Image))
            {
                g2.Clear(Color.FromArgb(26, 26, 46));
                string initial = string.IsNullOrEmpty(user.FullName) ? "?" : user.FullName[0].ToString().ToUpper();
                using (var font = new Font("Segoe UI", 40, FontStyle.Regular))
                using (var brush = new SolidBrush(Color.FromArgb(60, 60, 80)))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g2.DrawString(initial, font, brush, new RectangleF(0, 0, 120, 120), sf);
                }
            }
        }

        private void CheckTimeAvailability(User user)
        {
            _timeWarningLabel.Visible = false;
            if (!user.ProfileId.HasValue) return;

            var rules = _db.GetTimeRules(user.ProfileId.Value);
            if (rules.Count == 0) return;

            var now = DateTime.Now;
            var currentDay = (int)now.DayOfWeek;
            var currentTime = now.TimeOfDay;

            var todayRules = rules.Where(r => r.DayOfWeek == currentDay).ToList();
            if (todayRules.Count == 0)
            {
                var nextDay = FindNextAvailableDay(rules, currentDay, currentTime);
                _timeWarningLabel.Text = $"Fora do horário permitido.\nPróximo horário: {nextDay}";
                _timeWarningLabel.Visible = true;
                _loginBtn.Enabled = false;
                return;
            }

            var activeRule = todayRules.FirstOrDefault(r =>
                currentTime >= r.StartTime && currentTime <= r.EndTime);

            if (activeRule != null)
            {
                _loginBtn.Enabled = true;
                return;
            }

            var nextRule = todayRules.FirstOrDefault(r => currentTime < r.StartTime);
            if (nextRule != null)
            {
                _timeWarningLabel.Text = $"Fora do horário. Próximo horário hoje: {nextRule.StartTime:hh\\:mm} às {nextRule.EndTime:hh\\:mm}";
            }
            else
            {
                var nextDay = FindNextAvailableDay(rules, currentDay, currentTime);
                _timeWarningLabel.Text = $"Fora do horário.\n{nextDay}";
            }
            _timeWarningLabel.Visible = true;
            _loginBtn.Enabled = false;
        }

        private string FindNextAvailableDay(List<TimeRule> rules, int currentDay, TimeSpan currentTime)
        {
            for (int d = 1; d <= 7; d++)
            {
                int day = (currentDay + d) % 7;
                var dayRules = rules.Where(r => r.DayOfWeek == day).OrderBy(r => r.StartTime).ToList();
                if (dayRules.Count > 0)
                {
                    var firstRule = dayRules[0];
                    string dayName = GetDayName(day);
                    return $"{dayName} às {firstRule.StartTime:hh\\:mm}";
                }
            }
            return "Nenhum horário configurado";
        }

        private string GetDayName(int day)
        {
            string[] names = { "Domingo", "Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado" };
            return day >= 0 && day < 7 ? names[day] : "?";
        }

        private void PinBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar))
                e.Handled = true;
        }

        private void PinBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                DoLogin();
            }
        }

        private void LoginBtn_Click(object sender, EventArgs e)
        {
            DoLogin();
        }

        private void DoLogin()
        {
            if (_userCombo.SelectedIndex < 0)
            {
                MessageBox.Show("Selecione um usuário.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string pin = _pinBox.Text.Trim();
            if (pin.Length < 4 || pin.Length > 8)
            {
                MessageBox.Show("O PIN deve ter entre 4 e 8 dígitos.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var user = _users[_userCombo.SelectedIndex];
            if (_db.VerifyUserPin(user.Username, pin))
            {
                _db.AddLogEntry(EntryType.Login.ToString(), user.Id, null, $"Login de {user.Username}");
                var mainForm = new MainForm(user);
                mainForm.Show();
                this.Hide();
            }
            else
            {
                _db.AddLogEntry(EntryType.SystemEvent.ToString(), user.Id, null, $"Tentativa de login falhou para {user.Username}");
                MessageBox.Show("PIN incorreto.", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _pinBox.Text = "";
                _pinBox.Focus();
            }
        }

        private void BioBtn_Click(object sender, EventArgs e)
        {
            try
            {
                int? verifiedId = _biometric.Verify();
                if (verifiedId.HasValue)
                {
                    var user = _db.GetUserById(verifiedId.Value);
                    if (user != null)
                    {
                        _db.AddLogEntry(EntryType.Login.ToString(), user.Id, null, $"Login biométrico de {user.Username}");
                        var mainForm = new MainForm(user);
                        mainForm.Show();
                        this.Hide();
                    }
                }
                else
                {
                    MessageBox.Show("Usuário não reconhecido biometricamente.", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (NotImplementedException ex)
            {
                MessageBox.Show(ex.Message, "Biometria", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void AdminBtn_Click(object sender, EventArgs e)
        {
            using (var dialog = new AdminPasswordDialog())
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    if (_db.IsDefaultAdminPassword())
                    {
                        MessageBox.Show(
                            "ATENÇÃO: A senha padrão do administrador (APAC@Admin2024) está em uso.\n\n" +
                            "Troque a senha imediatamente no Painel de Administração > Configurações do Sistema.",
                            "Alerta de Segurança", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }

                    var adminPanel = new Admin.AdminPanelForm();
                    adminPanel.ShowDialog(this);
                    LoadUsers();
                }
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
        }
    }

    internal class AdminPasswordDialog : Form
    {
        private TextBox _passwordBox;
        private Button _okBtn;
        private Button _cancelBtn;

        public AdminPasswordDialog()
        {
            this.Text = "Acesso Administrativo";
            this.Size = new Size(380, 200);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(15, 15, 35);
            this.ForeColor = Color.White;

            var label = new Label
            {
                Text = "Senha do Administrador:",
                Location = new Point(30, 30),
                AutoSize = true,
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.FromArgb(200, 200, 220)
            };

            _passwordBox = new TextBox
            {
                Location = new Point(30, 55),
                Size = new Size(300, 25),
                PasswordChar = '\u25CF',
                Font = new Font("Segoe UI", 11),
                BackColor = Color.FromArgb(26, 26, 46),
                ForeColor = Color.White
            };
            _passwordBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) Verify();
            };

            _okBtn = new Button
            {
                Text = "OK",
                Location = new Point(160, 100),
                Size = new Size(80, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(124, 58, 237),
                ForeColor = Color.White
            };
            _okBtn.FlatAppearance.BorderSize = 0;
            _okBtn.Click += (s, e) => Verify();

            _cancelBtn = new Button
            {
                Text = "Cancelar",
                Location = new Point(250, 100),
                Size = new Size(80, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(60, 60, 80),
                ForeColor = Color.White
            };
            _cancelBtn.FlatAppearance.BorderSize = 0;
            _cancelBtn.Click += (s, e) => this.DialogResult = DialogResult.Cancel;

            this.Controls.AddRange(new Control[] { label, _passwordBox, _okBtn, _cancelBtn });
        }

        private void Verify()
        {
            if (DatabaseManager.Instance.VerifyAdminPassword(_passwordBox.Text))
            {
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            else
            {
                MessageBox.Show("Senha incorreta.", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _passwordBox.Text = "";
                _passwordBox.Focus();
            }
        }
    }
}
