namespace ApacKiosk.Services;

public class CameraCaptureService
{
    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private readonly int _userId;
    private const string DefaultFolder = "Logs\\Camera";
    private const int DefaultIntervalSec = 120;

    public CameraCaptureService(int userId)
    {
        _userId = userId;
    }

    public async Task StartAsync()
    {
        var enabled = Data.DatabaseHelper.GetMonitoringConfig("camera", "enabled", "false");
        if (enabled.ToLower() != "true") return;

        int interval = int.TryParse(Data.DatabaseHelper.GetMonitoringConfig("camera", "interval_seconds", "120"), out var i) ? i : DefaultIntervalSec;
        interval = Math.Max(interval, 30);

        var deviceName = Data.DatabaseHelper.GetMonitoringConfig("camera", "device_name", "");
        if (string.IsNullOrEmpty(deviceName))
        {
            Data.DatabaseHelper.InsertLog("system_event", _userId, null, "Câmera não configurada");
            return;
        }

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
        bool cameraAvailable = true;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var folder = Data.DatabaseHelper.GetMonitoringConfig("camera", "folder_path", DefaultFolder);
                Directory.CreateDirectory(folder);

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var filename = $"camera_{timestamp}_user{_userId}.jpg";
                var fullPath = Path.Combine(folder, filename);

                var captured = CaptureFrame(fullPath);

                if (!captured && cameraAvailable)
                {
                    cameraAvailable = false;
                    Data.DatabaseHelper.InsertLog("system_event", _userId, null, "Câmera não encontrada");
                }
                else if (captured)
                {
                    cameraAvailable = true;
                    Data.DatabaseHelper.InsertLog("camera", _userId, fullPath, null);
                }
            }
            catch (Exception ex)
            {
                Data.DatabaseHelper.InsertLog("system_event", _userId, null, $"Erro câmera: {ex.Message}");
            }

            try { await Task.Delay(interval * 1000, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private bool CaptureFrame(string filePath)
    {
        try
        {
            var deviceName = Data.DatabaseHelper.GetMonitoringConfig("camera", "device_name", "");
            var videoDevices = new AForge.Video.DirectShow.FilterInfoCollection(
                AForge.Video.DirectShow.FilterCategory.VideoInputDevice);

            AForge.Video.DirectShow.VideoCaptureDevice? device = null;
            foreach (AForge.Video.DirectShow.FilterInfo info in videoDevices)
            {
                if (info.Name == deviceName)
                {
                    device = new AForge.Video.DirectShow.VideoCaptureDevice(info.MonikerString);
                    break;
                }
            }

            if (device == null) return false;

            var frameEvent = new ManualResetEvent(false);
            System.Drawing.Bitmap? capturedFrame = null;

            device.NewFrame += (sender, e) =>
            {
                capturedFrame = (System.Drawing.Bitmap)e.Frame.Clone();
                frameEvent.Set();
            };

            device.Start();
            var signaled = frameEvent.WaitOne(5000);
            device.SignalToStop();
            device.WaitForStop();

            if (signaled && capturedFrame != null)
            {
                var quality = Data.DatabaseHelper.GetMonitoringConfig("camera", "quality", "medium");
                System.Drawing.Imaging.ImageCodecInfo encoder = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders()
                    .First(c => c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);
                var parameters = new System.Drawing.Imaging.EncoderParameters(1);
                long jpegQuality = quality.ToLower() switch { "low" => 30, "medium" => 60, _ => 90 };
                parameters.Param[0] = new System.Drawing.Imaging.EncoderParameter(
                    System.Drawing.Imaging.Encoder.Quality, jpegQuality);
                capturedFrame.Save(filePath, encoder, parameters);
                capturedFrame.Dispose();
                return true;
            }

            capturedFrame?.Dispose();
            return false;
        }
        catch
        {
            return false;
        }
    }
}
