using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using ApacKiosk.Forms;
using ApacKiosk.Forms.Admin;
using ApacKiosk.Utils;
using Microsoft.Win32;

namespace ApacKiosk;

static class Program
{
    public static Database.DatabaseManager Db { get; private set; } = null!;
    public static Utils.ConfigManager Config { get; private set; } = null!;
    public static Services.LogService LogService { get; private set; } = null!;
    public static Services.UserService UserService { get; private set; } = null!;
    public static Services.ProfileService ProfileService { get; private set; } = null!;
    public static Services.SiteService SiteService { get; private set; } = null!;
    public static Services.MonitorService Monitor { get; private set; } = null!;
    public static Services.SecurityManager Security { get; private set; } = null!;
    public static Services.ProcessWatcher ProcessWatcher { get; private set; } = null!;
    public static Services.FileDialogBlocker FileDialogBlocker { get; private set; } = null!;
    public static Services.ShellExecuteGuard ShellGuard { get; private set; } = null!;
    public static Services.LicenseManager License { get; private set; } = null!;

    private static Mutex _singleInstanceMutex = null!;

    [STAThread]
    static void Main(string[] args)
    {
        // 1. Tratar Desinstalação chamada pelo Painel de Controle
        if (args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase) || a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            HandleUninstallBootstrap();
            return;
        }

        if (args.Any(a => a.Equals("--uninstall-runner", StringComparison.OrdinalIgnoreCase)))
        {
            HandleUninstallExecution();
            return;
        }

        bool isNewInstance;
        _singleInstanceMutex = new Mutex(true, "Global\\APACKioskGuardian_SingleInstance", out isNewInstance);
        if (!isNewInstance)
        {
            Trace("Já existe uma instância rodando. Encerrando silenciosamente.");
            return;
        }

        Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", "--no-sandbox");
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            var msg = ex?.ToString() ?? e.ExceptionObject?.ToString();
            try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "ApacKiosk_Crash.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UnhandledException: {msg}\n"); } catch { }
        };
        Application.ThreadException += (s, e) =>
        {
            try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "ApacKiosk_Crash.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ThreadException: {e.Exception}\n"); } catch { }
            KioskDialog.ShowMessage($"Ocorreu um erro inesperado:\n{e.Exception.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ApacKiosk", "apac.db");

        EnsureDataDirectory(dbPath);

        Db = new Database.DatabaseManager(dbPath);
        Db.Initialize();

        Config = new Utils.ConfigManager(Db);
        License = new Services.LicenseManager(Config);

        // Garantir registro no Painel de Controle do Windows
        UninstallHelper.RegisterUninstall();

        bool silentInstall = false;
        string? silentSerial = null, silentState = null, silentCity = null, silentGender = null, silentEmail = null, silentName = null;
        foreach (var arg in args)
        {
            var lowerArg = arg.ToLower();
            if (lowerArg == "/silent" || lowerArg == "/verysilent") silentInstall = true;
            else if (lowerArg.StartsWith("/serial=")) silentSerial = arg.Substring(8).Trim('"');
            else if (lowerArg.StartsWith("/uf=")) silentState = arg.Substring(4).Trim('"');
            else if (lowerArg.StartsWith("/city=")) silentCity = arg.Substring(6).Trim('"');
            else if (lowerArg.StartsWith("/gender=")) silentGender = arg.Substring(8).Trim('"');
            else if (lowerArg.StartsWith("/email=")) silentEmail = arg.Substring(7).Trim('"');
            else if (lowerArg.StartsWith("/name=")) silentName = arg.Substring(6).Trim('"');
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"Software\APAC\Kiosk", true);
            if (key != null)
            {
                var regSilent = key.GetValue("SilentInstall")?.ToString();
                if (regSilent == "1")
                {
                    silentInstall = true;
                    silentSerial ??= key.GetValue("Serial")?.ToString();
                    silentState ??= key.GetValue("UF")?.ToString();
                    silentCity ??= key.GetValue("City")?.ToString();
                    silentGender ??= key.GetValue("Gender")?.ToString();
                    silentEmail ??= key.GetValue("Email")?.ToString();
                    silentName ??= key.GetValue("Name")?.ToString();

                    try { key.DeleteValue("SilentInstall", false); } catch { }
                }
            }
        }
        catch { }

        if (!License.IsActivated())
        {
            Trace("Sistema não ativado e não está em período de avaliação válido.");

            if (silentInstall && !string.IsNullOrEmpty(silentSerial))
            {
                Trace($"Modo silencioso detectado, tentando ativar com serial {silentSerial}...");
                var task = License.ActivateOnlineAsync(silentSerial, silentState ?? "", silentCity ?? "", silentGender ?? "M", silentEmail ?? "", silentName ?? "");
                task.Wait();
                var activationResult = task.Result;
                if (!activationResult.success)
                {
                    Trace($"Erro na ativação silenciosa: {activationResult.error}");
                }
                else
                {
                    Trace("Ativação silenciosa concluída com sucesso.");
                }
            }
        }

        if (Config.IsFirstRun)
        {
            Trace("Primeira execução detectada. Solicitando senha de administrador.");
            bool authSuccess = false;

            using var dummyBg = new Form
            {
                TopMost = true,
                WindowState = FormWindowState.Maximized,
                FormBorderStyle = FormBorderStyle.None,
                BackColor = Color.FromArgb(10, 10, 25),
                ShowInTaskbar = false
            };
            dummyBg.Show();

            while (!authSuccess)
            {
                var (result, pwd) = KioskDialog.ShowPasswordPrompt(
                    "Esta é a primeira execução do sistema.\nPara evitar bloqueios acidentais, digite a senha de administrador para liberar o quiosque:",
                    "Primeiro Acesso", dummyBg);

                if (result == DialogResult.OK)
                {
                    using var conn = Db.GetConnection();
                    var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT password_hash FROM admins WHERE username = 'admin'";
                    var hash = cmd.ExecuteScalar()?.ToString();
                    if (hash != null && BCrypt.Net.BCrypt.Verify(pwd, hash))
                    {
                        authSuccess = true;
                        Config.IsFirstRun = false;
                        Trace("Senha de administrador validada. Primeira execução liberada.");
                    }
                    else
                    {
                        KioskDialog.ShowMessage("Senha inválida.", Config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error, dummyBg);
                    }
                }
                else
                {
                    Trace("Primeira execução cancelada. Encerrando sem aplicar bloqueios.");
                    dummyBg.Close();
                    return;
                }
            }
            dummyBg.Close();
        }

        // Exibir SplashScreen e checar atualizações silenciosas na inicialização
        Trace("Exibindo SplashScreen e verificando atualização na inicialização");
        using (var splash = new SplashScreen())
        {
            splash.ShowDialog();
        }

        LogService = new Services.LogService(Db);
        UserService = new Services.UserService(Db);
        ProfileService = new Services.ProfileService(Db);
        SiteService = new Services.SiteService(Db);

        var screenCapture = new Services.ScreenCapture(Config, Db);
        var cameraCapture = new Services.CameraCapture(Config, Db);
        var keyLogger = new Services.KeyLogger(Config, Db);
        Monitor = new Services.MonitorService(screenCapture, cameraCapture, keyLogger);
        Security = new Services.SecurityManager(Db);

        ProcessWatcher = new Services.ProcessWatcher(Db);
        ProcessWatcher.Start();

        FileDialogBlocker = new Services.FileDialogBlocker(Db);
        FileDialogBlocker.Start();

        ShellGuard = new Services.ShellExecuteGuard(Db);
        ShellGuard.Start();

        var cloudSync = new Services.CloudSyncService(Config);
        cloudSync.Start();

        var programService = new Services.ProgramService(Db);
        programService.SeedDefaults();

        SetupAutostart();

        while (true)
        {
            Trace("Mostrando LoginForm");
            using var loginForm = new LoginForm(Db, Config);
            if (loginForm.ShowDialog() != DialogResult.OK)
            {
                Trace("Login cancelado, encerrando");
                break;
            }

            Trace($"Login OK, IsAdmin={loginForm.IsAdmin}, User={loginForm.LoggedInUser?.Username}");

            if (loginForm.IsAdmin)
            {
                Trace("Iniciando AdminDashboard");
                using var adminDashboard = new AdminDashboard(Db, Config);
                Application.Run(adminDashboard);

                if (adminDashboard.LogoutRequested)
                {
                    Trace("Admin solicitou logout, retornando ao login");
                    continue;
                }

                Trace("AdminDashboard fechado sem logout, encerrando");
                break;
            }
            else if (loginForm.LoggedInUser != null)
            {
                if (FileDialogBlocker != null)
                {
                    FileDialogBlocker.CurrentUsername = loginForm.LoggedInUser.Username;
                    FileDialogBlocker.EnsureUserFolderExists();
                }

                Trace("Criando KioskDesktopForm...");
                using var kdf = new KioskDesktopForm(Db, Config, Monitor, Security, loginForm.LoggedInUser);
                Trace("KioskDesktopForm criado, iniciando Application.Run...");
                Application.Run(kdf);

                if (kdf.LogoutRequested)
                {
                    Trace("Usuario solicitou logout, retornando ao login");
                    continue;
                }

                Trace("KioskDesktopForm fechado sem logout, encerrando");
                break;
            }
        }

        Security?.Dispose();
        ProcessWatcher?.Dispose();
        FileDialogBlocker?.Dispose();
        ShellGuard?.Dispose();
        cloudSync?.Dispose();
        _singleInstanceMutex?.Dispose();
        Trace("Programa encerrado normalmente");
    }

    private static void HandleUninstallBootstrap()
    {
        try
        {
            string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? Application.ExecutablePath;
            string tempDir = Path.Combine(Path.GetTempPath(), "ApacKioskSetup");
            if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

            string tempExe = Path.Combine(tempDir, "ApacKioskUninstall.exe");
            File.Copy(currentExe, tempExe, true);

            var psi = new ProcessStartInfo
            {
                FileName = tempExe,
                Arguments = "--uninstall-runner",
                WorkingDirectory = tempDir,
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Falha ao iniciar o desinstalador:\n{ex.Message}", "APAC Kiosk Guardian", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void HandleUninstallExecution()
    {
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ApacKiosk", "apac.db");

        if (!File.Exists(dbPath))
        {
            // Se o banco não existir, confirmação direta
            var confirm = MessageBox.Show(
                "O banco de dados do quiosque não foi localizado.\nDeseja remover o APAC Kiosk Guardian deste computador?",
                "APAC Kiosk Guardian — Desinstalação",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm == DialogResult.Yes)
            {
                UninstallHelper.ExecuteFullUninstall(true);
                MessageBox.Show("APAC Kiosk Guardian foi desinstalado com sucesso.", "Desinstalação Concluída", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return;
        }

        var db = new Database.DatabaseManager(dbPath);
        using var form = new UninstallConfirmForm(db);
        if (form.ShowDialog() == DialogResult.OK)
        {
            try
            {
                UninstallHelper.ExecuteFullUninstall(form.PurgeData);
                MessageBox.Show(
                    "O APAC Kiosk Guardian foi desinstalado com sucesso deste computador.",
                    "Desinstalação Concluída",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ocorreu um erro durante a desinstalação:\n{ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private static void EnsureDataDirectory(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    private static void SetupAutostart()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);

            if (key == null) return;

            if (Config.AutostartEnabled)
            {
                var exePath = Application.ExecutablePath;
                key.SetValue("ApacKioskGuardian", exePath);
            }
            else
            {
                try { key.DeleteValue("ApacKioskGuardian", false); } catch { }
            }
        }
        catch { }
    }

    private static void Trace(string msg)
    {
        try
        {
            var logPath = Path.Combine(Path.GetTempPath(), "ApacKiosk_Trace.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
        }
        catch { }
    }
}
