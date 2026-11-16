using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ApacKiosk.Services;

namespace ApacKiosk.Forms;

public class UpdateStatusDialog : Form
{
    private readonly Action<bool>? _beforeElevationPrompt;

    private Label _lblTitle = null!;
    private Label _lblSubtitle = null!;
    private Label _lblStage = null!;
    private Label _lblPercent = null!;
    private Panel _progressBg = null!;
    private Panel _progressBar = null!;
    private Panel _bannerPanel = null!;
    private Label _lblBanner = null!;
    private Button _btnPrimary = null!;
    private Button _btnClose = null!;

    private UpdateManager.UpdateCheckResult? _pendingUpdate;
    private volatile bool _isBusy;
    private CancellationTokenSource? _cts;
    private Func<Task>? _primaryAction;

    private UpdateStatusDialog(Action<bool>? beforeElevationPrompt)
    {
        _beforeElevationPrompt = beforeElevationPrompt;
        InitializeComponent();
    }

    public static void RunLiveUpdate(Form? ownerForm, Action<bool>? beforeElevationPrompt = null)
    {
        using var dlg = new UpdateStatusDialog(beforeElevationPrompt);
        dlg.ShowDialog(ownerForm);
    }

    private void InitializeComponent()
    {
        Text = "Atualização do Sistema";
        Size = new Size(560, 290);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Color.FromArgb(14, 17, 36);
        TopMost = true;
        DoubleBuffered = true;
        ShowInTaskbar = false;

        Paint += (_, ev) =>
        {
            using var pen = new Pen(Color.FromArgb(70, 75, 115), 2);
            ev.Graphics.DrawRectangle(pen, 1, 1, Width - 2, Height - 2);
        };

        var header = new Panel
        {
            Location = new Point(2, 2),
            Size = new Size(Width - 4, 66),
            BackColor = Color.FromArgb(20, 24, 48)
        };

        _lblTitle = new Label
        {
            Text = "🔄 Atualização do APAC Kiosk Guardian",
            Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(22, 10),
            Size = new Size(512, 28),
            BackColor = Color.Transparent
        };

        _lblSubtitle = new Label
        {
            Text = $"Versão atual: v{UpdateManager.GetCurrentVersion()}",
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(165, 170, 205),
            Location = new Point(22, 39),
            Size = new Size(512, 20),
            BackColor = Color.Transparent
        };
        header.Controls.AddRange(new Control[] { _lblTitle, _lblSubtitle });

        _lblStage = new Label
        {
            Text = "Verificando atualizações disponíveis...",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Color.FromArgb(167, 139, 250),
            Location = new Point(24, 82),
            Size = new Size(440, 24),
            BackColor = Color.Transparent
        };

        _lblPercent = new Label
        {
            Text = "0%",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleRight,
            Location = new Point(468, 82),
            Size = new Size(66, 24),
            BackColor = Color.Transparent
        };

        _progressBg = new Panel
        {
            Location = new Point(24, 112),
            Size = new Size(510, 8),
            BackColor = Color.FromArgb(35, 38, 66)
        };
        _progressBar = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(0, 8),
            BackColor = Color.FromArgb(124, 58, 237)
        };
        _progressBg.Controls.Add(_progressBar);

        _bannerPanel = new Panel
        {
            Location = new Point(24, 134),
            Size = new Size(510, 74),
            BackColor = Color.FromArgb(24, 34, 58),
            Visible = false
        };
        _lblBanner = new Label
        {
            Location = new Point(12, 8),
            Size = new Size(486, 58),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.Transparent
        };
        _bannerPanel.Controls.Add(_lblBanner);

        _btnPrimary = CreateButton("⬇ Baixar e Instalar", new Point(24, 228), new Size(230, 40), Color.FromArgb(16, 185, 129));
        _btnPrimary.Visible = false;
        _btnPrimary.Click += async (_, _) =>
        {
            if (_primaryAction != null) await _primaryAction();
        };

        _btnClose = CreateButton("Fechar", new Point(414, 228), new Size(120, 40), Color.FromArgb(65, 72, 95));
        _btnClose.Click += (_, _) =>
        {
            if (_isBusy)
            {
                _cts?.Cancel();
                _btnClose.Enabled = false;
                _btnClose.Text = "Cancelando...";
                return;
            }
            Close();
        };

        Controls.AddRange(new Control[]
        {
            header, _lblStage, _lblPercent, _progressBg, _bannerPanel, _btnPrimary, _btnClose
        });

        Shown += async (_, _) => await StartCheckAsync();

        FormClosed += (_, _) =>
        {
            _cts?.Cancel();
            _cts?.Dispose();
        };
    }

    private async Task StartCheckAsync()
    {
        _isBusy = true;
        _btnPrimary.Visible = false;
        _bannerPanel.Visible = false;
        _btnClose.Text = "Cancelar";
        _btnClose.Enabled = true;
        _progressBar.BackColor = Color.FromArgb(124, 58, 237);
        SetProgress(15, "Verificando se há novas versões disponíveis...");

        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        var mgr = new UpdateManager();
        using var checkCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        checkCts.CancelAfter(TimeSpan.FromSeconds(15));

        var info = await mgr.CheckForUpdatesAsync(checkCts.Token);
        _isBusy = false;
        _btnClose.Text = "Fechar";
        _btnClose.Enabled = true;

        if (IsDisposed) return;

        if (info == null)
        {
            SetProgress(0, "Falha ao verificar atualizações");
            _progressBar.BackColor = Color.FromArgb(239, 68, 68);
            ShowBanner(Color.FromArgb(75, 22, 30), Color.FromArgb(254, 202, 202),
                "Não foi possível consultar atualizações no momento. Verifique sua conexão com a internet.");
            ConfigureRetryButton();
            return;
        }

        if (!info.UpdateAvailable)
        {
            SetProgress(100, "Sistema atualizado");
            _progressBar.BackColor = Color.FromArgb(16, 185, 129);
            ShowBanner(Color.FromArgb(16, 60, 48), Color.FromArgb(167, 243, 208),
                $"✔ O sistema já está na versão mais recente (v{info.CurrentVersion}).");
            ConfigureRetryButton("🔄 Verificar Novamente");
            return;
        }

        _pendingUpdate = info;
        SetProgress(20, $"Nova versão v{info.LatestVersion} disponível");
        ShowBanner(Color.FromArgb(30, 42, 82), Color.FromArgb(191, 219, 254),
            $"Nova versão encontrada: v{info.LatestVersion} (atual: v{info.CurrentVersion}). Clique abaixo para atualizar.");

        _btnPrimary.Text = $"⬇ Baixar e Instalar v{info.LatestVersion}";
        _btnPrimary.BackColor = Color.FromArgb(16, 185, 129);
        _btnPrimary.Visible = true;
        _primaryAction = StartDownloadAndInstallAsync;
    }

    private async Task StartDownloadAndInstallAsync()
    {
        if (_pendingUpdate == null)
        {
            await StartCheckAsync();
            return;
        }

        _isBusy = true;
        _btnPrimary.Visible = false;
        _bannerPanel.Visible = false;
        _btnClose.Text = "Cancelar";
        _btnClose.Enabled = true;
        _progressBar.BackColor = Color.FromArgb(124, 58, 237);

        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        var mgr = new UpdateManager();
        var (success, message) = await mgr.DownloadAndApplyConfirmedUpdateAsync(
            _pendingUpdate,
            info => UpdateFromProgress(info),
            activatingElevation =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                Invoke(() =>
                {
                    if (activatingElevation)
                    {
                        TopMost = false;
                        _beforeElevationPrompt?.Invoke(true);
                        Hide();
                    }
                    else
                    {
                        Show();
                        TopMost = true;
                        _beforeElevationPrompt?.Invoke(false);
                        BringToFront();
                    }
                });
            },
            _cts.Token);

        _isBusy = false;
        if (IsDisposed) return;

        _btnClose.Text = "Fechar";
        _btnClose.Enabled = true;

        if (!success)
        {
            _progressBar.BackColor = Color.FromArgb(239, 68, 68);
            _lblStage.ForeColor = Color.FromArgb(248, 113, 113);
            _lblStage.Text = "Falha na atualização";
            ShowBanner(Color.FromArgb(75, 22, 30), Color.FromArgb(254, 202, 202),
                "Não foi possível concluir a atualização. Tente novamente.");
            ConfigureRetryButton("🔄 Tentar Novamente");
        }
    }

    private void ConfigureRetryButton(string label = "🔄 Tentar Novamente")
    {
        _btnPrimary.Text = label;
        _btnPrimary.BackColor = Color.FromArgb(124, 58, 237);
        _btnPrimary.Visible = true;
        _primaryAction = StartCheckAsync;
    }

    private void UpdateFromProgress(UpdateProgressInfo info)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(() =>
            {
                if (IsDisposed) return;
                if (!string.IsNullOrWhiteSpace(info.Title))
                    _lblStage.Text = info.Title;
                if (info.Percent >= 0)
                    SetProgress(info.Percent, _lblStage.Text);
            });
        }
        catch { }
    }

    private void SetProgress(int pct, string stageText)
    {
        int clamped = Math.Clamp(pct, 0, 100);
        _lblStage.ForeColor = Color.FromArgb(167, 139, 250);
        _lblStage.Text = stageText;
        _lblPercent.Text = $"{clamped}%";
        _progressBar.Width = (int)(_progressBg.Width * (clamped / 100f));
    }

    private void ShowBanner(Color bg, Color fg, string text)
    {
        _bannerPanel.BackColor = bg;
        _lblBanner.ForeColor = fg;
        _lblBanner.Text = text;
        _bannerPanel.Visible = true;
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
}
