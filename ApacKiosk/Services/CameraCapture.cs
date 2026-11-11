using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using ApacKiosk.Database;
using ApacKiosk.Utils;
using AForge.Video.DirectShow;

namespace ApacKiosk.Services
{
    public class CameraCapture
    {
        private readonly ConfigManager _config;
        private readonly DatabaseManager _db;
        private Thread _thread;
        private volatile bool _isRunning;
        private int? _currentUserId;
        private string _currentUsername;
        private string _sessionTimestamp;
        private string _cameraDir;
        private VideoCaptureDevice _videoDevice;

        public CameraCapture(ConfigManager config, DatabaseManager db)
        {
            _config = config;
            _db = db;
        }

        public void SetSessionInfo(int userId, string username, string sessionTimestamp)
        {
            _currentUserId = userId;
            _currentUsername = SanitizeName(username);
            _sessionTimestamp = sessionTimestamp;
            _cameraDir = BuildCameraDir();
            Directory.CreateDirectory(_cameraDir);
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
                Name = "CameraCapture"
            };
            _thread.Start();
        }

        public void Stop()
        {
            _isRunning = false;
            _thread?.Join(3000);
        }

        private string BuildCameraDir()
        {
            var userLabel = _currentUsername ?? "system";
            var dateLabel = _sessionTimestamp ?? DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            return Path.Combine(_config.BaseLogPath, userLabel, dateLabel, "webcam");
        }

        private void CaptureLoop()
        {
            while (_isRunning)
            {
                try
                {
                    if (_config.CameraEnabled && _currentUserId.HasValue)
                    {
                        if (_cameraDir == null)
                        {
                            _cameraDir = BuildCameraDir();
                            Directory.CreateDirectory(_cameraDir);
                        }
                        CaptureNow();
                    }
                }
                catch (Exception ex)
                {
                    _db.InsertLog(_currentUserId, "camera_error", null, ex.Message);
                    Thread.Sleep(30000);
                }

                var sleepTime = Math.Max(_config.CameraIntervalSec, 30) * 1000;
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
            var devices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            if (devices.Count == 0)
            {
                _db.InsertLog(_currentUserId, "camera_error", null, "Câmera não encontrada");
                return;
            }

            var deviceName = _config.CameraDeviceName;
            var moniker = devices[0].MonikerString;

            if (!string.IsNullOrEmpty(deviceName))
            {
                for (int i = 0; i < devices.Count; i++)
                {
                    if (devices[i].Name == deviceName)
                    {
                        moniker = devices[i].MonikerString;
                        break;
                    }
                }
            }

            if (_videoDevice == null || !_videoDevice.IsRunning)
            {
                _videoDevice = new VideoCaptureDevice(moniker);
                _videoDevice.NewFrame += OnNewFrame;
                _videoDevice.Start();
                Thread.Sleep(1000);
            }
        }

        private void OnNewFrame(object sender, AForge.Video.NewFrameEventArgs eventArgs)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("HHmmss");
                var dir = _cameraDir ?? BuildCameraDir();
                Directory.CreateDirectory(dir);

                var fileName = $"camera_{timestamp}.jpg";
                var filePath = Path.Combine(dir, fileName);

                int quality;
                switch (_config.CameraQuality)
                {
                    case "High": quality = 90; break;
                    case "Low": quality = 40; break;
                    default: quality = 70; break;
                }

                var encoder = ImageCodecInfo.GetImageEncoders();
                var jpegEncoder = Array.Find(encoder, e => e.MimeType == "image/jpeg");
                var encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);

                using (var frame = (Bitmap)eventArgs.Frame.Clone())
                {
                    frame.Save(filePath, jpegEncoder, encoderParams);
                }
                encoderParams.Dispose();

                _db.InsertLog(_currentUserId, "camera", filePath, null);

                if (_videoDevice != null && _videoDevice.IsRunning)
                {
                    _videoDevice.SignalToStop();
                    _videoDevice.NewFrame -= OnNewFrame;
                    _videoDevice = null;
                }
            }
            catch (Exception ex)
            {
                _db.InsertLog(_currentUserId, "camera_error", null, ex.Message);
                try
                {
                    if (_videoDevice != null && _videoDevice.IsRunning)
                    {
                        _videoDevice.SignalToStop();
                        _videoDevice.NewFrame -= OnNewFrame;
                        _videoDevice = null;
                    }
                }
                catch { }
            }
        }

        public static string[] GetCameraDevices()
        {
            try
            {
                var devices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                var names = new string[devices.Count];
                for (int i = 0; i < devices.Count; i++)
                    names[i] = devices[i].Name;
                return names;
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        public Bitmap TestCameraFrame()
        {
            var devices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            if (devices.Count == 0) return null;

            var deviceName = _config.CameraDeviceName;
            var moniker = devices[0].MonikerString;

            if (!string.IsNullOrEmpty(deviceName))
            {
                for (int i = 0; i < devices.Count; i++)
                {
                    if (devices[i].Name == deviceName)
                    {
                        moniker = devices[i].MonikerString;
                        break;
                    }
                }
            }

            Bitmap result = null;
            var signal = new ManualResetEvent(false);

            var device = new VideoCaptureDevice(moniker);
            device.NewFrame += (s, ev) =>
            {
                if (result == null)
                {
                    try
                    {
                        result = (Bitmap)ev.Frame.Clone();
                        signal.Set();
                    }
                    catch { signal.Set(); }
                }
            };

            try
            {
                device.Start();
                if (!signal.WaitOne(5000))
                {
                    device.SignalToStop();
                    return null;
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                try { device.SignalToStop(); } catch { }
            }

            return result;
        }

        private static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "anon";
            return string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        }
    }
}
