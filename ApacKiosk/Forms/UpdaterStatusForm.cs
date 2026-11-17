using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ApacKiosk.Database;
using ApacKiosk.Services;

namespace ApacKiosk.Forms;

public class UpdaterStatusForm : Form
{
    private readonly string _targetExe;
    private readonly int _waitPid;
    private readonly bool _waitForBootConfirmation;
    private readonly bool _useServiceMode;
    private readonly string _targetVersion;
    private readonly string _expectedSha;
    private readonly string _readyToken;

    private Panel _centerCard = null!;
    private Label _lblTitle = null!;
    private Label _lblSubtitle = null!;
    private Label _lblStep = null!;
    private Label _lblPercent = null!;
    private Panel _progressBg = null!;
    private Panel _progressBar = null!;
    private Panel _errorPanel = null!;
    private Label _lblError = null!;
    private Button _btnRetry = null!;
    private Button _btnLaunchTarget = null!;
    private Button _btnClose = null!;

    private SecurityManager? _localSecurity;
    private volatile bool _isBusy = true;
    private volatile bool _handoverStarted;

    public int ExitCode { get; private set; } = 1;

    public UpdaterStatusForm(
        string targetExe,
        int waitPid,
        bool waitForBootConfirmation = true,
        bool useServiceMode = false,
        string? targetVersion = null,
        string? expectedSha = null,
        string? readyToken = null)
    {
        _targetExe = targetExe;
        _waitPid = waitPid;
        _waitForBootConfirmation = waitForBootConfirmation;
        _useServiceMode = useServiceMode;
        _targetVersion = targetVersion ?? "";
        _expectedSha = expectedSha ?? "";
        _readyToken = readyToken ?? "";
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
            Size = new Size(560, 290),
            BackColor = Color.FromArgb(14, 17, 36)
        };
        _centerCard.Paint += (_, ev) =>
        {
            using var pen = new Pen(Color.FromArgb(55, 58, 92), 1);
            ev.Graphics.DrawRectangle(pen, 0, 0, _centerCard.Width - 1, _centerCard.Height - 1);
        };

        _lblTitle = new Label
        {
            Text = "Atualizando o APAC Kiosk Guardian",
            Font = new Font("Segoe UI", 17, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(24, 30),
            Size = new Size(512, 36),
            BackColor = Color.Transparent
        };

        _lblSubtitle = new Label
        {
            Text = "Por favor, aguarde. O sistema reiniciará automaticamente.",
            Font = new Font("Segoe UI", 10),
            ForeColor = Color.FromArgb(155, 160, 195),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(24, 70),
            Size = new Size(512, 24),
            BackColor = Color.Transparent
        };

        _lblStep = new Label
        {
            Text = "Preparando instalação...",
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(167, 139, 250),
            Location = new Point(36, 118),
            Size = new Size(420, 24),
            BackColor = Color.Transparent
        };

        _lblPercent = new Label
        {
            Text = "0%",
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(220, 220, 245),
            TextAlign = ContentAlignment.MiddleRight,
            Location = new Point(458, 118),
            Size = new Size(66, 24),
            BackColor = Color.Transparent
        };

        _progressBg = new Panel
        {
            Size = new Size(488, 10),
            Location = new Point(36, 150),
            BackColor = Color.FromArgb(35, 38, 66)
        };

        _progressBar = new Panel
        {
            Size = new Size(0, 10),
            Location = new Point(0, 0),
            BackColor = Color.FromArgb(124, 58, 237)
        };
        _progressBg.Controls.Add(_progressBar);

        _errorPanel = new Panel
        {
            Location = new Point(36, 174),
            Size = new Size(488, 52),
            BackColor = Color.FromArgb(65, 20, 28),
            Visible = false
        };
        _lblError = new Label
        {
            Location = new Point(10, 6),
            Size = new Size(468, 40),
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(254, 202, 202),
            BackColor = Color.Transparent
        };
        _errorPanel.Controls.Add(_lblError);

        _btnRetry = CreateActionButton("🔄 Tentar Novamente", new Point(36, 238), new Size(170, 36), Color.FromArgb(16, 185, 129));
        _btnRetry.Visible = false;
        _btnRetry.Click += async (_, _) => await RunUpdateCycleAsync();

        _btnLaunchTarget = CreateActionButton("▶ Abrir Sistema", new Point(216, 238), new Size(160, 36), Color.FromArgb(124, 58, 237));
        _btnLaunchTarget.Visible = false;
        _btnLaunchTarget.Click += (_, _) => LaunchTargetAndClose();

        _btnClose = CreateActionButton("Fechar", new Point(386, 238), new Size(138, 36), Color.FromArgb(75, 85, 99));
        _btnClose.Visible = false;
        _btnClose.Click += (_, _) => Close();

        _centerCard.Controls.AddRange(new Control[]
        {
            _lblTitle, _lblSubtitle, _lblStep, _lblPercent,
            _progressBg, _errorPanel, _btnRetry, _btnLaunchTarget, _btnClose
        });

        Controls.Add(_centerCard);

        Layout += (_, _) =>
        {
            _centerCard.Location = new Point(
                Math.Max(0, (ClientSize.Width - _centerCard.Width) / 2),
                Math.Max(0, (ClientSize.Height - _centerCard.Height) / 2)
            );
        };

        Shown += async (_, _) =>
        {
            ActivateKioskScreenLock();
            UpdateManager.LogUpdate("UpdaterUI", $"UpdaterStatusForm exibido (ServiceMode={_useServiceMode}, Target='{_targetExe}', ReadyTokenPresent={!string.IsNullOrWhiteSpace(_readyToken)}).");
            if (!string.IsNullOrWhiteSpace(_readyToken))
            {
                UpdateManager.SignalUiReady(_readyToken);
            }
            await RunUpdateCycleAsync();
        };

        FormClosed += (_, _) =>
        {
            try
            {
                if (_handoverStarted)
                    _localSecurity?.ShutdownPreservingLock();
                else
                    _localSecurity?.DisableAllSecurity();
                _localSecurity = null;
            }
            catch { }
        };
    }

    private void ActivateKioskScreenLock()
    {
        try
        {
            if (!_useServiceMode) return;
            string dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ApacKiosk", "apac.db");
            var db = new DatabaseManager(dbPath);
            _localSecurity = new SecurityManager(db);
            _localSecurity.SetMainWindow(Handle);
            _localSecurity.SetKioskLock(true);
            _localSecurity.RegisterAllowedProcessName("ApacKiosk");
            _localSecurity.EnableAllSecurity(protectRegistry: false);
            _localSecurity.ForceHideTaskbar();
        }
        catch (Exception ex)
        {
            UpdateManager.LogUpdate("UpdaterUI", $"Aviso ao ativar bloqueio em UpdaterStatusForm: {ex.Message}");
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_handoverStarted && (_isBusy || _useServiceMode || File.Exists(_targetExe)) && e.CloseReason != CloseReason.ApplicationExitCall)
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
            Title = "Aplicando atualização do sistema...",
            Percent = 10
        });

        UpdateManager.ApplyUpdateResult result = await Task.Run(() =>
        {
            if (_useServiceMode)
            {
                return UpdateManager.RunServiceUpdateUiCore(
                    _targetExe,
                    _waitPid,
                    _targetVersion,
                    _expectedSha,
                    info => UpdateProgressUI(info),
                    newPid => _localSecurity?.RegisterAllowedProcess(newPid),
                    CancellationToken.None,
                    () => _localSecurity?.RegisterAllowedProcessName("ApacKiosk"));
            }

            return UpdateManager.RunApplyUpdateCore(
                _targetExe,
                _waitPid,
                _waitForBootConfirmation,
                info => UpdateProgressUI(info),
                CancellationToken.None);
        });

        if (result.Success)
        {
            ExitCode = 0;
            _isBusy = false;
            _handoverStarted = true;
            _progressBar.BackColor = Color.FromArgb(16, 185, 129);
            UpdateProgressUI(new UpdateProgressInfo
            {
                Stage = "Done",
                Title = "Atualização concluída! Reiniciando...",
                Percent = 100
            });
            await Task.Delay(350);
            _localSecurity?.ShutdownPreservingLock();
            if (!IsDisposed) Close();
            return;
        }

        ExitCode = result.ErrorCode != 0 ? result.ErrorCode : 1;
        _isBusy = false;
        _progressBar.BackColor = Color.FromArgb(239, 68, 68);
        _lblStep.ForeColor = Color.FromArgb(248, 113, 113);
        _lblStep.Text = "Não foi possível concluir a atualização";
        string friendlyErr = result.RolledBack
            ? "A atualização falhou e a versão anterior foi restaurada com segurança."
            : "Ocorreu uma falha durante a instalação. Tente novamente ou abra o sistema.";
        SetErrorMode(true, friendlyErr);
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
                    _lblStep.Text = ToFriendlyStepTitle(info.Title);
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

    private static string ToFriendlyStepTitle(string rawTitle)
    {
        if (rawTitle.Contains("PID", StringComparison.OrdinalIgnoreCase))
            return "Finalizando versão anterior...";
        return rawTitle;
    }

    private void SetErrorMode(bool hasError, string errorMsg)
    {
        _errorPanel.Visible = hasError;
        _lblError.Text = errorMsg;
        _btnRetry.Visible = hasError;
        _btnLaunchTarget.Visible = hasError && File.Exists(_targetExe);
        _btnClose.Visible = hasError && !_useServiceMode && !File.Exists(_targetExe);
    }

    private void LaunchTargetAndClose()
    {
        try
        {
            if (File.Exists(_targetExe))
            {
                _localSecurity?.RegisterAllowedProcessName("ApacKiosk");
                var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = _targetExe,
                    WorkingDirectory = Path.GetDirectoryName(_targetExe) ?? "",
                    UseShellExecute = true
                });
                if (proc != null)
                {
                    _localSecurity?.RegisterAllowedProcess(proc.Id);
                    _handoverStarted = true;
                    _localSecurity?.ShutdownPreservingLock();
                    Close();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            UpdateManager.LogUpdate("UpdaterUI", $"Falha ao relancar TargetExe: {ex.Message}");
        }
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
