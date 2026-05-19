using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Apac.Database;

namespace Apac.Services
{
    public class SecurityManager : IDisposable
    {
        private static readonly List<string> BlockedProcesses = new List<string>
        {
            "taskmgr", "regedit", "cmd", "powershell", "powershell_ise",
            "mmc", "msconfig", "gpedit", "eventvwr", "procexp", "procexp64",
            "procmon", "wireshark", "fiddler", "x64dbg", "ollydbg", "autoruns",
            "resmon", "perfmon", "ComputerDefaults", "SystemPropertiesAdvanced",
            "SystemSettings", "ms-settings", "explorer"
        };

        private readonly Form _ownerForm;
        private readonly string _adminPassword;

        private System.Threading.Timer _keepOnTopTimer;
        private System.Threading.Timer _watchdogTimer;
        private System.Threading.Timer _registryCheckTimer;
        private IntPtr _winEventHook;
        private WinEventDelegate _winEventProc;
        private volatile bool _running;

        private int _hotkeyWinId, _hotkeyWinDId, _hotkeyWinRId, _hotkeyWinLId, _hotkeyWinEId;
        private int _hotkeyAltF4Id, _hotkeyAltTabId, _hotkeyAltEscId;
        private int _hotkeyCtrlEscId, _hotkeyCtrlShiftEscId, _hotkeyF11Id;

        public SecurityManager(Form ownerForm, string adminPassword)
        {
            _ownerForm = ownerForm;
            _adminPassword = adminPassword;
        }

        public void Start()
        {
            if (_running) return;
            _running = true;

            RegisterHotkeys();
            StartKeepOnTop();
            StartProcessWatchdog();
            StartRegistryCheck();
            StartForegroundHook();
            ProtectSelf();

            DatabaseManager.Instance.AddLogEntry("SystemEvent", null, null, "Camadas de segurança ativadas");
        }

        public void Stop()
        {
            _running = false;
            UnregisterHotkeys();
            _keepOnTopTimer?.Dispose();
            _watchdogTimer?.Dispose();
            _registryCheckTimer?.Dispose();

            if (_winEventHook != IntPtr.Zero)
            {
                UnhookWinEvent(_winEventHook);
                _winEventHook = IntPtr.Zero;
            }

            DatabaseManager.Instance.AddLogEntry("SystemEvent", null, null, "Camadas de segurança desativadas");
        }

        public bool RequestExit(string password)
        {
            if (password == _adminPassword)
            {
                return true;
            }
            DatabaseManager.Instance.AddLogEntry("SystemEvent", null, null, "Tentativa de fechamento com senha incorreta");
            return false;
        }

        private void RegisterHotkeys()
        {
            IntPtr hwnd = _ownerForm.Handle;
            _hotkeyWinId = RegisterHotKey(hwnd, 1001, MOD_WIN, 0);
            _hotkeyWinDId = RegisterHotKey(hwnd, 1002, MOD_WIN, (int)Keys.D);
            _hotkeyWinRId = RegisterHotKey(hwnd, 1003, MOD_WIN, (int)Keys.R);
            _hotkeyWinLId = RegisterHotKey(hwnd, 1004, MOD_WIN, (int)Keys.L);
            _hotkeyWinEId = RegisterHotKey(hwnd, 1005, MOD_WIN, (int)Keys.E);
            _hotkeyAltF4Id = RegisterHotKey(hwnd, 1006, MOD_ALT, (int)Keys.F4);
            _hotkeyAltTabId = RegisterHotKey(hwnd, 1007, MOD_ALT, (int)Keys.Tab);
            _hotkeyAltEscId = RegisterHotKey(hwnd, 1008, MOD_ALT, (int)Keys.Escape);
            _hotkeyCtrlEscId = RegisterHotKey(hwnd, 1009, MOD_CONTROL, (int)Keys.Escape);
            _hotkeyCtrlShiftEscId = RegisterHotKey(hwnd, 1010, MOD_CONTROL | MOD_SHIFT, (int)Keys.Escape);
            _hotkeyF11Id = RegisterHotKey(hwnd, 1011, MOD_NONE, (int)Keys.F11);
        }

        private void UnregisterHotkeys()
        {
            IntPtr hwnd = _ownerForm.Handle;
            if (_hotkeyWinId > 0) UnregisterHotKey(hwnd, 1001);
            if (_hotkeyWinDId > 0) UnregisterHotKey(hwnd, 1002);
            if (_hotkeyWinRId > 0) UnregisterHotKey(hwnd, 1003);
            if (_hotkeyWinLId > 0) UnregisterHotKey(hwnd, 1004);
            if (_hotkeyWinEId > 0) UnregisterHotKey(hwnd, 1005);
            if (_hotkeyAltF4Id > 0) UnregisterHotKey(hwnd, 1006);
            if (_hotkeyAltTabId > 0) UnregisterHotKey(hwnd, 1007);
            if (_hotkeyAltEscId > 0) UnregisterHotKey(hwnd, 1008);
            if (_hotkeyCtrlEscId > 0) UnregisterHotKey(hwnd, 1009);
            if (_hotkeyCtrlShiftEscId > 0) UnregisterHotKey(hwnd, 1010);
            if (_hotkeyF11Id > 0) UnregisterHotKey(hwnd, 1011);
        }

        private void StartKeepOnTop()
        {
            _keepOnTopTimer = new System.Threading.Timer(_ =>
            {
                if (!_running) return;
                try
                {
                    _ownerForm.Invoke(new Action(() =>
                    {
                        SetWindowPos(_ownerForm.Handle, HWND_TOPMOST, 0, 0, 0, 0,
                            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    }));
                }
                catch { }
            }, null, 0, 1000);
        }

        private void StartForegroundHook()
        {
            _winEventProc = new WinEventDelegate(WinEventProc);
            _winEventHook = SetWinEventHook(
                EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, _winEventProc, 0, 0,
                WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

            if (_winEventHook == IntPtr.Zero)
            {
                DatabaseManager.Instance.AddLogEntry("SystemEvent", null, null, "Falha ao instalar hook de foreground");
            }
        }

        private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (!_running) return;
            if (hwnd == _ownerForm.Handle) return;

            Thread.Sleep(50);
            BringWindowToTop(_ownerForm.Handle);
            SetForegroundWindow(_ownerForm.Handle);
        }

        private void StartProcessWatchdog()
        {
            _watchdogTimer = new System.Threading.Timer(_ =>
            {
                if (!_running) return;
                foreach (var processName in BlockedProcesses)
                {
                    try
                    {
                        var processes = Process.GetProcessesByName(processName);
                        foreach (var p in processes)
                        {
                            try
                            {
                                if (p.Id != Process.GetCurrentProcess().Id)
                                {
                                    p.Kill();
                                    p.WaitForExit(1000);
                                    DatabaseManager.Instance.AddLogEntry("BlockedProcess", null, null,
                                        $"Processo bloqueado: {processName}.exe (PID {p.Id})");
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }, null, 1000, 2000);
        }

        private void StartRegistryCheck()
        {
            _registryCheckTimer = new System.Threading.Timer(_ =>
            {
                if (!_running) return;
                EnsureAutoStart();
            }, null, 30000, 30000);
        }

        public static void EnsureAutoStart()
        {
            try
            {
                string enabled = DatabaseManager.Instance.GetSetting("autostart", "false");
                if (enabled != "true") return;

                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key == null) return;
                    string exePath = Application.ExecutablePath;
                    string currentValue = key.GetValue("APAC") as string;
                    if (currentValue != exePath)
                    {
                        key.SetValue("APAC", exePath);
                    }
                }
            }
            catch { }
        }

        public static void RemoveAutoStart()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        key.DeleteValue("APAC", false);
                    }
                }
            }
            catch { }
        }

        private void ProtectSelf()
        {
            try
            {
                var currentProcess = Process.GetCurrentProcess();
                currentProcess.PriorityClass = ProcessPriorityClass.High;
                SetProcessWorkingSetSize(currentProcess.Handle, -1, -1);
            }
            catch { }
        }

        public void Dispose()
        {
            Stop();
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, int vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("kernel32.dll")]
        private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, int dwMinimumWorkingSetSize, int dwMaximumWorkingSetSize);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint MOD_NONE = 0x0000;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

        private enum Keys
        {
            D = 0x44, R = 0x52, L = 0x4C, E = 0x45,
            F4 = 0x73, Tab = 0x09, Escape = 0x1B, F11 = 0x7A
        }
    }
}
