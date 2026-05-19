using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Apac.App.Security;

public class HotkeyBlocker : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_HOTKEY = 0x0312;
    private const int MOD_ALT = 0x0001;
    private const int MOD_CONTROL = 0x0002;
    private const int MOD_WIN = 0x0008;
    private const int VK_TAB = 0x09;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_F4 = 0x73;
    private const int VK_F11 = 0x7A;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_F1 = 0x70;

    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc _proc;
    private readonly int[] _blockedHotkeyIds;
    private IntPtr _hwnd;
    private readonly DatabaseManager? _db;

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
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    public HotkeyBlocker(IntPtr hwnd, DatabaseManager? db = null)
    {
        _hwnd = hwnd;
        _db = db;
        _proc = HookCallback;
        _blockedHotkeyIds = new int[50];
        for (int i = 0; i < 50; i++) _blockedHotkeyIds[i] = i + 1;
    }

    public void Install()
    {
        using var curProc = Process.GetCurrentProcess();
        using var curMod = curProc.MainModule;
        if (curMod == null) return;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curMod.ModuleName), 0);

        RegisterHotKey(_hwnd, 1, MOD_ALT, VK_TAB);
        RegisterHotKey(_hwnd, 2, MOD_ALT, VK_ESCAPE);
        RegisterHotKey(_hwnd, 3, MOD_ALT, VK_F4);
        RegisterHotKey(_hwnd, 4, MOD_CONTROL, VK_ESCAPE);
        RegisterHotKey(_hwnd, 5, MOD_CONTROL | MOD_ALT, VK_TAB);
        RegisterHotKey(_hwnd, 6, MOD_WIN, (uint)Keys.LKeyCode);
        RegisterHotKey(_hwnd, 7, MOD_WIN, (uint)Keys.RKeyCode);
        RegisterHotKey(_hwnd, 8, MOD_WIN, (uint)Keys.D);
        RegisterHotKey(_hwnd, 9, MOD_WIN, (uint)Keys.E);
        RegisterHotKey(_hwnd, 10, MOD_WIN, (uint)Keys.R);
        RegisterHotKey(_hwnd, 11, MOD_WIN, (uint)Keys.L);
        RegisterHotKey(_hwnd, 12, MOD_CONTROL | MOD_WIN, VK_F4);
        RegisterHotKey(_hwnd, 13, 0, VK_F11);
        RegisterHotKey(_hwnd, 14, MOD_CONTROL | 0x0004, VK_ESCAPE);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
        {
            var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var vkCode = (int)kb.vkCode;
            var ctrl = (GetKeyState(VK_CONTROL) & 0x8000) != 0;
            var alt = (GetKeyState(VK_MENU) & 0x8000) != 0;
            var win = (GetKeyState(VK_LWIN) & 0x8000) != 0 || (GetKeyState(VK_RWIN) & 0x8000) != 0;
            var shift = (GetKeyState(VK_SHIFT) & 0x8000) != 0;

            var blocked = IsBlockedCombo(vkCode, ctrl, alt, win, shift);
            if (blocked)
            {
                _db?.InsertLog("hotkey_blocked", null, null, null,
                    $"Hotkey bloqueada: VK={vkCode} CTRL={ctrl} ALT={alt} WIN={win}");
                return (IntPtr)1;
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const int VK_SHIFT = 0x10;

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    private bool IsBlockedCombo(int vk, bool ctrl, bool alt, bool win, bool shift)
    {
        if (win) return true;
        if (ctrl && alt) return true;
        if (alt && vk == VK_TAB) return true;
        if (alt && vk == VK_F4) return true;
        if (ctrl && vk == VK_ESCAPE) return true;
        if (ctrl && shift && vk == VK_ESCAPE) return true;
        if (vk == VK_F4 && alt) return true;
        if (vk == VK_F11) return true;

        if (vk == VK_LWIN || vk == VK_RWIN) return true;

        return false;
    }

    public void Uninstall()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
        for (int i = 1; i <= 14; i++)
            UnregisterHotKey(_hwnd, i);
    }

    public void Dispose() => Uninstall();
}
