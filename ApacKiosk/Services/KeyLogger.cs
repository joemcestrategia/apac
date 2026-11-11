using System;
using System.Collections.Generic;
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
        private string _currentUsername;
        private string _sessionTimestamp;
        private string _keylogDir;
        private readonly StringBuilder _keyBuffer = new StringBuilder();
        private readonly StringBuilder _suspiciousBuffer = new StringBuilder();
        private string _currentLogFile;
        private string _currentSuspiciousFile;
        private DateTime _currentLogDate;

        private readonly StringBuilder _charGroup = new StringBuilder();
        private DateTime _groupFirstTime;
        private DateTime _groupLastTime;
        private readonly object _groupLock = new object();
        private System.Threading.Timer _flushTimer;
        private const int GROUP_TIMEOUT_MS = 2000;

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

        public void SetSessionInfo(int userId, string username, string sessionTimestamp)
        {
            FlushBuffer();
            FlushSuspiciousBuffer();
            _currentUserId = userId;
            _currentUsername = SanitizeName(username);
            _sessionTimestamp = sessionTimestamp;
            _currentLogDate = DateTime.MinValue;
            _currentLogFile = null;
            _currentSuspiciousFile = null;
            EnsureLogFile();
            EnsureSuspiciousFile();
        }

        public void SetCurrentUser(int? userId)
        {
            FlushCharGroup();
            FlushBuffer();
            FlushSuspiciousBuffer();
            _currentUserId = userId;
            EnsureLogFile();
            EnsureSuspiciousFile();
        }

        public void Start()
        {
            if (_isRunning) return;
            _isRunning = true;
            EnsureLogFile();
            EnsureSuspiciousFile();
            _flushTimer = new System.Threading.Timer(OnFlushTimer, null, GROUP_TIMEOUT_MS, GROUP_TIMEOUT_MS);
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
            _flushTimer?.Dispose();
            _flushTimer = null;
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
            FlushCharGroup();
            FlushBuffer();
            FlushSuspiciousBuffer();
            _thread?.Join(3000);
        }

        private void RunHookLoop()
        {
            _hookId = SetHook(_proc);
            System.Windows.Forms.Application.Run();
        }

        private string BuildSessionDir()
        {
            var userLabel = _currentUsername ?? "system";
            var dateLabel = _sessionTimestamp ?? DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            return Path.Combine(_config.BaseLogPath, userLabel, dateLabel, "teclado");
        }

        private void EnsureLogFile()
        {
            if (_currentLogFile == null)
            {
                _keylogDir = BuildSessionDir();
                Directory.CreateDirectory(_keylogDir);
                _currentLogDate = DateTime.Now;
                _currentLogFile = Path.Combine(_keylogDir, "keylog.txt");
            }
        }

        private void EnsureSuspiciousFile()
        {
            if (_currentSuspiciousFile == null)
            {
                var dir = BuildSessionDir();
                Directory.CreateDirectory(dir);
                _currentSuspiciousFile = Path.Combine(dir, "suspicious_activity.txt");
            }
        }

        private void OnFlushTimer(object state)
        {
            lock (_groupLock)
            {
                if (_charGroup.Length > 0 && (DateTime.Now - _groupLastTime).TotalMilliseconds >= GROUP_TIMEOUT_MS)
                    FlushCharGroup();
            }
        }

        private void FlushCharGroup()
        {
            lock (_groupLock)
            {
                if (_charGroup.Length == 0) return;

                var userLabel = _currentUserId?.ToString() ?? "system";
                var startTs = _groupFirstTime.ToString("yyyy-MM-dd HH:mm:ss");
                var endTs = _groupLastTime.ToString("yyyy-MM-dd HH:mm:ss");
                var count = _charGroup.Length;

                if (startTs == endTs)
                    _keyBuffer.AppendLine($"[{startTs}] {userLabel}: \"{_charGroup}\" ({count} teclas)");
                else
                    _keyBuffer.AppendLine($"[{startTs} - {endTs}] {userLabel}: \"{_charGroup}\" ({count} teclas)");

                _charGroup.Clear();
                if (_keyBuffer.Length > 4096)
                    FlushBuffer();
            }
        }

        private void AppendSuspicious(string code, string description)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var userLabel = _currentUserId?.ToString() ?? "system";
            _suspiciousBuffer.AppendLine($"[{timestamp}] {userLabel} | {code} | {description}");
            if (_suspiciousBuffer.Length > 2048)
                FlushSuspiciousBuffer();
        }

        private void FlushBuffer()
        {
            if (_keyBuffer.Length == 0) return;
            try
            {
                if (_currentLogFile != null)
                    File.AppendAllText(_currentLogFile, _keyBuffer.ToString());
                _keyBuffer.Clear();
            }
            catch { }
        }

        private void FlushSuspiciousBuffer()
        {
            if (_suspiciousBuffer.Length == 0) return;
            try
            {
                if (_currentSuspiciousFile != null)
                    File.AppendAllText(_currentSuspiciousFile, _suspiciousBuffer.ToString());
                _suspiciousBuffer.Clear();
            }
            catch { }
        }

        private static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "anon";
            return string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
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
                    EnsureSuspiciousFile();
                    int vkCode = Marshal.ReadInt32(lParam);
                    var isSpecial = IsSpecialKey(vkCode);
                    var modifiers = GetModifiers();
                    var hasSuspiciousMod = HasSuspiciousModifiers(modifiers);

                    if (hasSuspiciousMod)
                        DetectSuspicious(vkCode, modifiers);

                    if (isSpecial || hasSuspiciousMod)
                    {
                        FlushCharGroup();

                        var userLabel = _currentUserId?.ToString() ?? "system";
                        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        var key = MapVirtualKey(vkCode);
                        _keyBuffer.AppendLine($"[{timestamp}] {userLabel}: {key}");

                        if (_keyBuffer.Length > 4096)
                            FlushBuffer();
                    }
                    else
                    {
                        lock (_groupLock)
                        {
                            var now = DateTime.Now;
                            if (_charGroup.Length == 0)
                            {
                                _groupFirstTime = now;
                                _groupLastTime = now;
                            }
                            else if ((now - _groupLastTime).TotalMilliseconds >= GROUP_TIMEOUT_MS)
                            {
                                var saved = _charGroup.ToString();
                                _charGroup.Clear();
                                FlushCharGroupInline(saved, _groupFirstTime, _groupLastTime);
                                _groupFirstTime = now;
                                _groupLastTime = now;
                            }
                            else
                            {
                                _groupLastTime = now;
                            }

                            var ch = VkToChar(vkCode);
                            _charGroup.Append(ch);
                        }
                    }
                }
                catch { }
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private void FlushCharGroupInline(string saved, DateTime firstTime, DateTime lastTime)
        {
            var userLabel = _currentUserId?.ToString() ?? "system";
            var startTs = firstTime.ToString("yyyy-MM-dd HH:mm:ss");
            var endTs = lastTime.ToString("yyyy-MM-dd HH:mm:ss");
            var count = saved.Length;
            if (startTs == endTs)
                _keyBuffer.AppendLine($"[{startTs}] {userLabel}: \"{saved}\" ({count} teclas)");
            else
                _keyBuffer.AppendLine($"[{startTs} - {endTs}] {userLabel}: \"{saved}\" ({count} teclas)");
            if (_keyBuffer.Length > 4096)
                FlushBuffer();
        }

        private static bool IsSpecialKey(int vkCode)
        {
            return vkCode switch
            {
                0x08 => true, 0x09 => true, 0x0D => true, 0x1B => true, 0x20 => true,
                0x21 => true, 0x22 => true, 0x23 => true, 0x24 => true,
                0x25 => true, 0x26 => true, 0x27 => true, 0x28 => true,
                0x2D => true, 0x2E => true,
                >= 0x70 and <= 0x7B => true,
                _ => false
            };
        }

        private static char VkToChar(int vkCode)
        {
            var shift = (GetKeyState(0x10) & 0x8000) != 0;
            var caps = (GetKeyState(0x14) & 0x0001) != 0;
            var capsEffect = shift ^ caps;

            if (vkCode >= 0x30 && vkCode <= 0x39)
            {
                if (shift) return ")!@#$%^&*("[vkCode - 0x30];
                return (char)vkCode;
            }
            if (vkCode >= 0x41 && vkCode <= 0x5A)
            {
                return capsEffect ? (char)vkCode : (char)(vkCode + 32);
            }
            if (vkCode >= 0x60 && vkCode <= 0x6F)
                return (char)(vkCode - 0x30);
            if (vkCode == 0xBA) return shift ? ':' : ';';
            if (vkCode == 0xBB) return shift ? '+' : '=';
            if (vkCode == 0xBC) return shift ? '<' : ',';
            if (vkCode == 0xBD) return shift ? '_' : '-';
            if (vkCode == 0xBE) return shift ? '>' : '.';
            if (vkCode == 0xBF) return shift ? '?' : '/';
            if (vkCode == 0xC0) return shift ? '~' : '`';
            if (vkCode == 0xDB) return shift ? '{' : '[';
            if (vkCode == 0xDC) return shift ? '|' : '\\';
            if (vkCode == 0xDD) return shift ? '}' : ']';
            if (vkCode == 0xDE) return shift ? '"' : '\'';
            if (vkCode == 0xDF) return '\0';
            if (vkCode == 0xE2) return shift ? '>' : '<';

            if (vkCode >= 0x20 && vkCode <= 0x7E)
                return (char)vkCode;

            return (char)vkCode;
        }

        private class ModState
        {
            public bool Ctrl, Alt, Shift, Win;
        }

        private static ModState GetModifiers()
        {
            return new ModState
            {
                Ctrl = (GetKeyState(0x11) & 0x8000) != 0,
                Shift = (GetKeyState(0x10) & 0x8000) != 0,
                Alt = (GetKeyState(0x12) & 0x8000) != 0,
                Win = (GetKeyState(0x5B) & 0x8000) != 0 || (GetKeyState(0x5C) & 0x8000) != 0
            };
        }

        private static bool HasSuspiciousModifiers(ModState m)
        {
            return m.Ctrl || m.Alt || m.Win;
        }

        private void DetectSuspicious(int vkCode, ModState m)
        {
            if (m.Alt && vkCode == 0x73)
                AppendSuspicious("SUS_ALTF4", "Alt+F4 detectado");
            else if (m.Alt && vkCode == 0x09)
                AppendSuspicious("SUS_ALTTAB", "Alt+Tab detectado");
            else if (m.Ctrl && vkCode == 0x1B)
                AppendSuspicious("SUS_CTRLESC", "Ctrl+Esc detectado");
            else if (m.Ctrl && vkCode == 0x2E)
                AppendSuspicious("SUS_CTRL_ALT_DEL", "Ctrl+Alt+Del detectado");
            else if (m.Ctrl && !m.Alt && !m.Win && vkCode == 0x43)
                AppendSuspicious("SUS_CLIPBOARD_COPY", "Ctrl+C (copiar)");
            else if (m.Ctrl && !m.Alt && !m.Win && vkCode == 0x58)
                AppendSuspicious("SUS_CLIPBOARD_CUT", "Ctrl+X (recortar)");
            else if (m.Ctrl && !m.Alt && !m.Win && vkCode == 0x56)
                AppendSuspicious("SUS_CLIPBOARD_PASTE", "Ctrl+V (colar)");
            else if (m.Ctrl && !m.Alt && !m.Win && vkCode == 0x53)
                AppendSuspicious("SUS_SAVE", "Ctrl+S (salvar)");
            else if (m.Ctrl && !m.Alt && !m.Win && vkCode == 0x50)
                AppendSuspicious("SUS_PRINT", "Ctrl+P (imprimir)");
            else if (m.Ctrl && !m.Alt && !m.Win && vkCode == 0x5A)
                AppendSuspicious("SUS_UNDO", "Ctrl+Z (desfazer)");
            else if (m.Ctrl && !m.Alt && !m.Win && vkCode == 0x4E)
                AppendSuspicious("SUS_NEW_WINDOW", "Ctrl+N (nova janela)");
            else if (m.Ctrl && !m.Alt && !m.Win && vkCode == 0x54)
                AppendSuspicious("SUS_NEW_TAB", "Ctrl+T (nova aba)");
            else if (m.Ctrl && !m.Alt && !m.Win && vkCode == 0x57)
                AppendSuspicious("SUS_CLOSE_TAB", "Ctrl+W (fechar aba)");
            else if (m.Win && vkCode == 0x4C)
                AppendSuspicious("SUS_WINLOCK", "Win+L (bloquear)");
            else if (m.Win && vkCode == 0x52)
                AppendSuspicious("SUS_WINRUN", "Win+R (executar)");
            else if (m.Win)
                AppendSuspicious("SUS_WINKEY", $"Tecla Win + VK{vkCode:X2}");
            else if (m.Ctrl && m.Alt)
                AppendSuspicious("SUS_CTRL_ALT", $"Ctrl+Alt + VK{vkCode:X2}");
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
