using ApacKiosk.Interop;
using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace ApacKiosk.Security;

public class KioskProtection : IDisposable
{
    private readonly Form _targetForm;
    private System.Windows.Forms.Timer? _topMostTimer;
    private System.Windows.Forms.Timer? _registryTimer;
    private IntPtr _winEventHook;
    private readonly Win32.WinEventDelegate _winEventProc;

    public KioskProtection(Form targetForm)
    {
        _targetForm = targetForm;
        _winEventProc = WinEventCallback;
    }

    public void Activate()
    {
        _topMostTimer = new Timer(_ =>
        {
            _targetForm.BeginInvoke(() =>
            {
                var hWnd = _targetForm.Handle;
                Win32.SetWindowPos(hWnd, (IntPtr)Win32.HWND_TOPMOST,
                    0, 0, 0, 0,
                    Win32.SWP_NOSIZE | Win32.SWP_NOMOVE | Win32.SWP_SHOWWINDOW);
            });
        }, null, 0, 1000);

        _winEventHook = Win32.SetWinEventHook(
            Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _winEventProc,
            (uint)Process.GetCurrentProcess().Id, 0,
            Win32.WINEVENT_OUTOFCONTEXT);

        ProtectWorkingSet();

        _registryTimer = new Timer(_ => EnsureAutoStart(), null, 60000, 60000);
        EnsureAutoStart();
    }

    public void Deactivate()
    {
        _topMostTimer?.Dispose();
        _topMostTimer = null;
        _registryTimer?.Dispose();
        _registryTimer = null;
        if (_winEventHook != IntPtr.Zero)
        {
            Win32.UnhookWinEvent(_winEventHook);
            _winEventHook = IntPtr.Zero;
        }
    }

    private void WinEventCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (hwnd != _targetForm.Handle)
        {
            _targetForm.BeginInvoke(() =>
            {
                Win32.BringWindowToTop(_targetForm.Handle);
                Win32.SetFocus(_targetForm.Handle);
            });
        }
    }

    private void ProtectWorkingSet()
    {
        try
        {
            var proc = Process.GetCurrentProcess();
            Win32.SetProcessWorkingSetSize(proc.Handle, -1, -1);
        }
        catch { }
    }

    private void EnsureAutoStart()
    {
        try
        {
            var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null)
            {
                var value = key.GetValue("APAC_Kiosk")?.ToString();
                var exePath = Application.ExecutablePath;
                if (value != exePath)
                {
                    key.SetValue("APAC_Kiosk", exePath);
                }
                key.Close();
            }
        }
        catch { }
    }

    public void RemoveAutoStart()
    {
        try
        {
            var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null)
            {
                key.DeleteValue("APAC_Kiosk", false);
                key.Close();
            }
        }
        catch { }
    }

    public void Dispose()
    {
        Deactivate();
    }
}
