using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Apac.Database;
using Apac.Models;

namespace Apac.Services
{
    public class SecurityService : IDisposable
    {
        private Form _targetForm;
        private CancellationTokenSource _cts;
        private List<Task> _securityTasks = new List<Task>();
        private List<int> _registeredHotkeys = new List<int>();
        private IntPtr _shellEventHook = IntPtr.Zero;
        private bool _isDisposing;

        private const uint WINEVENT_OUTOFCONTEXT = 0;
        private const uint EVENT_SYSTEM_FOREGROUND = 3;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);
        private WinEventDelegate _winEventDelegate;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll")]
        private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        public SecurityService(Form targetForm)
        {
            _targetForm = targetForm;
        }

        public void EnableAllSecurity()
        {
            _cts = new CancellationTokenSource();

            BlockHotkeys();
            StartProcessWatchdog();
            StartAlwaysOnTop();
            ProtectOwnProcess();

            _targetForm.FormClosing += OnFormClosing;
        }

        public void DisableAllSecurity()
        {
            _isDisposing = true;
            _cts?.Cancel();

            foreach (var id in _registeredHotkeys)
                UnregisterHotKey(_targetForm.Handle, id);
            _registeredHotkeys.Clear();

            if (_shellEventHook != IntPtr.Zero)
            {
                UnhookWinEvent(_shellEventHook);
                _shellEventHook = IntPtr.Zero;
            }

            Task.WaitAll(_securityTasks.ToArray(), 3000);
            _targetForm.FormClosing -= OnFormClosing;
        }

        private void BlockHotkeys()
        {
            var keys = new (uint mod, uint key)[]
            {
                (0, 0x5B),                     // LWin
                (0, 0x5C),                     // RWin
                (8, 0x44),                     // Win+D
                (8, 0x52),                     // Win+R
                (8, 0x4C),                     // Win+L
                (8, 0x45),                     // Win+E
                (8, 0x54),                     // Win+T
                (0, 0x73),                     // F4 (Alt+F4 via modifier check in WndProc)
                (1, 0x09),                     // Alt+Tab
                (1, 0x1B),                     // Alt+Esc
                (2, 0x1B),                     // Ctrl+Esc
                (6, 0x1B),                     // Ctrl+Shift+Esc
                (0, 0x7A),                     // F11
                (2, 0x2E),                     // Ctrl+Del
                (3, 0x2E),                     // Ctrl+Alt+Del
            };

            for (int i = 0; i < keys.Length; i++)
            {
                bool result = RegisterHotKey(_targetForm.Handle, i + 1000, keys[i].mod, keys[i].key);
                if (result) _registeredHotkeys.Add(i + 1000);
            }
        }

        public void HandleHotkeyMessage(ref Message m)
        {
            const int WM_HOTKEY = 0x0312;
            if (m.Msg == WM_HOTKEY)
            {
                m.Result = IntPtr.Zero;
            }
        }

        private void StartProcessWatchdog()
        {
            var task = Task.Run(() =>
            {
                var blockedProcesses = new string[]
                {
                    "taskmgr", "regedit", "cmd", "powershell", "powershell_ise",
                    "mmc", "msconfig", "gpedit", "eventvwr", "procexp", "procmon",
                    "wireshark", "fiddler", "x64dbg", "ollydbg", "autoruns",
                    "ProcessHacker", "ProcessExplorer"
                };

                while (!_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        foreach (var procName in blockedProcesses)
                        {
                            try
                            {
                                var processes = Process.GetProcessesByName(procName);
                                foreach (var proc in processes)
                                {
                                    try { proc.Kill(); } catch { }
                                    DatabaseService.Instance.InsertLogEntry(new LogEntry
                                    {
                                        Type = "system_event",
                                        Details = $"Blocked process: {procName}"
                                    });
                                }
                            }
                            catch { }
                        }

                        Thread.Sleep(2000);
                    }
                    catch { }
                }
            }, _cts.Token);
            _securityTasks.Add(task);
        }

        private void StartAlwaysOnTop()
        {
            var topmostTask = Task.Run(() =>
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        _targetForm.Invoke(new Action(() =>
                        {
                            SetWindowPos(_targetForm.Handle, HWND_TOPMOST, 0, 0, 0, 0,
                                SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                        }));
                    }
                    catch { }
                    Thread.Sleep(1000);
                }
            }, _cts.Token);
            _securityTasks.Add(topmostTask);

            _winEventDelegate = new WinEventDelegate(WinEventProc);
            var focusTask = Task.Run(() =>
            {
                _shellEventHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
                    IntPtr.Zero, _winEventDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);

                while (!_cts.Token.IsCancellationRequested)
                {
                    System.Windows.Forms.Application.DoEvents();
                    Thread.Sleep(100);
                }
            }, _cts.Token);
            _securityTasks.Add(focusTask);
        }

        private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (hwnd != _targetForm.Handle && hwnd != IntPtr.Zero)
            {
                try
                {
                    _targetForm.Invoke(new Action(() =>
                    {
                        BringWindowToTop(_targetForm.Handle);
                        SetForegroundWindow(_targetForm.Handle);
                    }));
                }
                catch { }
            }
        }

        private void ProtectOwnProcess()
        {
            try
            {
                var process = GetCurrentProcess();
                SetProcessWorkingSetSize(process, new IntPtr(-1), new IntPtr(-1));
            }
            catch { }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_isDisposing) return;

            e.Cancel = true;

            using (var dialog = new Form())
            {
                dialog.Text = "Confirmar Saída";
                dialog.Size = new System.Drawing.Size(400, 220);
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterScreen;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;

                var label = new Label
                {
                    Text = "Digite a senha de administrador para fechar o APAC:",
                    Location = new System.Drawing.Point(20, 20),
                    Size = new System.Drawing.Size(340, 30)
                };

                var textBox = new TextBox
                {
                    Location = new System.Drawing.Point(20, 60),
                    Size = new System.Drawing.Size(340, 25),
                    PasswordChar = '*'
                };

                var okButton = new Button
                {
                    Text = "Fechar",
                    Location = new System.Drawing.Point(200, 120),
                    Size = new System.Drawing.Size(80, 30),
                    DialogResult = DialogResult.OK
                };

                var cancelButton = new Button
                {
                    Text = "Cancelar",
                    Location = new System.Drawing.Point(290, 120),
                    Size = new System.Drawing.Size(80, 30),
                    DialogResult = DialogResult.Cancel
                };

                dialog.Controls.AddRange(new Control[] { label, textBox, okButton, cancelButton });
                dialog.AcceptButton = okButton;
                dialog.CancelButton = cancelButton;

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    var hash = DatabaseService.Instance.GetAdminPasswordHash("admin");
                    if (hash != null && BCrypt.Net.BCrypt.Verify(textBox.Text, hash))
                    {
                        _isDisposing = true;
                        e.Cancel = false;
                        Application.Exit();
                    }
                    else
                    {
                        MessageBox.Show("Senha incorreta.", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        public void RestoreSystem()
        {
            DisableAllSecurity();
        }

        public void Dispose()
        {
            DisableAllSecurity();
            _cts?.Dispose();
        }
    }
}
