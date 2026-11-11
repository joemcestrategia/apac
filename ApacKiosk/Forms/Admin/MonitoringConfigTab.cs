using System;
using System.Drawing;
using System.Windows.Forms;
using ApacKiosk.Database;
using ApacKiosk.Services;
using ApacKiosk.Utils;

namespace ApacKiosk.Forms.Admin
{
    public class MonitoringConfigTab : UserControl
    {
        private readonly DatabaseManager _db;
        private readonly ConfigManager _config;

        private CheckBox _chkScreenshotEnabled;
        private NumericUpDown _numScreenshotInterval;
        private ComboBox _cmbScreenshotQuality;
        private TextBox _txtBaseLogPath;
        private Button _btnBrowseBaseLog;

        private CheckBox _chkCameraEnabled;
        private NumericUpDown _numCameraInterval;
        private ComboBox _cmbCameraQuality;
        private ComboBox _cmbCameraDevice;
        private Button _btnTestCamera;
        private PictureBox _cameraPreview;

        private CheckBox _chkKeyloggerEnabled;

        private NumericUpDown _numRetentionDays;
        private NumericUpDown _numMaxSizeGb;
        private Button _btnSave;

        public MonitoringConfigTab(DatabaseManager db, ConfigManager config)
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

            y = AddSectionLabel("📂 Diretório Base de Armazenamento", y);
            _txtBaseLogPath = AddPathWithBrowse("Pasta base para arquivos (Logs, Prints e Câmera):", ref y, out _btnBrowseBaseLog, "Logs");
            _btnBrowseBaseLog.Click += (s, e) => BrowseFolder(_txtBaseLogPath);
            y += 10;

            y = AddSectionLabel("📸 Screenshots", y);
            _chkScreenshotEnabled = AddCheckbox("Ativar captura de tela", ref y);
            _numScreenshotInterval = AddNumericWithLabel("Intervalo (segundos, mín. 10):", ref y, 10, 3600);
            _cmbScreenshotQuality = AddComboWithLabel("Qualidade:", ref y, new[] { "Baixa", "Média", "Alta" });

            y += 10;
            int yCameraStart = y;
            y = AddSectionLabel("📷 Câmera", y);
            _chkCameraEnabled = AddCheckbox("Ativar captura de câmera", ref y);
            _numCameraInterval = AddNumericWithLabel("Intervalo (segundos, mín. 30):", ref y, 30, 3600);
            _cmbCameraQuality = AddComboWithLabel("Qualidade:", ref y, new[] { "Baixa", "Média", "Alta" });
            _cmbCameraDevice = AddComboWithLabel("Dispositivo de Câmera:", ref y, CameraCapture.GetCameraDevices());

            _btnTestCamera = new Button
            {
                Text = "🔍 Testar Câmera",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Size = new Size(160, 32),
                Location = new Point(450, y),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(59, 130, 246),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            _btnTestCamera.FlatAppearance.BorderSize = 0;
            _btnTestCamera.Click += (s, e) => TestCamera();
            Controls.Add(_btnTestCamera);
            y += 45;

            _cameraPreview = new PictureBox
            {
                Size = new Size(240, 180),
                Location = new Point(660, yCameraStart + 35),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(26, 26, 46),
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(_cameraPreview);
            
            int previewBottom = _cameraPreview.Bottom + 20;
            if (y < previewBottom) y = previewBottom;

            y += 10;
            y = AddSectionLabel("⌨ Keylogger", y);
            _chkKeyloggerEnabled = AddCheckbox("Ativar keylogger", ref y);

            y += 20;
            var sepLine = new Panel { Location = new Point(0, y), Size = new Size(1000, 2), BackColor = Color.FromArgb(50, 50, 80) };
            Controls.Add(sepLine);
            y += 20;

            y = AddSectionLabel("⚙ Retenção de Dados", y);
            _numRetentionDays = AddNumericWithLabel("Manter arquivos por (dias):", ref y, 1, 365);
            _numMaxSizeGb = AddNumericWithLabel("Tamanho máximo da pasta (GB):", ref y, 1, 500);

            var storageInfo = new Label
            {
                Text = GetStorageInfo(),
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.FromArgb(167, 139, 250),
                Location = new Point(20, y),
                AutoSize = true
            };
            y += 30;
            Controls.Add(storageInfo);

            _btnSave = new Button
            {
                Text = "Salvar Configurações",
                Location = new Point(200, y + 15),
                Size = new Size(220, 42),
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(124, 58, 237),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
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
                ForeColor = Color.FromArgb(124, 58, 237), Location = new Point(15, y),
                AutoSize = true
            };
            Controls.Add(lbl);
            return y + 30;
        }

        private CheckBox AddCheckbox(string text, ref int y)
        {
            var chk = new CheckBox
            {
                Text = text, Font = new Font("Segoe UI", 11),
                ForeColor = Color.FromArgb(220, 220, 230),
                Location = new Point(35, y), AutoSize = true
            };
            Controls.Add(chk);
            y += 30;
            return chk;
        }

        private NumericUpDown AddNumericWithLabel(string label, ref int y, int min, int max)
        {
            var lbl = new Label
            {
                Text = label, Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 200, 220), Location = new Point(50, y + 2),
                AutoSize = true
            };
            var num = new NumericUpDown
            {
                Location = new Point(450, y), Size = new Size(80, 28),
                Minimum = min, Maximum = max, Font = new Font("Segoe UI", 10),
                BackColor = Color.FromArgb(26, 26, 46), ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(lbl);
            Controls.Add(num);
            y += 40;
            return num;
        }

        private ComboBox AddComboWithLabel(string label, ref int y, string[] items)
        {
            var lbl = new Label
            {
                Text = label, Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 200, 220), Location = new Point(50, y + 2),
                AutoSize = true
            };
            var cmb = new ComboBox
            {
                Location = new Point(450, y), Size = new Size(180, 28),
                DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 10),
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(26, 26, 46),
                ForeColor = Color.White
            };
            cmb.Items.AddRange(items);
            Controls.Add(lbl);
            Controls.Add(cmb);
            y += 40;
            return cmb;
        }

        private TextBox AddPathWithBrowse(string label, ref int y, out Button browseBtn, string defaultFolder)
        {
            var lbl = new Label
            {
                Text = label, Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 200, 220), Location = new Point(50, y + 5),
                AutoSize = true
            };
            var txt = new TextBox
            {
                Location = new Point(450, y), Size = new Size(320, 28),
                Font = new Font("Segoe UI", 9), BackColor = Color.FromArgb(26, 26, 46),
                ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, ReadOnly = true
            };
            browseBtn = new Button
            {
                Text = "...", Location = new Point(780, y), Size = new Size(35, 28),
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(59, 130, 246),
                ForeColor = Color.White, Cursor = Cursors.Hand
            };
            browseBtn.FlatAppearance.BorderSize = 0;
            Controls.Add(lbl);
            Controls.Add(txt);
            Controls.Add(browseBtn);
            y += 40;
            return txt;
        }

        private void BrowseFolder(TextBox target)
        {
            using var fbd = new FolderBrowserDialog { Description = "Selecione a pasta de destino" };
            if (fbd.ShowDialog(this.FindForm()) == DialogResult.OK)
                target.Text = fbd.SelectedPath;
        }

        private void LoadSettings()
        {
            _chkScreenshotEnabled.Checked = _config.ScreenshotEnabled;
            _numScreenshotInterval.Value = _config.ScreenshotIntervalSec;
            _cmbScreenshotQuality.Text = _config.ScreenshotQuality switch
            {
                "Low" => "Baixa", "High" => "Alta", _ => "Média"
            };
            _txtBaseLogPath.Text = _config.BaseLogPath;

            _chkCameraEnabled.Checked = _config.CameraEnabled;
            _numCameraInterval.Value = _config.CameraIntervalSec;
            _cmbCameraQuality.Text = _config.CameraQuality switch
            {
                "Low" => "Baixa", "High" => "Alta", _ => "Média"
            };


            var devices = CameraCapture.GetCameraDevices();
            if (!string.IsNullOrEmpty(_config.CameraDeviceName) && devices.Length > 0)
            {
                var idx = Array.IndexOf(devices, _config.CameraDeviceName);
                if (idx >= 0) _cmbCameraDevice.SelectedIndex = idx;
                else if (_cmbCameraDevice.Items.Count > 0) _cmbCameraDevice.SelectedIndex = 0;
            }
            else if (_cmbCameraDevice.Items.Count > 0)
            {
                _cmbCameraDevice.SelectedIndex = 0;
            }

            _chkKeyloggerEnabled.Checked = _config.KeyloggerEnabled;

            _numRetentionDays.Value = _config.LogRetentionDays;
            _numMaxSizeGb.Value = (decimal)_config.LogMaxSizeGb;
        }

        private void SaveSettings()
        {
            _config.ScreenshotEnabled = _chkScreenshotEnabled.Checked;
            _config.ScreenshotIntervalSec = (int)_numScreenshotInterval.Value;
            _config.ScreenshotQuality = _cmbScreenshotQuality.Text switch
            {
                "Baixa" => "Low", "Alta" => "High", _ => "Medium"
            };
            _config.BaseLogPath = _txtBaseLogPath.Text;

            _config.CameraEnabled = _chkCameraEnabled.Checked;
            _config.CameraIntervalSec = (int)_numCameraInterval.Value;
            _config.CameraQuality = _cmbCameraQuality.Text switch
            {
                "Baixa" => "Low", "Alta" => "High", _ => "Medium"
            };
            _config.CameraDeviceName = _cmbCameraDevice.SelectedItem?.ToString() ?? "";

            _config.KeyloggerEnabled = _chkKeyloggerEnabled.Checked;

            _config.LogRetentionDays = (int)_numRetentionDays.Value;
            _config.LogMaxSizeGb = (double)_numMaxSizeGb.Value;

            _db.InsertLog(null, "event", null, "Configurações de monitoramento atualizadas");

            KioskDialog.ShowMessage("Configurações salvas com sucesso!", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information, this);
        }

        private void TestCamera()
        {
            _cameraPreview.Image = null;
            _btnTestCamera.Enabled = false;
            _btnTestCamera.Text = "⏳ Testando...";

            Task.Run(() =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(_cmbCameraDevice.SelectedItem?.ToString()))
                    {
                        _config.CameraDeviceName = _cmbCameraDevice.SelectedItem.ToString();
                    }

                    var tempCapture = new CameraCapture(_config, _db);
                    var frame = tempCapture.TestCameraFrame();

                    _btnTestCamera.BeginInvoke((Action)(() =>
                    {
                        _btnTestCamera.Enabled = true;
                        _btnTestCamera.Text = "🔍 Testar Câmera";
                        if (frame != null)
                        {
                            _cameraPreview.Image = frame;
                        }
                        else
                        {
                            KioskDialog.ShowMessage(
                                "Não foi possível capturar da câmera.\nVerifique se a câmera está conectada e funcionando.",
                                _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning, this);
                        }
                    }));
                }
                catch (Exception ex)
                {
                    _btnTestCamera.BeginInvoke((Action)(() =>
                    {
                        _btnTestCamera.Enabled = true;
                        _btnTestCamera.Text = "🔍 Testar Câmera";
                        KioskDialog.ShowMessage(
                            $"Erro ao testar câmera:\n{ex.Message}",
                            _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error, this);
                    }));
                }
            });
        }

        private string GetStorageInfo()
        {
            var size = _db.GetLogStorageSize();
            var gb = size / (1024.0 * 1024 * 1024);
            var maxGb = _config.LogMaxSizeGb;
            var percent = maxGb > 0 ? (gb / maxGb * 100) : 0;
            return $"Armazenamento Total (Sessões/Logs): {gb:F2} GB de {maxGb:F0} GB ({percent:F0}%)";
        }
    }
}
