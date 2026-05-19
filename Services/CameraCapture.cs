using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Apac.Database;
using Apac.Models;

namespace Apac.Services
{
    public class CameraCapture : IDisposable
    {
        private CancellationTokenSource _cts;
        private Task _captureTask;
        private readonly int _userId;
        private readonly string _username;

        public CameraCapture(int userId, string username)
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
                    if (!config.CameraEnabled)
                    {
                        await Task.Delay(10000, token);
                        continue;
                    }

                    int interval = Math.Max(config.CameraIntervalSeconds, 30) * 1000;
                    string folder = config.CameraFolder ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Camera");
                    Directory.CreateDirectory(folder);

                    try
                    {
                        using (var captured = CaptureFromCamera(config.CameraDevice))
                        {
                            if (captured != null)
                            {
                                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                                string filename = $"camera_{timestamp}_{_username}.jpg";
                                string filepath = Path.Combine(folder, filename);

                                string quality = config.CameraQuality ?? "Media";
                                long qualityLevel = quality == "Alta" ? 95L : quality == "Media" ? 70L : 40L;

                                var encoder = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                                var parameters = new EncoderParameters(1);
                                parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, qualityLevel);
                                captured.Save(filepath, encoder, parameters);

                                DatabaseService.Instance.InsertLogEntry(new LogEntry
                                {
                                    UserId = _userId,
                                    Type = "camera",
                                    FilePath = filepath,
                                    Details = $"Device: {config.CameraDevice ?? "auto"}"
                                });
                            }
                        }
                    }
                    catch
                    {
                        DatabaseService.Instance.InsertLogEntry(new LogEntry
                        {
                            Type = "system_event",
                            Details = "Camera not found or failed to capture"
                        });
                    }

                    CleanOldFiles(folder, config.RetentionDays);
                    await Task.Delay(interval, token);
                }
                catch (OperationCanceledException) { return; }
                catch { await Task.Delay(10000, token); }
            }
        }

        private Bitmap CaptureFromCamera(string deviceMoniker)
        {
            if (string.IsNullOrEmpty(deviceMoniker))
            {
                var devices = GetCameraDevices();
                if (devices.Length == 0) throw new Exception("No camera found");
                deviceMoniker = devices[0];
            }
            return new Bitmap(320, 240);
        }

        public static string[] GetCameraDevices()
        {
            try
            {
                var filterInfoCollection = new AForge.Video.DirectShow.FilterInfoCollection(
                    AForge.Video.DirectShow.FilterCategory.VideoInputDevice);
                var devices = new string[filterInfoCollection.Count];
                for (int i = 0; i < filterInfoCollection.Count; i++)
                    devices[i] = filterInfoCollection[i].MonikerString;
                return devices;
            }
            catch
            {
                return new string[0];
            }
        }

        private void CleanOldFiles(string folder, int retentionDays)
        {
            try
            {
                var cutoff = DateTime.Now.AddDays(-retentionDays);
                foreach (var file in Directory.GetFiles(folder, "camera_*.jpg"))
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
