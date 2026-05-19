using ApacKiosk.Data;

namespace ApacKiosk.Forms;

public class AdminLoginDialog : Form
{
    private TextBox _passwordBox;
    private Label _titleLabel;
    private Label _alertLabel;
    private Button _confirmButton;
    private Button _cancelButton;

    public AdminLoginDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = "Acesso Administrativo";
        Size = new Size(420, 300);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        BackColor = Color.FromArgb(26, 26, 46);
        ForeColor = Color.FromArgb(224, 224, 224);
        MaximizeBox = false;
        MinimizeBox = false;

        _titleLabel = new Label
        {
            Text = "Senha do Administrador",
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            ForeColor = Color.FromArgb(167, 139, 250),
            Size = new Size(360, 35),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(30, 25)
        };

        _alertLabel = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 10),
            ForeColor = Color.FromArgb(251, 191, 36),
            Size = new Size(360, 40),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(30, 65),
            Visible = false
        };

        _passwordBox = new TextBox
        {
            Size = new Size(280, 35),
            Location = new Point(70, 115),
            Font = new Font("Segoe UI", 14),
            PasswordChar = '\u25CF',
            BackColor = Color.FromArgb(15, 15, 35),
            ForeColor = Color.FromArgb(224, 224, 224),
            BorderStyle = BorderStyle.FixedSingle,
            TextAlign = HorizontalAlignment.Center
        };

        _confirmButton = new Button
        {
            Text = "Entrar",
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            Size = new Size(130, 40),
            Location = new Point(70, 175),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(124, 58, 237),
            ForeColor = Color.White,
            Cursor = Cursors.Hand
        };
        _confirmButton.FlatAppearance.BorderSize = 0;
        _confirmButton.Click += ConfirmButton_Click;

        _cancelButton = new Button
        {
            Text = "Cancelar",
            Font = new Font("Segoe UI", 11),
            Size = new Size(130, 40),
            Location = new Point(220, 175),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(55, 55, 80),
            ForeColor = Color.FromArgb(224, 224, 224),
            Cursor = Cursors.Hand
        };
        _cancelButton.FlatAppearance.BorderSize = 0;
        _cancelButton.Click += (s, e) => DialogResult = DialogResult.Cancel;

        Controls.Add(_titleLabel);
        Controls.Add(_alertLabel);
        Controls.Add(_passwordBox);
        Controls.Add(_confirmButton);
        Controls.Add(_cancelButton);

        AcceptButton = _confirmButton;
        CancelButton = _cancelButton;
    }

    private void ConfirmButton_Click(object? sender, EventArgs e)
    {
        var password = _passwordBox.Text;

        if (string.IsNullOrEmpty(password))
        {
            _alertLabel.Text = "Digite a senha.";
            _alertLabel.Visible = true;
            return;
        }

        var admins = DatabaseHelper.Query<Models.Admin>("SELECT * FROM admins WHERE username = 'admin'");
        var admin = admins.FirstOrDefault();

        if (admin == null || !BCrypt.Net.BCrypt.Verify(password, admin.PasswordHash))
        {
            _alertLabel.Text = "Senha incorreta.";
            _alertLabel.ForeColor = Color.FromArgb(239, 68, 68);
            _alertLabel.Visible = true;
            return;
        }

        if (admin.MustChangePassword)
        {
            _alertLabel.Text = "ATENÇÃO: Altere a senha padrão no Painel Admin!\n(Senha atual: APAC@Admin2024)";
            _alertLabel.ForeColor = Color.FromArgb(251, 191, 36);
            _alertLabel.Visible = true;
            _alertLabel.Height = 45;
            Task.Delay(3000).ContinueWith(_ =>
            {
                Invoke(() => DialogResult = DialogResult.OK);
            });
            return;
        }

        DialogResult = DialogResult.OK;
    }
}
