using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using ApacKiosk.Database;

namespace ApacKiosk.Services
{
    public enum FileDialogAction
    {
        Close,
        RestrictNavigation
    }

    public class FileDialogBlocker : IDisposable
    {
        private readonly DatabaseManager _db;
        private Thread _hookThread;
        private volatile bool _isRunning;
        private IntPtr _hookHandle = IntPtr.Zero;
        private WinEventDelegate _delegate;
        private readonly HashSet<int> _allowedProcessPids = new();
        private readonly object _lockAllowed = new();
        private readonly FileDialogAction _action;
        private readonly Dictionary<IntPtr, IntPtr> _subclassedEdits = new();
        private readonly object _lockSubclassed = new();
        private WndProcDelegate _editWndProc;

        private IntPtr _keyboardHookHandle = IntPtr.Zero;
        private LowLevelKeyboardProc _keyboardProc;
        private System.Threading.Timer _enforceTimer;
        private IntPtr _activeDialogHwnd = IntPtr.Zero;


        public string CurrentUsername { get; set; } = "Arquivos_Salvos";

        private const uint EVENT_OBJECT_SHOW = 0x8002;
        private const uint EVENT_SYSTEM_MENUPOPUPSTART = 0x0006;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

        private static readonly string[] _prohibitedExtensions =
            { ".bat", ".ps1", ".vbs", ".exe", ".com", ".cmd", ".msi", ".reg", ".scr", ".js", ".vbe", ".wsf", ".wsh" };

        public FileDialogBlocker(DatabaseManager db, FileDialogAction action = FileDialogAction.Close)
        {
            _db = db;
            _action = action;
            _delegate = WinEventCallback;
            _editWndProc = EditSubclassProc;
        }

        public void EnsureUserFolderExists()
        {
            try
            {
                var baseDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var folderName = string.IsNullOrWhiteSpace(CurrentUsername) ? "Arquivos_Salvos" : CurrentUsername;
                var docsPath = Path.Combine(baseDocs, folderName);
                if (!Directory.Exists(docsPath))
                {
                    Directory.CreateDirectory(docsPath);
                }
            }
            catch (Exception ex)
            {
                LogTrace("FileDialogBlocker", $"Erro ao criar pasta do usuario: {ex.Message}");
            }
        }

        public void Start()
        {
            if (_isRunning) return;
            _isRunning = true;

            lock (_lockAllowed)
            {
                _allowedProcessPids.Add(Environment.ProcessId);
            }

            _hookThread = new Thread(RunHookLoop)
            {
                IsBackground = true,
                Name = "FileDialogBlocker",
                ApartmentState = ApartmentState.STA
            };
            _hookThread.Start();
        }

        public void Stop()
        {
            _isRunning = false;
            if (_hookHandle != IntPtr.Zero)
            {
                UnhookWinEvent(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }

            lock (_lockSubclassed)
            {
                foreach (var kvp in _subclassedEdits)
                {
                    try { SetWindowLongPtr(kvp.Key, GWL_WNDPROC, kvp.Value); } catch { }
                }
                _subclassedEdits.Clear();
            }

            StopKeyboardHook();
            _enforceTimer?.Dispose();
            _enforceTimer = null;

            _hookThread?.Join(3000);
        }

        public void RegisterAllowedProcess(int pid)
        {
            lock (_lockAllowed)
            {
                _allowedProcessPids.Add(pid);
            }
        }

        public void UnregisterAllowedProcess(int pid)
        {
            lock (_lockAllowed)
            {
                _allowedProcessPids.Remove(pid);
            }
        }

        private void RunHookLoop()
        {
            _hookHandle = SetWinEventHook(
                EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW,
                IntPtr.Zero, _delegate,
                0, 0,
                WINEVENT_OUTOFCONTEXT);

            System.Windows.Forms.Application.Run();
        }

        private void WinEventCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (!_isRunning) return;
            if (idObject != 0) return;

            try
            {
                var className = GetClassName(hwnd);
                if (className != "#32770") return;

                GetWindowThreadProcessId(hwnd, out uint pid);

                var isAllowedProcess = false;
                lock (_lockAllowed)
                {
                    if (_allowedProcessPids.Contains((int)pid))
                        isAllowedProcess = true;
                }

                if (!isAllowedProcess)
                {
                    try
                    {
                        var proc = Process.GetProcessById((int)pid);
                        if (proc.ProcessName.Equals("msedgewebview2", StringComparison.OrdinalIgnoreCase))
                        {
                            isAllowedProcess = true;
                            RegisterAllowedProcess((int)pid);
                        }
                    }
                    catch { }
                }

                if (!IsFileDialog(hwnd)) return;

                if (isAllowedProcess)
                {
                    RestrictDialogNavigation(hwnd, (int)pid);
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        Thread.Sleep(100);
                        BlockNavigationPane(hwnd);
                        SubclassEditControl(hwnd);
                        InstallContextMenuHook(hwnd, dwEventThread);
                        ForceInitialFolder(hwnd);
                    });

                    _activeDialogHwnd = hwnd;
                    StartKeyboardHook();
                    _enforceTimer?.Dispose();
                    _enforceTimer = new System.Threading.Timer(CheckDialogFolder, hwnd, 500, 500);
                }
                else
                {
                    CloseDialog(hwnd, (int)pid);
                }
            }
            catch (Exception ex)
            {
                try { LogTrace("FileDialogBlocker", $"WinEventCallback erro: {ex.Message}"); } catch { }
            }
        }

        private bool IsFileDialog(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            GetWindowText(hwnd, sb, sb.Capacity);
            var title = sb.ToString();

            if (string.IsNullOrEmpty(title)) return false;

            var fileKeywords = new[]
            {
                "Abrir", "Open", "Salvar", "Save", "Selecionar", "Select",
                "Browse", "Procurar", "Explorar", "Arquivo", "File", "Pasta", "Folder"
            };

            foreach (var kw in fileKeywords)
            {
                if (title.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            var hasCmb13 = FindWindowEx(hwnd, IntPtr.Zero, "ComboBoxEx32", null);
            var hasEdt1 = FindWindowEx(hwnd, IntPtr.Zero, "Edit", null);

            if (hasCmb13 != IntPtr.Zero && hasEdt1 != IntPtr.Zero)
                return true;

            return false;
        }

        private void CloseDialog(IntPtr hwnd, int pid)
        {
            PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            try
            {
                string procName;
                try { procName = Process.GetProcessById(pid)?.ProcessName ?? "desconhecido"; }
                catch { procName = "desconhecido"; }

                _db.InsertLog(null, "dialog_blocked", null,
                    $"Diálogo de arquivo fechado: PID {pid} ({procName})");
            }
            catch { }
        }

        private void RestrictDialogNavigation(IntPtr hwnd, int pid)
        {
            var toolbar = FindWindowEx(hwnd, IntPtr.Zero, "ToolbarWindow32", null);
            if (toolbar != IntPtr.Zero)
            {
                EnableWindow(toolbar, false);
            }

            var breadcrumb = FindWindowEx(hwnd, IntPtr.Zero, "msctls_progress32", null);
            if (breadcrumb == IntPtr.Zero)
                breadcrumb = FindWindowEx(hwnd, IntPtr.Zero, "ReBarWindow32", null);

            if (breadcrumb != IntPtr.Zero)
            {
                var workerW = FindWindowEx(breadcrumb, IntPtr.Zero, "WorkerW", null);
                if (workerW != IntPtr.Zero)
                {
                    var toolbar2 = FindWindowEx(workerW, IntPtr.Zero, "ToolbarWindow32", null);
                    if (toolbar2 != IntPtr.Zero)
                        EnableWindow(toolbar2, false);
                }

                var comboBox = FindWindowEx(breadcrumb, IntPtr.Zero, "ComboBoxEx32", null);
                if (comboBox != IntPtr.Zero)
                    EnableWindow(comboBox, false);
            }

            var fileCombo = FindWindowEx(hwnd, IntPtr.Zero, "ComboBoxEx32", null);
            if (fileCombo != IntPtr.Zero)
                EnableWindow(fileCombo, false);

            try
            {
                string procName;
                try { procName = Process.GetProcessById(pid)?.ProcessName ?? "desconhecido"; }
                catch { procName = "desconhecido"; }

                _db.InsertLog(null, "dialog_restricted", null,
                    $"Navegação em diálogo de arquivo restrita: PID {pid} ({procName})");
            }
            catch { }
        }

        private void BlockNavigationPane(IntPtr dialogHwnd)
        {
            try
            {
                EnumChildWindows(dialogHwnd, (childHwnd, lParam) =>
                {
                    var cls = GetClassName(childHwnd);
                    if (cls == "SysTreeView32")
                    {
                        EnableWindow(childHwnd, false);
                        ShowWindow(childHwnd, SW_HIDE);
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
        }

        private void SubclassEditControl(IntPtr dialogHwnd)
        {
            try
            {
                EnumChildWindows(dialogHwnd, (childHwnd, lParam) =>
                {
                    var cls = GetClassName(childHwnd);
                    if (cls == "Edit")
                    {
                        lock (_lockSubclassed)
                        {
                            if (_subclassedEdits.ContainsKey(childHwnd))
                                return true;

                            var oldProc = SetWindowLongPtr(childHwnd, GWL_WNDPROC,
                                Marshal.GetFunctionPointerForDelegate(_editWndProc));
                            if (oldProc != IntPtr.Zero)
                            {
                                _subclassedEdits[childHwnd] = oldProc;
                            }
                        }
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
        }

        private IntPtr EditSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            IntPtr oldProc;
            lock (_lockSubclassed)
            {
                _subclassedEdits.TryGetValue(hWnd, out oldProc);
            }

            if (oldProc == IntPtr.Zero)
                return DefWindowProc(hWnd, msg, wParam, lParam);

            if (msg == WM_KEYDOWN)
            {
                var vk = (int)wParam;
                var text = GetWindowTextHelper(hWnd);

                if (!string.IsNullOrEmpty(text) && ((vk == VK_RETURN) || (vk == VK_F5)))
                {
                    var lower = text.ToLowerInvariant();

                    if (lower.Contains(@":\") || lower.Contains(@"\\"))
                    {
                        SetWindowText(hWnd, "");
                        return IntPtr.Zero;
                    }

                    foreach (var ext in _prohibitedExtensions)
                    {
                        if (lower.EndsWith(ext))
                        {
                            SetWindowText(hWnd, "");
                            return IntPtr.Zero;
                        }
                    }
                }

                if (vk == VK_ESCAPE)
                {
                    SetWindowText(hWnd, "");
                }
            }

            if (msg == WM_CHAR)
            {
                var ch = (char)wParam;
                var currentText = GetWindowTextHelper(hWnd);

                if (ch == ':' && currentText.Length >= 1)
                {
                    return IntPtr.Zero;
                }
            }

            return CallWindowProc(oldProc, hWnd, msg, wParam, lParam);
        }

        private void InstallContextMenuHook(IntPtr dialogHwnd, uint dialogThreadId)
        {
            try
            {
                WinEventDelegate menuProc = (hook, eventType, hwnd, idObj, idChild, eventThread, eventTime) =>
                {
                    if (!_isRunning) return;

                    try
                    {
                        var cls = GetClassName(hwnd);
                        if (cls == "#32768")
                        {
                            var owner = GetParent(hwnd);
                            if (owner != IntPtr.Zero)
                            {
                                var ownerCls = GetClassName(owner);
                                if (ownerCls == "#32770" && owner == dialogHwnd)
                                {
                                    PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                                }
                            }
                        }
                    }
                    catch { }
                };

                SetWinEventHook(EVENT_SYSTEM_MENUPOPUPSTART, EVENT_SYSTEM_MENUPOPUPSTART,
                    IntPtr.Zero, menuProc, 0, dialogThreadId, WINEVENT_OUTOFCONTEXT);
            }
            catch { }
        }

        private void ForceInitialFolder(IntPtr dialogHwnd)
        {
            try
            {
                var baseDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var folderName = string.IsNullOrWhiteSpace(CurrentUsername) ? "Arquivos_Salvos" : CurrentUsername;
                var docsPath = Path.Combine(baseDocs, folderName);
                if (!Directory.Exists(docsPath))
                {
                    try { Directory.CreateDirectory(docsPath); } catch { }
                }

                var comboBox = FindWindowEx(dialogHwnd, IntPtr.Zero, "ComboBoxEx32", null);
                if (comboBox != IntPtr.Zero)
                {
                    EnableWindow(comboBox, true);

                    var childEdit = FindWindowEx(comboBox, IntPtr.Zero, "Edit", null);
                    if (childEdit == IntPtr.Zero)
                        childEdit = FindWindowEx(comboBox, IntPtr.Zero, "ComboBox", null);

                    if (childEdit != IntPtr.Zero)
                    {
                        SendMessage(childEdit, WM_SETTEXT, IntPtr.Zero, docsPath);
                        SendMessage(childEdit, WM_KEYDOWN, (IntPtr)VK_RETURN, IntPtr.Zero);
                    }
                }
                else
                {
                    var editCtrl = FindWindowEx(dialogHwnd, IntPtr.Zero, "Edit", null);
                    if (editCtrl != IntPtr.Zero)
                    {
                        SendMessage(editCtrl, WM_SETTEXT, IntPtr.Zero, docsPath);
                        SendMessage(editCtrl, WM_KEYDOWN, (IntPtr)VK_RETURN, IntPtr.Zero);
                    }
                }
            }
            catch { }
        }

        private void StartKeyboardHook()
        {
            if (_keyboardHookHandle == IntPtr.Zero)
            {
                _keyboardProc = KeyboardHookCallback;
                using var curProcess = Process.GetCurrentProcess();
                using var curModule = curProcess.MainModule;
                _keyboardHookHandle = SetWindowsHookEx(13, _keyboardProc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private void StopKeyboardHook()
        {
            if (_keyboardHookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_keyboardHookHandle);
                _keyboardHookHandle = IntPtr.Zero;
            }
        }

        private IntPtr GetFocusedControl(IntPtr fgWindow)
        {
            uint fgThread = GetWindowThreadProcessId(fgWindow, out _);
            uint currentThread = GetCurrentThreadId();
            if (fgThread != currentThread)
            {
                if (AttachThreadInput(currentThread, fgThread, true))
                {
                    IntPtr focused = GetFocus();
                    AttachThreadInput(currentThread, fgThread, false);
                    return focused;
                }
            }
            return GetFocus();
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)0x0100 || wParam == (IntPtr)0x0104))
            {
                var fg = GetForegroundWindow();
                if (IsFileDialog(fg))
                {
                    int vkCode = Marshal.ReadInt32(lParam);
                    var shift = (GetKeyState(0x10) & 0x8000) != 0;

                    if (vkCode == 0xDC || 
                        vkCode == 0xBF || 
                        vkCode == 0x6F || 
                        (vkCode == 0xBA && shift) || 
                        (vkCode == 0x35 && shift))   
                    {
                        return (IntPtr)1; 
                    }

                    if (wParam == (IntPtr)0x0104 && (vkCode == 0x26 || vkCode == 0x25)) 
                    {
                        return (IntPtr)1; 
                    }

                    if (vkCode == 0x08) 
                    {
                        var focused = GetFocusedControl(fg);
                        if (focused != IntPtr.Zero)
                        {
                            var sb = new StringBuilder(256);
                            GetClassName(focused, sb, sb.Capacity);
                            if (sb.ToString() != "Edit")
                            {
                                return (IntPtr)1; 
                            }
                        }
                    }
                }
            }
            return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
        }

        private void CheckDialogFolder(object state)
        {
            var hwnd = (IntPtr)state;
            if (!IsWindow(hwnd))
            {
                _enforceTimer?.Dispose();
                _enforceTimer = null;
                StopKeyboardHook();
                return;
            }

            var path = GetDialogCurrentPath(hwnd);
            if (!string.IsNullOrWhiteSpace(path))
            {
                var baseDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var folderName = string.IsNullOrWhiteSpace(CurrentUsername) ? "Arquivos_Salvos" : CurrentUsername;
                var docsPath = Path.Combine(baseDocs, folderName);
                
                if (path.IndexOf(docsPath, StringComparison.OrdinalIgnoreCase) == -1)
                {
                    PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                }
            }
        }

        private string GetDialogCurrentPath(IntPtr dialogHwnd)
        {
            var breadcrumb = FindWindowEx(dialogHwnd, IntPtr.Zero, "msctls_progress32", null);
            if (breadcrumb == IntPtr.Zero)
                breadcrumb = FindWindowEx(dialogHwnd, IntPtr.Zero, "ReBarWindow32", null);

            if (breadcrumb != IntPtr.Zero)
            {
                var workerW = FindWindowEx(breadcrumb, IntPtr.Zero, "WorkerW", null);
                if (workerW != IntPtr.Zero)
                {
                    var toolbar = FindWindowEx(workerW, IntPtr.Zero, "ToolbarWindow32", null);
                    if (toolbar != IntPtr.Zero)
                    {
                        var sb = new StringBuilder(512);
                        SendMessage(toolbar, 0x000D, 512, sb);
                        return sb.ToString();
                    }
                }
            }
            return null;
        }

        #region P/Invoke

        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType,
            IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        private delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax,
            IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc,
            uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter,
            string lpszClass, string lpszWindow);

        [DllImport("user32.dll")]
        private static extern bool EnableWindow(IntPtr hWnd, bool bEnable);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

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

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int SendMessage(IntPtr hWnd, uint Msg, int wParam, StringBuilder lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool SetWindowText(IntPtr hWnd, string lpString);

        private const uint WM_CLOSE = 0x0010;
        private const uint WM_KEYDOWN = 0x0100;
        private const uint WM_CHAR = 0x0102;
        private const uint WM_SETTEXT = 0x000C;

        private const int GWL_WNDPROC = -4;
        private const int SW_HIDE = 0;
        private const int VK_RETURN = 0x0D;
        private const int VK_ESCAPE = 0x1B;
        private const int VK_F5 = 0x74;

        private static string GetClassName(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private static string GetWindowTextHelper(IntPtr hwnd)
        {
            var sb = new StringBuilder(512);
            GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        #endregion

        private static void LogTrace(string category, string msg)
        {
            try
            {
                var logPath = Path.Combine(Path.GetTempPath(), "ApacKiosk_Trace.log");
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] [{category}] {msg}\n");
            }
            catch { }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
