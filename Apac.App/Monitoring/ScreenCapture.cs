using System.Drawing.Imaging;

namespace Apac.App.Monitoring;

public class ScreenCapture : IDisposable
{
    private Timer? _timer;
    private readonly string _outputPath;
    private readonly int _intervalSeconds;
    private readonly string _quality;
    private readonly DatabaseManager _db;
    private int? _userId;
    private string? _username;

    public ScreenCapture(DatabaseManager db, string outputPath, int intervalSeconds, string quality)
    {
        _db = db;
        _outputPath = outputPath;
        _intervalSeconds = Math.Max(10, intervalSeconds);
        _quality = quality;
        Directory.CreateDirectory(_outputPath);
    }

    public void SetUser(int? userId, string? username)
    {
        _userId = userId;
        _username = username;
    }

    public void Start()
    {
        _timer = new Timer(Capture, null, 3000, _intervalSeconds * 1000);
    }

    private void Capture(object? state)
    {
        try
        {
            using var bmp = new Bitmap(Screen.PrimaryScreen!.Bounds.Width, Screen.PrimaryScreen.Bounds.Height);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(0, 0, 0, 0, bmp.Size);

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
            var fileName = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}_{userTag}.jpg";
            var filePath = Path.Combine(_outputPath, fileName);

            var encoder = GetEncoder(ImageFormat.Jpeg);
            if (encoder != null)
            {
                var encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, qualityLevel);
                bmp.Save(filePath, encoder, encoderParams);
            }
            else
            {
                bmp.Save(filePath, ImageFormat.Jpeg);
            }

            _db.InsertLog("screenshot", filePath, _userId, _username, null);
        }
        catch
        {
        }
    }

    private static ImageCodecInfo? GetEncoder(ImageFormat format)
    {
        return ImageCodecInfo.GetImageDecoders().FirstOrDefault(c => c.FormatID == format.Guid);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose() => Stop();
}
