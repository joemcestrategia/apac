using System.Runtime.InteropServices;
using System.Diagnostics;

namespace ApacKiosk.Services;

public class SecurityService : IDisposable
{
    private readonly Form _mainForm;
    private CancellationTokenSource? _cts;
    private Task? _watchdogTask;
    private IntPtr _foregroundHook;

    private const uint WM_HOTKEY = 0x0312;
    private const int HWND_TOPMOST = -1;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    private static readonly string[] ForbiddenProcesses = new[]
    {
        "taskmgr", "regedit", "cmd", "powershell", "powershell_ise",
        "mmc", "msconfig", "gpedit", "eventvwr", "procexp", "procmon",
        "wireshark", "fiddler", "x64dbg", "ollydbg", "autoruns"
    };

    private readonly HashSet<int> _blockedKeys = new()
    {
        0x5B, // Left Win
        0x5C, // Right Win
        0x6B, // F11
    };

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, int dwMinimumWorkingSetSize, int dwMaximumWorkingSetSize);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern ushort GetAsyncKeyState(int vKey);

    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    private WinEventDelegate? _eventDelegate;

    public SecurityService(Form mainForm)
    {
        _mainForm = mainForm;
        _eventDelegate = ForegroundEventProc;
    }

    public async Task StartAsync()
    {
        _cts = new CancellationTokenSource();

        RegisterBlockedHotKeys();
        RegisterForegroundHook();
        ProtectOwnProcess();

        _watchdogTask = WatchdogLoopAsync(_cts.Token);
        await Task.CompletedTask;
    }

    public void Stop()
    {
        _cts?.Cancel();
        UnregisterBlockedHotKeys();
        UnregisterForegroundHook();
    }

    private void RegisterBlockedHotKeys()
    {
        IntPtr handle = _mainForm.Handle;

        RegisterHotKey(handle, 1, 0x0008, 0x5B);
        RegisterHotKey(handle, 2, 0x0008, 0x44);
        RegisterHotKey(handle, 3, 0x0008, 0x52);
        RegisterHotKey(handle, 4, 0x0008, 0x4C);
        RegisterHotKey(handle, 5, 0x0008, 0x45);
        RegisterHotKey(handle, 6, 0x0000, 0x12, unchecked((uint)Keys.Alt));
        RegisterHotKey(handle, 7, 0x0000, 0x73, unchecked((uint)Keys.Alt));
        RegisterHotKey(handle, 8, 0x0001, 0x09, unchecked((uint)Keys.Alt));
        RegisterHotKey(handle, 9, 0x0001, 0x1B, unchecked((uint)Keys.Alt));
        RegisterHotKey(handle, 10, 0x0002, 0x1B, unchecked((uint)Keys.Ctrl));
        RegisterHotKey(handle, 11, 0x0006, 0x1B, unchecked((uint)Keys.Ctrl | (uint)Keys.Shift));
        RegisterHotKey(handle, 12, 0x0002, 0x2E, unchecked((uint)Keys.Ctrl | (uint)Keys.Alt));
        RegisterHotKey(handle, 13, 0x0000, 0x7A, 0);
    }

    private void UnregisterBlockedHotKeys()
    {
        IntPtr handle = _mainForm.Handle;
        for (int i = 1; i <= 13; i++)
            UnregisterHotKey(handle, i);
    }

    private void RegisterForegroundHook()
    {
        var currentThread = (uint)Environment.CurrentManagedThreadId;
        _foregroundHook = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _eventDelegate!, 0, currentThread, WINEVENT_OUTOFCONTEXT);
    }

    private void UnregisterForegroundHook()
    {
        if (_foregroundHook != IntPtr.Zero)
        {
            UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }
    }

    private void ForegroundEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (hwnd != _mainForm.Handle && hwnd != IntPtr.Zero)
        {
            _mainForm.BeginInvoke(() =>
            {
                SetWindowPos(_mainForm.Handle, (IntPtr)HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
                BringWindowToTop(_mainForm.Handle);
                SetFocus(_mainForm.Handle);
            });
        }
    }

    public void ForceToFront()
    {
        SetWindowPos(_mainForm.Handle, (IntPtr)HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
        BringWindowToTop(_mainForm.Handle);
    }

    private void ProtectOwnProcess()
    {
        try
        {
            var handle = OpenProcess(0x1F0FFF, false, Environment.ProcessId);
            if (handle != IntPtr.Zero)
            {
                SetProcessWorkingSetSize(handle, -1, -1);
                CloseHandle(handle);
            }
        }
        catch { }
    }

    private async Task WatchdogLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { KillForbiddenProcesses(); } catch { }
            try { ForceToFront(); } catch { }
            try { CheckRegistryAutostart(); } catch { }
            try { SuppressKeyPresses(); } catch { }
            await Task.Delay(2000, ct);
        }
    }

    private void KillForbiddenProcesses()
    {
        foreach (var name in ForbiddenProcesses)
        {
            var procs = Process.GetProcessesByName(name);
            foreach (var proc in procs)
            {
                try
                {
                    proc.Kill();
                    Data.DatabaseHelper.InsertLog("system_event", null, null, $"Processo bloqueado: {name}");
                }
                catch { }
            }
        }
    }

    private void CheckRegistryAutostart()
    {
        try
        {
            var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key != null)
            {
                var existing = key.GetValue("ApacKiosk") as string;
                var exePath = Application.ExecutablePath;
                if (existing == null || existing != exePath)
                {
                    key.SetValue("ApacKiosk", exePath);
                }
                key.Close();
            }
        }
        catch { }
    }

    private void SuppressKeyPresses()
    {
        foreach (var key in _blockedKeys)
        {
            if ((GetAsyncKeyState(key) & 0x8000) != 0)
                continue;
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
