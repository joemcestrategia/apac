using System;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using BCrypt.Net;
using Microsoft.Data.Sqlite;
using ApacKiosk.Database;
using ApacKiosk.Services;
using ApacKiosk.Utils;

namespace ApacKiosk.Forms
{
    public class LoginForm : Form
    {
        private readonly DatabaseManager _db;
        private readonly ConfigManager _config;

        private PictureBox _logoBox;
        private Label _appNameLabel;
        private Label _welcomeLabel;
        private Panel _cardPanel;
        private Label _cardTitle;
        private Label _cardSubtitle;

        private ToggleButton _userModeBtn;
        private ToggleButton _adminModeBtn;

        private Panel _userPanel;
        private TextBox _usernameBox;
        private TextBox _pinBox;
        private ModernButton _userLoginBtn;
        private PictureBox _photoBox;
        private Label _timeInfoLabel;

        private Panel _adminPanel;
        private TextBox _adminUsernameBox;
        private TextBox _adminPasswordBox;
        private ModernButton _adminLoginBtn;
        private Panel _tabPanel;
        private Label _closeLabel;
        private Button _btnSystemUpdate;
        private Label _mottoLabel;
        private Panel _photoBanner;
        private Panel _bgPanel;

        private Panel _decorPanel;
        private Panel _formContainerPanel;

        private bool _isAdminMode;
        private volatile bool _isShuttingDown;
        public Models.User LoggedInUser { get; private set; }
        public bool IsAdmin { get; private set; }

        public LoginForm(DatabaseManager db, ConfigManager config)
        {
            _db = db;
            _config = config;
            InitializeComponent();
            SwitchToUserMode();
        }

        private void InitializeComponent()
        {
            Text = _config.DisplayName;
            Size = new Size(1024, 768);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.FromArgb(10, 10, 25);
            ForeColor = Color.White;
            WindowState = FormWindowState.Maximized;
            TopMost = true;
            DoubleBuffered = true;

            _bgPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(10, 10, 25) };

            // Painel Esquerdo Decorativo (55% da largura da tela)
            _decorPanel = new Panel
            {
                Size = new Size(560, 768),
                BackColor = Color.FromArgb(10, 10, 30)
            };
            
            // Pintura de fundo Aurora Neon Glow de altíssima qualidade
            _decorPanel.Paint += (s, ev) =>
            {
                var g = ev.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var rect = _decorPanel.ClientRectangle;

                // Fundo gradiente linear principal
                using (var bgBrush = new LinearGradientBrush(
                    rect,
                    Color.FromArgb(8, 8, 22),
                    Color.FromArgb(18, 12, 36),
                    LinearGradientMode.Vertical))
                {
                    g.FillRectangle(bgBrush, rect);
                }

                // Efeito Aurora Glow 1 (Luz roxa difusa no canto superior esquerdo)
                using (var path = new GraphicsPath())
                {
                    var radius = 280;
                    var cx = 50;
                    var cy = 100;
                    path.AddEllipse(cx - radius, cy - radius, radius * 2, radius * 2);
                    using (var pgb = new PathGradientBrush(path))
                    {
                        pgb.CenterColor = Color.FromArgb(45, 124, 58, 237);
                        pgb.SurroundColors = new Color[] { Color.Transparent };
                        g.FillEllipse(pgb, cx - radius, cy - radius, radius * 2, radius * 2);
                    }
                }

                // Efeito Aurora Glow 2 (Luz azul neon difusa na parte inferior direita)
                using (var path = new GraphicsPath())
                {
                    var radius = 320;
                    var cx = rect.Width - 100;
                    var cy = rect.Height - 150;
                    path.AddEllipse(cx - radius, cy - radius, radius * 2, radius * 2);
                    using (var pgb = new PathGradientBrush(path))
                    {
                        pgb.CenterColor = Color.FromArgb(35, 59, 130, 246);
                        pgb.SurroundColors = new Color[] { Color.Transparent };
                        g.FillEllipse(pgb, cx - radius, cy - radius, radius * 2, radius * 2);
                    }
                }

                // Desenha linha divisória sutil na borda direita do painel decorativo
                using (var borderPen = new Pen(Color.FromArgb(20, 255, 255, 255), 1))
                {
                    g.DrawLine(borderPen, rect.Width - 1, 0, rect.Width - 1, rect.Height);
                }
            };

            // Painel Direito Formulário (45% da largura da tela)
            _formContainerPanel = new Panel
            {
                BackColor = Color.FromArgb(12, 12, 28)
            };
            
            _formContainerPanel.Paint += (s, ev) =>
            {
                var g = ev.Graphics;
                var rect = _formContainerPanel.ClientRectangle;
                using (var bgBrush = new LinearGradientBrush(
                    rect,
                    Color.FromArgb(12, 12, 28),
                    Color.FromArgb(8, 8, 20),
                    LinearGradientMode.Vertical))
                {
                    g.FillRectangle(bgBrush, rect);
                }
            };

            _logoBox = new PictureBox
            {
                Size = new Size(120, 120),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
            LoadLogo();

            _appNameLabel = new Label
            {
                Text = _config.DisplayName,
                Font = new Font("Segoe UI Semibold", 28, FontStyle.Bold),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false,
                Size = new Size(500, 50)
            };

            _welcomeLabel = new Label
            {
                Text = _config.WelcomeMessage,
                Font = new Font("Segoe UI", 12),
                ForeColor = Color.FromArgb(160, 170, 195),
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false,
                Size = new Size(500, 25)
            };

            if (!Program.License.IsActivated())
            {
                _welcomeLabel.Text = "SISTEMA DESATIVADO - LICENÇA INVÁLIDA OU REVOGADA";
                _welcomeLabel.ForeColor = Color.FromArgb(239, 68, 68); // Red
                _welcomeLabel.Font = new Font("Segoe UI", 12, FontStyle.Bold);
            }

            _mottoLabel = new Label
            {
                Text = "\"Sem disciplina não há recuperação\"",
                Font = new Font("Segoe UI Semibold", 13, FontStyle.Italic),
                ForeColor = Color.FromArgb(212, 175, 55),
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false,
                Size = new Size(500, 25)
            };

            // Card de login com efeito Glassmorphic
            _cardPanel = new Panel
            {
                Size = new Size(420, 430),
                BackColor = Color.FromArgb(20, 20, 42)
            };
            
            _cardPanel.Paint += (s, ev) =>
            {
                ev.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = GetRoundRect(0, 0, _cardPanel.Width, _cardPanel.Height, 18))
                {
                    using (var brush = new SolidBrush(Color.FromArgb(22, 22, 45)))
                    {
                        ev.Graphics.FillPath(brush, path);
                    }
                    // Borda brilhante muito suave
                    using (var pen = new Pen(Color.FromArgb(35, 255, 255, 255), 1))
                    {
                        ev.Graphics.DrawPath(pen, path);
                    }
                }
            };
            
            _cardPanel.SizeChanged += (s, ev) =>
            {
                using (var path = GetRoundRect(0, 0, _cardPanel.Width, _cardPanel.Height, 18))
                {
                    _cardPanel.Region = new Region(path);
                }
            };

            _cardTitle = new Label
            {
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(30, 25),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            
            _cardSubtitle = new Label
            {
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(145, 145, 175),
                Location = new Point(30, 58),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            
            _cardPanel.Controls.Add(_cardTitle);
            _cardPanel.Controls.Add(_cardSubtitle);

            // Tab Panel para alternar entre Usuário e Administrador
            _tabPanel = new Panel
            {
                Location = new Point(40, 95),
                Size = new Size(340, 44),
                BackColor = Color.FromArgb(15, 15, 30)
            };
            
            _tabPanel.Paint += (s, ev) =>
            {
                ev.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = GetRoundRect(0, 0, _tabPanel.Width, _tabPanel.Height, 10))
                {
                    using (var pen = new Pen(Color.FromArgb(30, 255, 255, 255), 1))
                    {
                        ev.Graphics.DrawPath(pen, path);
                    }
                }
            };
            
            _tabPanel.SizeChanged += (s, ev) =>
            {
                using (var path = GetRoundRect(0, 0, _tabPanel.Width, _tabPanel.Height, 10))
                {
                    _tabPanel.Region = new Region(path);
                }
            };

            _userModeBtn = new ToggleButton
            {
                Text = "Usuário",
                Size = new Size(168, 40),
                Location = new Point(1, 2),
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Selected = true
            };
            _userModeBtn.OnToggle += () => SwitchToUserMode();

            _adminModeBtn = new ToggleButton
            {
                Text = "Administrador",
                Size = new Size(168, 40),
                Location = new Point(171, 2),
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Selected = false
            };
            _adminModeBtn.OnToggle += () => SwitchToAdminMode();

            _tabPanel.Controls.Add(_userModeBtn);
            _tabPanel.Controls.Add(_adminModeBtn);
            _cardPanel.Controls.Add(_tabPanel);

            BuildUserPanel();
            BuildAdminPanel();
            _cardPanel.Controls.Add(_userPanel);
            _cardPanel.Controls.Add(_adminPanel);

            // Banner na parte inferior esquerda com fotos dos recuperandos
            _photoBanner = new Panel
            {
                Size = new Size(600, 110),
                BackColor = Color.Transparent
            };
            
            _photoBanner.Paint += (s, ev) =>
            {
                var g = ev.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var rect = _photoBanner.ClientRectangle;

                // Desenha borda decorativa com linhas pontilhadas discretas
                using (var borderPen = new Pen(Color.FromArgb(40, 124, 58, 237), 1))
                {
                    borderPen.DashStyle = DashStyle.Dash;
                    g.DrawRectangle(borderPen, 0, 0, rect.Width - 1, rect.Height - 1);
                }
            };
            
            LoadBannerPhotos();

            _closeLabel = new Label
            {
                Text = "✕",
                Font = new Font("Segoe UI Semibold", 18),
                ForeColor = Color.FromArgb(140, 140, 160),
                AutoSize = true,
                Cursor = Cursors.Hand,
                BackColor = Color.Transparent
            };
            
            _closeLabel.Click += (s, e) => ShowShutdownPrompt();
            _closeLabel.MouseEnter += (s, e) => _closeLabel.ForeColor = Color.FromArgb(239, 68, 68);
            _closeLabel.MouseLeave += (s, e) => _closeLabel.ForeColor = Color.FromArgb(140, 140, 160);

            _btnSystemUpdate = new Button
            {
                Text = "🔄 Atualizar Sistema",
                Size = new Size(160, 32),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(20, 20, 42),
                ForeColor = Color.FromArgb(160, 160, 200),
                Cursor = Cursors.Hand
            };
            _btnSystemUpdate.FlatAppearance.BorderColor = Color.FromArgb(40, 40, 75);
            _btnSystemUpdate.Click += (s, e) => PromptAdminAndUpdate();
            _btnSystemUpdate.MouseEnter += (s, e) => { _btnSystemUpdate.ForeColor = Color.FromArgb(52, 211, 153); _btnSystemUpdate.FlatAppearance.BorderColor = Color.FromArgb(52, 211, 153); };
            _btnSystemUpdate.MouseLeave += (s, e) => { _btnSystemUpdate.ForeColor = Color.FromArgb(160, 160, 200); _btnSystemUpdate.FlatAppearance.BorderColor = Color.FromArgb(40, 40, 75); };

            // Adiciona controles nos seus painéis respectivos
            _decorPanel.Controls.AddRange(new Control[] { _logoBox, _appNameLabel, _welcomeLabel, _mottoLabel, _photoBanner });
            _formContainerPanel.Controls.AddRange(new Control[] { _cardPanel, _closeLabel, _btnSystemUpdate });
            
            _bgPanel.Controls.Add(_formContainerPanel);
            _bgPanel.Controls.Add(_decorPanel);
            _decorPanel.BringToFront();
            _formContainerPanel.SendToBack();
            Controls.Add(_bgPanel);

            Resize += (s, e) => CenterLoginLayout();
            Shown += (s, e) => CenterLoginLayout();
        }

        private void BuildUserPanel()
        {
            _userPanel = new Panel { Size = new Size(420, 280), Location = new Point(0, 150), BackColor = Color.Transparent };

            _usernameBox = new TextBox
            {
                Size = new Size(275, 36),
                Font = new Font("Segoe UI", 12),
                BackColor = Color.FromArgb(26, 26, 50),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            _usernameBox.Leave += UsernameBox_Leave;

            var userComboWrapper = CreateModernInputWrapper(_usernameBox, "Matrícula do Usuário", 30, 10, 275, 56);

            _pinBox = new TextBox
            {
                Size = new Size(200, 36),
                Font = new Font("Segoe UI", 16),
                PasswordChar = '●',
                TextAlign = HorizontalAlignment.Center,
                MaxLength = 8,
                BackColor = Color.FromArgb(26, 26, 50),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            _pinBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) DoUserLogin(); };

            var pinBoxWrapper = CreateModernInputWrapper(_pinBox, "PIN de Acesso", 30, 85, 200, 56);

            _userLoginBtn = new ModernButton
            {
                Text = "ENTRAR",
                Location = new Point(245, 97),
                Size = new Size(145, 44),
                Font = new Font("Segoe UI", 12, FontStyle.Bold)
            };
            _userLoginBtn.Click += (s, e) => DoUserLogin();

            _photoBox = new PictureBox
            {
                Location = new Point(315, 10),
                Size = new Size(75, 75),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(20, 20, 42)
            };
            
            _photoBox.Paint += (s, ev) =>
            {
                ev.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(Color.FromArgb(124, 58, 237), 2))
                {
                    ev.Graphics.DrawEllipse(pen, 1, 1, _photoBox.Width - 3, _photoBox.Height - 3);
                }
                if (_photoBox.Image == null)
                {
                    using (var brush = new SolidBrush(Color.FromArgb(60, 60, 90)))
                    {
                        ev.Graphics.FillEllipse(brush, (_photoBox.Width - 24) / 2, 18, 24, 24);
                        ev.Graphics.FillPie(brush, (_photoBox.Width - 46) / 2, 44, 46, 40, 180, 180);
                    }
                }
            };
            
            using (var path = GetRoundRect(0, 0, 75, 75, 37))
            {
                _photoBox.Region = new Region(path);
            }

            _timeInfoLabel = new Label
            {
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 211, 153),
                Location = new Point(32, 165),
                AutoSize = true,
                MaximumSize = new Size(360, 40),
                BackColor = Color.Transparent
            };

            _userPanel.Controls.AddRange(new Control[] { userComboWrapper, pinBoxWrapper, _userLoginBtn, _photoBox, _timeInfoLabel });
        }

        private void BuildAdminPanel()
        {
            _adminPanel = new Panel { Size = new Size(420, 280), Location = new Point(0, 150), BackColor = Color.Transparent, Visible = false };

            _adminUsernameBox = new TextBox
            {
                Text = "admin",
                Size = new Size(360, 36),
                Font = new Font("Segoe UI", 12),
                BackColor = Color.FromArgb(26, 26, 50),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            var adminUsernameWrapper = CreateModernInputWrapper(_adminUsernameBox, "Usuário Administrador", 30, 10, 360, 56);

            _adminPasswordBox = new TextBox
            {
                Size = new Size(360, 36),
                Font = new Font("Segoe UI", 12),
                PasswordChar = '●',
                BackColor = Color.FromArgb(26, 26, 50),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            _adminPasswordBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) DoAdminLogin(); };

            var adminPasswordWrapper = CreateModernInputWrapper(_adminPasswordBox, "Senha de Acesso", 30, 85, 360, 56);

            _adminLoginBtn = new ModernButton
            {
                Text = "ACESSAR PAINEL DE ADMINISTRAÇÃO",
                Location = new Point(30, 165),
                Size = new Size(360, 46),
                Font = new Font("Segoe UI", 11, FontStyle.Bold)
            };
            _adminLoginBtn.Click += (s, e) => DoAdminLogin();

            _adminPanel.Controls.AddRange(new Control[] { adminUsernameWrapper, adminPasswordWrapper, _adminLoginBtn });
        }

        private Panel CreateModernInputWrapper(Control control, string labelText, int x, int y, int width, int height)
        {
            var wrapper = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(width, height),
                BackColor = Color.FromArgb(26, 26, 50),
                Cursor = Cursors.IBeam
            };

            var lbl = new Label
            {
                Text = labelText,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(130, 130, 160),
                Location = new Point(12, 6),
                AutoSize = true,
                BackColor = Color.Transparent
            };

            wrapper.Controls.Add(lbl);

            control.Location = new Point(12, 26);
            control.Width = width - 24;
            control.BackColor = Color.FromArgb(26, 26, 50);
            control.ForeColor = Color.White;

            if (control is TextBox txt)
            {
                txt.BorderStyle = BorderStyle.None;
                txt.Font = new Font("Segoe UI", 11);
            }
            else if (control is ComboBox cb)
            {
                cb.Font = new Font("Segoe UI", 11);
            }

            wrapper.Controls.Add(control);

            wrapper.Paint += (s, ev) =>
            {
                ev.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = GetRoundRect(0, 0, wrapper.Width, wrapper.Height, 8))
                {
                    using (var brush = new SolidBrush(wrapper.BackColor))
                    {
                        ev.Graphics.FillPath(brush, path);
                    }

                    var isFocused = control.Focused || (control is ComboBox combo && combo.Focused);
                    var borderColor = isFocused ? Color.FromArgb(139, 92, 246) : Color.FromArgb(45, 45, 75);
                    var borderThickness = isFocused ? 2 : 1;

                    using (var pen = new Pen(borderColor, borderThickness))
                    {
                        ev.Graphics.DrawPath(pen, path);
                    }
                }
            };

            control.GotFocus += (s, e) => { wrapper.Invalidate(); lbl.ForeColor = Color.FromArgb(167, 139, 250); };
            control.LostFocus += (s, e) => { wrapper.Invalidate(); lbl.ForeColor = Color.FromArgb(130, 130, 160); };

            if (control is ComboBox combo2)
            {
                combo2.SelectedIndexChanged += (s, e) => { wrapper.Invalidate(); };
            }

            wrapper.SizeChanged += (s, e) =>
            {
                using (var path = GetRoundRect(0, 0, wrapper.Width, wrapper.Height, 8))
                {
                    wrapper.Region = new Region(path);
                }
            };

            return wrapper;
        }

        private GraphicsPath GetRoundRect(int x, int y, int w, int h, int r)
        {
            var path = new GraphicsPath();
            path.AddArc(x, y, r * 2, r * 2, 180, 90);
            path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
            path.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void SwitchToUserMode()
        {
            _isAdminMode = false;
            _userModeBtn.Selected = true;
            _adminModeBtn.Selected = false;
            _userPanel.Visible = true;
            _adminPanel.Visible = false;
            _cardTitle.Text = "Acesso do Usuário";
            _cardSubtitle.Text = "Digite sua matrícula e seu PIN";
            _pinBox.Clear();
            _usernameBox?.Clear();
        }

        private void SwitchToAdminMode()
        {
            _isAdminMode = true;
            _userModeBtn.Selected = false;
            _adminModeBtn.Selected = true;
            _userPanel.Visible = false;
            _adminPanel.Visible = true;
            _cardTitle.Text = "Administração";
            _cardSubtitle.Text = "Digite suas credenciais de administrador";
            _adminPasswordBox.Clear();
            _adminPasswordBox.Focus();
        }

        private void LoadLogo()
        {
            _logoBox.Image = null;
            string[] paths = {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "LogoApac.png"),
                Path.Combine(Application.StartupPath, "Resources", "LogoApac.png"),
            };

            foreach (var p in paths)
            {
                if (File.Exists(p))
                {
                    try { _logoBox.Image = Image.FromFile(p); return; } catch { }
                }
            }

            var configPath = _config.LogoPath;
            if (!string.IsNullOrEmpty(configPath) && File.Exists(configPath))
            {
                try { _logoBox.Image = Image.FromFile(configPath); return; } catch { }
            }

            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ApacKiosk.Resources.LogoApac.png");
                if (stream != null) { _logoBox.Image = Image.FromStream(stream); return; }
            }
            catch { }

            try { _logoBox.Image = CreateDefaultLogo(); } catch { }
        }

        private Bitmap CreateDefaultLogo()
        {
            var bmp = new Bitmap(120, 120);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(Color.White))
                using (var font = new Font("Segoe UI", 36, FontStyle.Bold))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString("APAC", font, brush, new RectangleF(0, 0, 120, 120), sf);
                }
            }
            return bmp;
        }

        private UserListItem _selectedUser;

        private void UsernameBox_Leave(object sender, EventArgs e)
        {
            _photoBox.Image = null;
            _timeInfoLabel.Text = "";
            _selectedUser = null;
            
            var username = _usernameBox.Text.Trim();
            if (string.IsNullOrEmpty(username)) return;

            using var conn = _db.GetConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, full_name, username, photo_path, profile_id, is_active FROM users WHERE username = @un AND is_active = 1";
            cmd.Parameters.AddWithValue("@un", username);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                _selectedUser = new UserListItem
                {
                    Id = Convert.ToInt32(reader["id"]),
                    FullName = reader["full_name"].ToString(),
                    Username = reader["username"].ToString(),
                    PhotoPath = reader["photo_path"]?.ToString(),
                    ProfileId = reader["profile_id"] as int?,
                    IsActive = Convert.ToBoolean(reader["is_active"])
                };
            }
        }

        private string CheckUserTimeAllowed(UserListItem user)
        {
            if (!user.ProfileId.HasValue) return "";
            using var conn = _db.GetConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT day_of_week, start_time, end_time FROM time_rules WHERE profile_id = @pid";
            cmd.Parameters.AddWithValue("@pid", user.ProfileId.Value);
            using var reader = cmd.ExecuteReader();
            var dt = new DataTable();
            dt.Load(reader);
            if (dt.Rows.Count == 0) return "";

            var now = DateTime.Now;
            var currentDay = ((int)now.DayOfWeek == 0) ? 7 : (int)now.DayOfWeek;

            foreach (DataRow row in dt.Rows)
            {
                if (Convert.ToInt32(row["day_of_week"]) != currentDay) continue;
                if (TimeSpan.TryParse(row["start_time"].ToString(), out var start) &&
                    TimeSpan.TryParse(row["end_time"].ToString(), out var end))
                {
                    if (now.TimeOfDay >= start && now.TimeOfDay <= end)
                        return $"Disponível até {end:h\\:mm} ✓";
                    if (now.TimeOfDay < start)
                        return $"Disponível a partir das {start:h\\:mm}";
                }
            }
            return "Fora do horário de acesso permitido";
        }

        private void ShowShutdownPrompt()
        {
            var (result, pwd) = KioskDialog.ShowPasswordPrompt(
                "Digite a senha de administrador para fechar o sistema:",
                "Senha para Fechar", this);

            if (result == DialogResult.OK)
            {
                using var conn = _db.GetConnection();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT password_hash FROM admins WHERE username = 'admin'";
                var hash = cmd.ExecuteScalar()?.ToString();
                if (hash != null && BCrypt.Net.BCrypt.Verify(pwd, hash))
                {
                    _isShuttingDown = true;
                    Close();
                }
                else
                {
                    KioskDialog.ShowMessage("Senha inválida.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error, this);
                }
            }
        }

        private DateTime _lastUpdateCheck = DateTime.MinValue;

        private async void PromptAdminAndUpdate()
        {
            // Proteção anti-spam: evita múltiplos cliques seguidos
            if ((DateTime.Now - _lastUpdateCheck).TotalSeconds < 5)
            {
                return;
            }
            _lastUpdateCheck = DateTime.Now;

            _btnSystemUpdate.Enabled = false;
            _btnSystemUpdate.Text = "Verificando...";

            try
            {
                var updateMgr = new UpdateManager();
                var (success, msg) = await updateMgr.PerformManualUpdateAsync(this, progress =>
                {
                    if (!IsDisposed)
                    {
                        Invoke(() => _btnSystemUpdate.Text = progress);
                    }
                });

                if (!success)
                {
                    KioskDialog.ShowMessage(msg, _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information, this);
                }
            }
            catch (Exception ex)
            {
                KioskDialog.ShowMessage($"Erro ao verificar atualização:\n{ex.Message}", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error, this);
            }
            finally
            {
                if (!IsDisposed && !_btnSystemUpdate.IsDisposed)
                {
                    _btnSystemUpdate.Enabled = true;
                    _btnSystemUpdate.Text = "🔄 Atualizar Sistema";
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_isShuttingDown || DialogResult == DialogResult.OK)
            {
                base.OnFormClosing(e);
                return;
            }

            e.Cancel = true;
            ShowShutdownPrompt();
        }

        private void DoUserLogin()
        {
            if (!Program.License.IsActivated())
            {
                KioskDialog.ShowMessage("O sistema encontra-se desativado pois a licença está revogada ou expirada.\nApenas o administrador pode acessar o sistema para regularizar a licença.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error, this);
                _pinBox.Clear();
                _usernameBox.Clear();
                return;
            }

            if (_selectedUser == null || _selectedUser.Username != _usernameBox.Text.Trim())
            {
                UsernameBox_Leave(null, null);
            }

            if (_selectedUser == null)
            {
                KioskDialog.ShowMessage("Matrícula não encontrada ou usuário inativo.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning, this);
                _pinBox.Clear();
                _usernameBox.Clear();
                return;
            }
            if (string.IsNullOrWhiteSpace(_pinBox.Text))
            {
                KioskDialog.ShowMessage("Digite o PIN.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning, this);
                return;
            }

            var user = _selectedUser;

            var timeInfo = CheckUserTimeAllowed(user);
            if (!string.IsNullOrEmpty(timeInfo) && !timeInfo.Contains("✓") && !timeInfo.Contains("Disponível"))
            {
                KioskDialog.ShowMessage(timeInfo, _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning, this);
                _pinBox.Clear();
                return;
            }

            using var conn = _db.GetConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT pin_hash FROM users WHERE id = @id AND is_active = 1";
            cmd.Parameters.AddWithValue("@id", user.Id);
            var pinHash = cmd.ExecuteScalar()?.ToString();

            if (pinHash == null || !PasswordHash.VerifyPassword(_pinBox.Text, pinHash))
            {
                KioskDialog.ShowMessage("PIN inválido.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error, this);
                _pinBox.Clear();
                return;
            }

            _db.InsertLog(user.Id, "login", null, $"Login: {user.FullName}");

            KioskDialog.ShowMessage($"Bem-vindo(a), {user.FullName}!", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information, this);

            LoggedInUser = new Models.User
            {
                Id = user.Id, FullName = user.FullName, Username = user.Username,
                PhotoPath = user.PhotoPath, ProfileId = user.ProfileId, IsActive = user.IsActive
            };
            DialogResult = DialogResult.OK;
            Close();
        }

        private void DoAdminLogin()
        {
            var username = _adminUsernameBox.Text.Trim();
            var password = _adminPasswordBox.Text;

            Task.Run(() =>
            {
                try
                {
                    var camera = new Services.CameraCapture(_config, _db);
                    using var bmp = camera.TestCameraFrame();
                    if (bmp != null)
                    {
                        var dir = Path.Combine(_config.BaseLogPath, "admin_logins");
                        Directory.CreateDirectory(dir);
                        var filename = $"login_attempt_{username}_{DateTime.Now:yyyyMMdd_HHmmss}.jpg";
                        bmp.Save(Path.Combine(dir, filename), System.Drawing.Imaging.ImageFormat.Jpeg);
                    }
                }
                catch { }
            });

            if (string.IsNullOrWhiteSpace(password))
            {
                KioskDialog.ShowMessage("Digite a senha.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning, this);
                return;
            }

            using var conn = _db.GetConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT password_hash FROM admins WHERE username = @u";
            cmd.Parameters.AddWithValue("@u", username);
            var hash = cmd.ExecuteScalar()?.ToString();

            if (hash != null && PasswordHash.VerifyPassword(password, hash))
            {
                _db.InsertLog(null, "admin_login", null, $"Login admin: {username}");

                if (PasswordHash.VerifyPassword("APAC@Admin2024", hash))
                {
                    KioskDialog.ShowMessage("ATENÇÃO: A senha de administrador ainda é a padrão.\nAltere-a no Painel Admin > Configurações do Sistema.",
                        "Alerta de Segurança", MessageBoxButtons.OK, MessageBoxIcon.Warning, this);
                }

                IsAdmin = true;
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                KioskDialog.ShowMessage("Usuário ou senha inválidos.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error, this);
                _adminPasswordBox.Clear();
                _adminPasswordBox.Focus();
            }
        }

        private void CenterLoginLayout()
        {
            SuspendLayout();
            try
            {
                var cw = ClientSize.Width;
                var ch = ClientSize.Height;

                if (cw == 0 || ch == 0) return;

                int decorWidth = (int)(cw * 0.55);

                _decorPanel.Location = new Point(0, 0);
                _decorPanel.Size = new Size(decorWidth, ch);

                _formContainerPanel.Location = new Point(decorWidth, 0);
                _formContainerPanel.Size = new Size(cw - decorWidth, ch);

                // Centraliza o conteúdo no painel esquerdo
                var leftCh = _decorPanel.Height;
                var leftCw = _decorPanel.Width;
                var leftTotalHeight = 490;
                var leftYOffset = Math.Max(25, (leftCh - leftTotalHeight) / 2);

                _logoBox.Location = new Point((leftCw - 120) / 2, leftYOffset);
                _appNameLabel.Location = new Point((leftCw - 500) / 2, leftYOffset + 130);
                _mottoLabel.Location = new Point((leftCw - 500) / 2, leftYOffset + 185);
                _welcomeLabel.Location = new Point((leftCw - 500) / 2, leftYOffset + 215);

                _photoBanner.Location = new Point((leftCw - 600) / 2, leftYOffset + 270);

                // Centraliza o card no painel direito
                var rightCh = _formContainerPanel.Height;
                var rightCw = _formContainerPanel.Width;
                _cardPanel.Location = new Point((rightCw - 420) / 2, (rightCh - 430) / 2);

                // Botão de fechar fica no canto superior direito do painel formulário
                _closeLabel.Location = new Point(rightCw - 40, 15);

                if (_btnSystemUpdate != null)
                {
                    _btnSystemUpdate.Location = new Point(rightCw - 180, rightCh - 45);
                }
            }
            finally
            {
                ResumeLayout();
            }
        }

        private void LoadBannerPhotos()
        {
            var photosDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Photos");
            
            // Prioriza nossas fotos oficiais de recuperandos felizes se existirem
            string[] files = {
                Path.Combine(photosDir, "recuperandos1.png"),
                Path.Combine(photosDir, "recuperandos2.png"),
                Path.Combine(photosDir, "recuperandos3.png")
            };

            // Fallback para as imagens dinâmicas ou locais se faltar alguma padrão
            if (!files.All(File.Exists))
            {
                if (Directory.Exists(photosDir))
                {
                    files = Directory.GetFiles(photosDir, "*.jpg")
                        .Concat(Directory.GetFiles(photosDir, "*.png"))
                        .Concat(Directory.GetFiles(photosDir, "*.jpeg"))
                        .Take(3)
                        .ToArray();
                }
                else
                {
                    files = new string[0];
                }
            }

            if (files.Length == 0) return;

            _photoBanner.Controls.Clear();
            var photoWidth = 120;
            var gap = 20;
            var startX = (600 - (photoWidth * 3 + gap * 2)) / 2;
            var y = 15;

            for (var i = 0; i < files.Length; i++)
            {
                if (!File.Exists(files[i])) continue;
                try
                {
                    var x = startX + i * (photoWidth + gap);
                    var picBox = new PictureBox
                    {
                        Image = Image.FromFile(files[i]),
                        Size = new Size(photoWidth, 80),
                        Location = new Point(x, y),
                        SizeMode = PictureBoxSizeMode.Zoom,
                        BackColor = Color.FromArgb(20, 20, 42)
                    };
                    
                    // Cantos arredondados na imagem
                    using (var path = new GraphicsPath())
                    {
                        var r = 10;
                        path.AddArc(0, 0, r * 2, r * 2, 180, 90);
                        path.AddArc(photoWidth - r * 2, 0, r * 2, r * 2, 270, 90);
                        path.AddArc(photoWidth - r * 2, 80 - r * 2, r * 2, r * 2, 0, 90);
                        path.AddArc(0, 80 - r * 2, r * 2, r * 2, 90, 90);
                        path.CloseFigure();
                        picBox.Region = new Region(path);
                    }
                    _photoBanner.Controls.Add(picBox);
                }
                catch { }
            }
        }

        private class ToggleButton : Button
        {
            private bool _selected;
            public bool Selected
            {
                get => _selected;
                set { _selected = value; UpdateStyle(); }
            }

            public event Action OnToggle;

            public ToggleButton()
            {
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                Cursor = Cursors.Hand;
                DoubleBuffered = true;
                Click += (s, e) => { Selected = true; OnToggle?.Invoke(); };
                UpdateStyle();
            }

            protected override void OnPaint(PaintEventArgs pevent)
            {
                var g = pevent.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var rect = ClientRectangle;
                var r = 8;

                using (var path = new GraphicsPath())
                {
                    path.AddArc(0, 0, r * 2, r * 2, 180, 90);
                    path.AddArc(rect.Width - r * 2, 0, r * 2, r * 2, 270, 90);
                    path.AddArc(rect.Width - r * 2, rect.Height - r * 2, r * 2, r * 2, 0, 90);
                    path.AddArc(0, rect.Height - r * 2, r * 2, r * 2, 90, 90);
                    path.CloseFigure();

                    Region = new Region(path);

                    using (var brush = new SolidBrush(BackColor))
                    {
                        g.FillPath(brush, path);
                    }
                }

                var sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };

                using (var brush = new SolidBrush(ForeColor))
                {
                    g.DrawString(Text, Font, brush, rect, sf);
                }
            }

            private void UpdateStyle()
            {
                BackColor = _selected ? Color.FromArgb(108, 50, 210) : Color.FromArgb(15, 15, 30);
                ForeColor = _selected ? Color.White : Color.FromArgb(160, 160, 180);
                Invalidate();
            }
        }

        public class ModernButton : Button
        {
            private Color _colorStart = Color.FromArgb(124, 58, 237);
            private Color _colorEnd = Color.FromArgb(79, 70, 229);
            private bool _isHovered;

            public ModernButton()
            {
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                BackColor = Color.Transparent;
                ForeColor = Color.White;
                Cursor = Cursors.Hand;
                DoubleBuffered = true;

                MouseEnter += (s, e) => { _isHovered = true; Invalidate(); };
                MouseLeave += (s, e) => { _isHovered = false; Invalidate(); };
            }

            protected override void OnPaint(PaintEventArgs pevent)
            {
                var g = pevent.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                var rect = ClientRectangle;
                var r = 10;
                
                using (var path = new GraphicsPath())
                {
                    path.AddArc(0, 0, r * 2, r * 2, 180, 90);
                    path.AddArc(rect.Width - r * 2, 0, r * 2, r * 2, 270, 90);
                    path.AddArc(rect.Width - r * 2, rect.Height - r * 2, r * 2, r * 2, 0, 90);
                    path.AddArc(0, rect.Height - r * 2, r * 2, r * 2, 90, 90);
                    path.CloseFigure();

                    Region = new Region(path);

                    var startColor = _isHovered ? Color.FromArgb(139, 92, 246) : _colorStart;
                    var endColor = _isHovered ? Color.FromArgb(99, 102, 241) : _colorEnd;

                    using (var brush = new LinearGradientBrush(rect, startColor, endColor, 45F))
                    {
                        g.FillPath(brush, path);
                    }

                    using (var pen = new Pen(Color.FromArgb(40, 255, 255, 255), 1))
                    {
                        g.DrawPath(pen, path);
                    }
                }

                var sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                
                using (var brush = new SolidBrush(ForeColor))
                {
                    g.DrawString(Text, Font, brush, rect, sf);
                }
            }
        }

        private class UserListItem
        {
            public int Id { get; set; }
            public string FullName { get; set; }
            public string Username { get; set; }
            public string PhotoPath { get; set; }
            public int? ProfileId { get; set; }
            public bool IsActive { get; set; }
            public override string ToString() => $"{FullName} ({Username})";
        }
    }
}
