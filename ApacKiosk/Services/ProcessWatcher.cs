using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using ApacKiosk.Database;

namespace ApacKiosk.Services
{
    public class ProcessWatcher : IDisposable
    {
        private readonly DatabaseManager _db;
        private Thread _watcherThread;
        private volatile bool _isRunning;
        private readonly HashSet<int> _allowedPids = new();
        private readonly object _lockAllowedPids = new();
        private int _suspiciousCount;

        private static readonly HashSet<string> _neverKill = new(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Idle", "Registry", "Memory Compression",
            "wininit.exe", "csrss.exe", "smss.exe", "services.exe",
            "lsass.exe", "winlogon.exe", "svchost.exe", "explorer.exe",
            "dwm.exe", "spoolsv.exe", "fontdrvhost.exe", "RuntimeBroker.exe",
            "ShellExperienceHost.exe", "SearchIndexer.exe", "SecurityHealthService.exe",
            "SecurityHealthSystray.exe", "TextInputHost.exe", "ctfmon.exe",
            "sihost.exe", "taskhostw.exe", "ApplicationFrameHost.exe",
            "SystemSettings.exe", "StartMenuExperienceHost.exe",
            "Widgets.exe", "audiodg.exe", "LogonUI.exe", "WUDFHost.exe",
        };

        private static readonly HashSet<string> _knownChildren = new(StringComparer.OrdinalIgnoreCase)
        {
            "splwow64.exe",
            "dllhost.exe",
            "conhost.exe",
        };

        public ProcessWatcher(DatabaseManager db)
        {
            _db = db;
        }

        public int SuspiciousCount
        {
            get { var c = _suspiciousCount; return c; }
        }

        public void Start()
        {
            if (_isRunning) return;
            _isRunning = true;
            _watcherThread = new Thread(Run)
            {
                IsBackground = true,
                Name = "ProcessWatcher"
            };
            _watcherThread.Start();
        }

        public void Stop()
        {
            _isRunning = false;
        }

        public void RegisterAllowedProcess(int pid)
        {
            lock (_lockAllowedPids)
            {
                _allowedPids.Add(pid);
            }
        }

        public void UnregisterAllowedProcess(int pid)
        {
            lock (_lockAllowedPids)
            {
                _allowedPids.Remove(pid);
            }
        }

        private void Run()
        {
            var myPid = 0;
            try { myPid = Environment.ProcessId; } catch { }
            var appExeName = "ApacKiosk.exe";

            while (_isRunning)
            {
                try
                {
                    var (allowedNames, allowedPaths) = BuildAllowedDataFromDatabase();
                    allowedNames.Add(appExeName);

                    var processes = Process.GetProcesses();
                    foreach (var proc in processes)
                    {
                        if (!_isRunning) break;
                        try
                        {
                            if (proc.Id == myPid) continue;
                            if (proc.HasExited) continue;

                            string procName;
                            try { procName = proc.ProcessName + ".exe"; } catch { continue; }

                            if (_neverKill.Contains(procName)) continue;
                            if (_knownChildren.Contains(procName)) continue;
                            if (TrustedProcessNames.IsWebViewRuntime(procName)) continue;

                            lock (_lockAllowedPids)
                            {
                                if (_allowedPids.Contains(proc.Id)) continue;
                            }

                            if (allowedNames.Contains(procName))
                            {
                                if (allowedPaths.Count > 0)
                                {
                                    var isPathValid = ValidatePathAndSignature(proc, procName, allowedPaths);
                                    if (!isPathValid)
                                    {
                                        KillProcess(proc, procName);
                                        continue;
                                    }
                                }
                                continue;
                            }

                            KillProcess(proc, procName);
                        }
                        catch { }
                    }
                }
                catch { }
                Thread.Sleep(200);
            }
        }

        private bool ValidatePathAndSignature(Process proc, string procName,
            Dictionary<string, string> allowedPaths)
        {
            string procPath;
            try
            {
                procPath = proc.MainModule.FileName;
            }
            catch (Win32Exception) { return true; }
            catch (InvalidOperationException) { return true; }
            catch { return true; }

            if (string.IsNullOrEmpty(procPath)) return true;

            var normalizedExePath = NormalizePath(procPath);

            if (allowedPaths.TryGetValue(procName, out var allowedPath))
            {
                var normalizedAllowed = NormalizePath(allowedPath);
                if (!string.Equals(normalizedExePath, normalizedAllowed, StringComparison.OrdinalIgnoreCase))
                {
                    _db.InsertLog(null, "process_blocked", null,
                        $"Processo bloqueado [caminho inválido]: {procName} em {procPath} (esperado: {allowedPath})");
                    return false;
                }
            }

            try
            {
                var cert = X509Certificate.CreateFromSignedFile(procPath);
                if (cert != null)
                {
                    var subject = cert.Subject?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(subject))
                    {
                        var knownPublishers = new[]
                        {
                            "Microsoft Corporation",
                            "Microsoft",
                            "Google LLC",
                            "Google Inc",
                            "Mozilla Corporation",
                            "Adobe Inc.",
                            "Adobe Systems",
                        };

                        var isKnown = false;
                        foreach (var publisher in knownPublishers)
                        {
                            if (subject.Contains(publisher, StringComparison.OrdinalIgnoreCase))
                            {
                                isKnown = true;
                                break;
                            }
                        }

                        if (!isKnown)
                        {
                            Interlocked.Increment(ref _suspiciousCount);
                            _db.InsertLog(null, "process_signature_warning", null,
                                $"Assinatura desconhecida: {procName} ({subject})");
                        }
                    }
                }
            }
            catch { }

            return true;
        }

        private (HashSet<string> names, Dictionary<string, string> paths) BuildAllowedDataFromDatabase()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var conn = _db.GetConnection();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT executable_path FROM allowed_programs";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var path = reader.GetString(0);
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    var name = Path.GetFileName(path);
                    if (!string.IsNullOrEmpty(name))
                    {
                        names.Add(name);
                        paths[name] = path;
                    }
                }
            }
            catch { }
            return (names, paths);
        }

        private static string NormalizePath(string path)
        {
            try
            {
                return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path;
            }
        }

        private void KillProcess(Process proc, string procName)
        {
            try
            {
                proc.Kill();
                try
                {
                    _db.InsertLog(null, "process_blocked", null,
                        $"Processo bloqueado [ProcessWatcher]: {procName} (PID {proc.Id})");
                }
                catch { }
            }
            catch { }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
