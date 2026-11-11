using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using ApacKiosk.Services;
using ApacKiosk.Utils;

namespace ApacKiosk.Forms;

public class RegistrationForm : Form
{
    private readonly LicenseManager _licenseManager;
    private readonly ConfigManager _config;
    private readonly string _serial;
    private readonly string _clientName;

    private ComboBox _cmbState;
    private ComboBox _cmbCity;
    private ComboBox _cmbGender;
    private TextBox _txtEmail;
    private TextBox _txtName;
    private Button _btnActivate;
    private Label _lblMessage;
    private Label _lblLoading;

    private readonly string[] _states = {
        "AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS",
        "MG", "PA", "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO"
    };

    public RegistrationForm(LicenseManager licenseManager, ConfigManager config, string serial, string clientName = "")
    {
        _licenseManager = licenseManager;
        _config = config;
        _serial = serial;
        _clientName = clientName;

        InitializeComponent();
        LoadStates();
        PreFillSavedData();
    }

    private void InitializeComponent()
    {
        this.Text = "Dados da Unidade";
        this.Size = new Size(540, 470);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.None;
        this.TopMost = true;
        this.BackColor = Color.FromArgb(10, 10, 25);
        this.ForeColor = Color.White;

        var pnlBorder = new Panel { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
        this.Controls.Add(pnlBorder);

        // Header and close
        var lblTitle = new Label
        {
            Text = "Completar Ativação",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            AutoSize = false,
            Size = new Size(500, 35),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(10, 20)
        };
        pnlBorder.Controls.Add(lblTitle);

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

        var lblInst = new Label
        {
            Text = "Preencha os dados da unidade para registrar o quiosque.",
            Font = new Font("Segoe UI", 10),
            AutoSize = false,
            Size = new Size(500, 20),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(10, 60),
            ForeColor = Color.FromArgb(160, 160, 180)
        };
        pnlBorder.Controls.Add(lblInst);

        int dy = 100;

        // State
        pnlBorder.Controls.Add(new Label { Text = "Estado (UF):", Location = new Point(35, dy), AutoSize = true, Font = new Font("Segoe UI", 10) });
        _cmbState = new ComboBox
        {
            Location = new Point(35, dy + 22),
            Width = 100,
            Font = new Font("Segoe UI", 12),
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(20, 20, 40),
            ForeColor = Color.White
        };
        _cmbState.SelectedIndexChanged += CmbState_SelectedIndexChanged;
        pnlBorder.Controls.Add(_cmbState);

        // City
        pnlBorder.Controls.Add(new Label { Text = "Cidade:", Location = new Point(155, dy), AutoSize = true, Font = new Font("Segoe UI", 10) });
        _cmbCity = new ComboBox
        {
            Location = new Point(155, dy + 22),
            Width = 290,
            Font = new Font("Segoe UI", 12),
            DropDownStyle = ComboBoxStyle.DropDown,
            BackColor = Color.FromArgb(20, 20, 40),
            ForeColor = Color.White
        };
        pnlBorder.Controls.Add(_cmbCity);

        dy += 65;

        // Gender
        pnlBorder.Controls.Add(new Label { Text = "Gênero da Unidade:", Location = new Point(35, dy), AutoSize = true, Font = new Font("Segoe UI", 10) });
        _cmbGender = new ComboBox
        {
            Location = new Point(35, dy + 22),
            Width = 410,
            Font = new Font("Segoe UI", 12),
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(20, 20, 40),
            ForeColor = Color.White
        };
        _cmbGender.Items.AddRange(new[] { "Masculina (M)", "Feminina (F)" });
        pnlBorder.Controls.Add(_cmbGender);

        dy += 65;

        // Email
        pnlBorder.Controls.Add(new Label { Text = "E-mail de Contato:", Location = new Point(35, dy), AutoSize = true, Font = new Font("Segoe UI", 10) });
        _txtEmail = new TextBox
        {
            Location = new Point(35, dy + 22),
            Width = 410,
            Font = new Font("Segoe UI", 12),
            BackColor = Color.FromArgb(20, 20, 40),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        pnlBorder.Controls.Add(_txtEmail);

        dy += 65;

        // Nome
        pnlBorder.Controls.Add(new Label { Text = "Nome do Responsável (Opcional):", Location = new Point(35, dy), AutoSize = true, Font = new Font("Segoe UI", 10) });
        _txtName = new TextBox
        {
            Location = new Point(35, dy + 22),
            Width = 410,
            Font = new Font("Segoe UI", 12),
            BackColor = Color.FromArgb(20, 20, 40),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        pnlBorder.Controls.Add(_txtName);

        dy += 60;

        _btnActivate = new Button
        {
            Text = "Ativar Sistema",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            BackColor = Color.FromArgb(40, 120, 255),
            FlatStyle = FlatStyle.Flat,
            Size = new Size(180, 35),
            Location = new Point(175, dy),
            Cursor = Cursors.Hand
        };
        _btnActivate.FlatAppearance.BorderSize = 0;
        _btnActivate.Click += async (s, e) => await BtnActivate_Click();
        pnlBorder.Controls.Add(_btnActivate);

        // Mensagem de erro/status
        _lblMessage = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = Color.Salmon,
            AutoSize = false,
            Size = new Size(490, 50),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(10, dy + 45)
        };
        pnlBorder.Controls.Add(_lblMessage);

        _lblLoading = new Label
        {
            Text = "Aguarde...",
            Font = new Font("Segoe UI", 10),
            ForeColor = Color.Yellow,
            AutoSize = false,
            Size = new Size(490, 20),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(10, dy + 45),
            Visible = false
        };
        pnlBorder.Controls.Add(_lblLoading);
    }

    private void LoadStates()
    {
        _cmbState.Items.AddRange(_states);
        _cmbState.SelectedIndex = -1;
    }

    private void PreFillSavedData()
    {
        var savedState = _config.UnitState;
        var savedCity = _config.UnitCity;
        var savedGender = _config.UnitGender;
        var savedEmail = _config.ContactEmail;
        var savedName = _config.ContactName;
        
        bool fromSerial = false;
        if (!string.IsNullOrWhiteSpace(_clientName))
        {
            // APAC-Itabirito-MG-M-01
            var parts = _clientName.Split('-');
            if (parts.Length >= 5 && parts[0] == "APAC")
            {
                savedGender = parts[parts.Length - 2];
                savedState = parts[parts.Length - 3];
                savedCity = string.Join("-", parts.Skip(1).Take(parts.Length - 4));
                fromSerial = true;
            }
        }

        if (!string.IsNullOrWhiteSpace(savedState))
        {
            var idx = Array.IndexOf(_states, savedState);
            if (idx >= 0) _cmbState.SelectedIndex = idx;
        }

        if (!string.IsNullOrWhiteSpace(savedCity))
            _cmbCity.Text = savedCity;

        if (!string.IsNullOrWhiteSpace(savedGender))
        {
            for (int i = 0; i < _cmbGender.Items.Count; i++)
            {
                var item = _cmbGender.Items[i]?.ToString() ?? "";
                if ((savedGender == "M" && item.Contains("Masculina")) ||
                    (savedGender == "F" && item.Contains("Feminina")))
                {
                    _cmbGender.SelectedIndex = i;
                    break;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(savedEmail))
            _txtEmail.Text = savedEmail;

        if (!string.IsNullOrWhiteSpace(savedName))
            _txtName.Text = savedName;

        if (fromSerial)
        {
            _cmbState.Enabled = false;
            _cmbCity.Enabled = false;
            _cmbGender.Enabled = false;
        }
    }

    private async void CmbState_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_cmbState.SelectedItem == null) return;
        string uf = _cmbState.SelectedItem.ToString()!;

        var savedCity = _cmbCity.Text;
        _cmbCity.Items.Clear();
        _cmbCity.Text = "Carregando...";
        _cmbCity.Enabled = false;

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.GetStringAsync($"https://servicodados.ibge.gov.br/api/v1/localidades/estados/{uf}/municipios");
            var cities = JsonSerializer.Deserialize<List<IbgeCity>>(response);

            _cmbCity.Items.Clear();
            if (cities != null)
            {
                foreach (var c in cities) _cmbCity.Items.Add(c.nome);
            }
            _cmbCity.Text = savedCity;
            _cmbCity.Enabled = true;
        }
        catch
        {
            _cmbCity.Items.Clear();
            _cmbCity.Text = savedCity;
            _cmbCity.Enabled = true;
        }
    }

    private async Task BtnActivate_Click()
    {
        if (string.IsNullOrWhiteSpace(_cmbState.Text) ||
            string.IsNullOrWhiteSpace(_cmbCity.Text) ||
            string.IsNullOrWhiteSpace(_txtEmail.Text) ||
            _cmbGender.SelectedItem == null)
        {
            _lblMessage.Text = "Preencha todos os campos obrigatórios.";
            _lblMessage.ForeColor = Color.Salmon;
            return;
        }

        if (!_txtEmail.Text.Contains("@"))
        {
            _lblMessage.Text = "E-mail inválido.";
            _lblMessage.ForeColor = Color.Salmon;
            return;
        }

        _btnActivate.Enabled = false;
        _lblLoading.Text = "Conectando ao servidor...";
        _lblLoading.Visible = true;
        _lblMessage.Text = "";

        string genderCode = _cmbGender.SelectedItem.ToString()!.Contains("Masculina") ? "M" : "F";

        var (success, error) = await _licenseManager.ActivateOnlineAsync(
            _serial,
            _cmbState.Text,
            _cmbCity.Text,
            genderCode,
            _txtEmail.Text.Trim(),
            _txtName.Text.Trim()
        );

        _lblLoading.Visible = false;
        _btnActivate.Enabled = true;

        if (success)
        {
            MessageBox.Show("Sistema ativado e registrado com sucesso!", "Sucesso", MessageBoxButtons.OK, MessageBoxIcon.Information);
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
        else
        {
            _lblMessage.Text = error;
            _lblMessage.ForeColor = Color.Salmon;
        }
    }

    private class IbgeCity { public string nome { get; set; } = ""; }

    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x0084;
        const int HTCLIENT = 1;
        const int HTCAPTION = 2;
        
        base.WndProc(ref m);
        
        if (m.Msg == WM_NCHITTEST && (int)m.Result == HTCLIENT)
        {
            m.Result = (IntPtr)HTCAPTION;
        }
    }
}
