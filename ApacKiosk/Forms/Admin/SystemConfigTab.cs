using System;
using System.Drawing;
using System.IO;
using System.Diagnostics;
using System.Windows.Forms;
using Microsoft.Win32;
using ApacKiosk.Database;
using ApacKiosk.Services;
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

        private Label _lblSerialKey;
        private Button _btnChangeSerial;

        private TextBox _txtCurrentPassword;
        private TextBox _txtNewPassword;
        private TextBox _txtConfirmPassword;
        private Button _btnChangePassword;

        private NumericUpDown _numEmergencyMinutes;
        private TextBox _txtEmergencyReason;
        private Button _btnEmergencyDisable;

        private Button _btnCheckUpdate;

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
            _txtLogoPath.Size = new Size(190, 28);
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
                if (ofd.ShowDialog(this.FindForm()) == DialogResult.OK)
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

            y = AddSectionLabel("🛡️ Licença e Ativação", y);

            var lblSerialDesc = new Label
            {
                Text = "Serial Ativo:",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 200, 220),
                Location = new Point(50, y + 8),
                AutoSize = true
            };
            Controls.Add(lblSerialDesc);

            _lblSerialKey = new Label
            {
                Text = "Nenhuma licença ativa",
                Font = new Font("Consolas", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 211, 153),
                BackColor = Color.FromArgb(26, 26, 46),
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(200, y + 4),
                Size = new Size(320, 32),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0)
            };
            Controls.Add(_lblSerialKey);

            _btnChangeSerial = new Button
            {
                Text = "🔑 Alterar Serial",
                Location = new Point(530, y + 3),
                Size = new Size(160, 34),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(59, 130, 246),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            _btnChangeSerial.FlatAppearance.BorderSize = 0;
            _btnChangeSerial.Click += (s, e) => ChangeSerial();
            Controls.Add(_btnChangeSerial);
            y += 50;

            y += 10;
            var sepLicense = new Panel { Location = new Point(0, y), Size = new Size(600, 2), BackColor = Color.FromArgb(50, 50, 80) };
            Controls.Add(sepLicense);
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

            y += 10;
            var sepUpdate = new Panel { Location = new Point(0, y), Size = new Size(600, 2), BackColor = Color.FromArgb(50, 50, 80) };
            Controls.Add(sepUpdate);
            y += 20;

            y = AddSectionLabel("🔄 Atualização do Sistema", y);
            var currentVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
            var lblUpdateInfo = new Label
            {
                Text = $"Versão atual: v{currentVer}\nVerifica se há novas versões disponíveis na internet e atualiza mantendo todas as configurações.",
                Font = new Font("Segoe UI", 10), ForeColor = Color.FromArgb(180, 180, 200),
                Location = new Point(35, y), AutoSize = true
            };
            Controls.Add(lblUpdateInfo);
            y += 45;

            _btnCheckUpdate = new Button
            {
                Text = "Verificar e Atualizar Sistema", Location = new Point(200, y + 5), Size = new Size(280, 38),
                Font = new Font("Segoe UI", 11, FontStyle.Bold), FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(16, 185, 129), ForeColor = Color.White, Cursor = Cursors.Hand
            };
            _btnCheckUpdate.FlatAppearance.BorderSize = 0;
            _btnCheckUpdate.Click += (s, e) => CheckAndUpdateSystem();
            Controls.Add(_btnCheckUpdate);
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
            UpdateSerialDisplay();
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

            KioskDialog.ShowMessage("Configurações salvas com sucesso!", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information, this);
        }

        private void ChangeSerial()
        {
            var licenseManager = new ApacKiosk.Services.LicenseManager(_config);
            using var actForm = new ActivationForm(licenseManager, _config);
            if (actForm.ShowDialog(this.FindForm()) == DialogResult.OK)
            {
                UpdateSerialDisplay();
                _db.InsertLog(null, "event", null, "Chave serial atualizada");
            }
        }

        private void UpdateSerialDisplay()
        {
            var serial = _config.LicenseSignature;
            if (string.IsNullOrWhiteSpace(serial))
            {
                _lblSerialKey.Text = "Nenhuma licença ativa";
                _lblSerialKey.ForeColor = Color.FromArgb(239, 68, 68);
            }
            else
            {
                _lblSerialKey.Text = serial;
                _lblSerialKey.ForeColor = Color.FromArgb(52, 211, 153);
            }
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
                KioskDialog.ShowMessage("Preencha todos os campos.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning, this);
                return;
            }

            if (newPass != confirm)
            {
                KioskDialog.ShowMessage("As senhas não coincidem.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning, this);
                return;
            }

            if (newPass.Length < 6)
            {
                KioskDialog.ShowMessage("A senha deve ter pelo menos 6 caracteres.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning, this);
                return;
            }

            using var conn = _db.GetConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT password_hash FROM admins WHERE username = 'admin'";
            var hash = cmd.ExecuteScalar()?.ToString();

            if (hash == null || !BCrypt.Net.BCrypt.Verify(current, hash))
            {
                KioskDialog.ShowMessage("Senha atual incorreta.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error, this);
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

            KioskDialog.ShowMessage("Senha alterada com sucesso!", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information, this);
        }

        private void EmergencyDisable()
        {
            var reason = _txtEmergencyReason.Text;
            if (string.IsNullOrWhiteSpace(reason))
            {
                KioskDialog.ShowMessage("Informe a justificativa para desativação de emergência.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning, this);
                return;
            }

            var pwdText = KioskDialog.ShowInput("Digite a senha de administrador:", "Confirmação de Emergência", "", true, this);
            if (string.IsNullOrEmpty(pwdText)) return;
            using var conn = _db.GetConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT password_hash FROM admins WHERE username = 'admin'";
            var hash = cmd.ExecuteScalar()?.ToString();

            if (hash == null || !BCrypt.Net.BCrypt.Verify(pwdText, hash))
            {
                KioskDialog.ShowMessage("Senha incorreta.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error, this);
                return;
            }

            var minutes = (int)_numEmergencyMinutes.Value;
            _config.EmergencyDisableMinutes = minutes;
            _db.InsertLog(null, "event", null, $"DESATIVAÇÃO DE EMERGÊNCIA: {minutes}min — Justificativa: {reason}");

            KioskDialog.ShowMessage(
                $"O kiosk será desativado por {minutes} minutos após fechar esta janela.\n" +
                "Todos os controles de segurança serão suspensos.\n\n" +
                "O sistema será reativado automaticamente após o tempo configurado OU na próxima reinicialização.",
                "EMERGÊNCIA", MessageBoxButtons.OK, MessageBoxIcon.Warning, this);

            var form = FindForm();
            if (form != null)
            {
                var timer = new System.Windows.Forms.Timer { Interval = minutes * 60 * 1000 };
                timer.Tick += (s, e) => { timer.Stop(); _config.EmergencyDisableMinutes = 0; };
                timer.Start();
                form.Close();
            }
        }

        private async void CheckAndUpdateSystem()
        {
            var form = FindForm();
            _btnCheckUpdate.Enabled = false;
            _btnCheckUpdate.Text = "Verificando...";

            try
            {
                var updateManager = new UpdateManager();
                var (success, message) = await updateManager.PerformManualUpdateAsync(form, progress =>
                {
                    if (form != null && !form.IsDisposed)
                    {
                        form.Invoke(() => _btnCheckUpdate.Text = progress);
                    }
                });

                if (!success)
                {
                    KioskDialog.ShowMessage(message, _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information, (Control?)form ?? this);
                }
            }
            catch (Exception ex)
            {
                KioskDialog.ShowMessage($"Erro ao atualizar sistema:\n{ex.Message}", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error, (Control?)form ?? this);
            }
            finally
            {
                if (!_btnCheckUpdate.IsDisposed)
                {
                    _btnCheckUpdate.Enabled = true;
                    _btnCheckUpdate.Text = "Verificar e Atualizar Sistema";
                }
            }
        }
    }
}
