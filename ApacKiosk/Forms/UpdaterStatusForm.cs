using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ApacKiosk.Services;

namespace ApacKiosk.Forms;

public class UpdaterStatusForm : Form
{
    private readonly string _targetExe;
    private readonly int _waitPid;
    private readonly bool _waitForBootConfirmation;

    private Panel _centerCard = null!;
    private Label _lblTitle = null!;
    private Label _lblSubtitle = null!;
    private Label _lblStep = null!;
    private Label _lblPercent = null!;
    private Panel _progressBg = null!;
    private Panel _progressBar = null!;
    private TextBox _txtLog = null!;
    private Panel _errorPanel = null!;
    private Label _lblError = null!;
    private Button _btnRetry = null!;
    private Button _btnCopyLog = null!;
    private Button _btnLaunchTarget = null!;
    private Button _btnClose = null!;

    private volatile bool _isBusy = true;
    private Action<string>? _logHandler;

    public int ExitCode { get; private set; } = 1;

    public UpdaterStatusForm(string targetExe, int waitPid, bool waitForBootConfirmation = true)
    {
        _targetExe = targetExe;
        _waitPid = waitPid;
        _waitForBootConfirmation = waitForBootConfirmation;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = "APAC Kiosk Guardian — Atualização";
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
            Size = new Size(680, 480),
            BackColor = Color.FromArgb(14, 17, 36)
        };
        _centerCard.Paint += (s, ev) =>
        {
            using var pen = new Pen(Color.FromArgb(55, 58, 92), 1);
            ev.Graphics.DrawRectangle(pen, 0, 0, _centerCard.Width - 1, _centerCard.Height - 1);
        };

        _lblTitle = new Label
        {
            Text = "Instalando Atualização do Sistema",
            Font = new Font("Segoe UI", 17, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(28, 22),
            Size = new Size(624, 34),
            BackColor = Color.Transparent
        };

        _lblSubtitle = new Label
        {
            Text = $"Destino: {_targetExe}",
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.FromArgb(150, 155, 190),
            Location = new Point(28, 58),
            Size = new Size(624, 20),
            AutoEllipsis = true,
            BackColor = Color.Transparent
        };

        _lblStep = new Label
        {
            Text = "Iniciando processo de atualização...",
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(167, 139, 250),
            Location = new Point(28, 88),
            Size = new Size(540, 24),
            BackColor = Color.Transparent
        };

        _lblPercent = new Label
        {
            Text = "0%",
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(220, 220, 245),
            TextAlign = ContentAlignment.MiddleRight,
            Location = new Point(575, 88),
            Size = new Size(77, 24),
            BackColor = Color.Transparent
        };

        _progressBg = new Panel
        {
            Size = new Size(624, 10),
            Location = new Point(28, 118),
            BackColor = Color.FromArgb(35, 38, 66)
        };

        _progressBar = new Panel
        {
            Size = new Size(0, 10),
            Location = new Point(0, 0),
            BackColor = Color.FromArgb(124, 58, 237)
        };
        _progressBg.Controls.Add(_progressBar);

        var lblLogTitle = new Label
        {
            Text = "Status e Log em Tempo Real:",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(190, 195, 225),
            Location = new Point(28, 138),
            Size = new Size(300, 20),
            BackColor = Color.Transparent
        };

        _txtLog = new TextBox
        {
            Location = new Point(28, 162),
            Size = new Size(624, 195),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            BackColor = Color.FromArgb(9, 11, 24),
            ForeColor = Color.FromArgb(205, 215, 235),
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 8.5f)
        };

        _errorPanel = new Panel
        {
            Location = new Point(28, 366),
            Size = new Size(624, 54),
            BackColor = Color.FromArgb(65, 20, 28),
            Visible = false
        };
        _lblError = new Label
        {
            Location = new Point(10, 6),
            Size = new Size(604, 42),
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(254, 202, 202),
            BackColor = Color.Transparent
        };
        _errorPanel.Controls.Add(_lblError);

        _btnRetry = CreateActionButton("🔄 Tentar Novamente", new Point(28, 428), new Size(165, 36), Color.FromArgb(16, 185, 129));
        _btnRetry.Visible = false;
        _btnRetry.Click += async (_, _) => await RunUpdateCycleAsync();

        _btnCopyLog = CreateActionButton("📋 Copiar Log", new Point(203, 428), new Size(130, 36), Color.FromArgb(59, 130, 246));
        _btnCopyLog.Visible = false;
        _btnCopyLog.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(_txtLog.Text);
                _lblStep.Text = "Log copiado para a área de transferência.";
            }
            catch { }
        };

        _btnLaunchTarget = CreateActionButton("▶ Abrir Sistema Anterior", new Point(343, 428), new Size(185, 36), Color.FromArgb(124, 58, 237));
        _btnLaunchTarget.Visible = false;
        _btnLaunchTarget.Click += (_, _) => LaunchTargetAndClose();

        _btnClose = CreateActionButton("Fechar", new Point(538, 428), new Size(114, 36), Color.FromArgb(75, 85, 99));
        _btnClose.Visible = false;
        _btnClose.Click += (_, _) => Close();

        _centerCard.Controls.AddRange(new Control[]
        {
            _lblTitle, _lblSubtitle, _lblStep, _lblPercent,
            _progressBg, lblLogTitle, _txtLog, _errorPanel,
            _btnRetry, _btnCopyLog, _btnLaunchTarget, _btnClose
        });

        Controls.Add(_centerCard);

        Layout += (_, _) =>
        {
            _centerCard.Location = new Point(
                Math.Max(0, (ClientSize.Width - _centerCard.Width) / 2),
                Math.Max(0, (ClientSize.Height - _centerCard.Height) / 2)
            );
        };

        _logHandler = line => AppendLogSafe(line);
        UpdateManager.OnUpdateLog += _logHandler;

        Shown += async (_, _) => await RunUpdateCycleAsync();
        FormClosed += (_, _) =>
        {
            if (_logHandler != null)
            {
                UpdateManager.OnUpdateLog -= _logHandler;
                _logHandler = null;
            }
        };
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_isBusy && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            return;
        }
        base.OnFormClosing(e);
    }

    private async Task RunUpdateCycleAsync()
    {
        _isBusy = true;
        SetErrorMode(false, "");
        _progressBar.BackColor = Color.FromArgb(124, 58, 237);
        UpdateProgressUI(new UpdateProgressInfo
        {
            Stage = "Updater",
            Title = "Preparando substituição de arquivos...",
            Percent = 5
        });

        var result = await Task.Run(() =>
            UpdateManager.RunApplyUpdateCore(
                _targetExe,
                _waitPid,
                _waitForBootConfirmation,
                info => UpdateProgressUI(info),
                CancellationToken.None));

        if (result.Success)
        {
            ExitCode = 0;
            _isBusy = false;
            _progressBar.BackColor = Color.FromArgb(16, 185, 129);
            UpdateProgressUI(new UpdateProgressInfo
            {
                Stage = "Done",
                Title = result.Summary,
                Percent = 100
            });
            await Task.Delay(450);
            if (!IsDisposed) Close();
            return;
        }

        ExitCode = result.ErrorCode != 0 ? result.ErrorCode : 1;
        _isBusy = false;
        _progressBar.BackColor = Color.FromArgb(239, 68, 68);
        _lblStep.ForeColor = Color.FromArgb(248, 113, 113);
        _lblStep.Text = "Falha na instalação da atualização";
        string errText = result.RolledBack
            ? $"{result.Summary} (A versão anterior foi restaurada automaticamente.)"
            : result.Summary;
        SetErrorMode(true, errText);
    }

    private void UpdateProgressUI(UpdateProgressInfo info)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(() =>
            {
                if (IsDisposed) return;
                if (!string.IsNullOrWhiteSpace(info.Title))
                {
                    _lblStep.ForeColor = info.Stage == "Done"
                        ? Color.FromArgb(52, 211, 153)
                        : Color.FromArgb(167, 139, 250);
                    _lblStep.Text = info.Title;
                }
                if (info.Percent >= 0)
                {
                    int clamped = Math.Clamp(info.Percent, 0, 100);
                    _lblPercent.Text = $"{clamped}%";
                    _progressBar.Width = (int)(_progressBg.Width * (clamped / 100f));
                }
            });
        }
        catch { }
    }

    private void AppendLogSafe(string line)
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

    private void SetErrorMode(bool hasError, string errorMsg)
    {
        _errorPanel.Visible = hasError;
        _lblError.Text = errorMsg;
        _btnRetry.Visible = hasError;
        _btnCopyLog.Visible = hasError;
        _btnLaunchTarget.Visible = hasError && File.Exists(_targetExe);
        _btnClose.Visible = hasError;
    }

    private void LaunchTargetAndClose()
    {
        try
        {
            if (File.Exists(_targetExe))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _targetExe,
                    WorkingDirectory = Path.GetDirectoryName(_targetExe) ?? "",
                    UseShellExecute = true
                });
            }
        }
        catch { }
        Close();
    }

    private static Button CreateActionButton(string text, Point location, Size size, Color backColor)
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
