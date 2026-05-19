using ApacKiosk.Interop;
using System;
using System.Windows.Forms;

namespace ApacKiosk.Security;

public class HotkeyBlocker : IDisposable
{
    private readonly Form _targetForm;
    private IntPtr _keyboardHookId;
    private IntPtr _mouseHookId;
    private readonly Win32.LowLevelHookProc _keyboardProc;
    private readonly Win32.LowLevelHookProc _mouseProc;

    private static readonly int[] HotkeyIds = { 9001, 9002, 9003, 9004, 9005, 9006, 9007, 9008, 9009, 9010, 9011, 9012 };

    public HotkeyBlocker(Form targetForm)
    {
        _targetForm = targetForm;
        _keyboardProc = KeyboardHookCallback;
        _mouseProc = MouseHookCallback;
    }

    public void Activate()
    {
        var hWnd = _targetForm.Handle;
        int idx = 0;

        Win32.RegisterHotKey(hWnd, HotkeyIds[idx++], Win32.MOD_WIN, (uint)Keys.D);
        Win32.RegisterHotKey(hWnd, HotkeyIds[idx++], Win32.MOD_WIN, (uint)Keys.R);
        Win32.RegisterHotKey(hWnd, HotkeyIds[idx++], Win32.MOD_WIN, (uint)Keys.L);
        Win32.RegisterHotKey(hWnd, HotkeyIds[idx++], Win32.MOD_WIN, (uint)Keys.E);
        Win32.RegisterHotKey(hWnd, HotkeyIds[idx++], Win32.MOD_WIN, (uint)Keys.LWin);
        Win32.RegisterHotKey(hWnd, HotkeyIds[idx++], Win32.MOD_WIN, (uint)Keys.RWin);

        Win32.RegisterHotKey(hWnd, HotkeyIds[idx++], Win32.MOD_ALT, (uint)Keys.F4);
        Win32.RegisterHotKey(hWnd, HotkeyIds[idx++], Win32.MOD_ALT, (uint)Keys.Tab);
        Win32.RegisterHotKey(hWnd, HotkeyIds[idx++], Win32.MOD_ALT, (uint)Keys.Escape);
        Win32.RegisterHotKey(hWnd, HotkeyIds[idx++], Win32.MOD_CONTROL, (uint)Keys.Escape);
        Win32.RegisterHotKey(hWnd, HotkeyIds[idx++], Win32.MOD_CONTROL | Win32.MOD_SHIFT, (uint)Keys.Escape);
        Win32.RegisterHotKey(hWnd, HotkeyIds[idx++], (uint)Keys.F11);

        using var process = System.Diagnostics.Process.GetCurrentProcess();
        using var module = process.MainModule;
        var moduleHandle = Win32.GetModuleHandle(module?.ModuleName ?? "");

        _keyboardHookId = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);
        _mouseHookId = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _mouseProc, moduleHandle, 0);
    }

    public void Deactivate()
    {
        if (_keyboardHookId != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_keyboardHookId);
            _keyboardHookId = IntPtr.Zero;
        }
        if (_mouseHookId != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_mouseHookId);
            _mouseHookId = IntPtr.Zero;
        }
        var hWnd = _targetForm.Handle;
        foreach (var id in HotkeyIds)
        {
            Win32.UnregisterHotKey(hWnd, id);
        }
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var kb = (Win32.KBDLLHOOKSTRUCT)System.Runtime.InteropServices.Marshal.PtrToStructure(lParam, typeof(Win32.KBDLLHOOKSTRUCT))!;
            var vkCode = (int)kb.vkCode;

            if ((int)wParam == Win32.WM_KEYDOWN || (int)wParam == Win32.WM_SYSKEYDOWN)
            {
                bool ctrl = (Win32.GetAsyncKeyState((int)Keys.ControlKey) & 0x8000) != 0;
                bool alt = (Win32.GetAsyncKeyState((int)Keys.Menu) & 0x8000) != 0;
                bool shift = (Win32.GetAsyncKeyState((int)Keys.ShiftKey) & 0x8000) != 0;
                bool win = (Win32.GetAsyncKeyState((int)Keys.LWin) & 0x8000) != 0 ||
                           (Win32.GetAsyncKeyState((int)Keys.RWin) & 0x8000) != 0;

                if (vkCode == (int)Keys.LWin || vkCode == (int)Keys.RWin) return (IntPtr)1;
                if (win && (vkCode == (int)Keys.D || vkCode == (int)Keys.R || vkCode == (int)Keys.L ||
                    vkCode == (int)Keys.E || vkCode == (int)Keys.M)) return (IntPtr)1;
                if (alt && vkCode == (int)Keys.F4) return (IntPtr)1;
                if (alt && vkCode == (int)Keys.Tab) return (IntPtr)1;
                if (alt && vkCode == (int)Keys.Escape) return (IntPtr)1;
                if (ctrl && vkCode == (int)Keys.Escape) return (IntPtr)1;
                if (ctrl && shift && vkCode == (int)Keys.Escape) return (IntPtr)1;
                if (vkCode == (int)Keys.F11) return (IntPtr)1;
                if (ctrl && alt && vkCode == (int)Keys.Delete) return (IntPtr)1;
            }
        }
        return Win32.CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        return Win32.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        Deactivate();
    }
}
