using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace ApacKiosk.Forms
{
    public class SplashScreen : Form
    {
        private Timer _timer;
        private int _elapsed;
        private Panel _progressBar;

        public SplashScreen()
        {
            InitializeComponent();
            _timer = new Timer { Interval = 30 };
            _timer.Tick += Timer_Tick;
        }

        private void InitializeComponent()
        {
            Text = "APAC Kiosk Guardian";
            Size = new Size(500, 420);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.FromArgb(10, 10, 25);
            TopMost = true;
            DoubleBuffered = true;
            ShowInTaskbar = false;

            Paint += (s, ev) =>
            {
                using var brush = new LinearGradientBrush(
                    ClientRectangle,
                    Color.FromArgb(8, 12, 28),
                    Color.FromArgb(14, 8, 26),
                    LinearGradientMode.Vertical);
                ev.Graphics.FillRectangle(brush, ClientRectangle);
            };

            var logoBox = new PictureBox
            {
                Size = new Size(110, 110),
                Location = new Point(195, 40),
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
                Location = new Point(20, 158),
                BackColor = Color.Transparent
            };

            var accentLine = new Panel
            {
                Size = new Size(80, 3),
                Location = new Point(210, 198),
                BackColor = Color.FromArgb(196, 160, 90)
            };

            var mottoLabel = new Label
            {
                Text = "\"Aqui o homem entra, o crime fica lá fora\"",
                Font = new Font("Segoe UI", 10, FontStyle.Italic),
                ForeColor = Color.FromArgb(150, 150, 175),
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(460, 22),
                Location = new Point(20, 205),
                BackColor = Color.Transparent
            };

            var disciplineLabel = new Label
            {
                Text = "\"Sem disciplina não há recuperação\"",
                Font = new Font("Segoe UI", 10, FontStyle.Italic),
                ForeColor = Color.FromArgb(196, 160, 90),
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(460, 22),
                Location = new Point(20, 227),
                BackColor = Color.Transparent
            };

            var progressBg = new Panel
            {
                Size = new Size(300, 6),
                Location = new Point(100, 272),
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

            var loadingLabel = new Label
            {
                Text = "Inicializando...",
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(120, 120, 150),
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(300, 20),
                Location = new Point(100, 284),
                BackColor = Color.Transparent
            };

            var devLabel = new Label
            {
                Text = "Desenvolvido por Dionathan A. Iannini",
                Font = new Font("Segoe UI", 9, FontStyle.Regular),
                ForeColor = Color.FromArgb(60, 60, 90),
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(460, 20),
                Location = new Point(20, 380),
                BackColor = Color.Transparent
            };

            Controls.AddRange(new Control[] { logoBox, titleLabel, accentLine, mottoLabel, disciplineLabel, progressBg, loadingLabel, devLabel });

            Shown += (s, e) => _timer.Start();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            _elapsed += 30;
            var pct = Math.Min(_elapsed / 8000f, 1f);
            _progressBar.Width = (int)(300 * pct);

            if (_elapsed >= 8000)
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
                using (var brush = new SolidBrush(Color.White))
                using (var font = new Font("Segoe UI", 38, FontStyle.Bold))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString("APAC", font, brush, new RectangleF(0, 0, 110, 110), sf);
                }
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
}
