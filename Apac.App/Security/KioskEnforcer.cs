using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Apac.App.Security;

public class KioskEnforcer : IDisposable
{
    private const int HWND_TOPMOST = -1;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private IntPtr _hwnd;
    private System.Windows.Forms.Timer? _topmostTimer;
    private System.Windows.Forms.Timer? _focusTimer;
    private readonly DatabaseManager? _db;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, int hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    public KioskEnforcer(IntPtr hwnd, DatabaseManager? db = null)
    {
        _hwnd = hwnd;
        _db = db;
    }

    public void Start()
    {
        _topmostTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _topmostTimer.Tick += (s, e) =>
        {
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
        };
        _topmostTimer.Start();

        _focusTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _focusTimer.Tick += (s, e) =>
        {
            var fgw = GetForegroundWindow();
            if (fgw != _hwnd && fgw != IntPtr.Zero)
            {
                uint pid;
                GetWindowThreadProcessId(fgw, out pid);
                if (pid != Process.GetCurrentProcess().Id)
                {
                    _db?.InsertLog("window_blocked", null, null, null, "Outra janela tentou ganhar foco");
                }
                BringWindowToTop(_hwnd);
                SetFocus(_hwnd);
            }
        };
        _focusTimer.Start();
    }

    public void Stop()
    {
        _topmostTimer?.Stop();
        _topmostTimer?.Dispose();
        _focusTimer?.Stop();
        _focusTimer?.Dispose();
    }

    public void Dispose() => Stop();
}
