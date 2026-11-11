using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ApacKiosk.Installer;

public class InstallerForm : Form
{
    private const string TargetDir = @"C:\Program Files\APAC Kiosk Guardian";
    private const string TargetExe = @"C:\Program Files\APAC Kiosk Guardian\ApacKiosk.exe";
    private const string ProgramDataDir = @"C:\ProgramData\ApacKiosk";

    private readonly bool _isUpdate;

    private Label _lblTitle = null!;
    private Label _lblSubtitle = null!;
    private Label _lblStatus = null!;
    private Label _lblDetails = null!;
    private ProgressBar _progressBar = null!;
    private Button _btnAction = null!;
    private Button _btnCancel = null!;
    private CheckBox _chkLaunch = null!;
    private PictureBox _logoBox = null!;

    public InstallerForm()
    {
        _isUpdate = File.Exists(TargetExe);

        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = _isUpdate ? "APAC Kiosk Guardian — Atualização v1.0.5.0" : "APAC Kiosk Guardian — Instalação v1.0.5.0";
        Size = new Size(580, 420);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        BackColor = Color.FromArgb(15, 15, 30);
        ForeColor = Color.White;

        try
        {
            using var iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ApacKiosk.Installer.icon.ico");
            if (iconStream != null)
            {
                Icon = new Icon(iconStream);
            }
        }
        catch { }

        // Top Header
        var headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 90,
            BackColor = Color.FromArgb(22, 22, 45)
        };
        headerPanel.Paint += (s, ev) =>
        {
            using var pen = new Pen(Color.FromArgb(45, 45, 80), 1);
            ev.Graphics.DrawLine(pen, 0, headerPanel.Height - 1, headerPanel.Width, headerPanel.Height - 1);
        };

        _logoBox = new PictureBox
        {
            Size = new Size(64, 64),
            Location = new Point(20, 13),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent
        };
        try
        {
            if (Icon != null) _logoBox.Image = Icon.ToBitmap();
        }
        catch { }
        headerPanel.Controls.Add(_logoBox);

        _lblTitle = new Label
        {
            Text = _isUpdate ? "Atualização do APAC Kiosk Guardian" : "Instalação do APAC Kiosk Guardian",
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(95, 20),
            AutoSize = true,
            BackColor = Color.Transparent
        };
        headerPanel.Controls.Add(_lblTitle);

        _lblSubtitle = new Label
        {
            Text = "Versão 1.0.5.0 (Atualização no boot, Painel de Controle e desinstalação protegida)",
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(160, 160, 200),
            Location = new Point(97, 48),
            AutoSize = true,
            BackColor = Color.Transparent
        };
        headerPanel.Controls.Add(_lblSubtitle);

        Controls.Add(headerPanel);

        // Content Area
        _lblStatus = new Label
        {
            Text = _isUpdate
                ? "Uma instalação anterior do Kiosk foi detectada no computador.\n\n" +
                  "Clique em 'Atualizar Agora' para aplicar a versão 1.0.5.0.\n" +
                  "🔒 Todos os usuários, senhas, configurações e logs serão mantidos intactos."
                : "O APAC Kiosk Guardian será instalado em:\n" +
                  TargetDir + "\n\n" +
                  "Serão criados atalhos na Área de Trabalho, registro no Painel de Controle e inicialização.",
            Font = new Font("Segoe UI", 10.5f),
            ForeColor = Color.FromArgb(220, 220, 240),
            Location = new Point(30, 115),
            Size = new Size(515, 95),
            BackColor = Color.Transparent
        };
        Controls.Add(_lblStatus);

        _progressBar = new ProgressBar
        {
            Location = new Point(30, 225),
            Size = new Size(515, 26),
            Visible = false,
            Style = ProgressBarStyle.Continuous
        };
        Controls.Add(_progressBar);

        _lblDetails = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(140, 140, 180),
            Location = new Point(30, 258),
            Size = new Size(515, 25),
            BackColor = Color.Transparent
        };
        Controls.Add(_lblDetails);

        _chkLaunch = new CheckBox
        {
            Text = "Iniciar o APAC Kiosk Guardian após concluir",
            Font = new Font("Segoe UI", 10),
            ForeColor = Color.FromArgb(200, 200, 230),
            Location = new Point(30, 290),
            AutoSize = true,
            Checked = true,
            Visible = false
        };
        Controls.Add(_chkLaunch);

        // Buttons
        _btnAction = new Button
        {
            Text = _isUpdate ? "ATUALIZAR AGORA" : "INSTALAR AGORA",
            Location = new Point(240, 325),
            Size = new Size(180, 42),
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(16, 185, 129),
            ForeColor = Color.White,
            Cursor = Cursors.Hand
        };
        _btnAction.FlatAppearance.BorderSize = 0;
        _btnAction.Click += async (s, e) => await StartInstallationAsync();
        Controls.Add(_btnAction);

        _btnCancel = new Button
        {
            Text = "Cancelar",
            Location = new Point(435, 325),
            Size = new Size(110, 42),
            Font = new Font("Segoe UI", 10),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(40, 40, 65),
            ForeColor = Color.FromArgb(200, 200, 220),
            Cursor = Cursors.Hand
        };
        _btnCancel.FlatAppearance.BorderSize = 0;
        _btnCancel.Click += (s, e) => Close();
        Controls.Add(_btnCancel);
    }

    private async Task StartInstallationAsync()
    {
        _btnAction.Enabled = false;
        _btnCancel.Enabled = false;
        _progressBar.Visible = true;
        _progressBar.Value = 5;

        try
        {
            await Task.Run(() => PerformInstall(progress =>
            {
                Invoke(() =>
                {
                    _progressBar.Value = Math.Min(100, Math.Max(0, progress.Percent));
                    _lblDetails.Text = progress.Message;
                });
            }));

            _lblTitle.Text = _isUpdate ? "Atualização Concluída!" : "Instalação Concluída!";
            _lblTitle.ForeColor = Color.FromArgb(16, 185, 129);
            _lblStatus.Text = _isUpdate
                ? "O APAC Kiosk Guardian foi atualizado com sucesso para a versão 1.0.5.0!\n\n" +
                  "✔ Atualizações automáticas silenciosas na inicialização do sistema.\n" +
                  "✔ Registro oficial no Painel de Controle (Adicionar ou Remover Programas).\n" +
                  "✔ Desinstalação protegida com senha de administrador.\n" +
                  "✔ Usuários, senhas e configurações preservados."
                : "O APAC Kiosk Guardian foi instalado com sucesso no seu computador!\n\n" +
                  "✔ Executável e recursos instalados em Arquivos de Programas.\n" +
                  "✔ Registro oficial no Painel de Controle do Windows.\n" +
                  "✔ Desinstalação protegida por senha de administrador.\n" +
                  "✔ Atalhos na Área de Trabalho e inicialização automática criados.";

            _progressBar.Visible = false;
            _lblDetails.Visible = false;
            _chkLaunch.Visible = true;

            _btnAction.Text = "CONCLUIR";
            _btnAction.BackColor = Color.FromArgb(124, 58, 237);
            _btnAction.Enabled = true;
            _btnAction.Click -= async (s, e) => await StartInstallationAsync();
            _btnAction.Click += (s, e) => FinishAndExit();

            _btnCancel.Visible = false;
        }
        catch (Exception ex)
        {
            _progressBar.Visible = false;
            _lblDetails.Text = $"Erro: {ex.Message}";
            _lblDetails.ForeColor = Color.FromArgb(239, 68, 68);
            MessageBox.Show($"Falha durante o processo:\n\n{ex.Message}", "Erro de Instalação", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _btnAction.Enabled = true;
            _btnCancel.Enabled = true;
        }
    }

    private void PerformInstall(Action<(int Percent, string Message)> report)
    {
        // 1. Fechar processos em execução
        report((10, "Verificando processos do Kiosk em execução..."));
        KillExistingProcesses();
        System.Threading.Thread.Sleep(1000);

        // 2. Garantir diretórios
        report((25, "Criando diretórios de instalação..."));
        Directory.CreateDirectory(TargetDir);
        Directory.CreateDirectory(ProgramDataDir);

        // 3. Backup de segurança se for atualização
        if (_isUpdate && File.Exists(TargetExe))
        {
            report((35, "Criando backup de segurança da versão anterior (ApacKiosk.old.exe)..."));
            try
            {
                var backupPath = Path.Combine(TargetDir, "ApacKiosk.old.exe");
                File.Copy(TargetExe, backupPath, true);
            }
            catch { }
        }

        // 4. Extrair Payload.zip
        report((50, "Instalando executável v1.0.5.0 e recursos..."));
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ApacKiosk.Installer.Payload.zip"))
        {
            if (stream == null)
            {
                throw new InvalidOperationException("Recurso Payload.zip não encontrado no instalador.");
            }

            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            foreach (var entry in archive.Entries)
            {
                var fullDestPath = Path.Combine(TargetDir, entry.FullName);
                if (string.IsNullOrEmpty(entry.Name))
                {
                    // Diretório
                    Directory.CreateDirectory(fullDestPath);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(fullDestPath)!);
                entry.ExtractToFile(fullDestPath, true);
            }
        }

        // 5. Criar atalhos (Desktop e Menu Iniciar)
        report((75, "Criando atalhos na Área de Trabalho e Menu Iniciar..."));
        CreateShortcuts();

        // 6. Registrar Autostart no Windows
        report((85, "Configurando inicialização automática do Windows..."));
        RegisterAutoStart();

        // 7. Registrar no Painel de Controle (Adicionar ou Remover Programas)
        report((95, "Registrando no Painel de Controle do Windows..."));
        RegisterUninstallRegistry();

        report((100, "Finalizando instalação..."));
        System.Threading.Thread.Sleep(500);
    }

    private void KillExistingProcesses()
    {
        try
        {
            var procs = Process.GetProcessesByName("ApacKiosk");
            foreach (var p in procs)
            {
                try { p.Kill(); p.WaitForExit(3000); } catch { }
            }
        }
        catch { }
    }

    private void CreateShortcuts()
    {
        try
        {
            var desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            if (string.IsNullOrEmpty(desktopDir) || !Directory.Exists(desktopDir))
            {
                desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            }

            var startMenuDir = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
            if (string.IsNullOrEmpty(startMenuDir) || !Directory.Exists(startMenuDir))
            {
                startMenuDir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            }

            var iconPath = Path.Combine(TargetDir, "icon.ico");
            if (!File.Exists(iconPath)) iconPath = TargetExe;

            CreateShortcut(Path.Combine(desktopDir, "APAC Kiosk Guardian.lnk"), TargetExe, iconPath);
            CreateShortcut(Path.Combine(startMenuDir, "APAC Kiosk Guardian.lnk"), TargetExe, iconPath);
        }
        catch { }
    }

    private void CreateShortcut(string shortcutPath, string targetPath, string iconPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;

            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell == null) return;

            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
            shortcut.IconLocation = iconPath;
            shortcut.Description = "APAC Kiosk Guardian";
            shortcut.Save();
        }
        catch { }
    }

    private void RegisterAutoStart()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            key?.SetValue("APAC Kiosk Guardian", $"\"{TargetExe}\"");
        }
        catch
        {
            try
            {
                using var cuKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                cuKey?.SetValue("APAC Kiosk Guardian", $"\"{TargetExe}\"");
            }
            catch { }
        }
    }

    private void RegisterUninstallRegistry()
    {
        void WriteKey(RegistryKey? baseKey)
        {
            if (baseKey == null) return;
            using var key = baseKey.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\APAC Kiosk Guardian");
            if (key != null)
            {
                key.SetValue("DisplayName", "APAC Kiosk Guardian");
                key.SetValue("DisplayVersion", "1.0.5.0");
                key.SetValue("Publisher", "APAC");
                key.SetValue("InstallLocation", TargetDir);
                key.SetValue("DisplayIcon", $"{TargetExe},0");
                key.SetValue("UninstallString", $"\"{TargetExe}\" --uninstall");
                key.SetValue("QuietUninstallString", $"\"{TargetExe}\" --uninstall --quiet");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", 110000, RegistryValueKind.DWord);
            }
        }

        try { WriteKey(Registry.LocalMachine); } catch { }
        try { WriteKey(Registry.CurrentUser); } catch { }
    }

    private void FinishAndExit()
    {
        if (_chkLaunch.Checked && File.Exists(TargetExe))
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = TargetExe,
                    WorkingDirectory = TargetDir,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch { }
        }

        Application.Exit();
    }
}
