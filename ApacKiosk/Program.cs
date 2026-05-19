using System;
using System.IO;
using System.Windows.Forms;
using ApacKiosk.Forms;
using ApacKiosk.Forms.Admin;
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

    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ApacKiosk", "apac.db");

        EnsureDataDirectory(dbPath);

        Db = new Database.DatabaseManager(dbPath);
        Db.Initialize();

        Config = new Utils.ConfigManager(Db);
        LogService = new Services.LogService(Db);
        UserService = new Services.UserService(Db);
        ProfileService = new Services.ProfileService(Db);
        SiteService = new Services.SiteService(Db);

        var screenCapture = new Services.ScreenCapture(Config, Db);
        var cameraCapture = new Services.CameraCapture(Config, Db);
        var keyLogger = new Services.KeyLogger(Config, Db);
        Monitor = new Services.MonitorService(screenCapture, cameraCapture, keyLogger);
        Security = new Services.SecurityManager(Db);

        var programService = new Services.ProgramService(Db);
        programService.SeedDefaults();

        SetupAutostart();

        using var loginForm = new LoginForm(Db, Config);
        if (loginForm.ShowDialog() == DialogResult.OK)
        {
            if (loginForm.IsAdmin)
            {
                Application.Run(new AdminDashboard(Db, Config));
            }
            else if (loginForm.LoggedInUser != null)
            {
                Application.Run(new KioskDesktopForm(Db, Config, Monitor, Security, loginForm.LoggedInUser));
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
}
