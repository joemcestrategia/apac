using ApacKiosk.Database;
using ApacKiosk.Utils;

namespace ApacKiosk.Services
{
    public class MonitorService
    {
        public ScreenCapture Screenshot { get; }
        public CameraCapture Camera { get; }
        public KeyLogger Keylog { get; }

        private int? _currentUserId;

        public MonitorService(ScreenCapture screenshot, CameraCapture camera, KeyLogger keylogger)
        {
            Screenshot = screenshot;
            Camera = camera;
            Keylog = keylogger;
        }

        public void SetCurrentUser(int? userId)
        {
            _currentUserId = userId;
            Screenshot.SetCurrentUser(userId);
            Camera.SetCurrentUser(userId);
            Keylog.SetCurrentUser(userId);
        }

        public void StartAll()
        {
            Screenshot.Start();
            Camera.Start();
            Keylog.Start();
        }

        public void StopAll()
        {
            Screenshot.Stop();
            Camera.Stop();
            Keylog.Stop();
        }
    }
}
