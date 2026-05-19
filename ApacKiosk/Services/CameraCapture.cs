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
        private VideoCaptureDevice _videoDevice;

        public CameraCapture(ConfigManager config, DatabaseManager db)
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
                Name = "CameraCapture"
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
                    if (_config.CameraEnabled && _currentUserId.HasValue)
                    {
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

            if (_videoDevice == null || !_videoDevice.IsRunning)
            {
                _videoDevice = new VideoCaptureDevice(devices[0].MonikerString);
                _videoDevice.NewFrame += OnNewFrame;
                _videoDevice.Start();
                Thread.Sleep(1000);
            }
        }

        private void OnNewFrame(object sender, AForge.Video.NewFrameEventArgs eventArgs)
        {
            try
            {
                var userLabel = _currentUserId?.ToString() ?? "system";
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var dir = _config.CameraPath;
                Directory.CreateDirectory(dir);

                var fileName = $"camera_{timestamp}_{userLabel}.jpg";
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
    }
}
