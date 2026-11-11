using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using ApacKiosk.Services;
using ApacKiosk.Utils;
using ApacKiosk.Interop;

namespace ApacKiosk.Forms;

public class ActivationForm : Form
{
    private readonly LicenseManager _licenseManager;
    private readonly ConfigManager _config;

    private TextBox _txtSerial;
    private Button _btnVerify;
    private Label _lblMessage;
    private Label _lblLoading;

    public ActivationForm(LicenseManager licenseManager, ConfigManager config)
    {
        _licenseManager = licenseManager;
        _config = config;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        this.Text = "Ativação do APAC Kiosk Guardian";
        this.Size = new Size(540, 320); // Smaller form
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.None;
        this.TopMost = true;
        this.BackColor = Color.FromArgb(10, 10, 25);
        this.ForeColor = Color.White;

        var pnlBorder = new Panel { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
        this.Controls.Add(pnlBorder);

        // Botão Fechar
        var btnClose = new Button
        {
            Text = "X",
            Location = new Point(500, 10),
            Size = new Size(30, 30),
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.Gray,
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };
        btnClose.FlatAppearance.BorderSize = 0;
        btnClose.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
        pnlBorder.Controls.Add(btnClose);

        // Logo
        try
        {
            var logo = new PictureBox
            {
                Image = Image.FromFile(@"Resources\LogoApac.png"),
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(180, 60),
                Location = new Point(180, 20)
            };
            pnlBorder.Controls.Add(logo);
        }
        catch 
        {
            // Fallback se não achar a logo
            var lblTitle = new Label
            {
                Text = "APAC Kiosk",
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                AutoSize = false,
                Size = new Size(500, 35),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(10, 30)
            };
            lblTitle.MouseDown += Form_MouseDown;
            pnlBorder.Controls.Add(lblTitle);
        }

        var lblInst = new Label
        {
            Text = "Insira o serial da licença para ativar este quiosque.",
            Font = new Font("Segoe UI", 10),
            AutoSize = false,
            Size = new Size(500, 20),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(10, 100),
            ForeColor = Color.FromArgb(160, 160, 180)
        };
        pnlBorder.Controls.Add(lblInst);

        int y = 140;

        pnlBorder.Controls.Add(new Label
        {
            Text = "Serial:",
            Location = new Point(35, y),
            AutoSize = true,
            Font = new Font("Segoe UI", 10)
        });

        // Serial input
        _txtSerial = new TextBox
        {
            Location = new Point(35, y + 22),
            Width = 470,
            Height = 22,
            Font = new Font("Consolas", 9),
            Text = "",
            MaxLength = 400,
            BackColor = Color.FromArgb(20, 20, 40),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        _txtSerial.KeyDown += async (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await VerifySerialAsync();
            }
        };
        pnlBorder.Controls.Add(_txtSerial);

        // Botão Verificar
        _btnVerify = new Button
        {
            Text = "Verificar Chave",
            Location = new Point(180, y + 65),
            Size = new Size(180, 35),
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            BackColor = Color.FromArgb(40, 80, 200),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        _btnVerify.FlatAppearance.BorderSize = 0;
        _btnVerify.Click += async (s, e) => await VerifySerialAsync();
        pnlBorder.Controls.Add(_btnVerify);

        // Loading
        _lblLoading = new Label
        {
            Text = "Aguarde...",
            Font = new Font("Segoe UI", 10),
            ForeColor = Color.Yellow,
            AutoSize = false,
            Size = new Size(500, 20),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(10, y + 110),
            Visible = false
        };
        pnlBorder.Controls.Add(_lblLoading);

        // Error message
        _lblMessage = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = Color.Salmon,
            AutoSize = false,
            Size = new Size(500, 45),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(10, y + 110)
        };
        pnlBorder.Controls.Add(_lblMessage);

        pnlBorder.MouseDown += Form_MouseDown;
    }

    private void Form_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(this.Handle, 0xA1, 0x2, 0);
        }
    }

    private async Task VerifySerialAsync()
    {
        var serial = _txtSerial.Text.Trim();
        if (string.IsNullOrWhiteSpace(serial) || serial == "APAC-")
        {
            _lblMessage.Text = "Digite o serial antes de verificar.";
            return;
        }

        _btnVerify.Enabled = false;
        _txtSerial.Enabled = false;
        _lblLoading.Text = "Verificando chave no servidor...";
        _lblLoading.Visible = true;
        _lblMessage.Text = "";

        var result = await _licenseManager.ValidateSerialOnlineAsync(serial);

        _lblLoading.Visible = false;

        if (!result.Valid)
        {
            _btnVerify.Enabled = true;
            _txtSerial.Enabled = true;
            _lblMessage.Text = result.Reason;
            _lblMessage.ForeColor = Color.Salmon;
            return;
        }

        var hasSavedData = !string.IsNullOrWhiteSpace(_config.UnitState) &&
                           !string.IsNullOrWhiteSpace(_config.UnitCity) &&
                           !string.IsNullOrWhiteSpace(_config.UnitGender) &&
                           !string.IsNullOrWhiteSpace(_config.ContactEmail);

        if (hasSavedData)
        {
            // Ativa direto
            await ActivarComDadosSalvos(serial);
        }
        else
        {
            // Mostra o form de registro
            this.Hide();
            using var regForm = new RegistrationForm(_licenseManager, _config, serial, result.ClientName);
            var dialogResult = regForm.ShowDialog();
            
            if (dialogResult == DialogResult.OK)
            {
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            else
            {
                // Cancelou o registro, volta para a tela do serial
                this.Show();
                _btnVerify.Enabled = true;
                _txtSerial.Enabled = true;
            }
        }
    }

    private async Task ActivarComDadosSalvos(string serial)
    {
        _lblLoading.Text = "Ativando com dados salvos...";
        _lblLoading.Visible = true;
        _lblMessage.Text = "";

        var (success, error) = await _licenseManager.ActivateOnlineAsync(
            serial,
            _config.UnitState,
            _config.UnitCity,
            _config.UnitGender,
            _config.ContactEmail,
            _config.ContactName
        );

        _lblLoading.Visible = false;

        if (success)
        {
            MessageBox.Show("Sistema ativado com sucesso!", "Sucesso", MessageBoxButtons.OK, MessageBoxIcon.Information);
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
        else
        {
            _btnVerify.Enabled = true;
            _txtSerial.Enabled = true;
            _lblMessage.Text = error;
            _lblMessage.ForeColor = Color.Salmon;
        }
    }
}
