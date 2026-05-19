using System.Diagnostics;

namespace Apac.App.Security;

public class ProcessWatchdog : IDisposable
{
    private readonly HashSet<string> _forbiddenProcesses;
    private readonly DatabaseManager? _db;
    private CancellationTokenSource? _cts;
    private Task? _watchTask;

    public ProcessWatchdog(DatabaseManager? db = null)
    {
        _db = db;
        _forbiddenProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "taskmgr", "regedit", "cmd", "powershell", "powershell_ise",
            "mmc", "msconfig", "gpedit", "eventvwr", "procexp", "procexp64",
            "procmon", "wireshark", "fiddler", "x64dbg", "x32dbg",
            "ollydbg", "autoruns", "autoruns64", "explorer",
            "rundll32", "mshta", "wscript", "cscript", "conhost"
        };

        var preventPID = Process.GetCurrentProcess().Id;
        _forbiddenProcesses.Add(Process.GetProcessById(preventPID).ProcessName);
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _watchTask = Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var currentPid = Process.GetCurrentProcess().Id;
                    var processes = Process.GetProcesses();
                    foreach (var proc in processes)
                    {
                        if (proc.Id == currentPid) continue;
                        try
                        {
                            if (_forbiddenProcesses.Contains(proc.ProcessName))
                            {
                                _db?.InsertLog("process_blocked", null, null, null,
                                    $"Processo bloqueado: {proc.ProcessName} (PID: {proc.Id})");
                                proc.Kill();
                            }
                        }
                        catch { }
                    }
                }
                catch { }
                await Task.Delay(2000, _cts.Token);
            }
        }, _cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _watchTask?.Wait(TimeSpan.FromSeconds(3));
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose() => Stop();
}
