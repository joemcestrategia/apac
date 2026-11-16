using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ApacKiosk.Services;

namespace ApacKiosk.Forms;

public class UpdateStatusDialog : Form
{
    private readonly bool _diagnosticsOnly;
    private readonly Form? _ownerForm;
    private readonly Action<bool>? _beforeElevationPrompt;

    private Label _lblTitle = null!;
    private Label _lblSubtitle = null!;
    private Label _lblStage = null!;
    private Label _lblPercent = null!;
    private Panel _progressBg = null!;
    private Panel _progressBar = null!;
    private Panel _bannerPanel = null!;
    private Label _lblBanner = null!;
    private TextBox _txtLog = null!;
    private Button _btnPrimary = null!;
    private Button _btnSecondary = null!;
    private Button _btnCopy = null!;
    private Button _btnClose = null!;

    private UpdateManager.UpdateCheckResult? _pendingUpdate;
    private volatile bool _isBusy;
    private CancellationTokenSource? _cts;
    private Action<string>? _logHandler;
    private Func<Task>? _primaryAction;

    private UpdateStatusDialog(bool diagnosticsOnly, Form? ownerForm, Action<bool>? beforeElevationPrompt)
    {
        _diagnosticsOnly = diagnosticsOnly;
        _ownerForm = ownerForm;
        _beforeElevationPrompt = beforeElevationPrompt;
        InitializeComponent();
    }

    public static void RunLiveUpdate(Form? ownerForm, Action<bool>? beforeElevationPrompt = null)
    {
        using var dlg = new UpdateStatusDialog(false, ownerForm, beforeElevationPrompt);
        dlg.ShowDialog(ownerForm);
    }

    public static void ShowDiagnostics(IWin32Window? owner)
    {
        using var dlg = new UpdateStatusDialog(true, owner as Form, null);
        dlg.ShowDialog(owner);
    }

    private void InitializeComponent()
    {
        Text = _diagnosticsOnly ? "Status e Logs da Atualização" : "Atualização do Sistema";
        Size = new Size(700, 520);
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
            Text = _diagnosticsOnly ? "📋 Status e Diagnóstico de Atualização" : "🔄 Atualização do APAC Kiosk Guardian",
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(22, 10),
            Size = new Size(640, 28),
            BackColor = Color.Transparent
        };

        _lblSubtitle = new Label
        {
            Text = $"Versão instalada: v{UpdateManager.GetCurrentVersion()} • Modo Admin: {(UpdateManager.IsAdministrator() ? "Sim" : "Não")}",
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(165, 170, 205),
            Location = new Point(22, 39),
            Size = new Size(640, 20),
            BackColor = Color.Transparent
        };
        header.Controls.AddRange(new Control[] { _lblTitle, _lblSubtitle });

        _lblStage = new Label
        {
            Text = _diagnosticsOnly ? "Relatório do ambiente e histórico recente de atualizações:" : "Consultando servidor de atualizações...",
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(167, 139, 250),
            Location = new Point(24, 78),
            Size = new Size(570, 24),
            BackColor = Color.Transparent
        };

        _lblPercent = new Label
        {
            Text = _diagnosticsOnly ? "" : "0%",
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleRight,
            Location = new Point(600, 78),
            Size = new Size(74, 24),
            BackColor = Color.Transparent
        };

        _progressBg = new Panel
        {
            Location = new Point(24, 106),
            Size = new Size(650, 8),
            BackColor = Color.FromArgb(35, 38, 66),
            Visible = !_diagnosticsOnly
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
            Location = new Point(24, 122),
            Size = new Size(650, 56),
            BackColor = Color.FromArgb(24, 34, 58),
            Visible = false
        };
        _lblBanner = new Label
        {
            Location = new Point(12, 8),
            Size = new Size(626, 40),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.Transparent
        };
        _bannerPanel.Controls.Add(_lblBanner);

        _txtLog = new TextBox
        {
            Location = new Point(24, _diagnosticsOnly ? 112 : 188),
            Size = new Size(650, _diagnosticsOnly ? 344 : 268),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = !_diagnosticsOnly,
            BackColor = Color.FromArgb(9, 11, 24),
            ForeColor = Color.FromArgb(210, 218, 235),
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 8.5f)
        };

        _btnPrimary = CreateButton("⬇ Baixar e Instalar", new Point(24, 466), new Size(195, 38), Color.FromArgb(16, 185, 129));
        _btnPrimary.Visible = false;
        _btnPrimary.Click += async (_, _) =>
        {
            if (_primaryAction != null) await _primaryAction();
        };

        _btnSecondary = CreateButton("🔄 Atualizar Log", new Point(24, 466), new Size(155, 38), Color.FromArgb(124, 58, 237));
        _btnSecondary.Visible = _diagnosticsOnly;
        if (_diagnosticsOnly)
        {
            _btnSecondary.Click += (_, _) => LoadDiagnosticsText();
        }

        _btnCopy = CreateButton("📋 Copiar Log", new Point(229, 466), new Size(140, 38), Color.FromArgb(59, 130, 246));
        _btnCopy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(_txtLog.Text);
                _lblStage.Text = "Log copiado para a área de transferência.";
            }
            catch { }
        };

        _btnClose = CreateButton("Fechar", new Point(554, 466), new Size(120, 38), Color.FromArgb(65, 72, 95));
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
            header, _lblStage, _lblPercent, _progressBg, _bannerPanel,
            _txtLog, _btnPrimary, _btnSecondary, _btnCopy, _btnClose
        });

        if (_diagnosticsOnly)
        {
            LoadDiagnosticsText();
        }
        else
        {
            _logHandler = line => AppendLog(line);
            UpdateManager.OnUpdateLog += _logHandler;
            Shown += async (_, _) => await StartCheckAsync();
        }

        FormClosed += (_, _) =>
        {
            _cts?.Cancel();
            _cts?.Dispose();
            if (_logHandler != null)
            {
                UpdateManager.OnUpdateLog -= _logHandler;
                _logHandler = null;
            }
        };
    }

    private void LoadDiagnosticsText()
    {
        _txtLog.Text = UpdateManager.GetDiagnosticsReport();
        _txtLog.SelectionStart = _txtLog.TextLength;
        _txtLog.ScrollToCaret();
    }

    private async Task StartCheckAsync()
    {
        _isBusy = true;
        _btnPrimary.Visible = false;
        _btnSecondary.Visible = false;
        _bannerPanel.Visible = false;
        _btnClose.Text = "Cancelar";
        _btnClose.Enabled = true;
        _progressBar.BackColor = Color.FromArgb(124, 58, 237);
        SetProgress(10, "Consultando GitHub Releases...");

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
                mgr.LastCheckError ?? "Não foi possível consultar o servidor de atualizações.");
            ConfigureRetryButton();
            return;
        }

        if (!info.UpdateAvailable)
        {
            SetProgress(100, "Verificação concluída");
            _progressBar.BackColor = Color.FromArgb(16, 185, 129);
            ShowBanner(Color.FromArgb(16, 60, 48), Color.FromArgb(167, 243, 208),
                $"✔ O sistema já está atualizado na versão mais recente (v{info.CurrentVersion}).");
            ConfigureRetryButton("🔄 Verificar Novamente");
            return;
        }

        _pendingUpdate = info;
        SetProgress(20, $"Nova versão encontrada: v{info.LatestVersion}");
        ShowBanner(Color.FromArgb(30, 42, 82), Color.FromArgb(191, 219, 254),
            $"Nova versão disponível: v{info.LatestVersion} (atual: v{info.CurrentVersion}). Clique em 'Baixar e Instalar' para iniciar.");

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
        _btnSecondary.Visible = false;
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
            ShowBanner(Color.FromArgb(75, 22, 30), Color.FromArgb(254, 202, 202), message.Replace("\n", " "));
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

    private void AppendLog(string line)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(() =>
            {
                if (IsDisposed || _txtLog.IsDisposed) return;
                _txtLog.AppendText(line + Environment.NewLine);
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
}
