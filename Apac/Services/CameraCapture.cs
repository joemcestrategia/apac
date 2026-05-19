using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using Apac.Database;

namespace Apac.Services
{
    public class CameraCapture : IDisposable
    {
        private System.Threading.Timer _timer;
        private volatile bool _running;
        private readonly int _userId;
        private readonly string _username;
        private readonly string _basePath;

        public CameraCapture(int userId, string username)
        {
            _userId = userId;
            _username = username;
            _basePath = DatabaseManager.Instance.GetSetting("camera_path", @"Logs\Camera");
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            Directory.CreateDirectory(_basePath);

            int interval = int.Parse(DatabaseManager.Instance.GetSetting("camera_interval", "120"));
            interval = Math.Max(interval, 30);

            DatabaseManager.Instance.AddLogEntry("SystemEvent", _userId, null,
                "CameraCapture iniciado - câmera pode não estar disponível");

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
                string device = DatabaseManager.Instance.GetSetting("camera_device", "");
                if (string.IsNullOrEmpty(device))
                {
                    return;
                }

                try
                {
                    using (var bitmap = new Bitmap(640, 480))
                    using (var g = Graphics.FromImage(bitmap))
                    {
                        g.Clear(Color.Black);
                        using (var font = new Font("Arial", 14))
                        using (var brush = new SolidBrush(Color.White))
                        {
                            g.DrawString("Câmera indisponível\n\nPara ativar a câmera:\n1. Instale drivers da webcam\n2. Configure no Painel Admin > Monitoramento",
                                font, brush, new RectangleF(0, 0, 640, 480),
                                new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                        }

                        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        string filename = $"camera_{timestamp}_{_username}.jpg";
                        string filePath = Path.Combine(_basePath, filename);
                        bitmap.Save(filePath, ImageFormat.Jpeg);

                        DatabaseManager.Instance.AddLogEntry("Camera", _userId, filePath, null);
                    }
                }
                catch
                {
                    DatabaseManager.Instance.AddLogEntry("SystemEvent", _userId, null, "Falha ao capturar câmera");
                }
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
