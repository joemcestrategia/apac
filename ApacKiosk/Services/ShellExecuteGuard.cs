using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;
using ApacKiosk.Database;

namespace ApacKiosk.Services
{
    public class ShellExecuteGuard : IDisposable
    {
        private readonly DatabaseManager _db;
        private ManagementEventWatcher _watcher;
        private volatile bool _isRunning;
        private readonly HashSet<int> _allowedPids = new();
        private readonly object _lockAllowed = new();
        private readonly HashSet<string> _allowedSitePatterns = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _allowedExeNames = new(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> _safeChildProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "conhost.exe",
            "splwow64.exe",
            "dllhost.exe",
        };

        private static readonly HashSet<string> _osProtectedProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Idle", "Registry", "Memory Compression",
            "wininit.exe", "csrss.exe", "smss.exe", "services.exe",
            "lsass.exe", "winlogon.exe", "svchost.exe", "explorer.exe",
            "dwm.exe", "spoolsv.exe", "fontdrvhost.exe",
        };

        public event Action<string> UrlRedirectRequested;

        public ShellExecuteGuard(DatabaseManager db)
        {
            _db = db;
            LoadAllowedFromDatabase();
        }

        public void Start()
        {
            if (_isRunning) return;
            _isRunning = true;

            var thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "ShellExecuteGuard",
                ApartmentState = ApartmentState.MTA
            };
            thread.Start();
        }

        public void Stop()
        {
            _isRunning = false;
            try { _watcher?.Stop(); } catch { }
            try { _watcher?.Dispose(); } catch { }
            _watcher = null;
        }

        public void RegisterAllowedProcess(int pid)
        {
            lock (_lockAllowed)
            {
                _allowedPids.Add(pid);
            }
        }

        public void UnregisterAllowedProcess(int pid)
        {
            lock (_lockAllowed)
            {
                _allowedPids.Remove(pid);
            }
        }

        public bool IsUrlAllowed(string url)
        {
            foreach (var pattern in _allowedSitePatterns)
            {
                if (MatchPattern(url, pattern))
                    return true;
            }
            return false;
        }

        private void LoadAllowedFromDatabase()
        {
            try
            {
                using var conn = _db.GetConnection();

                var siteCmd = conn.CreateCommand();
                siteCmd.CommandText = "SELECT url_pattern FROM allowed_sites";
                using var reader = siteCmd.ExecuteReader();
                while (reader.Read())
                {
                    _allowedSitePatterns.Add(reader.GetString(0));
                }

                var progCmd = conn.CreateCommand();
                progCmd.CommandText = "SELECT executable_path FROM allowed_programs";
                using var reader2 = progCmd.ExecuteReader();
                while (reader2.Read())
                {
                    var path = reader2.GetString(0);
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        var name = Path.GetFileName(path);
                        if (!string.IsNullOrEmpty(name))
                            _allowedExeNames.Add(name);
                    }
                }
                _allowedExeNames.Add(Process.GetCurrentProcess().ProcessName + ".exe");
            }
            catch { }
        }

        private void Run()
        {
            try
            {
                var scope = new ManagementScope(@"\\.\root\CIMV2");
                var query = new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace");
                _watcher = new ManagementEventWatcher(scope, query);
                _watcher.EventArrived += OnProcessStarted;
                _watcher.Start();

                while (_isRunning)
                {
                    Thread.Sleep(500);
                }
            }
            catch (Exception ex)
            {
                try
                {
                    _db.InsertLog(null, "system_event", null,
                        $"ShellExecuteGuard falhou ao iniciar: {ex.Message}");
                }
                catch { }
            }
        }

        private void OnProcessStarted(object sender, EventArrivedEventArgs e)
        {
            if (!_isRunning) return;

            try
            {
                var processName = e.NewEvent.Properties["ProcessName"]?.Value?.ToString() ?? "";
                var processId = Convert.ToInt32(e.NewEvent.Properties["ProcessID"]?.Value ?? 0);
                var parentProcessId = Convert.ToInt32(e.NewEvent.Properties["ParentProcessID"]?.Value ?? 0);

                if (processId == 0) return;

                if (processId == Environment.ProcessId) return;

                if (_osProtectedProcesses.Contains(processName)) return;
                if (TrustedProcessNames.IsWebViewRuntime(processName)) return;

                lock (_lockAllowed)
                {
                    if (_allowedPids.Contains(processId)) return;
                    if (parentProcessId != 0 && _allowedPids.Contains(parentProcessId)
                        && _safeChildProcesses.Contains(processName))
                        return;
                }

                if (_allowedExeNames.Contains(processName)) return;

                string commandLine;
                try
                {
                    using var proc = Process.GetProcessById(processId);
                    commandLine = GetCommandLine(processId) ?? "";
                }
                catch
                {
                    commandLine = "";
                }

                if (!string.IsNullOrEmpty(commandLine) && HasUrlInCommandLine(commandLine))
                {
                    var url = ExtractUrl(commandLine);
                    if (url != null)
                    {
                        if (IsUrlAllowed(url))
                        {
                            KillProcess(processId);
                            UrlRedirectRequested?.Invoke(url);
                            LogEvent($"URL redirecionada ao WebView2: {url} (de {processName})");
                        }
                        else
                        {
                            KillProcess(processId);
                            LogEvent($"URL bloqueada: {url} (de {processName})");
                        }
                        return;
                    }
                }

                var isBrowser = IsBrowserProcess(processName);
                if (isBrowser && !string.IsNullOrEmpty(commandLine))
                {
                    var url = ExtractUrl(commandLine);
                    if (url != null)
                    {
                        if (IsUrlAllowed(url))
                        {
                            KillProcess(processId);
                            UrlRedirectRequested?.Invoke(url);
                            LogEvent($"URL redirecionada: {url} (de {processName})");
                            return;
                        }
                    }
                }

                KillProcess(processId);
                LogEvent($"Execução bloqueada: {processName} (PID {processId})");
            }
            catch { }
        }

        private void KillProcess(int pid)
        {
            try
            {
                using var proc = Process.GetProcessById(pid);
                if (!proc.HasExited)
                {
                    proc.Kill();
                    Thread.Sleep(50);
                }
            }
            catch { }
        }

        private void LogEvent(string description)
        {
            try
            {
                _db.InsertLog(null, "shellexec_blocked", null, description);
            }
            catch { }
        }

        private static bool HasUrlInCommandLine(string commandLine)
        {
            return commandLine.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
                   commandLine.Contains("https://", StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractUrl(string commandLine)
        {
            var match = Regex.Match(commandLine, @"https?://[^\s""]+");
            return match.Success ? match.Value : null;
        }

        private static bool IsBrowserProcess(string processName)
        {
            var browserNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "chrome.exe", "firefox.exe", "msedge.exe", "iexplore.exe",
                "opera.exe", "brave.exe", "chromium.exe"
            };
            return browserNames.Contains(processName);
        }

        private static bool MatchPattern(string url, string pattern)
        {
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(pattern))
                return false;

            if (pattern == "*")
                return true;

            if (pattern.Contains("*"))
            {
                var regexPattern = "^" + Regex.Escape(pattern)
                    .Replace("\\*", ".*") + "$";
                return Regex.IsMatch(url, regexPattern, RegexOptions.IgnoreCase);
            }

            var uri = url.Trim().TrimEnd('/').ToLowerInvariant();
            var pat = pattern.Trim().TrimEnd('/').ToLowerInvariant();

            if (uri == pat)
                return true;
            if (uri.StartsWith(pat))
                return true;

            try
            {
                var uriObj = new Uri(url.Trim());
                var hostOnly = uriObj.GetComponents(UriComponents.Host, UriFormat.Unescaped);
                if (hostOnly.Equals(pattern.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { }

            return false;
        }

        private static string GetCommandLine(int pid)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
                using var results = searcher.Get();
                foreach (ManagementObject obj in results)
                {
                    var cmd = obj["CommandLine"]?.ToString();
                    if (!string.IsNullOrEmpty(cmd))
                        return cmd;
                }
            }
            catch { }
            return null;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
