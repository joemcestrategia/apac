using ApacKiosk.Interop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ApacKiosk.Security;

public class ProcessWatchdog : IDisposable
{
    private CancellationTokenSource? _cts;
    private readonly Action<string> _onBlocked;

    private static readonly HashSet<string> BlockedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "taskmgr", "Taskmgr", "regedit", "cmd", "powershell", "powershell_ise",
        "mmc", "msconfig", "gpedit", "eventvwr", "procexp", "procexp64",
        "procmon", "procmon64", "wireshark", "fiddler", "everywhere",
        "x64dbg", "x32dbg", "ollydbg", "autoruns", "autoruns64",
        "processhacker", "SystemInformer"
    };

    public ProcessWatchdog(Action<string> onBlocked)
    {
        _onBlocked = onBlocked;
    }

    public void Start()
    {
        Stop();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Task.Run(() => WatchLoop(token), token);
    }

    private void WatchLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var procs = Process.GetProcesses();
                foreach (var proc in procs)
                {
                    if (token.IsCancellationRequested) break;
                    try
                    {
                        if (BlockedProcesses.Contains(proc.ProcessName))
                        {
                            _onBlocked($"Processo bloqueado: {proc.ProcessName}");
                            proc.Kill();
                        }
                    }
                    catch { }
                }
            }
            catch { }

            try { Task.Delay(2000, token).Wait(token); }
            catch { break; }
        }
    }

    public void Stop()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
