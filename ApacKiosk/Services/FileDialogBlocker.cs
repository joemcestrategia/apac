using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using ApacKiosk.Database;

namespace ApacKiosk.Services;

public enum FileDialogAction
{
    Close,
    RestrictNavigation
}

public class FileDialogBlocker : IDisposable
{
    private readonly DatabaseManager _db;
    private readonly UserFolderEnvironmentService? _envService;
    private Thread? _hookThread;
    private volatile bool _isRunning;
    private volatile bool _restrictionActive;
    private uint _hookThreadId;
    private IntPtr _showHookHandle = IntPtr.Zero;
    private WinEventDelegate? _showDelegate;

    private readonly HashSet<int> _allowedProcessPids = new();
    private readonly HashSet<string> _allowedExeNames = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastExeRefresh = DateTime.MinValue;
    private readonly object _lockAllowed = new();

    private IntPtr _keyboardHookHandle = IntPtr.Zero;
    private LowLevelProc? _keyboardProc;
    private IntPtr _mouseHookHandle = IntPtr.Zero;
    private LowLevelProc? _mouseProc;

    private System.Windows.Forms.Timer? _enforceTimer;
    private UiAutomationHelper? _uia;
    private readonly Dictionary<IntPtr, DialogContext> _trackedDialogs = new();
    private readonly object _lockDialogs = new();
    private IntPtr _activeForegroundDialog = IntPtr.Zero;
    private bool _activeDialogIsStrictSubfolder;

    public string CurrentUsername { get; set; } = "Arquivos_Salvos";
    public string UserFolderPath { get; set; } = string.Empty;

    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;

    private static readonly string[] _prohibitedExtensions =
        { ".bat", ".ps1", ".vbs", ".exe", ".com", ".cmd", ".msi", ".reg", ".scr", ".js", ".vbe", ".wsf", ".wsh" };

    public FileDialogBlocker(DatabaseManager db, UserFolderEnvironmentService? envService = null, FileDialogAction action = FileDialogAction.RestrictNavigation)
    {
        _db = db;
        _envService = envService;
        _ = action;
    }

    public void SetRestrictionActive(bool active)
    {
        _restrictionActive = active;
    }

    public void EnsureUserFolderExists()
    {
        try
        {
            var path = ResolveTargetUserFolder();
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            UserFolderPath = path;
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
            Name = "FileDialogBlocker"
        };
        _hookThread.SetApartmentState(ApartmentState.STA);
        _hookThread.Start();
    }

    public void Stop()
    {
        _isRunning = false;
        _restrictionActive = false;

        if (_hookThreadId != 0)
            PostThreadMessage(_hookThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);

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

    private string ResolveTargetUserFolder()
    {
        if (!string.IsNullOrWhiteSpace(UserFolderPath))
            return NormalizePath(UserFolderPath);

        if (_envService != null)
            return _envService.GetUserFolderPath(CurrentUsername);

        var baseDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var folderName = string.IsNullOrWhiteSpace(CurrentUsername) ? "Arquivos_Salvos" : CurrentUsername;
        return NormalizePath(Path.Combine(baseDocs, folderName));
    }

    private void RunHookLoop()
    {
        try
        {
            _hookThreadId = GetCurrentThreadId();
            _uia = new UiAutomationHelper();
            _showDelegate = WinEventCallback;
            _showHookHandle = SetWinEventHook(
                EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW,
                IntPtr.Zero, _showDelegate,
                0, 0,
                WINEVENT_OUTOFCONTEXT);

            _enforceTimer = new System.Windows.Forms.Timer { Interval = 120 };
            _enforceTimer.Tick += (_, _) => EnforceAllTrackedAndScan();
            _enforceTimer.Start();

            Application.Run();

            _enforceTimer.Stop();
            _enforceTimer.Dispose();
            _enforceTimer = null;

            if (_showHookHandle != IntPtr.Zero)
            {
                UnhookWinEvent(_showHookHandle);
                _showHookHandle = IntPtr.Zero;
            }

            StopInputHooks();
            _uia.Dispose();
            _uia = null;
        }
        catch (Exception ex)
        {
            LogTrace("FileDialogBlocker", $"RunHookLoop erro fatal: {ex.Message}");
        }
    }

    private void WinEventCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (!_isRunning || !_restrictionActive || idObject != 0 || hwnd == IntPtr.Zero)
            return;

        try
        {
            var className = GetClassName(hwnd);
            if (className == "CabinetWClass" || className == "ExploreWClass")
            {
                PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                return;
            }

            if (className != "#32770")
                return;

            HandleDialogWindow(hwnd);
        }
        catch (Exception ex)
        {
            LogTrace("FileDialogBlocker", $"WinEventCallback erro: {ex.Message}");
        }
    }

    private void EnforceAllTrackedAndScan()
    {
        if (!_isRunning) return;

        if (!_restrictionActive)
        {
            lock (_lockDialogs)
            {
                _trackedDialogs.Clear();
                _activeForegroundDialog = IntPtr.Zero;
            }
            StopInputHooks();
            return;
        }

        try
        {
            var foundDialogs = new List<IntPtr>();
            EnumWindows((hwnd, _) =>
            {
                if (!IsWindowVisible(hwnd)) return true;
                var cls = GetClassName(hwnd);
                if (cls == "CabinetWClass" || cls == "ExploreWClass")
                {
                    PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    return true;
                }
                if (cls == "#32770")
                    foundDialogs.Add(hwnd);
                return true;
            }, IntPtr.Zero);

            foreach (var dlg in foundDialogs)
                HandleDialogWindow(dlg);

            CleanupClosedDialogs();
            EnforceTrackedDialogs();
            SyncInputHooksWithForeground();
        }
        catch { }
    }

    private void HandleDialogWindow(IntPtr hwnd)
    {
        if (!IsFileDialog(hwnd))
            return;

        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == (uint)Environment.ProcessId)
            return;

        if (!IsAllowedProcess((int)pid))
        {
            CloseDialog(hwnd, (int)pid, "Diálogo de arquivo fechado (processo não autorizado)");
            return;
        }

        if (IsBrowseForFolderDialog(hwnd))
        {
            CloseDialog(hwnd, (int)pid, "Diálogo Procurar Pasta fechado por segurança");
            return;
        }

        DialogContext ctx;
        bool isNew = false;
        lock (_lockDialogs)
        {
            if (!_trackedDialogs.TryGetValue(hwnd, out ctx!))
            {
                ctx = new DialogContext { Hwnd = hwnd, Pid = (int)pid, CreatedAtUtc = DateTime.UtcNow };
                _trackedDialogs[hwnd] = ctx;
                isNew = true;
            }
        }

        if (isNew)
        {
            RefreshDialogHandles(ctx);
            ApplyStaticLockdown(ctx);
            LogDialogRestricted((int)pid);
        }
    }

    private void CleanupClosedDialogs()
    {
        lock (_lockDialogs)
        {
            var toRemove = new List<IntPtr>();
            foreach (var kvp in _trackedDialogs)
            {
                if (!IsWindow(kvp.Key))
                    toRemove.Add(kvp.Key);
            }
            foreach (var h in toRemove)
                _trackedDialogs.Remove(h);
        }
    }

    private void EnforceTrackedDialogs()
    {
        List<DialogContext> contexts;
        lock (_lockDialogs)
        {
            if (_trackedDialogs.Count == 0) return;
            contexts = new List<DialogContext>(_trackedDialogs.Values);
        }

        var targetFolder = ResolveTargetUserFolder();
        EnsureDirectoryExists(targetFolder);

        foreach (var ctx in contexts)
        {
            if (!IsWindow(ctx.Hwnd)) continue;

            RefreshDialogHandles(ctx);
            ApplyStaticLockdown(ctx);
            SanitizeFileNameEdit(ctx);
            EnforceDialogFolder(ctx, targetFolder);
            RefreshBlockedRects(ctx);
        }
    }

    private void SyncInputHooksWithForeground()
    {
        var fg = GetForegroundWindow();
        DialogContext? activeCtx = null;

        lock (_lockDialogs)
        {
            if (_trackedDialogs.Count == 0)
            {
                _activeForegroundDialog = IntPtr.Zero;
                _activeDialogIsStrictSubfolder = false;
                StopInputHooks();
                return;
            }

            if (fg != IntPtr.Zero && _trackedDialogs.TryGetValue(fg, out var ctx))
            {
                activeCtx = ctx;
                _activeForegroundDialog = fg;
                _activeDialogIsStrictSubfolder = ctx.IsStrictSubfolder;
            }
            else
            {
                _activeForegroundDialog = IntPtr.Zero;
                _activeDialogIsStrictSubfolder = false;
            }
        }

        if (activeCtx != null)
            StartInputHooks();
        else
            StopInputHooks();
    }

    private void RefreshDialogHandles(DialogContext ctx)
    {
        ctx.BlockedChildHwnds.Clear();
        ctx.UpBandHwnd = IntPtr.Zero;
        ctx.AddressBandHwnd = IntPtr.Zero;
        ctx.BreadcrumbToolbarHwnd = IntPtr.Zero;
        ctx.AddressEditHwnd = IntPtr.Zero;
        ctx.FileNameEditHwnd = IntPtr.Zero;
        ctx.ConfirmButtonHwnd = IntPtr.Zero;
        ctx.ShellDefViewHwnd = IntPtr.Zero;

        var childList = new List<IntPtr>();
        EnumChildWindows(ctx.Hwnd, (c, _) =>
        {
            childList.Add(c);
            return true;
        }, IntPtr.Zero);

        foreach (var c in childList)
        {
            var cls = GetClassName(c);
            if (cls == "SHELLDLL_DefView")
                ctx.ShellDefViewHwnd = c;
            else if (cls == "UpBand")
                ctx.UpBandHwnd = c;
            else if (cls == "Address Band Root")
                ctx.AddressBandHwnd = c;
            else if (cls == "TravelBand" || cls == "UniversalSearchBand" || cls == "Search Box" ||
                     cls == "SearchEditBoxWrapperClass" || cls == "NamespaceTreeControl" ||
                     cls == "ProperTreeHost" || cls == "SysTreeView32")
            {
                ctx.BlockedChildHwnds.Add(c);
                if (cls == "NamespaceTreeControl")
                {
                    var parent = GetParent(c);
                    if (parent != IntPtr.Zero && parent != ctx.Hwnd && GetClassName(parent) == "CtrlNotifySink")
                        ctx.BlockedChildHwnds.Add(parent);
                }
            }
        }

        if (ctx.AddressBandHwnd != IntPtr.Zero)
        {
            EnumChildWindows(ctx.AddressBandHwnd, (c, _) =>
            {
                var cls = GetClassName(c);
                var parentCls = GetClassName(GetParent(c));
                if (cls == "ToolbarWindow32" && parentCls == "Breadcrumb Parent")
                    ctx.BreadcrumbToolbarHwnd = c;
                else if (cls == "Edit" && parentCls == "ComboBox")
                    ctx.AddressEditHwnd = c;
                return true;
            }, IntPtr.Zero);
        }

        foreach (var c in childList)
        {
            var cls = GetClassName(c);
            if (cls == "Button")
            {
                var ctrlId = GetDlgCtrlID(c);
                var text = GetWindowTextHelper(c).Replace("&", "").Trim();
                if (ctrlId == 1)
                {
                    ctx.ConfirmButtonHwnd = c;
                }
                else if (IsForbiddenDialogButton(text, ctrlId))
                {
                    ctx.BlockedChildHwnds.Add(c);
                }
            }
            else if (cls == "Edit")
            {
                if (c != ctx.AddressEditHwnd &&
                    !IsDescendantOfClass(c, ctx.Hwnd, "WorkerW") &&
                    !IsDescendantOfClass(c, ctx.Hwnd, "SHELLDLL_DefView") &&
                    !IsDescendantOfClass(c, ctx.Hwnd, "UniversalSearchBand"))
                {
                    ctx.FileNameEditHwnd = c;
                }
            }
            else if (cls == "ToolbarWindow32" && ctx.AddressBandHwnd == IntPtr.Zero)
            {
                if (GetParent(c) == ctx.Hwnd)
                    ctx.BlockedChildHwnds.Add(c);
            }
            else if (cls == "ComboBoxEx32" && ctx.AddressBandHwnd == IntPtr.Zero)
            {
                if (GetParent(c) == ctx.Hwnd)
                    ctx.BlockedChildHwnds.Add(c);
            }
        }
    }

    private void ApplyStaticLockdown(DialogContext ctx)
    {
        foreach (var h in ctx.BlockedChildHwnds)
        {
            if (IsWindow(h))
            {
                EnableWindow(h, false);
                ShowWindow(h, SW_HIDE);
            }
        }

        if (!ctx.IsNavigatingAddressBar && ctx.AddressBandHwnd != IntPtr.Zero && IsWindow(ctx.AddressBandHwnd))
        {
            EnableWindow(ctx.AddressBandHwnd, false);
        }
    }

    private void EnforceDialogFolder(DialogContext ctx, string targetFolder)
    {
        if (ctx.BreadcrumbToolbarHwnd == IntPtr.Zero)
        {
            ctx.IsStrictSubfolder = false;
            return;
        }

        var rawTitle = GetWindowTextHelper(ctx.BreadcrumbToolbarHwnd);
        var (isInside, isStrictSubfolder) = EvaluateToolbarPath(rawTitle, targetFolder);
        ctx.IsStrictSubfolder = isStrictSubfolder;

        if (ctx.UpBandHwnd != IntPtr.Zero && IsWindow(ctx.UpBandHwnd))
            EnableWindow(ctx.UpBandHwnd, isStrictSubfolder);

        if (isInside)
        {
            ctx.OutOfFolderFailures = 0;
            return;
        }

        if ((DateTime.UtcNow - ctx.CreatedAtUtc).TotalMilliseconds < 250 && string.IsNullOrWhiteSpace(rawTitle))
            return;

        if ((DateTime.UtcNow - ctx.LastNavigationAttemptUtc).TotalMilliseconds < 900)
            return;

        ctx.LastNavigationAttemptUtc = DateTime.UtcNow;
        ctx.OutOfFolderFailures++;

        if (ctx.OutOfFolderFailures > 4)
        {
            CloseDialog(ctx.Hwnd, ctx.Pid, $"Diálogo fechado após falha em redirecionar para pasta permitida ({rawTitle})");
            return;
        }

        NavigateDialogToFolder(ctx, targetFolder);
    }

    private (bool isInside, bool isStrictSubfolder) EvaluateToolbarPath(string rawToolbarText, string targetFolder)
    {
        if (string.IsNullOrWhiteSpace(rawToolbarText))
            return (false, false);

        var extracted = rawToolbarText.Trim();
        var colonIdx = extracted.IndexOf(':');
        if (colonIdx > 1)
            extracted = extracted.Substring(colonIdx + 1).Trim();

        if (string.IsNullOrWhiteSpace(extracted))
            return (false, false);

        if (Path.IsPathRooted(extracted) && !extracted.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var normCurrent = NormalizePath(extracted);
            var normTarget = NormalizePath(targetFolder);
            if (string.Equals(normCurrent, normTarget, StringComparison.OrdinalIgnoreCase))
                return (true, false);

            var prefix = normTarget + Path.DirectorySeparatorChar;
            if (normCurrent.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return (true, true);

            return (false, false);
        }

        if (_envService != null && _envService.IsDocumentsKnownFolderPointingTo(targetFolder))
        {
            if (string.Equals(extracted, "Documentos", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extracted, "Documents", StringComparison.OrdinalIgnoreCase))
            {
                return (true, false);
            }

            if ((extracted.StartsWith(@"Documentos\", StringComparison.OrdinalIgnoreCase) ||
                 extracted.StartsWith(@"Documents\", StringComparison.OrdinalIgnoreCase)) &&
                !extracted.Contains(".."))
            {
                return (true, true);
            }
        }

        return (false, false);
    }

    private void NavigateDialogToFolder(DialogContext ctx, string targetFolder)
    {
        if (ctx.BreadcrumbToolbarHwnd == IntPtr.Zero || ctx.AddressBandHwnd == IntPtr.Zero)
            return;

        ctx.IsNavigatingAddressBar = true;
        try
        {
            EnableWindow(ctx.AddressBandHwnd, true);
            EnableWindow(ctx.BreadcrumbToolbarHwnd, true);

            if (GetClientRect(ctx.BreadcrumbToolbarHwnd, out var rc))
            {
                var clickX = Math.Max(4, rc.Right - rc.Left - 8);
                var clickY = Math.Max(4, (rc.Bottom - rc.Top) / 2);
                var lParam = (IntPtr)((clickY << 16) | (clickX & 0xFFFF));
                SendMessage(ctx.BreadcrumbToolbarHwnd, WM_LBUTTONDOWN, (IntPtr)1, lParam);
                SendMessage(ctx.BreadcrumbToolbarHwnd, WM_LBUTTONUP, IntPtr.Zero, lParam);
            }

            if (ctx.AddressEditHwnd == IntPtr.Zero)
                RefreshDialogHandles(ctx);

            if (ctx.AddressEditHwnd != IntPtr.Zero)
            {
                SendMessage(ctx.AddressEditHwnd, WM_SETTEXT, IntPtr.Zero, targetFolder);
                PostMessage(ctx.AddressEditHwnd, WM_KEYDOWN, (IntPtr)VK_RETURN, IntPtr.Zero);
                PostMessage(ctx.AddressEditHwnd, WM_KEYUP, (IntPtr)VK_RETURN, IntPtr.Zero);
            }
        }
        finally
        {
            ctx.IsNavigatingAddressBar = false;
            if (IsWindow(ctx.AddressBandHwnd))
                EnableWindow(ctx.AddressBandHwnd, false);
        }
    }

    private void RefreshBlockedRects(DialogContext ctx)
    {
        var rects = new List<RECT>();

        foreach (var h in ctx.BlockedChildHwnds)
        {
            if (IsWindow(h) && GetWindowRect(h, out var r) && r.Right > r.Left && r.Bottom > r.Top)
                rects.Add(r);
        }

        if (ctx.AddressBandHwnd != IntPtr.Zero && IsWindow(ctx.AddressBandHwnd) && GetWindowRect(ctx.AddressBandHwnd, out var addrRect))
            rects.Add(addrRect);

        if (!ctx.IsStrictSubfolder && ctx.UpBandHwnd != IntPtr.Zero && IsWindow(ctx.UpBandHwnd) && GetWindowRect(ctx.UpBandHwnd, out var upRect))
            rects.Add(upRect);

        if (_uia != null && (DateTime.UtcNow - ctx.LastUiaScanUtc).TotalMilliseconds >= 400)
        {
            ctx.LastUiaScanUtc = DateTime.UtcNow;
            ctx.CachedUiaRegions = _uia.GetBlockedDialogRegions(ctx.Hwnd);
        }

        foreach (var region in ctx.CachedUiaRegions)
        {
            rects.Add(new RECT
            {
                Left = region.Left,
                Top = region.Top,
                Right = region.Right,
                Bottom = region.Bottom
            });
        }

        lock (_lockDialogs)
        {
            ctx.BlockedScreenRects = rects;
        }
    }

    private void SanitizeFileNameEdit(DialogContext ctx)
    {
        if (ctx.FileNameEditHwnd == IntPtr.Zero || !IsWindow(ctx.FileNameEditHwnd))
            return;

        var text = GetWindowTextHelper(ctx.FileNameEditHwnd);
        if (IsForbiddenFileNameInput(text))
            SendMessage(ctx.FileNameEditHwnd, WM_SETTEXT, IntPtr.Zero, "");
    }

    private static bool IsForbiddenFileNameInput(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        if (trimmed.Contains(':') || trimmed.Contains('\\') || trimmed.Contains('/') ||
            trimmed.Contains("..") || trimmed.Contains('%'))
        {
            return true;
        }

        var lower = trimmed.ToLowerInvariant();
        foreach (var ext in _prohibitedExtensions)
        {
            if (lower.EndsWith(ext))
                return true;
        }

        return false;
    }

    private static bool IsForbiddenDialogButton(string text, int ctrlId)
    {
        if (ctrlId == 1 || ctrlId == 2)
            return false;

        if (text.Contains("pastas", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("folder", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("ferramentas", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("tools", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "Ajuda", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "Help", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private bool IsFileDialog(IntPtr hwnd)
    {
        var hasShellChild = false;
        var hasComboBoxEx = false;
        var hasEdit = false;

        EnumChildWindows(hwnd, (c, _) =>
        {
            var cls = GetClassName(c);
            if (cls == "DUIViewWndClassName" || cls == "SHELLDLL_DefView" ||
                cls == "Breadcrumb Parent" || cls == "SHBrowseForFolder ShellNameSpace Control")
            {
                hasShellChild = true;
                return false;
            }
            if (cls == "ComboBoxEx32") hasComboBoxEx = true;
            if (cls == "Edit") hasEdit = true;
            return true;
        }, IntPtr.Zero);

        if (hasShellChild)
            return true;

        if (hasComboBoxEx && hasEdit)
            return true;

        var title = GetWindowTextHelper(hwnd);
        if (string.IsNullOrWhiteSpace(title))
            return false;

        var fileKeywords = new[]
        {
            "Abrir", "Open", "Salvar", "Save", "Selecionar", "Select",
            "Browse", "Procurar", "Explorar", "Inserir", "Insert", "Anexar", "Attach"
        };

        foreach (var kw in fileKeywords)
        {
            if (title.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                return hasEdit;
        }

        return false;
    }

    private static bool IsBrowseForFolderDialog(IntPtr hwnd)
    {
        bool hasBrowseControl = false;
        bool hasDuiView = false;

        EnumChildWindows(hwnd, (c, _) =>
        {
            var cls = GetClassName(c);
            if (cls == "SHBrowseForFolder ShellNameSpace Control") hasBrowseControl = true;
            if (cls == "DUIViewWndClassName") hasDuiView = true;
            return true;
        }, IntPtr.Zero);

        return hasBrowseControl && !hasDuiView;
    }

    private bool IsAllowedProcess(int pid)
    {
        lock (_lockAllowed)
        {
            if (_allowedProcessPids.Contains(pid))
                return true;
        }

        string? procName = null;
        try
        {
            using var proc = Process.GetProcessById(pid);
            procName = proc.ProcessName;
        }
        catch { }

        if (string.IsNullOrWhiteSpace(procName))
            return false;

        if (procName.Equals("msedgewebview2", StringComparison.OrdinalIgnoreCase))
        {
            RegisterAllowedProcess(pid);
            return true;
        }

        RefreshAllowedExeNamesIfNeeded();

        lock (_lockAllowed)
        {
            if (_allowedExeNames.Contains(procName + ".exe"))
            {
                _allowedProcessPids.Add(pid);
                return true;
            }
        }

        return false;
    }

    private void RefreshAllowedExeNamesIfNeeded()
    {
        if ((DateTime.UtcNow - _lastExeRefresh).TotalSeconds < 5)
            return;

        try
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT executable_path FROM allowed_programs";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var path = reader.GetString(0);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    var fileName = Path.GetFileName(path);
                    if (!string.IsNullOrWhiteSpace(fileName))
                        names.Add(fileName);
                }
            }

            lock (_lockAllowed)
            {
                _allowedExeNames.Clear();
                foreach (var n in names)
                    _allowedExeNames.Add(n);
                _lastExeRefresh = DateTime.UtcNow;
            }
        }
        catch { }
    }

    private void StartInputHooks()
    {
        if (_keyboardHookHandle == IntPtr.Zero)
        {
            _keyboardProc = KeyboardHookCallback;
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            var modHandle = curModule != null ? GetModuleHandle(curModule.ModuleName) : IntPtr.Zero;
            _keyboardHookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, modHandle, 0);
        }

        if (_mouseHookHandle == IntPtr.Zero)
        {
            _mouseProc = MouseHookCallback;
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            var modHandle = curModule != null ? GetModuleHandle(curModule.ModuleName) : IntPtr.Zero;
            _mouseHookHandle = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, modHandle, 0);
        }
    }

    private void StopInputHooks()
    {
        if (_keyboardHookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHookHandle);
            _keyboardHookHandle = IntPtr.Zero;
        }
        if (_mouseHookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHookHandle);
            _mouseHookHandle = IntPtr.Zero;
        }
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _restrictionActive && _activeForegroundDialog != IntPtr.Zero)
        {
            var msg = (uint)wParam;
            if (msg == WM_LBUTTONDOWN || msg == WM_LBUTTONUP || msg == WM_RBUTTONDOWN || msg == WM_RBUTTONUP || msg == WM_MBUTTONDOWN)
            {
                var fg = GetForegroundWindow();
                if (fg == _activeForegroundDialog)
                {
                    var mouse = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    DialogContext? ctx = null;
                    List<RECT>? blockedRects = null;
                    lock (_lockDialogs)
                    {
                        if (_trackedDialogs.TryGetValue(fg, out ctx))
                            blockedRects = new List<RECT>(ctx.BlockedScreenRects);
                    }

                    if (blockedRects != null)
                    {
                        foreach (var r in blockedRects)
                        {
                            if (mouse.pt.X >= r.Left && mouse.pt.X <= r.Right &&
                                mouse.pt.Y >= r.Top && mouse.pt.Y <= r.Bottom)
                            {
                                return (IntPtr)1;
                            }
                        }
                    }

                    if (ctx != null && msg == WM_LBUTTONDOWN &&
                        ctx.ConfirmButtonHwnd != IntPtr.Zero &&
                        GetWindowRect(ctx.ConfirmButtonHwnd, out var btnRect) &&
                        mouse.pt.X >= btnRect.Left && mouse.pt.X <= btnRect.Right &&
                        mouse.pt.Y >= btnRect.Top && mouse.pt.Y <= btnRect.Bottom)
                    {
                        if (ctx.FileNameEditHwnd != IntPtr.Zero)
                        {
                            var fileName = GetWindowTextHelper(ctx.FileNameEditHwnd);
                            if (IsForbiddenFileNameInput(fileName))
                            {
                                SendMessage(ctx.FileNameEditHwnd, WM_SETTEXT, IntPtr.Zero, "");
                                return (IntPtr)1;
                            }
                        }
                    }
                }
            }
        }

        return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _restrictionActive && _activeForegroundDialog != IntPtr.Zero &&
            (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            var fg = GetForegroundWindow();
            if (fg == _activeForegroundDialog)
            {
                var kbd = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                var vkCode = (int)kbd.vkCode;
                var altDown = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0 || (kbd.flags & 0x20) != 0;
                var ctrlDown = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;

                if (vkCode == VK_F1 || vkCode == VK_F3 || vkCode == VK_F4)
                    return (IntPtr)1;

                if (ctrlDown && (vkCode == 'L' || vkCode == 'F' || vkCode == 'E' || vkCode == 'N'))
                    return (IntPtr)1;

                if (altDown && (vkCode == 'D' || vkCode == VK_LEFT || vkCode == VK_RIGHT))
                    return (IntPtr)1;

                if (altDown && vkCode == VK_UP && !_activeDialogIsStrictSubfolder)
                    return (IntPtr)1;

                if (TryGetTypedChar(vkCode, kbd.scanCode, out var typedChar))
                {
                    if (typedChar == '\\' || typedChar == '/' || typedChar == ':' || typedChar == '%')
                        return (IntPtr)1;
                }

                if (vkCode == VK_BACK)
                {
                    var focused = GetFocusedControl(fg);
                    var cls = focused != IntPtr.Zero ? GetClassName(focused) : "";
                    if (cls != "Edit" && !_activeDialogIsStrictSubfolder)
                        return (IntPtr)1;
                }

                if (vkCode == VK_RETURN)
                {
                    DialogContext? ctx = null;
                    lock (_lockDialogs)
                    {
                        _trackedDialogs.TryGetValue(fg, out ctx);
                    }

                    if (ctx != null && ctx.FileNameEditHwnd != IntPtr.Zero)
                    {
                        var fileName = GetWindowTextHelper(ctx.FileNameEditHwnd);
                        if (IsForbiddenFileNameInput(fileName))
                        {
                            SendMessage(ctx.FileNameEditHwnd, WM_SETTEXT, IntPtr.Zero, "");
                            return (IntPtr)1;
                        }
                    }

                    var focused = GetFocusedControl(fg);
                    if (focused != IntPtr.Zero && GetClassName(focused) == "Edit")
                    {
                        var focusedText = GetWindowTextHelper(focused);
                        if (IsForbiddenFileNameInput(focusedText))
                        {
                            SendMessage(focused, WM_SETTEXT, IntPtr.Zero, "");
                            return (IntPtr)1;
                        }
                    }
                }
            }
        }

        return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
    }

    private static bool TryGetTypedChar(int vkCode, uint scanCode, out char ch)
    {
        ch = '\0';
        var keyState = new byte[256];
        if (!GetKeyboardState(keyState))
            return false;

        if ((GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0) keyState[VK_SHIFT] = 0x80;
        if ((GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0) keyState[VK_CONTROL] = 0x80;
        if ((GetAsyncKeyState(VK_MENU) & 0x8000) != 0) keyState[VK_MENU] = 0x80;

        var fg = GetForegroundWindow();
        var threadId = fg != IntPtr.Zero ? GetWindowThreadProcessId(fg, out _) : 0;
        var layout = GetKeyboardLayout(threadId);
        var sb = new StringBuilder(4);
        var res = ToUnicodeEx((uint)vkCode, scanCode, keyState, sb, sb.Capacity, 0, layout);
        if (res > 0 && sb.Length > 0)
        {
            ch = sb[0];
            return true;
        }
        return false;
    }

    private void CloseDialog(IntPtr hwnd, int pid, string reason)
    {
        PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        try
        {
            string procName;
            try { procName = Process.GetProcessById(pid)?.ProcessName ?? "desconhecido"; }
            catch { procName = "desconhecido"; }

            _db.InsertLog(null, "dialog_blocked", "", $"{reason}: PID {pid} ({procName})");
        }
        catch { }
    }

    private void LogDialogRestricted(int pid)
    {
        try
        {
            string procName;
            try { procName = Process.GetProcessById(pid)?.ProcessName ?? "desconhecido"; }
            catch { procName = "desconhecido"; }

            _db.InsertLog(null, "dialog_restricted", "", $"Navegação em diálogo de arquivo restrita: PID {pid} ({procName})");
        }
        catch { }
    }

    private static bool IsDescendantOfClass(IntPtr child, IntPtr root, string ancestorClass)
    {
        var cur = GetParent(child);
        while (cur != IntPtr.Zero && cur != root)
        {
            if (GetClassName(cur) == ancestorClass)
                return true;
            cur = GetParent(cur);
        }
        return false;
    }

    private static IntPtr GetFocusedControl(IntPtr fgWindow)
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

    private static void EnsureDirectoryExists(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }
        catch { }
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static string GetClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string GetWindowTextHelper(IntPtr hwnd)
    {
        var sb = new StringBuilder(512);
        SendMessage(hwnd, WM_GETTEXT, sb.Capacity, sb);
        if (sb.Length > 0) return sb.ToString();
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static void LogTrace(string category, string msg)
    {
        try
        {
            var logPath = Path.Combine(Path.GetTempPath(), "ApacKiosk_Trace.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] [{category}] {msg}\n");
        }
        catch { }
    }

    public void Dispose()
    {
        Stop();
    }

    private sealed class DialogContext
    {
        public IntPtr Hwnd;
        public int Pid;
        public DateTime CreatedAtUtc;
        public DateTime LastNavigationAttemptUtc = DateTime.MinValue;
        public DateTime LastUiaScanUtc = DateTime.MinValue;
        public int OutOfFolderFailures;
        public bool IsStrictSubfolder;
        public bool IsNavigatingAddressBar;

        public IntPtr UpBandHwnd;
        public IntPtr AddressBandHwnd;
        public IntPtr BreadcrumbToolbarHwnd;
        public IntPtr AddressEditHwnd;
        public IntPtr FileNameEditHwnd;
        public IntPtr ConfirmButtonHwnd;
        public IntPtr ShellDefViewHwnd;

        public readonly HashSet<IntPtr> BlockedChildHwnds = new();
        public List<UiAutomationHelper.DialogBlockedRegion> CachedUiaRegions = new();
        public List<RECT> BlockedScreenRects = new();
    }

    #region P/Invoke

    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType,
        IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax,
        IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc,
        uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool EnableWindow(IntPtr hWnd, bool bEnable);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetDlgCtrlID(IntPtr hwndCtl);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern bool GetKeyboardState(byte[] lpKeyState);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll")]
    private static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState,
        [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int SendMessage(IntPtr hWnd, uint Msg, int wParam, StringBuilder lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_QUIT = 0x0012;
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_GETTEXT = 0x000D;
    private const uint WM_SETTEXT = 0x000C;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const uint WM_SYSKEYDOWN = 0x0104;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONDOWN = 0x0204;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_MBUTTONDOWN = 0x0207;

    private const int SW_HIDE = 0;
    private const int VK_BACK = 0x08;
    private const int VK_RETURN = 0x0D;
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const int VK_LEFT = 0x25;
    private const int VK_UP = 0x26;
    private const int VK_RIGHT = 0x27;
    private const int VK_F1 = 0x70;
    private const int VK_F3 = 0x72;
    private const int VK_F4 = 0x73;

    #endregion
}
