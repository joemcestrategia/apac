using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using ApacKiosk.Database;

namespace ApacKiosk.Services
{
    public class SecurityManager : IDisposable
    {
        private readonly DatabaseManager _db;
        private Thread _watchdogThread;
        private Thread _windowProtectorThread;
        private volatile bool _isRunning;
        private IntPtr _mainWindowHandle;
        private bool _isKioskLocked = true;
        private readonly List<Process> _killedProcesses = new List<Process>();

        private static readonly string[] ProhibitedProcesses = new[]
        {
            "taskmgr", "regedit", "cmd", "powershell", "powershell_ise",
            "mmc", "msconfig", "gpedit", "eventvwr", "procexp", "procexp64",
            "procmon", "procmon64", "wireshark", "fiddler", "x64dbg",
            "ollydbg", "autoruns", "autoruns64", "msra", "compmgmt",
            "devmgmt", "diskmgmt", "services", "perfmon", "resmon",
            "rundll32", "wscript", "cscript", "mshta", "msiexec"
        };

        public SecurityManager(DatabaseManager db)
        {
            _db = db;
        }

        public void SetMainWindow(IntPtr handle)
        {
            _mainWindowHandle = handle;
        }

        public void EnableAllSecurity()
        {
            _isRunning = true;
            EnableHotkeyBlock();
            EnableWatchdog();
            EnableWindowProtector();
            EnableRegistryProtector();
        }

        public void DisableAllSecurity()
        {
            _isRunning = false;
            UnregisterHotKeys();
        }

        private void EnableHotkeyBlock()
        {
            var thread = new Thread(() =>
            {
                while (_isRunning)
                {
                    try
                    {
                        BlockWinKey();
                        BlockAltF4();
                        BlockAltTab();
                        BlockCtrlEsc();
                    }
                    catch { }
                    Thread.Sleep(200);
                }
            })
            { IsBackground = true, Name = "HotkeyBlocker" };
            thread.Start();
        }

        private void EnableWatchdog()
        {
            _watchdogThread = new Thread(() =>
            {
                while (_isRunning)
                {
                    try
                    {
                        foreach (var procName in ProhibitedProcesses)
                        {
                            var processes = Process.GetProcessesByName(procName);
                            foreach (var proc in processes)
                            {
                                try
                                {
                                    if (!proc.HasExited)
                                    {
                                        proc.Kill();
                                        _db.InsertLog(null, "system_event", null,
                                            $"Processo bloqueado: {procName} (PID {proc.Id})");
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                    Thread.Sleep(2000);
                }
            })
            { IsBackground = true, Name = "Watchdog" };
            _watchdogThread.Start();
        }

        private void EnableWindowProtector()
        {
            _windowProtectorThread = new Thread(() =>
            {
                while (_isRunning)
                {
                    try
                    {
                        if (_mainWindowHandle != IntPtr.Zero)
                        {
                            SetWindowPosTopMost(_mainWindowHandle);
                            var fgWnd = GetForegroundWindow();
                            if (fgWnd != _mainWindowHandle && fgWnd != IntPtr.Zero)
                            {
                                BringWindowToFront(_mainWindowHandle);
                                SetFocus(_mainWindowHandle);
                            }
                        }
                    }
                    catch { }
                    Thread.Sleep(500);
                }
            })
            { IsBackground = true, Name = "WindowProtector" };
            _windowProtectorThread.Start();
        }

        private void EnableRegistryProtector()
        {
            var thread = new Thread(() =>
            {
                var autoStartPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
                var appName = "APAC_Kiosk";
                var appPath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                while (_isRunning)
                {
                    try
                    {
                        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(autoStartPath, true);
                        if (key != null)
                        {
                            var existing = key.GetValue(appName)?.ToString();
                            if (existing != appPath)
                            {
                                key.SetValue(appName, appPath);
                                _db.InsertLog(null, "system_event", null, "Chave de autostart recriada automaticamente");
                            }
                        }
                    }
                    catch { }
                    Thread.Sleep(30000);
                }
            })
            { IsBackground = true, Name = "RegistryProtector" };
            thread.Start();
        }

        public void SetKioskLock(bool locked)
        {
            _isKioskLocked = locked;
        }

        #region Win32 P/Invoke

        [DllImport("user32.dll")]
        private static extern bool BlockInput(bool fBlockIt);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool BringWindowToFront(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 2;
        private const uint SWP_NOSIZE = 1;
        private const uint SWP_SHOWWINDOW = 0x40;

        private static void SetWindowPosTopMost(IntPtr hWnd)
        {
            SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern uint keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const byte VK_LWIN = 0x5B;
        private const byte VK_RWIN = 0x5C;
        private const byte VK_MENU = 0x12;
        private const byte VK_CONTROL = 0x11;
        private const byte VK_ESCAPE = 0x1B;
        private const byte VK_TAB = 0x09;
        private const byte VK_F4 = 0x73;
        private const byte VK_F11 = 0x7A;
        private const uint KEYEVENTF_KEYUP = 0x2;

        private void BlockWinKey()
        {
            if (_isKioskLocked && (GetAsyncKeyState(VK_LWIN) < 0 || GetAsyncKeyState(VK_RWIN) < 0))
            {
                keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                keybd_event(VK_RWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
        }

        private void BlockAltF4()
        {
            if (_isKioskLocked && GetAsyncKeyState(VK_MENU) < 0 && GetAsyncKeyState(VK_F4) < 0)
            {
                keybd_event(VK_F4, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
        }

        private void BlockAltTab()
        {
            if (_isKioskLocked && GetAsyncKeyState(VK_MENU) < 0 && GetAsyncKeyState(VK_TAB) < 0)
            {
                keybd_event(VK_TAB, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
        }

        private void BlockCtrlEsc()
        {
            if (_isKioskLocked && GetAsyncKeyState(VK_CONTROL) < 0 && GetAsyncKeyState(VK_ESCAPE) < 0)
            {
                keybd_event(VK_ESCAPE, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
        }

        private void UnregisterHotKeys()
        {
            _isKioskLocked = false;
        }

        #endregion

        public void Dispose()
        {
            _isRunning = false;
        }
    }
}
