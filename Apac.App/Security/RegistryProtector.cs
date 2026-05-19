using Microsoft.Win32;

namespace Apac.App.Security;

public class RegistryProtector : IDisposable
{
    private const string RUN_KEY = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string APP_NAME = "APAC_Kiosk";
    private readonly string _appPath;
    private Timer? _checkTimer;
    private readonly DatabaseManager? _db;

    public RegistryProtector(string appPath, DatabaseManager? db = null)
    {
        _appPath = appPath;
        _db = db;
    }

    public void Install()
    {
        EnsureAutostart();
    }

    public void EnsureAutostart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RUN_KEY, true);
            if (key == null)
            {
                using var newKey = Registry.CurrentUser.CreateSubKey(RUN_KEY);
                newKey?.SetValue(APP_NAME, _appPath);
            }
            else
            {
                var existing = key.GetValue(APP_NAME) as string;
                if (existing != _appPath)
                    key.SetValue(APP_NAME, _appPath);
            }
        }
        catch (Exception ex)
        {
            _db?.InsertLog("registry_error", null, null, null, $"Erro ao configurar autostart: {ex.Message}");
        }
    }

    public void StartMonitor()
    {
        _checkTimer = new Timer(CheckRegistry, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    private void CheckRegistry(object? state)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RUN_KEY);
            if (key == null)
            {
                _db?.InsertLog("registry_error", null, null, null, "Chave de autostart não encontrada, recriando...");
                EnsureAutostart();
                return;
            }
            var value = key.GetValue(APP_NAME) as string;
            if (string.IsNullOrEmpty(value))
            {
                EnsureAutostart();
                _db?.InsertLog("registry_error", null, null, null, "Valor de autostart ausente, recriando...");
            }
        }
        catch
        {
            try { EnsureAutostart(); } catch { }
        }
    }

    public void RemoveAutostart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RUN_KEY, true);
            key?.DeleteValue(APP_NAME, false);
        }
        catch { }
    }

    public void Dispose()
    {
        _checkTimer?.Dispose();
    }
}
