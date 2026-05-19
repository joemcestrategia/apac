using System.Drawing.Imaging;

namespace ApacKiosk.Services;

public class ScreenCaptureService
{
    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private readonly int _userId;
    private const string DefaultFolder = "Logs\\Screenshots";
    private const int DefaultIntervalSec = 60;
    private const string DefaultQuality = "medium";

    public ScreenCaptureService(int userId)
    {
        _userId = userId;
    }

    public async Task StartAsync()
    {
        var enabled = Data.DatabaseHelper.GetMonitoringConfig("screenshots", "enabled", "true");
        if (enabled.ToLower() != "true") return;

        int interval = int.TryParse(Data.DatabaseHelper.GetMonitoringConfig("screenshots", "interval_seconds", "60"), out var i) ? i : DefaultIntervalSec;
        interval = Math.Max(interval, 10);

        _cts = new CancellationTokenSource();
        _captureTask = CaptureLoopAsync(interval, _cts.Token);
        await Task.CompletedTask;
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    private async Task CaptureLoopAsync(int interval, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var folder = Data.DatabaseHelper.GetMonitoringConfig("screenshots", "folder_path", DefaultFolder);
                Directory.CreateDirectory(folder);

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var filename = $"screenshot_{timestamp}_user{_userId}.jpg";
                var fullPath = Path.Combine(folder, filename);

                TakeScreenshot(fullPath);

                Data.DatabaseHelper.InsertLog("screenshot", _userId, fullPath, null);
            }
            catch (Exception ex)
            {
                Data.DatabaseHelper.InsertLog("system_event", _userId, null, $"Erro screenshot: {ex.Message}");
            }

            try { await Task.Delay(interval * 1000, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void TakeScreenshot(string filePath)
    {
        var qualityStr = Data.DatabaseHelper.GetMonitoringConfig("screenshots", "quality", DefaultQuality);
        var bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);

        using var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using var g = Graphics.FromImage(bitmap);
        g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size);

        int targetWidth = qualityStr.ToLower() switch
        {
            "low" => bounds.Width / 3,
            "medium" => bounds.Width / 2,
            _ => bounds.Width
        };
        int targetHeight = bounds.Height * targetWidth / bounds.Width;

        using var resized = new Bitmap(targetWidth, targetHeight);
        using var gr = Graphics.FromImage(resized);
        gr.DrawImage(bitmap, 0, 0, targetWidth, targetHeight);

        var quality = qualityStr.ToLower() switch
        {
            "low" => 30L,
            "medium" => 60L,
            _ => 90L
        };

        var encoder = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
        resized.Save(filePath, encoder, parameters);
    }
}
