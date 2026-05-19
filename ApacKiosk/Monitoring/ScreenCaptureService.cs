using ApacKiosk.Database;
using ApacKiosk.Services;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ApacKiosk.Monitoring;

public class ScreenCaptureService : IDisposable
{
    private readonly DatabaseManager _db;
    private readonly LogService _logService;
    private CancellationTokenSource? _cts;
    private Task? _captureTask;

    public ScreenCaptureService(DatabaseManager db, LogService logService)
    {
        _db = db;
        _logService = logService;
    }

    public void Start(int? userId)
    {
        Stop();
        if (!bool.TryParse(_db.GetSetting("screenshot_enabled", "true"), out var enabled) || !enabled)
            return;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var interval = int.Parse(_db.GetSetting("screenshot_interval_sec", "60"));
        var quality = _db.GetSetting("screenshot_quality", "Medium");
        var path = _db.GetSetting("screenshot_path");

        _captureTask = Task.Run(() => CaptureLoop(token, userId, interval, quality, path), token);
    }

    private void CaptureLoop(CancellationToken token, int? userId, int intervalSec, string quality, string basePath)
    {
        try { Directory.CreateDirectory(basePath); } catch { return; }

        while (!token.IsCancellationRequested)
        {
            try
            {
                var timestamp = DateTime.Now;
                var username = userId.HasValue ? $"user{userId}" : "system";
                var fileName = $"screenshot_{timestamp:yyyyMMdd_HHmmss}_{username}.jpg";
                var filePath = Path.Combine(basePath, fileName);

                using var bitmap = new Bitmap(Screen.PrimaryScreen!.Bounds.Width, Screen.PrimaryScreen.Bounds.Height);
                using var graphics = Graphics.FromImage(bitmap);
                graphics.CopyFromScreen(0, 0, 0, 0, bitmap.Size);

                int qualityLevel = quality switch
                {
                    "High" => 90L,
                    "Low" => 40L,
                    _ => 70L
                };

                int targetW = quality switch
                {
                    "High" => bitmap.Width,
                    "Low" => bitmap.Width / 2,
                    _ => (int)(bitmap.Width * 0.75)
                };
                int targetH = (int)((long)targetW * bitmap.Height / bitmap.Width);

                using var resized = new Bitmap(targetW, targetH);
                using var g = Graphics.FromImage(resized);
                g.DrawImage(bitmap, 0, 0, targetW, targetH);

                var jpegCodec = GetEncoder(ImageFormat.Jpeg);
                var encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, qualityLevel);
                resized.Save(filePath, jpegCodec, encoderParams);

                _logService.Log(userId, "screenshot", filePath, null);
            }
            catch { }

            try { Task.Delay(intervalSec * 1000, token).Wait(token); }
            catch { break; }
        }
    }

    private static ImageCodecInfo GetEncoder(ImageFormat format)
    {
        foreach (var codec in ImageCodecInfo.GetImageEncoders())
            if (codec.FormatID == format.Guid) return codec;
        return ImageCodecInfo.GetImageEncoders()[0];
    }

    public void Stop()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }
        _captureTask = null;
    }

    public void Dispose()
    {
        Stop();
    }
}
