using System;
using System.Drawing;
using System.IO;
using System.Diagnostics;
using System.Windows.Forms;
using Microsoft.Win32;
using ApacKiosk.Database;
using ApacKiosk.Utils;

namespace ApacKiosk.Forms.Admin
{
    public class SystemConfigTab : UserControl
    {
        private readonly DatabaseManager _db;
        private readonly ConfigManager _config;

        private TextBox _txtDisplayName;
        private TextBox _txtWelcomeMessage;
        private TextBox _txtLogoPath;
        private Button _btnBrowseLogo;
        private PictureBox _logoPreview;
        private CheckBox _chkAutostart;

        private TextBox _txtCurrentPassword;
        private TextBox _txtNewPassword;
        private TextBox _txtConfirmPassword;
        private Button _btnChangePassword;

        private NumericUpDown _numEmergencyMinutes;
        private TextBox _txtEmergencyReason;
        private Button _btnEmergencyDisable;

        private Button _btnSave;

        public SystemConfigTab(DatabaseManager db, ConfigManager config)
        {
            _db = db;
            _config = config;
            BackColor = Color.FromArgb(15, 15, 35);
            AutoScroll = true;
            InitializeComponent();
            LoadSettings();
        }

        private void InitializeComponent()
        {
            int y = 10;

            y = AddSectionLabel("🔤 Identificação", y);
            _txtDisplayName = AddTextField("Nome Exibido:", ref y);
            _txtWelcomeMessage = AddTextField("Mensagem de Boas-Vindas:", ref y);

            _txtLogoPath = AddTextField("Caminho do Logo:", ref y);
            _txtLogoPath.ReadOnly = true;
            _btnBrowseLogo = new Button
            {
                Text = "Procurar...", Location = new Point(550, y - 30), Size = new Size(100, 28),
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(59, 130, 246),
                ForeColor = Color.White, Cursor = Cursors.Hand
            };
            _btnBrowseLogo.FlatAppearance.BorderSize = 0;
            _btnBrowseLogo.Click += (s, e) =>
            {
                using var ofd = new OpenFileDialog { Filter = "Imagens|*.png;*.jpg;*.jpeg" };
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    _txtLogoPath.Text = ofd.FileName;
                    try { _logoPreview.Image = Image.FromFile(ofd.FileName); } catch { }
                }
            };

            _logoPreview = new PictureBox
            {
                Location = new Point(660, y - 30), Size = new Size(80, 80),
                SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(26, 26, 46),
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(_btnBrowseLogo);
            Controls.Add(_logoPreview);

            _chkAutostart = new CheckBox
            {
                Text = "Iniciar automaticamente com o Windows", Font = new Font("Segoe UI", 11),
                ForeColor = Color.FromArgb(220, 220, 230), Location = new Point(35, y + 5), AutoSize = true
            };
            Controls.Add(_chkAutostart);
            y += 55;

            y += 10;
            var sep1 = new Panel { Location = new Point(0, y), Size = new Size(600, 2), BackColor = Color.FromArgb(50, 50, 80) };
            Controls.Add(sep1);
            y += 20;

            y = AddSectionLabel("🔑 Alterar Senha do Admin", y);
            _txtCurrentPassword = AddPasswordField("Senha Atual:", ref y);
            _txtNewPassword = AddPasswordField("Nova Senha:", ref y);
            _txtConfirmPassword = AddPasswordField("Confirmar Nova Senha:", ref y);

            _btnChangePassword = new Button
            {
                Text = "Alterar Senha", Location = new Point(200, y + 5), Size = new Size(180, 38),
                Font = new Font("Segoe UI", 11, FontStyle.Bold), FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(200, 50, 50), ForeColor = Color.White, Cursor = Cursors.Hand
            };
            _btnChangePassword.FlatAppearance.BorderSize = 0;
            _btnChangePassword.Click += (s, e) => ChangePassword();
            Controls.Add(_btnChangePassword);
            y += 55;

            y += 10;
            var sep2 = new Panel { Location = new Point(0, y), Size = new Size(600, 2), BackColor = Color.FromArgb(50, 50, 80) };
            Controls.Add(sep2);
            y += 20;

            y = AddSectionLabel("🆘 Emergência", y);
            var lblEmergency = new Label
            {
                Text = "Desativa o kiosk temporariamente (requer reinicialização para reativar)",
                Font = new Font("Segoe UI", 10), ForeColor = Color.FromArgb(180, 180, 200),
                Location = new Point(35, y), AutoSize = true
            };
            Controls.Add(lblEmergency);
            y += 25;

            _numEmergencyMinutes = AddNumericWithLabel("Desativar por (minutos):", ref y, 1, 1440);
            _txtEmergencyReason = AddTextField("Justificativa:", ref y);

            _btnEmergencyDisable = new Button
            {
                Text = "Desativar Kiosk Temporariamente", Location = new Point(200, y + 5), Size = new Size(280, 38),
                Font = new Font("Segoe UI", 11, FontStyle.Bold), FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(245, 158, 11), ForeColor = Color.White, Cursor = Cursors.Hand
            };
            _btnEmergencyDisable.FlatAppearance.BorderSize = 0;
            _btnEmergencyDisable.Click += (s, e) => EmergencyDisable();
            Controls.Add(_btnEmergencyDisable);
            y += 55;

            _btnSave = new Button
            {
                Text = "Salvar Configurações", Location = new Point(200, y + 10), Size = new Size(220, 42),
                Font = new Font("Segoe UI", 12, FontStyle.Bold), FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(124, 58, 237), ForeColor = Color.White, Cursor = Cursors.Hand
            };
            _btnSave.FlatAppearance.BorderSize = 0;
            _btnSave.Click += (s, e) => SaveSettings();
            Controls.Add(_btnSave);
        }

        private int AddSectionLabel(string text, int y)
        {
            var lbl = new Label
            {
                Text = text, Font = new Font("Segoe UI", 14, FontStyle.Bold),
                ForeColor = Color.FromArgb(124, 58, 237), Location = new Point(15, y), AutoSize = true
            };
            Controls.Add(lbl);
            return y + 35;
        }

        private TextBox AddTextField(string label, ref int y)
        {
            var lbl = new Label
            {
                Text = label, Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 200, 220), Location = new Point(50, y + 5), AutoSize = true
            };
            var txt = new TextBox
            {
                Location = new Point(350, y), Size = new Size(340, 28),
                Font = new Font("Segoe UI", 10), BackColor = Color.FromArgb(26, 26, 46),
                ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(lbl);
            Controls.Add(txt);
            y += 35;
            return txt;
        }

        private TextBox AddPasswordField(string label, ref int y)
        {
            var txt = AddTextField(label, ref y);
            txt.PasswordChar = '●';
            return txt;
        }

        private NumericUpDown AddNumericWithLabel(string label, ref int y, int min, int max)
        {
            var lbl = new Label
            {
                Text = label, Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 200, 220), Location = new Point(50, y + 2), AutoSize = true
            };
            var num = new NumericUpDown
            {
                Location = new Point(350, y), Size = new Size(100, 28),
                Minimum = min, Maximum = max, Font = new Font("Segoe UI", 10),
                BackColor = Color.FromArgb(26, 26, 46), ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(lbl);
            Controls.Add(num);
            y += 35;
            return num;
        }

        private void LoadSettings()
        {
            _txtDisplayName.Text = _config.DisplayName;
            _txtWelcomeMessage.Text = _config.WelcomeMessage;
            _txtLogoPath.Text = _config.LogoPath;
            if (!string.IsNullOrEmpty(_config.LogoPath) && File.Exists(_config.LogoPath))
            {
                try { _logoPreview.Image = Image.FromFile(_config.LogoPath); } catch { }
            }
            _chkAutostart.Checked = _config.AutostartEnabled;
        }

        private void SaveSettings()
        {
            _config.DisplayName = _txtDisplayName.Text;
            _config.WelcomeMessage = _txtWelcomeMessage.Text;
            _config.LogoPath = _txtLogoPath.Text;
            _config.AutostartEnabled = _chkAutostart.Checked;

            UpdateAutostartRegistry(_chkAutostart.Checked);

            _db.InsertLog(null, "event", null, "Configurações do sistema atualizadas");

            MessageBox.Show("Configurações salvas com sucesso!", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void UpdateAutostartRegistry(bool enable)
        {
            try
            {
                var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                if (key == null) return;

                if (enable)
                {
                    var exePath = Application.ExecutablePath;
                    key.SetValue("ApacKioskGuardian", exePath);
                }
                else
                {
                    try { key.DeleteValue("ApacKioskGuardian", false); } catch { }
                }
                key.Close();
            }
            catch { }
        }

        private void ChangePassword()
        {
            var current = _txtCurrentPassword.Text;
            var newPass = _txtNewPassword.Text;
            var confirm = _txtConfirmPassword.Text;

            if (string.IsNullOrEmpty(current) || string.IsNullOrEmpty(newPass))
            {
                MessageBox.Show("Preencha todos os campos.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (newPass != confirm)
            {
                MessageBox.Show("As senhas não coincidem.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (newPass.Length < 6)
            {
                MessageBox.Show("A senha deve ter pelo menos 6 caracteres.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using var conn = _db.GetConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT password_hash FROM admins WHERE username = 'admin'";
            var hash = cmd.ExecuteScalar()?.ToString();

            if (hash == null || !BCrypt.Net.BCrypt.Verify(current, hash))
            {
                MessageBox.Show("Senha atual incorreta.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var newHash = BCrypt.Net.BCrypt.HashPassword(newPass);
            cmd.CommandText = "UPDATE admins SET password_hash = @h WHERE username = 'admin'";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@h", newHash);
            cmd.ExecuteNonQuery();

            _db.InsertLog(null, "event", null, "Senha do administrador alterada");

            _txtCurrentPassword.Clear();
            _txtNewPassword.Clear();
            _txtConfirmPassword.Clear();

            MessageBox.Show("Senha alterada com sucesso!", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void EmergencyDisable()
        {
            var reason = _txtEmergencyReason.Text;
            if (string.IsNullOrWhiteSpace(reason))
            {
                MessageBox.Show("Informe a justificativa para desativação de emergência.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var pwdText = Microsoft.VisualBasic.Interaction.InputBox("Digite a senha de administrador:", "Confirmação de Emergência", "");
            using var conn = _db.GetConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT password_hash FROM admins WHERE username = 'admin'";
            var hash = cmd.ExecuteScalar()?.ToString();

            if (hash == null || !BCrypt.Net.BCrypt.Verify(pwdText, hash))
            {
                MessageBox.Show("Senha incorreta.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var minutes = (int)_numEmergencyMinutes.Value;
            _config.EmergencyDisableMinutes = minutes;
            _db.InsertLog(null, "event", null, $"DESATIVAÇÃO DE EMERGÊNCIA: {minutes}min — Justificativa: {reason}");

            MessageBox.Show(
                $"O kiosk será desativado por {minutes} minutos após fechar esta janela.\n" +
                "Todos os controles de segurança serão suspensos.\n\n" +
                "O sistema será reativado automaticamente após o tempo configurado OU na próxima reinicialização.",
                "EMERGÊNCIA", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            var form = FindForm();
            if (form != null)
            {
                var timer = new System.Windows.Forms.Timer { Interval = minutes * 60 * 1000 };
                timer.Tick += (s, e) => { timer.Stop(); _config.EmergencyDisableMinutes = 0; };
                timer.Start();
                form.Close();
            }
        }
    }
}
