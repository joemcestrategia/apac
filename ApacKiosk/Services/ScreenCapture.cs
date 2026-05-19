using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using ApacKiosk.Database;
using ApacKiosk.Utils;

namespace ApacKiosk.Services
{
    public class ScreenCapture
    {
        private readonly ConfigManager _config;
        private readonly DatabaseManager _db;
        private Thread _thread;
        private volatile bool _isRunning;
        private int? _currentUserId;

        public ScreenCapture(ConfigManager config, DatabaseManager db)
        {
            _config = config;
            _db = db;
        }

        public void SetCurrentUser(int? userId)
        {
            _currentUserId = userId;
        }

        public void Start()
        {
            if (_isRunning) return;
            _isRunning = true;
            _thread = new Thread(CaptureLoop)
            {
                IsBackground = true,
                Name = "ScreenCapture"
            };
            _thread.Start();
        }

        public void Stop()
        {
            _isRunning = false;
            _thread?.Join(3000);
        }

        private void CaptureLoop()
        {
            while (_isRunning)
            {
                try
                {
                    if (_config.ScreenshotEnabled && _currentUserId.HasValue)
                    {
                        var interval = Math.Max(_config.ScreenshotIntervalSec, 10);
                        CaptureNow();
                    }
                }
                catch (Exception ex)
                {
                    _db.InsertLog(_currentUserId, "screenshot_error", null, ex.Message);
                }

                var sleepTime = Math.Max(_config.ScreenshotIntervalSec, 10) * 1000;
                var slept = 0;
                while (slept < sleepTime && _isRunning)
                {
                    Thread.Sleep(100);
                    slept += 100;
                }
            }
        }

        public void CaptureNow()
        {
            var userLabel = _currentUserId?.ToString() ?? "system";
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var dir = _config.ScreenshotPath;
            Directory.CreateDirectory(dir);

            var fileName = $"screenshot_{timestamp}_{userLabel}.jpg";
            var filePath = Path.Combine(dir, fileName);

            using var bmp = new Bitmap(System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width,
                                       System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(0, 0, 0, 0, bmp.Size);

            int quality;
            switch (_config.ScreenshotQuality)
            {
                case "High": quality = 90; break;
                case "Low": quality = 40; break;
                default: quality = 70; break;
            }

            var encoder = ImageCodecInfo.GetImageEncoders();
            var jpegEncoder = Array.Find(encoder, e => e.MimeType == "image/jpeg");
            var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);

            bmp.Save(filePath, jpegEncoder, encoderParams);
            encoderParams.Dispose();

            _db.InsertLog(_currentUserId, "screenshot", filePath, null);
        }
    }
}
