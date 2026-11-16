using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using ApacKiosk.Services;
using Timer = System.Windows.Forms.Timer;

namespace ApacKiosk.Forms;

public class SplashScreen : Form
{
    private Timer _timer = null!;
    private int _elapsed;
    private Panel _centerCard = null!;
    private PictureBox _logoBox = null!;
    private Label _titleLabel = null!;
    private Panel _accentLine = null!;
    private Label _mottoLabel = null!;
    private Label _disciplineLabel = null!;
    private Panel _progressBg = null!;
    private Panel _progressBar = null!;
    private Label _loadingLabel = null!;
    private Label _devLabel = null!;
    private TextBox _txtUpdateLog = null!;
    private Button _btnRetryUpdate = null!;
    private Button _btnSkipUpdate = null!;

    private volatile bool _isUpdating;
    private volatile bool _updateCheckDone;
    private volatile bool _hasFatalUpdateFailure;
    private bool _expandedForUpdate;
    private Action<string>? _logHandler;

    public SplashScreen()
    {
        InitializeComponent();
        _timer = new Timer { Interval = 30 };
        _timer.Tick += Timer_Tick;
    }

    private void InitializeComponent()
    {
        Text = "APAC Kiosk Guardian";
        StartPosition = FormStartPosition.Manual;
        if (Screen.PrimaryScreen != null)
            Bounds = Screen.PrimaryScreen.Bounds;
        WindowState = FormWindowState.Maximized;
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Color.FromArgb(8, 10, 22);
        TopMost = true;
        DoubleBuffered = true;
        ShowInTaskbar = false;

        _centerCard = new Panel
        {
            Size = new Size(500, 420),
            BackColor = Color.Transparent
        };
        _centerCard.Paint += (_, ev) =>
        {
            using var brush = new LinearGradientBrush(
                _centerCard.ClientRectangle,
                Color.FromArgb(12, 16, 36),
                Color.FromArgb(18, 12, 32),
                LinearGradientMode.Vertical);
            ev.Graphics.FillRectangle(brush, _centerCard.ClientRectangle);

            using var pen = new Pen(Color.FromArgb(45, 45, 75), 1);
            ev.Graphics.DrawRectangle(pen, 0, 0, _centerCard.Width - 1, _centerCard.Height - 1);
        };

        _logoBox = new PictureBox
        {
            Size = new Size(110, 110),
            Location = new Point(195, 35),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent
        };
        LoadLogo(_logoBox);

        _titleLabel = new Label
        {
            Text = "APAC Kiosk Guardian",
            Font = new Font("Segoe UI", 22, FontStyle.Bold),
            ForeColor = Color.FromArgb(220, 220, 240),
            TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(460, 40),
            Location = new Point(20, 153),
            BackColor = Color.Transparent
        };

        _accentLine = new Panel
        {
            Size = new Size(80, 3),
            Location = new Point(210, 195),
            BackColor = Color.FromArgb(196, 160, 90)
        };

        _mottoLabel = new Label
        {
            Text = "\"Aqui o homem entra, o crime fica lá fora\"",
            Font = new Font("Segoe UI", 10, FontStyle.Italic),
            ForeColor = Color.FromArgb(150, 150, 175),
            TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(460, 22),
            Location = new Point(20, 203),
            BackColor = Color.Transparent
        };

        _disciplineLabel = new Label
        {
            Text = "\"Sem disciplina não há recuperação\"",
            Font = new Font("Segoe UI", 10, FontStyle.Italic),
            ForeColor = Color.FromArgb(196, 160, 90),
            TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(460, 22),
            Location = new Point(20, 225),
            BackColor = Color.Transparent
        };

        _progressBg = new Panel
        {
            Size = new Size(300, 6),
            Location = new Point(100, 270),
            BackColor = Color.FromArgb(35, 35, 60)
        };
        using (var path = GetRoundRect(0, 0, 300, 6, 3))
            _progressBg.Region = new Region(path);

        _progressBar = new Panel
        {
            Size = new Size(0, 6),
            Location = new Point(0, 0),
            BackColor = Color.FromArgb(124, 58, 237)
        };
        _progressBg.Controls.Add(_progressBar);

        _loadingLabel = new Label
        {
            Text = "Verificando integridade e atualizações...",
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.FromArgb(140, 140, 180),
            TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(460, 24),
            Location = new Point(20, 284),
            BackColor = Color.Transparent
        };

        _txtUpdateLog = new TextBox
        {
            Location = new Point(30, 326),
            Size = new Size(560, 142),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            BackColor = Color.FromArgb(9, 11, 24),
            ForeColor = Color.FromArgb(205, 215, 235),
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 8.25f),
            Visible = false
        };

        _btnRetryUpdate = CreateButton("🔄 Tentar Novamente", new Point(110, 480), new Size(190, 36), Color.FromArgb(16, 185, 129));
        _btnRetryUpdate.Visible = false;
        _btnRetryUpdate.Click += async (_, _) => await RetryStartupUpdateAsync();

        _btnSkipUpdate = CreateButton("➡ Avançar para o Login", new Point(320, 480), new Size(200, 36), Color.FromArgb(75, 85, 110));
        _btnSkipUpdate.Visible = false;
        _btnSkipUpdate.Click += (_, _) =>
        {
            _hasFatalUpdateFailure = false;
            _isUpdating = false;
            _updateCheckDone = true;
            _timer.Stop();
            DialogResult = DialogResult.OK;
            Close();
        };

        _devLabel = new Label
        {
            Text = "Desenvolvido por Dionathan A. Iannini",
            Font = new Font("Segoe UI", 9, FontStyle.Regular),
            ForeColor = Color.FromArgb(70, 70, 100),
            TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(460, 20),
            Location = new Point(20, 380),
            BackColor = Color.Transparent
        };

        _centerCard.Controls.AddRange(new Control[] {
            _logoBox, _titleLabel, _accentLine, _mottoLabel,
            _disciplineLabel, _progressBg, _loadingLabel,
            _txtUpdateLog, _btnRetryUpdate, _btnSkipUpdate, _devLabel
        });

        Controls.Add(_centerCard);

        Layout += (_, _) => CenterCard();

        _logHandler = line => AppendLogSafe(line);
        UpdateManager.OnUpdateLog += _logHandler;

        Shown += async (_, _) =>
        {
            Program.Security?.SetMainWindow(Handle);
            Program.Security?.SetKioskLock(true);
            Program.Security?.ResumeWindowProtector();
            Program.Security?.ForceHideTaskbar();
            _timer.Start();
            await CheckUpdatesInBackgroundAsync();
        };

        FormClosed += (_, _) =>
        {
            if (_logHandler != null)
            {
                UpdateManager.OnUpdateLog -= _logHandler;
                _logHandler = null;
            }
        };
    }

    private void CenterCard()
    {
        _centerCard.Location = new Point(
            Math.Max(0, (ClientSize.Width - _centerCard.Width) / 2),
            Math.Max(0, (ClientSize.Height - _centerCard.Height) / 2)
        );
    }

    private void ExpandForDetailedUpdate()
    {
        if (_expandedForUpdate || IsDisposed) return;
        _expandedForUpdate = true;

        _centerCard.Size = new Size(620, 560);
        _logoBox.Location = new Point(255, 22);
        _titleLabel.Size = new Size(580, 38);
        _titleLabel.Location = new Point(20, 138);
        _accentLine.Location = new Point(270, 178);
        _mottoLabel.Size = new Size(580, 22);
        _mottoLabel.Location = new Point(20, 188);
        _disciplineLabel.Size = new Size(580, 22);
        _disciplineLabel.Location = new Point(20, 212);

        _progressBg.Region = null;
        _progressBg.Size = new Size(560, 8);
        _progressBg.Location = new Point(30, 248);

        _loadingLabel.Size = new Size(560, 42);
        _loadingLabel.Location = new Point(30, 264);
        _loadingLabel.TextAlign = ContentAlignment.MiddleCenter;

        _txtUpdateLog.Location = new Point(30, 314);
        _txtUpdateLog.Size = new Size(560, 152);
        _txtUpdateLog.Visible = true;

        _btnRetryUpdate.Location = new Point(105, 480);
        _btnSkipUpdate.Location = new Point(320, 480);

        _devLabel.Size = new Size(580, 20);
        _devLabel.Location = new Point(20, 528);

        CenterCard();
        _centerCard.Invalidate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult != DialogResult.OK && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            return;
        }
        base.OnFormClosing(e);
    }

    private async Task RetryStartupUpdateAsync()
    {
        _hasFatalUpdateFailure = false;
        _updateCheckDone = false;
        _isUpdating = true;
        _btnRetryUpdate.Visible = false;
        _btnSkipUpdate.Visible = false;
        _progressBar.BackColor = Color.FromArgb(124, 58, 237);
        _progressBar.Width = 0;
        _loadingLabel.ForeColor = Color.FromArgb(167, 139, 250);
        _loadingLabel.Text = "Reiniciando verificação de atualização...";
        if (!_timer.Enabled) _timer.Start();
        await CheckUpdatesInBackgroundAsync();
    }

    private async Task CheckUpdatesInBackgroundAsync()
    {
        bool updated = false;
        try
        {
            var updateMgr = new UpdateManager();
            updated = await updateMgr.CheckAndApplyStartupUpdateAsync(
                info =>
                {
                    if (info.Stage is "Download" or "Verify" or "Apply")
                    {
                        _isUpdating = true;
                    }

                    if (!IsDisposed && IsHandleCreated)
                    {
                        try
                        {
                            Invoke(() =>
                            {
                                if (info.Stage is "Download" or "Verify" or "Apply" or "Error")
                                {
                                    ExpandForDetailedUpdate();
                                }
                                if (!string.IsNullOrWhiteSpace(info.Title))
                                {
                                    _loadingLabel.ForeColor = info.Stage == "Error"
                                        ? Color.FromArgb(248, 113, 113)
                                        : Color.FromArgb(167, 139, 250);
                                    _loadingLabel.Text = info.Title;
                                }
                                if (info.Percent >= 0)
                                {
                                    _progressBar.Width = (int)(_progressBg.Width * Math.Clamp(info.Percent / 100f, 0f, 1f));
                                }
                            });
                        }
                        catch { }
                    }
                },
                activatingElevation =>
                {
                    if (!IsDisposed && IsHandleCreated)
                    {
                        try
                        {
                            Invoke(() =>
                            {
                                if (activatingElevation)
                                {
                                    TopMost = false;
                                    Hide();
                                }
                                else
                                {
                                    Show();
                                    TopMost = true;
                                    Program.Security?.SetMainWindow(Handle);
                                    Program.Security?.ForceHideTaskbar();
                                }
                            });
                        }
                        catch { }
                    }
                }
            );

            if (updated)
            {
                return;
            }

            if (updateMgr.HadUpdateAttempt && !string.IsNullOrWhiteSpace(updateMgr.LastFailureMessage))
            {
                _hasFatalUpdateFailure = true;
                if (!IsDisposed && IsHandleCreated)
                {
                    try
                    {
                        Invoke(() =>
                        {
                            ExpandForDetailedUpdate();
                            _timer.Stop();
                            _progressBar.BackColor = Color.FromArgb(239, 68, 68);
                            _progressBar.Width = _progressBg.Width;
                            _loadingLabel.ForeColor = Color.FromArgb(248, 113, 113);
                            _loadingLabel.Text = $"Falha ao atualizar: {updateMgr.LastFailureMessage}";
                            _btnRetryUpdate.Visible = true;
                            _btnSkipUpdate.Visible = true;
                        });
                    }
                    catch { }
                }
                return;
            }
        }
        catch (Exception ex)
        {
            UpdateManager.LogUpdate("Splash", $"Excecao em CheckUpdatesInBackgroundAsync: {ex.Message}");
        }
        finally
        {
            if (!updated && !_hasFatalUpdateFailure)
            {
                _isUpdating = false;
                _updateCheckDone = true;
            }
        }
    }

    private void Timer_Tick(object sender, EventArgs e)
    {
        if (_isUpdating || _hasFatalUpdateFailure)
        {
            return;
        }

        _elapsed += 30;
        var targetDuration = _updateCheckDone ? 3000f : 5000f;
        var pct = Math.Min(_elapsed / targetDuration, 1f);
        _progressBar.Width = (int)(_progressBg.Width * pct);

        if (_elapsed >= targetDuration && _updateCheckDone)
        {
            _timer.Stop();
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    private void AppendLogSafe(string line)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(() =>
            {
                if (IsDisposed || _txtUpdateLog.IsDisposed) return;
                _txtUpdateLog.AppendText(line + Environment.NewLine);
            });
        }
        catch { }
    }

    private static Button CreateButton(string text, Point location, Size size, Color backColor)
    {
        var btn = new Button
        {
            Text = text,
            Location = location,
            Size = size,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            BackColor = backColor,
            ForeColor = Color.White,
            Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderSize = 0;
        return btn;
    }

    private void LoadLogo(PictureBox box)
    {
        var paths = new[] {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "LogoApac.png"),
            Path.Combine(Application.StartupPath, "Resources", "LogoApac.png"),
        };
        foreach (var p in paths)
        {
            if (File.Exists(p))
            {
                try { box.Image = Image.FromFile(p); return; } catch { }
            }
        }
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ApacKiosk.Resources.LogoApac.png");
            if (stream != null) { box.Image = Image.FromStream(stream); return; }
        }
        catch { }
        try { box.Image = CreateDefaultLogo(); } catch { }
    }

    private Bitmap CreateDefaultLogo()
    {
        var bmp = new Bitmap(110, 110);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(Color.White);
            using var font = new Font("Segoe UI", 38, FontStyle.Bold);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("APAC", font, brush, new RectangleF(0, 0, 110, 110), sf);
        }
        return bmp;
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
}
