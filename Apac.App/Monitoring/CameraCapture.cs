namespace Apac.App.Monitoring;

public class CameraCapture : IDisposable
{
    private Timer? _timer;
    private readonly string _outputPath;
    private readonly int _intervalSeconds;
    private readonly string _quality;
    private readonly DatabaseManager _db;
    private int? _userId;
    private string? _username;
    private bool _cameraAvailable;

    public CameraCapture(DatabaseManager db, string outputPath, int intervalSeconds, string quality, string? cameraDevice = null)
    {
        _db = db;
        _outputPath = outputPath;
        _intervalSeconds = Math.Max(30, intervalSeconds);
        _quality = quality;
        Directory.CreateDirectory(_outputPath);
    }

    public void SetUser(int? userId, string? username)
    {
        _userId = userId;
        _username = username;
    }

    public string[] GetAvailableCameras()
    {
        try
        {
            var devices = new AForge.Video.DirectShow.FilterInfoCollection(
                AForge.Video.DirectShow.FilterCategory.VideoInputDevice);
            return devices.Cast<AForge.Video.DirectShow.FilterInfo>().Select(f => f.Name).ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public void Start()
    {
        _cameraAvailable = false;
        try
        {
            var devices = new AForge.Video.DirectShow.FilterInfoCollection(
                AForge.Video.DirectShow.FilterCategory.VideoInputDevice);
            if (devices.Count == 0)
            {
                _db.InsertLog("camera_error", null, _userId, _username, "Nenhuma câmera detectada");
                return;
            }

            var camera = new AForge.Video.DirectShow.VideoCaptureDevice(devices[0].MonikerString);
            try
            {
                camera.Start();
                System.Threading.Thread.Sleep(1000);
                camera.Stop();
                _cameraAvailable = true;
            }
            catch
            {
                _db.InsertLog("camera_error", null, _userId, _username, "Falha ao inicializar câmera");
                _cameraAvailable = false;
            }

            if (_cameraAvailable)
            {
                _timer = new Timer(CaptureFrame, null, 3000, _intervalSeconds * 1000);
            }
        }
        catch
        {
            _db.InsertLog("camera_error", null, _userId, _username, "Câmera não disponível");
        }
    }

    private void CaptureFrame(object? state)
    {
        try
        {
            var devices = new AForge.Video.DirectShow.FilterInfoCollection(
                AForge.Video.DirectShow.FilterCategory.VideoInputDevice);
            if (devices.Count == 0) return;

            using var camera = new AForge.Video.DirectShow.VideoCaptureDevice(devices[0].MonikerString);
            System.Drawing.Bitmap? frame = null;
            var captureEvent = new ManualResetEvent(false);

            camera.NewFrame += (s, e) =>
            {
                frame = (System.Drawing.Bitmap)e.Frame.Clone();
                captureEvent.Set();
                ((AForge.Video.DirectShow.VideoCaptureDevice)s!).SignalToStop();
            };

            camera.Start();
            if (!captureEvent.WaitOne(5000))
            {
                camera.SignalToStop();
                return;
            }

            camera.WaitForStop();

            if (frame == null) return;

            var qualityLevel = _quality switch
            {
                "Alta" => 90L,
                "Média" => 70L,
                "Baixa" => 40L,
                "High" => 90L,
                "Medium" => 70L,
                "Low" => 40L,
                _ => 70L
            };

            var userTag = _username ?? "sistema";
            var fileName = $"camera_{DateTime.Now:yyyyMMdd_HHmmss}_{userTag}.jpg";
            var filePath = Path.Combine(_outputPath, fileName);

            var encoder = System.Drawing.Imaging.ImageCodecInfo.GetImageDecoders()
                .FirstOrDefault(c => c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);

            if (encoder != null)
            {
                var encParams = new System.Drawing.Imaging.EncoderParameters(1);
                encParams.Param[0] = new System.Drawing.Imaging.EncoderParameter(
                    System.Drawing.Imaging.Encoder.Quality, qualityLevel);
                frame.Save(filePath, encoder, encParams);
            }
            else
            {
                frame.Save(filePath, System.Drawing.Imaging.ImageFormat.Jpeg);
            }

            frame.Dispose();
            _db.InsertLog("camera", filePath, _userId, _username, null);
        }
        catch
        {
        }
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose() => Stop();
}
