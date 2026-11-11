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
    private Panel _progressBar = null!;
    private Label _loadingLabel = null!;
    private volatile bool _isUpdating;
    private volatile bool _updateCheckDone;

    public SplashScreen()
    {
        InitializeComponent();
        _timer = new Timer { Interval = 30 };
        _timer.Tick += Timer_Tick;
    }

    private void InitializeComponent()
    {
        Text = "APAC Kiosk Guardian";
        WindowState = FormWindowState.Maximized;
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Color.FromArgb(8, 10, 22);
        TopMost = true;
        DoubleBuffered = true;
        ShowInTaskbar = false;

        var centerCard = new Panel
        {
            Size = new Size(500, 420),
            BackColor = Color.Transparent
        };
        centerCard.Paint += (s, ev) =>
        {
            using var brush = new LinearGradientBrush(
                centerCard.ClientRectangle,
                Color.FromArgb(12, 16, 36),
                Color.FromArgb(18, 12, 32),
                LinearGradientMode.Vertical);
            ev.Graphics.FillRectangle(brush, centerCard.ClientRectangle);

            using var pen = new Pen(Color.FromArgb(45, 45, 75), 1);
            ev.Graphics.DrawRectangle(pen, 0, 0, centerCard.Width - 1, centerCard.Height - 1);
        };

        var logoBox = new PictureBox
        {
            Size = new Size(110, 110),
            Location = new Point(195, 35),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent
        };
        LoadLogo(logoBox);

        var titleLabel = new Label
        {
            Text = "APAC Kiosk Guardian",
            Font = new Font("Segoe UI", 22, FontStyle.Bold),
            ForeColor = Color.FromArgb(220, 220, 240),
            TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(460, 40),
            Location = new Point(20, 153),
            BackColor = Color.Transparent
        };

        var accentLine = new Panel
        {
            Size = new Size(80, 3),
            Location = new Point(210, 195),
            BackColor = Color.FromArgb(196, 160, 90)
        };

        var mottoLabel = new Label
        {
            Text = "\"Aqui o homem entra, o crime fica lá fora\"",
            Font = new Font("Segoe UI", 10, FontStyle.Italic),
            ForeColor = Color.FromArgb(150, 150, 175),
            TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(460, 22),
            Location = new Point(20, 203),
            BackColor = Color.Transparent
        };

        var disciplineLabel = new Label
        {
            Text = "\"Sem disciplina não há recuperação\"",
            Font = new Font("Segoe UI", 10, FontStyle.Italic),
            ForeColor = Color.FromArgb(196, 160, 90),
            TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(460, 22),
            Location = new Point(20, 225),
            BackColor = Color.Transparent
        };

        var progressBg = new Panel
        {
            Size = new Size(300, 6),
            Location = new Point(100, 270),
            BackColor = Color.FromArgb(35, 35, 60)
        };
        using (var path = GetRoundRect(0, 0, 300, 6, 3))
            progressBg.Region = new Region(path);

        _progressBar = new Panel
        {
            Size = new Size(0, 6),
            Location = new Point(0, 0),
            BackColor = Color.FromArgb(124, 58, 237)
        };
        progressBg.Controls.Add(_progressBar);

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

        var devLabel = new Label
        {
            Text = "Desenvolvido por Dionathan A. Iannini",
            Font = new Font("Segoe UI", 9, FontStyle.Regular),
            ForeColor = Color.FromArgb(70, 70, 100),
            TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(460, 20),
            Location = new Point(20, 380),
            BackColor = Color.Transparent
        };

        centerCard.Controls.AddRange(new Control[] {
            logoBox, titleLabel, accentLine, mottoLabel,
            disciplineLabel, progressBg, _loadingLabel, devLabel
        });

        Controls.Add(centerCard);

        Layout += (s, e) =>
        {
            centerCard.Location = new Point(
                Math.Max(0, (ClientSize.Width - centerCard.Width) / 2),
                Math.Max(0, (ClientSize.Height - centerCard.Height) / 2)
            );
        };

        Shown += async (s, e) =>
        {
            _timer.Start();
            await CheckUpdatesInBackgroundAsync();
        };
    }

    private async Task CheckUpdatesInBackgroundAsync()
    {
        try
        {
            var updateMgr = new UpdateManager();
            bool updated = await updateMgr.CheckAndApplyStartupUpdateAsync(
                status =>
                {
                    _isUpdating = true;
                    if (!IsDisposed && IsHandleCreated)
                    {
                        try { Invoke(() => _loadingLabel.Text = status); } catch { }
                    }
                },
                pct =>
                {
                    _isUpdating = true;
                    if (!IsDisposed && IsHandleCreated)
                    {
                        try
                        {
                            Invoke(() =>
                            {
                                _progressBar.Width = (int)(300 * Math.Clamp(pct / 100f, 0f, 1f));
                            });
                        }
                        catch { }
                    }
                }
            );

            if (updated)
            {
                // Se atualizou, ApplyAtomicUpdate reinicia o executável e faz Exit(0).
                return;
            }
        }
        catch { }
        finally
        {
            _updateCheckDone = true;
        }
    }

    private void Timer_Tick(object sender, EventArgs e)
    {
        if (_isUpdating)
        {
            // Se estiver em processo de download de atualização, a barra é controlada pelo download
            return;
        }

        _elapsed += 30;
        var targetDuration = _updateCheckDone ? 3000f : 5000f;
        var pct = Math.Min(_elapsed / targetDuration, 1f);
        _progressBar.Width = (int)(300 * pct);

        if (_elapsed >= targetDuration && _updateCheckDone)
        {
            _timer.Stop();
            DialogResult = DialogResult.OK;
            Close();
        }
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
