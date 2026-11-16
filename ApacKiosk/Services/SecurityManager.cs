using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using ApacKiosk.Database;
using ApacKiosk.Interop;

namespace ApacKiosk.Services
{
    public class SecurityManager : IDisposable
    {
        private readonly DatabaseManager _db;
        private Thread _watchdogThread;
        private Thread _windowProtectorThread;
        private Thread _registryProtectorThread;
        private Thread _hookThread;
        private volatile bool _isRunning;
        private volatile bool _isHookRunning;
        private volatile bool _windowProtectorPaused;
        private IntPtr _mainWindowHandle;
        private IntPtr _hookHandle = IntPtr.Zero;
        private uint _hookThreadId;
        private NativeMethods.LowLevelKeyboardProc _hookProc;
        private bool _isKioskLocked = true;
        private readonly List<Process> _killedProcesses = new List<Process>();
        private readonly HashSet<int> _allowedProcessIds = new HashSet<int>();
        private readonly HashSet<string> _allowedProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _lockAllowedPids = new object();

        private const int LLKHF_ALTDOWN = 0x20;

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
            HideTaskbar();
            if (_isRunning) return;
            _isRunning = true;
            EnableHotkeyBlock();
            EnableWatchdog();
            EnableWindowProtector();
            EnableRegistryProtector();
            SetupCtrlAltDelPolicy();
        }

        public void ForceHideTaskbar() => HideTaskbar();

        public void DisableAllSecurity()
        {
            _isRunning = false;
            RestoreTaskbar();
            StopHook();
        }

        public void ShutdownPreservingLock()
        {
            _isRunning = false;
            StopHook();
        }

        private void EnableHotkeyBlock()
        {
            if (_isHookRunning) return;
            _isHookRunning = true;
            _hookProc = HookCallback;
            _hookThread = new Thread(() =>
            {
                try
                {
                    _hookThreadId = GetCurrentThreadId();
                    _hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _hookProc,
                        NativeMethods.GetModuleHandle(null), 0);
                    System.Windows.Forms.Application.Run();
                }
                catch { }
                finally
                {
                    if (_hookHandle != IntPtr.Zero)
                    {
                        NativeMethods.UnhookWindowsHookEx(_hookHandle);
                        _hookHandle = IntPtr.Zero;
                    }
                    _hookThreadId = 0;
                    _isHookRunning = false;
                }
            })
            { IsBackground = true, Name = "KeyboardHook" };
            _hookThread.SetApartmentState(ApartmentState.STA);
            _hookThread.Start();
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _isKioskLocked)
            {
                var hookStruct = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                var vkCode = (int)hookStruct.vkCode;
                var flags = hookStruct.flags;
                var isAltDown = (flags & LLKHF_ALTDOWN) != 0;
                var msg = wParam.ToInt32();

                if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN ||
                    msg == WM_KEYUP || msg == WM_SYSKEYUP)
                {
                    if (vkCode == NativeMethods.VK_LWIN || vkCode == NativeMethods.VK_RWIN)
                        return (IntPtr)1;
                }

                if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
                {
                    if (vkCode == NativeMethods.VK_TAB && isAltDown)
                        return (IntPtr)1;

                    if (vkCode == NativeMethods.VK_ESCAPE && isAltDown)
                        return (IntPtr)1;

                    if (vkCode == NativeMethods.VK_ESCAPE && (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0)
                        return (IntPtr)1;

                    if (vkCode == NativeMethods.VK_F4 && isAltDown)
                        return (IntPtr)1;
                }
            }
            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        private void StopHook()
        {
            _isHookRunning = false;
            if (_hookHandle != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }
            if (_hookThreadId != 0)
            {
                PostThreadMessage(_hookThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
                _hookThreadId = 0;
            }
        }

        private void SetupCtrlAltDelPolicy()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Policies\System");
                key?.SetValue("DisableTaskMgr", 1, Microsoft.Win32.RegistryValueKind.DWord);

                using var explorerKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer");
                explorerKey?.SetValue("NoClose", 1, Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch { }
        }

        private void EnableWatchdog()
        {
            if (_watchdogThread != null && _watchdogThread.IsAlive) return;
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
                                        lock (_lockAllowedPids)
                                        {
                                            if (_allowedProcessIds.Contains(proc.Id)) continue;
                                        }
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
            if (_windowProtectorThread != null && _windowProtectorThread.IsAlive) return;
            _windowProtectorThread = new Thread(() =>
            {
                while (_isRunning)
                {
                    try
                    {
                        if (_isKioskLocked && !_windowProtectorPaused)
                        {
                            EnforceHiddenTaskbars();

                            if (_mainWindowHandle != IntPtr.Zero)
                            {
                                if (!IsIconic(_mainWindowHandle))
                                {
                                    SetWindowPosTopMost(_mainWindowHandle);
                                }

                                var fgWnd = GetForegroundWindow();
                                if (fgWnd != _mainWindowHandle && fgWnd != IntPtr.Zero && !IsAllowedForegroundWindow(fgWnd))
                                {
                                    SuppressStartMenuWindowIfNeeded(fgWnd);

                                    if (IsIconic(_mainWindowHandle))
                                        ShowWindow(_mainWindowHandle, SW_RESTORE);
                                    SetWindowPosTopMost(_mainWindowHandle);
                                    ForceWindowForeground(_mainWindowHandle);
                                }
                            }
                        }
                    }
                    catch { }
                    Thread.Sleep(150);
                }
            })
            { IsBackground = true, Name = "WindowProtector" };
            _windowProtectorThread.Start();
        }

        private bool IsAllowedForegroundWindow(IntPtr hWnd)
        {
            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == (uint)Environment.ProcessId) return true;

            lock (_lockAllowedPids)
            {
                if (_allowedProcessIds.Count == 0 && _allowedProcessNames.Count == 0) return false;
                if (_allowedProcessIds.Contains((int)pid)) return true;
            }

            string procName = null;
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                procName = proc.ProcessName;
            }
            catch { }

            if (!string.IsNullOrEmpty(procName))
            {
                lock (_lockAllowedPids)
                {
                    if (_allowedProcessNames.Contains(procName))
                    {
                        _allowedProcessIds.Add((int)pid);
                        return true;
                    }
                }
            }

            return false;
        }

        private void EnableRegistryProtector()
        {
            if (_registryProtectorThread != null && _registryProtectorThread.IsAlive) return;
            _registryProtectorThread = new Thread(() =>
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
            _registryProtectorThread.Start();
        }

        public void PauseWindowProtector()
        {
            _windowProtectorPaused = true;
        }

        public void ResumeWindowProtector()
        {
            _windowProtectorPaused = false;
        }

        public void RegisterAllowedProcess(int pid)
        {
            lock (_lockAllowedPids)
            {
                _allowedProcessIds.Add(pid);
            }
        }

        public void RegisterAllowedProcessName(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return;
            lock (_lockAllowedPids)
            {
                _allowedProcessNames.Add(processName);
            }
        }

        public void UnregisterAllowedProcess(int pid)
        {
            lock (_lockAllowedPids)
            {
                _allowedProcessIds.Remove(pid);
            }
        }

        public void UnregisterAllowedProcessName(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return;
            lock (_lockAllowedPids)
            {
                _allowedProcessNames.Remove(processName);
            }
        }

        public void SetKioskLock(bool locked)
        {
            _isKioskLocked = locked;
        }

        public bool IsShellReplacement()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon");
                var shell = key?.GetValue("Shell") as string;
                return shell != null && shell.Contains("ApacKiosk", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public void SetupShellReplacement(string kioskExePath)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon");
                key.SetValue("Shell", kioskExePath, Microsoft.Win32.RegistryValueKind.String);
            }
            catch { }
        }

        public void RemoveShellReplacement()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", writable: true);
                if (key != null)
                {
                    key.DeleteValue("Shell", throwOnMissingValue: false);
                }
            }
            catch { }
        }

        private void HideTaskbar()
        {
            try
            {
                EnumWindows((hwnd, _) =>
                {
                    var cls = GetWindowClassName(hwnd);
                    if (cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd")
                        ShowWindow(hwnd, SW_HIDE);
                    return true;
                }, IntPtr.Zero);

                var taskbar = FindWindow("Shell_TrayWnd", null);
                if (taskbar != IntPtr.Zero)
                    ShowWindow(taskbar, SW_HIDE);

                var startButton = FindWindow("Button", "Start");
                if (startButton == IntPtr.Zero)
                    startButton = FindWindow("Start", null);
                if (startButton != IntPtr.Zero)
                    ShowWindow(startButton, SW_HIDE);

                HideDesktopIcons();
            }
            catch { }
        }

        private void EnforceHiddenTaskbars()
        {
            try
            {
                EnumWindows((hwnd, _) =>
                {
                    if (!IsWindowVisible(hwnd)) return true;
                    var cls = GetWindowClassName(hwnd);
                    if (cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd" || IsStartOrSearchProcessWindow(hwnd))
                    {
                        ShowWindow(hwnd, SW_HIDE);
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
        }

        private void SuppressStartMenuWindowIfNeeded(IntPtr fgWnd)
        {
            try
            {
                var cls = GetWindowClassName(fgWnd);
                if (cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd" || IsStartOrSearchProcessWindow(fgWnd))
                {
                    ShowWindow(fgWnd, SW_HIDE);
                }
            }
            catch { }
        }

        private static bool IsStartOrSearchProcessWindow(IntPtr hwnd)
        {
            try
            {
                NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == 0) return false;
                using var proc = Process.GetProcessById((int)pid);
                var name = proc.ProcessName;
                return name.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase) ||
                       name.Equals("SearchHost", StringComparison.OrdinalIgnoreCase) ||
                       name.Equals("SearchUI", StringComparison.OrdinalIgnoreCase) ||
                       name.Equals("SearchApp", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void ForceWindowForeground(IntPtr hWnd)
        {
            try
            {
                var fg = GetForegroundWindow();
                uint fgThread = fg != IntPtr.Zero ? NativeMethods.GetWindowThreadProcessId(fg, out _) : 0;
                uint curThread = GetCurrentThreadId();
                bool attached = fgThread != 0 && fgThread != curThread && AttachThreadInput(curThread, fgThread, true);
                try
                {
                    BringWindowToFront(hWnd);
                    SetForegroundWindow(hWnd);
                    SetFocus(hWnd);
                }
                finally
                {
                    if (attached) AttachThreadInput(curThread, fgThread, false);
                }
            }
            catch
            {
                BringWindowToFront(hWnd);
                SetFocus(hWnd);
            }
        }

        private static string GetWindowClassName(IntPtr hwnd)
        {
            var sb = new StringBuilder(128);
            GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private void RestoreTaskbar()
        {
            try
            {
                EnumWindows((hwnd, _) =>
                {
                    var cls = GetWindowClassName(hwnd);
                    if (cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd")
                        ShowWindow(hwnd, SW_SHOW);
                    return true;
                }, IntPtr.Zero);

                var taskbar = FindWindow("Shell_TrayWnd", null);
                if (taskbar != IntPtr.Zero)
                    ShowWindow(taskbar, SW_SHOW);

                ShowDesktopIcons();
            }
            catch { }
        }

        private void HideDesktopIcons()
        {
            try
            {
                var progman = FindWindow("Progman", null);
                if (progman != IntPtr.Zero)
                {
                    var defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (defView != IntPtr.Zero)
                    {
                        var listView = FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
                        if (listView != IntPtr.Zero)
                            ShowWindow(listView, SW_HIDE);
                    }
                }

                var workerW = IntPtr.Zero;
                while ((workerW = FindWindowEx(IntPtr.Zero, workerW, "WorkerW", null)) != IntPtr.Zero)
                {
                    var defView2 = FindWindowEx(workerW, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (defView2 != IntPtr.Zero)
                    {
                        var listView2 = FindWindowEx(defView2, IntPtr.Zero, "SysListView32", null);
                        if (listView2 != IntPtr.Zero)
                            ShowWindow(listView2, SW_HIDE);
                    }
                }
            }
            catch { }
        }

        private void ShowDesktopIcons()
        {
            try
            {
                var progman = FindWindow("Progman", null);
                if (progman != IntPtr.Zero)
                {
                    var defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (defView != IntPtr.Zero)
                    {
                        var listView = FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
                        if (listView != IntPtr.Zero)
                            ShowWindow(listView, SW_SHOW);
                    }
                }

                var workerW = IntPtr.Zero;
                while ((workerW = FindWindowEx(IntPtr.Zero, workerW, "WorkerW", null)) != IntPtr.Zero)
                {
                    var defView2 = FindWindowEx(workerW, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (defView2 != IntPtr.Zero)
                    {
                        var listView2 = FindWindowEx(defView2, IntPtr.Zero, "SysListView32", null);
                        if (listView2 != IntPtr.Zero)
                            ShowWindow(listView2, SW_SHOW);
                    }
                }
            }
            catch { }
        }

        #region Win32 P/Invoke

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool BringWindowToFront(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string lpszClass, string lpszWindow);

        private const int SW_RESTORE = 9;
        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;
        private const uint WM_QUIT = 0x0012;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYUP = 0x0105;

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 2;
        private const uint SWP_NOSIZE = 1;
        private const uint SWP_SHOWWINDOW = 0x40;

        private static void SetWindowPosTopMost(IntPtr hWnd)
        {
            SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
        }

        #endregion

        public void Dispose()
        {
            _isRunning = false;
            RestoreTaskbar();
            StopHook();
        }
    }
}
