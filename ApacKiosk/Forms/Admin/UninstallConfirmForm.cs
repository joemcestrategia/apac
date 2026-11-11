using System;
using System.Drawing;
using System.Windows.Forms;
using ApacKiosk.Database;

namespace ApacKiosk.Forms.Admin;

public class UninstallConfirmForm : Form
{
    private readonly DatabaseManager _db;
    public bool PurgeData { get; private set; }

    private TextBox _txtPassword = null!;
    private CheckBox _chkPurge = null!;
    private Label _lblError = null!;
    private Button _btnUninstall = null!;
    private Button _btnCancel = null!;

    public UninstallConfirmForm(DatabaseManager db)
    {
        _db = db;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = "APAC Kiosk Guardian — Desinstalação";
        Size = new Size(500, 360);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(15, 15, 30);
        ForeColor = Color.White;
        TopMost = true;

        var pnlHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 70,
            BackColor = Color.FromArgb(22, 22, 45)
        };
        pnlHeader.Paint += (s, ev) =>
        {
            using var pen = new Pen(Color.FromArgb(45, 45, 80), 1);
            ev.Graphics.DrawLine(pen, 0, pnlHeader.Height - 1, pnlHeader.Width, pnlHeader.Height - 1);
        };

        var lblIcon = new Label
        {
            Text = "🛡️",
            Font = new Font("Segoe UI", 24),
            Location = new Point(20, 12),
            Size = new Size(45, 45),
            BackColor = Color.Transparent
        };
        pnlHeader.Controls.Add(lblIcon);

        var lblTitle = new Label
        {
            Text = "Desinstalação do APAC Kiosk Guardian",
            Font = new Font("Segoe UI", 12.5f, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(72, 14),
            AutoSize = true,
            BackColor = Color.Transparent
        };
        pnlHeader.Controls.Add(lblTitle);

        var lblSubtitle = new Label
        {
            Text = "Confirmação de segurança de administrador necessária",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(160, 160, 190),
            Location = new Point(74, 38),
            AutoSize = true,
            BackColor = Color.Transparent
        };
        pnlHeader.Controls.Add(lblSubtitle);

        Controls.Add(pnlHeader);

        var lblPrompt = new Label
        {
            Text = "Para desinstalar o quiosque deste computador, digite a senha do Administrador cadastrada:",
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(210, 210, 230),
            Location = new Point(30, 85),
            Size = new Size(440, 40),
            BackColor = Color.Transparent
        };
        Controls.Add(lblPrompt);

        _txtPassword = new TextBox
        {
            Location = new Point(30, 130),
            Size = new Size(430, 32),
            Font = new Font("Segoe UI", 12),
            PasswordChar = '●',
            BackColor = Color.FromArgb(26, 26, 46),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        _txtPassword.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                ValidateAndProceed();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };
        Controls.Add(_txtPassword);

        _chkPurge = new CheckBox
        {
            Text = "Excluir também banco de dados local e registros (C:\\ProgramData\\ApacKiosk)",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(170, 170, 200),
            Location = new Point(30, 175),
            Size = new Size(440, 40),
            Checked = false
        };
        Controls.Add(_chkPurge);

        _lblError = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = Color.FromArgb(239, 68, 68),
            Location = new Point(30, 218),
            Size = new Size(430, 22),
            BackColor = Color.Transparent
        };
        Controls.Add(_lblError);

        _btnUninstall = new Button
        {
            Text = "DESINSTALAR",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Location = new Point(200, 255),
            Size = new Size(140, 38),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(220, 38, 38),
            ForeColor = Color.White,
            Cursor = Cursors.Hand
        };
        _btnUninstall.FlatAppearance.BorderSize = 0;
        _btnUninstall.Click += (s, e) => ValidateAndProceed();
        Controls.Add(_btnUninstall);

        _btnCancel = new Button
        {
            Text = "Cancelar",
            Font = new Font("Segoe UI", 9.5f),
            Location = new Point(350, 255),
            Size = new Size(110, 38),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(40, 40, 65),
            ForeColor = Color.FromArgb(200, 200, 220),
            Cursor = Cursors.Hand
        };
        _btnCancel.FlatAppearance.BorderSize = 0;
        _btnCancel.Click += (s, e) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        Controls.Add(_btnCancel);

        Shown += (s, e) => _txtPassword.Focus();
    }

    private void ValidateAndProceed()
    {
        var pwd = _txtPassword.Text;
        if (string.IsNullOrEmpty(pwd))
        {
            _lblError.Text = "Digite a senha de administrador.";
            _txtPassword.Focus();
            return;
        }

        try
        {
            using var conn = _db.GetConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT password_hash FROM admins";
            using var reader = cmd.ExecuteReader();

            bool valid = false;
            while (reader.Read())
            {
                var hash = reader.GetString(0);
                if (BCrypt.Net.BCrypt.Verify(pwd, hash))
                {
                    valid = true;
                    break;
                }
            }

            if (!valid)
            {
                _lblError.Text = "Senha incorreta. Desinstalação não autorizada.";
                _txtPassword.SelectAll();
                _txtPassword.Focus();
                return;
            }

            PurgeData = _chkPurge.Checked;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            _lblError.Text = $"Erro ao validar credencial: {ex.Message}";
        }
    }
}
