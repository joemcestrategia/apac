using ApacKiosk.Data;
using ApacKiosk.Models;

namespace ApacKiosk.Forms;

public class LoginForm : Form
{
    private Label _titleLabel;
    private Label _welcomeLabel;
    private Label _statusLabel;
    private PictureBox _userPhoto;
    private Label _userNameLabel;
    private TextBox _pinDisplay;
    private Button _adminButton;
    private Panel _keypadPanel;
    private string _pinBuffer = "";
    private List<User> _activeUsers = new();

    private const int PinMaxLength = 8;
    private const int PinMinLength = 4;

    public LoginForm()
    {
        InitializeComponent();
        LoadActiveUsers();
    }

    private void InitializeComponent()
    {
        Text = "APAC — Acesso Digital";
        Size = new Size(900, 680);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Color.FromArgb(15, 15, 35);
        ForeColor = Color.FromArgb(224, 224, 224);
        KeyPreview = true;
        TopMost = true;

        _titleLabel = new Label
        {
            Text = "APAC",
            Font = new Font("Segoe UI", 42, FontStyle.Bold),
            ForeColor = Color.FromArgb(124, 58, 237),
            Size = new Size(400, 70),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(250, 30)
        };

        _welcomeLabel = new Label
        {
            Text = DatabaseHelper.GetConfig("welcome_message", "Bem-vindo ao sistema APAC"),
            Font = new Font("Segoe UI", 14),
            ForeColor = Color.FromArgb(167, 139, 250),
            Size = new Size(600, 40),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(150, 105)
        };

        _statusLabel = new Label
        {
            Text = "Digite seu PIN para acessar",
            Font = new Font("Segoe UI", 11),
            ForeColor = Color.FromArgb(156, 163, 175),
            Size = new Size(400, 30),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(250, 150)
        };

        _userPhoto = new PictureBox
        {
            Size = new Size(120, 120),
            Location = new Point(390, 190),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(26, 26, 46),
            Image = null
        };

        _userNameLabel = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            ForeColor = Color.FromArgb(167, 139, 250),
            Size = new Size(300, 30),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(300, 315),
            Visible = false
        };

        _pinDisplay = new TextBox
        {
            Size = new Size(260, 40),
            Location = new Point(320, 360),
            Font = new Font("Segoe UI", 22),
            TextAlign = HorizontalAlignment.Center,
            ReadOnly = true,
            BackColor = Color.FromArgb(26, 26, 46),
            ForeColor = Color.FromArgb(224, 224, 224),
            BorderStyle = BorderStyle.FixedSingle,
            PasswordChar = '\u25CF'
        };

        _adminButton = new Button
        {
            Text = "\u2699",
            Font = new Font("Segoe UI", 14),
            Size = new Size(44, 44),
            Location = new Point(8, Height - 60),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(15, 15, 35),
            ForeColor = Color.FromArgb(75, 85, 99),
            Cursor = Cursors.Hand
        };
        _adminButton.FlatAppearance.BorderSize = 0;
        _adminButton.Click += AdminButton_Click;

        BuildKeypad();

        Controls.Add(_titleLabel);
        Controls.Add(_welcomeLabel);
        Controls.Add(_statusLabel);
        Controls.Add(_userPhoto);
        Controls.Add(_userNameLabel);
        Controls.Add(_pinDisplay);
        Controls.Add(_keypadPanel);
        Controls.Add(_adminButton);

        KeyPress += LoginForm_KeyPress;
    }

    private void BuildKeypad()
    {
        _keypadPanel = new Panel
        {
            Size = new Size(340, 260),
            Location = new Point(280, 410),
            BackColor = Color.FromArgb(15, 15, 35)
        };

        int[] keys = { 1, 2, 3, 4, 5, 6, 7, 8, 9, -1, 0, -2 };
        int col = 0, row = 0;

        for (int i = 0; i < keys.Length; i++)
        {
            if (i % 3 == 0 && i > 0) { col = 0; row++; }

            var btn = new Button
            {
                Size = new Size(95, 55),
                Location = new Point(col * 110 + 5, row * 65 + 5),
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(30, 30, 55),
                ForeColor = Color.FromArgb(224, 224, 224),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(42, 42, 74);
            btn.FlatAppearance.BorderSize = 1;

            int key = keys[i];
            if (key == -1)
            {
                btn.Text = "<<";
                btn.Tag = "back";
                btn.Click += KeypadBack_Click;
            }
            else if (key == -2)
            {
                btn.Text = "OK";
                btn.BackColor = Color.FromArgb(124, 58, 237);
                btn.Tag = "confirm";
                btn.Click += KeypadConfirm_Click;
            }
            else
            {
                btn.Text = key.ToString();
                btn.Tag = key;
                btn.Click += KeypadDigit_Click;
            }

            _keypadPanel.Controls.Add(btn);
            col++;
        }
    }

    private void LoadActiveUsers()
    {
        _activeUsers = DatabaseHelper.Query<User>("SELECT * FROM users WHERE is_active = 1");
    }

    private void KeypadDigit_Click(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.Tag is int digit)
        {
            if (_pinBuffer.Length < PinMaxLength)
            {
                _pinBuffer += digit.ToString();
                _pinDisplay.Text = new string('\u25CF', _pinBuffer.Length);
                _statusLabel.Text = $"{_pinBuffer.Length} dígito(s) digitado(s)";
            }
        }
    }

    private void KeypadBack_Click(object? sender, EventArgs e)
    {
        if (_pinBuffer.Length > 0)
        {
            _pinBuffer = _pinBuffer[..^1];
            _pinDisplay.Text = _pinBuffer.Length > 0 ? new string('\u25CF', _pinBuffer.Length) : "";
            _statusLabel.Text = _pinBuffer.Length > 0
                ? $"{_pinBuffer.Length} dígito(s) digitado(s)"
                : "Digite seu PIN para acessar";
        }
    }

    private void KeypadConfirm_Click(object? sender, EventArgs e)
    {
        TryLogin();
    }

    private void LoginForm_KeyPress(object? sender, KeyPressEventArgs e)
    {
        if (char.IsDigit(e.KeyChar) && _pinBuffer.Length < PinMaxLength)
        {
            _pinBuffer += e.KeyChar;
            _pinDisplay.Text = new string('\u25CF', _pinBuffer.Length);
            _statusLabel.Text = $"{_pinBuffer.Length} dígito(s) digitado(s)";
        }
        else if (e.KeyChar == '\b' && _pinBuffer.Length > 0)
        {
            _pinBuffer = _pinBuffer[..^1];
            _pinDisplay.Text = _pinBuffer.Length > 0 ? new string('\u25CF', _pinBuffer.Length) : "";
        }
        else if (e.KeyChar == '\r')
        {
            TryLogin();
        }
    }

    private void TryLogin()
    {
        if (_pinBuffer.Length < PinMinLength)
        {
            _statusLabel.Text = "PIN muito curto (mínimo 4 dígitos)";
            _statusLabel.ForeColor = Color.FromArgb(239, 68, 68);
            return;
        }

        _statusLabel.ForeColor = Color.FromArgb(156, 163, 175);

        foreach (var user in _activeUsers)
        {
            if (BCrypt.Net.BCrypt.Verify(_pinBuffer, user.PinHash))
            {
                if (!CheckTimeAccess(user))
                    return;

                _statusLabel.Text = $"Bem-vindo, {user.FullName}!";
                StartKiosk(user);
                return;
            }
        }

        _statusLabel.Text = "PIN inválido. Tente novamente.";
        _statusLabel.ForeColor = Color.FromArgb(239, 68, 68);
        _pinBuffer = "";
        _pinDisplay.Text = "";
        _userNameLabel.Visible = false;
        _userPhoto.Image = null;
    }

    private bool CheckTimeAccess(User user)
    {
        if (user.ProfileId == null) return true;

        var now = DateTime.Now;
        var slots = DatabaseHelper.Query<ProfileTimeSlot>(
            "SELECT * FROM profile_time_slots WHERE profile_id = @p", new { p = user.ProfileId });

        if (slots.Count == 0) return true;

        var todaySlots = slots.Where(s => s.DayOfWeek == now.DayOfWeek).ToList();
        if (todaySlots.Count == 0)
        {
            ShowNoAccessMessage(user, "Não há horário disponível hoje.");
            return false;
        }

        var nowTime = now.TimeOfDay;
        var matchingSlot = todaySlots.FirstOrDefault(s => nowTime >= s.StartTime && nowTime <= s.EndTime);

        if (matchingSlot == null)
        {
            var next = todaySlots
                .Where(s => s.StartTime > nowTime)
                .OrderBy(s => s.StartTime)
                .FirstOrDefault();

            if (next != null)
            {
                var nextTime = DateTime.Today.Add(next.StartTime);
                ShowNoAccessMessage(user, $"Fora do horário permitido.\nPróximo horário disponível: {nextTime:HH:mm}");
            }
            else
            {
                var nextDay = now.Date.AddDays(1);
                while (nextDay.DayOfWeek != now.DayOfWeek)
                {
                    var daySlots = slots.Where(s => s.DayOfWeek == nextDay.DayOfWeek).OrderBy(s => s.StartTime).FirstOrDefault();
                    if (daySlots != null)
                    {
                        var nextTime = nextDay.Date.Add(daySlots.StartTime);
                        ShowNoAccessMessage(user, $"Fora do horário permitido.\nPróximo horário disponível: {nextTime:dd/MM HH:mm}");
                        return false;
                    }
                    nextDay = nextDay.AddDays(1);
                }
                ShowNoAccessMessage(user, "Fora do horário permitido.");
            }
            return false;
        }

        return true;
    }

    private void ShowNoAccessMessage(User user, string message)
    {
        _statusLabel.Text = message.Replace("\n", ". ");
        _statusLabel.ForeColor = Color.FromArgb(251, 191, 36);
    }

    private async void StartKiosk(User user)
    {
        Visible = false;
        var kiosk = new KioskBrowserForm(user);
        kiosk.FormClosed += (s, e) =>
        {
            _pinBuffer = "";
            _pinDisplay.Text = "";
            _userNameLabel.Visible = false;
            _userPhoto.Image = null;
            LoadActiveUsers();
            Visible = true;
            _statusLabel.Text = "Digite seu PIN para acessar";
            _statusLabel.ForeColor = Color.FromArgb(156, 163, 175);
        };
        kiosk.Show();
    }

    private void AdminButton_Click(object? sender, EventArgs e)
    {
        var adminLogin = new AdminLoginDialog();
        if (adminLogin.ShowDialog(this) == DialogResult.OK)
        {
            var adminPanel = new AdminPanelForm();
            adminPanel.FormClosed += (s, ev) => LoadActiveUsers();
            adminPanel.Show(this);
        }
    }
}
