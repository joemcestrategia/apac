using ApacKiosk.Database;
using ApacKiosk.Services;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AForge.Video.DirectShow;

namespace ApacKiosk.Monitoring;

public class CameraCaptureService : IDisposable
{
    private readonly DatabaseManager _db;
    private readonly LogService _logService;
    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private VideoCaptureDevice? _currentDevice;

    public CameraCaptureService(DatabaseManager db, LogService logService)
    {
        _db = db;
        _logService = logService;
    }

    public static List<(string Name, string Moniker)> GetDevices()
    {
        var devices = new List<(string Name, string Moniker)>();
        try
        {
            foreach (FilterInfo device in new FilterInfoCollection(FilterCategory.VideoInputDevice))
            {
                devices.Add((device.Name, device.MonikerString));
            }
        }
        catch { }
        return devices;
    }

    public void Start(int? userId)
    {
        Stop();
        if (!bool.TryParse(_db.GetSetting("camera_enabled", "false"), out var enabled) || !enabled)
            return;

        var interval = int.Parse(_db.GetSetting("camera_interval_sec", "120"));
        var quality = _db.GetSetting("camera_quality", "Medium");
        var path = _db.GetSetting("camera_path");

        var devices = GetDevices();
        if (devices.Count == 0)
        {
            _logService.Log(userId, "event", null, "Câmera não encontrada");
            return;
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var deviceMoniker = devices[0].Moniker;

        _captureTask = Task.Run(() => CameraLoop(token, userId, interval, quality, path, deviceMoniker), token);
    }

    private void CameraLoop(CancellationToken token, int? userId, int intervalSec, string quality, string basePath, string moniker)
    {
        try { Directory.CreateDirectory(basePath); } catch { return; }

        try
        {
            _currentDevice = new VideoCaptureDevice(moniker);
            _currentDevice.Start();

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var timestamp = DateTime.Now;
                    var username = userId.HasValue ? $"user{userId}" : "system";
                    var fileName = $"camera_{timestamp:yyyyMMdd_HHmmss}_{username}.jpg";
                    var filePath = Path.Combine(basePath, fileName);

                    var frameEvent = new ManualResetEvent(false);
                    Bitmap? frame = null;

                    _currentDevice.NewFrame += (s, e) =>
                    {
                        frame = (Bitmap)e.Frame.Clone();
                        frameEvent.Set();
                    };

                    if (frameEvent.WaitOne(5000) && frame != null)
                    {
                        long qualityLevel = quality switch
                        {
                            "High" => 90L,
                            "Low" => 40L,
                            _ => 70L
                        };

                        var jpegCodec = GetEncoder(ImageFormat.Jpeg);
                        var encoderParams = new EncoderParameters(1);
                        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, qualityLevel);
                        frame.Save(filePath, jpegCodec, encoderParams);

                        _logService.Log(userId, "camera", filePath, null);
                        frame.Dispose();
                    }

                    try { Task.Delay(intervalSec * 1000, token).Wait(token); }
                    catch { break; }
                }
                catch { break; }
            }
        }
        catch (Exception ex)
        {
            _logService.Log(userId, "event", null, $"Erro câmera: {ex.Message}");
        }
        finally
        {
            try { _currentDevice?.SignalToStop(); } catch { }
            try { _currentDevice?.WaitForStop(); } catch { }
            _currentDevice = null;
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
        try { _currentDevice?.SignalToStop(); } catch { }
    }
}
