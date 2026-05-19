using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Apac.Database;

namespace Apac.Services
{
    public class ScreenCapture : IDisposable
    {
        private System.Threading.Timer _timer;
        private volatile bool _running;
        private readonly int _userId;
        private readonly string _username;
        private readonly string _basePath;

        public ScreenCapture(int userId, string username)
        {
            _userId = userId;
            _username = username;
            _basePath = DatabaseManager.Instance.GetSetting("screenshots_path", @"Logs\Screenshots");
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            Directory.CreateDirectory(_basePath);

            int interval = int.Parse(DatabaseManager.Instance.GetSetting("screenshots_interval", "60"));
            interval = Math.Max(interval, 10);

            _timer = new System.Threading.Timer(_ => Capture(), null, 1000, interval * 1000);
        }

        public void Stop()
        {
            _running = false;
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            _timer?.Dispose();
            _timer = null;
        }

        private void Capture()
        {
            if (!_running) return;
            try
            {
                string quality = DatabaseManager.Instance.GetSetting("screenshots_quality", "Medium");
                int qualityPercent = quality == "Alta" ? 85 : quality == "Média" ? 60 : 35;

                var bounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                using (var bmp = new Bitmap(bounds.Width, bounds.Height))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
                    }

                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string filename = $"screenshot_{timestamp}_{_username}.jpg";
                    string filePath = Path.Combine(_basePath, filename);

                    var jpegEncoder = GetEncoderInfo("image/jpeg");
                    var encoderParams = new EncoderParameters(1);
                    encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)qualityPercent);

                    bmp.Save(filePath, jpegEncoder, encoderParams);

                    DatabaseManager.Instance.AddLogEntry("Screenshot", _userId, filePath, null);
                }
            }
            catch
            {
            }
        }

        private static ImageCodecInfo GetEncoderInfo(string mimeType)
        {
            var codecs = ImageCodecInfo.GetImageEncoders();
            foreach (var codec in codecs)
            {
                if (codec.MimeType == mimeType)
                    return codec;
            }
            return null;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
