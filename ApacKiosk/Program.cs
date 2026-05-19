using System;
using System.IO;
using ApacKiosk.Forms;
using ApacKiosk.Forms.Admin;

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

        using var loginForm = new LoginForm(Db, Config);
        if (loginForm.ShowDialog() == DialogResult.OK)
        {
            if (loginForm.IsAdmin)
            {
                Application.Run(new AdminDashboard(Db, Config));
            }
            else if (loginForm.LoggedInUser != null)
            {
                Application.Run(new KioskBrowserForm(Db, Config, Monitor, Security, loginForm.LoggedInUser));
            }
        }
    }
}
