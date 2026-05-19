using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Apac.Database;
using Apac.Models;

namespace Apac.Services
{
    public class ScreenCapture : IDisposable
    {
        private CancellationTokenSource _cts;
        private Task _captureTask;
        private readonly int _userId;
        private readonly string _username;

        public ScreenCapture(int userId, string username)
        {
            _userId = userId;
            _username = username;
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _captureTask = Task.Run(() => CaptureLoop(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _captureTask?.Wait(5000); } catch { }
        }

        private async Task CaptureLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var config = DatabaseService.Instance.GetMonitoringConfig();
                    if (!config.ScreenshotEnabled)
                    {
                        await Task.Delay(5000, token);
                        continue;
                    }

                    int interval = Math.Max(config.ScreenshotIntervalSeconds, 10) * 1000;
                    string folder = config.ScreenshotFolder ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Screenshots");
                    Directory.CreateDirectory(folder);

                    using (var bmp = new Bitmap(Screen.PrimaryScreen.Bounds.Width, Screen.PrimaryScreen.Bounds.Height))
                    {
                        using (var g = Graphics.FromImage(bmp))
                        {
                            g.CopyFromScreen(0, 0, 0, 0, bmp.Size);
                        }

                        string quality = config.ScreenshotQuality ?? "Media";
                        long qualityLevel = quality == "Alta" ? 95L : quality == "Media" ? 70L : 40L;

                        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        string filename = $"screenshot_{timestamp}_{_username}.jpg";
                        string filepath = Path.Combine(folder, filename);

                        var encoder = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                        var parameters = new EncoderParameters(1);
                        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, qualityLevel);

                        using (var resized = ResizeImage(bmp, quality))
                        {
                            resized.Save(filepath, encoder, parameters);
                        }

                        DatabaseService.Instance.InsertLogEntry(new LogEntry
                        {
                            UserId = _userId,
                            Type = "screenshot",
                            FilePath = filepath,
                            Details = $"Quality: {quality}"
                        });
                    }

                    CleanOldFiles(folder, config.RetentionDays);

                    await Task.Delay(interval, token);
                }
                catch (OperationCanceledException) { return; }
                catch { await Task.Delay(10000, token); }
            }
        }

        private Bitmap ResizeImage(Bitmap original, string quality)
        {
            float scale = quality == "Alta" ? 0.8f : quality == "Media" ? 0.5f : 0.25f;
            int w = (int)(original.Width * scale);
            int h = (int)(original.Height * scale);
            var resized = new Bitmap(w, h);
            using (var g = Graphics.FromImage(resized))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(original, 0, 0, w, h);
            }
            return resized;
        }

        private void CleanOldFiles(string folder, int retentionDays)
        {
            try
            {
                var cutoff = DateTime.Now.AddDays(-retentionDays);
                foreach (var file in Directory.GetFiles(folder, "screenshot_*.jpg"))
                {
                    if (File.GetCreationTime(file) < cutoff)
                        File.Delete(file);
                }
            }
            catch { }
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }
    }
}
