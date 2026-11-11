using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Timers;
using ApacKiosk.Utils;
using Timer = System.Timers.Timer;

namespace ApacKiosk.KioskService;

public class KioskMonitorService : ServiceBase
{
    private Timer _monitorTimer = null!;
    private Process? _kioskProcess;
    private ConfigManager? _config;
    private int _monitorIntervalSec = 3;
    private string _kioskExePath = "";

    public KioskMonitorService()
    {
        ServiceName = "KioskGuardianService";
        CanStop = true;
        CanShutdown = true;
    }

    protected override void OnStart(string[] args)
    {
        try
        {
            var dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ApacKiosk", "apac.db");
            var db = new ApacKiosk.Database.DatabaseManager(dbPath);
            db.Initialize();
            _config = new ConfigManager(db);

            _kioskExePath = _config.KioskExePath;

            _monitorIntervalSec = _config.MonitorIntervalSec;
            if (_monitorIntervalSec < 1) _monitorIntervalSec = 3;

            EventLog.WriteEntry("KioskGuardian", $"KioskService iniciado. Exe: {_kioskExePath}, Intervalo: {_monitorIntervalSec}s",
                EventLogEntryType.Information);

            _monitorTimer = new Timer(_monitorIntervalSec * 1000);
            _monitorTimer.Elapsed += MonitorKiosk;
            _monitorTimer.AutoReset = true;
            _monitorTimer.Start();

            MonitorKiosk(null, null!);
        }
        catch (Exception ex)
        {
            try { EventLog.WriteEntry("KioskGuardian", $"Erro ao iniciar: {ex.Message}", EventLogEntryType.Error); } catch { }
        }
    }

    private void MonitorKiosk(object? sender, ElapsedEventArgs e)
    {
        try
        {
            if (_kioskProcess == null || _kioskProcess.HasExited)
            {
                var sessionId = GetActiveConsoleSessionId();
                if (sessionId != 0xFFFFFFFF)
                {
                    StartProcessInUserSession(_kioskExePath, sessionId);
                }
            }
        }
        catch (Exception ex)
        {
            try { EventLog.WriteEntry("KioskGuardian", $"Erro ao monitorar Kiosk: {ex.Message}", EventLogEntryType.Warning); } catch { }
        }
    }

    private void StartProcessInUserSession(string appPath, uint sessionId)
    {
        try
        {
            IntPtr userToken = IntPtr.Zero;
            var gotToken = WTSQueryUserToken(sessionId, ref userToken);
            if (!gotToken || userToken == IntPtr.Zero)
            {
                EventLog.WriteEntry("KioskGuardian", $"Nao foi possivel obter token para sessao {sessionId}", EventLogEntryType.Warning);
                return;
            }

            var si = new STARTUPINFO();
            si.cb = Marshal.SizeOf(si);
            si.lpDesktop = "winsta0\\default";

            var pi = new PROCESS_INFORMATION();

            IntPtr envBlock = IntPtr.Zero;
            CreateEnvironmentBlock(ref envBlock, userToken, false);

            var result = CreateProcessAsUser(
                userToken,
                appPath,
                null,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                0x00000400,
                envBlock,
                Path.GetDirectoryName(appPath),
                ref si,
                out pi);

            if (envBlock != IntPtr.Zero)
                DestroyEnvironmentBlock(envBlock);

            CloseHandle(userToken);

            if (result)
            {
                _kioskProcess = Process.GetProcessById((int)pi.dwProcessId);
                EventLog.WriteEntry("KioskGuardian", $"KioskShell iniciado: PID {pi.dwProcessId}", EventLogEntryType.Information);
            }
            else
            {
                EventLog.WriteEntry("KioskGuardian", $"CreateProcessAsUser falhou: {Marshal.GetLastWin32Error()}", EventLogEntryType.Error);
            }
        }
        catch (Exception ex)
        {
            try { EventLog.WriteEntry("KioskGuardian", $"Excecao ao iniciar processo: {ex.Message}", EventLogEntryType.Error); } catch { }
        }
    }

    protected override void OnStop()
    {
        try
        {
            _monitorTimer?.Stop();
            _monitorTimer?.Dispose();
            EventLog.WriteEntry("KioskGuardian", "KioskService parado", EventLogEntryType.Information);
        }
        catch { }
    }

    protected override void OnShutdown()
    {
        OnStop();
        base.OnShutdown();
    }

    public void StartConsole()
    {
        OnStart(Array.Empty<string>());
    }

    public new void Stop()
    {
        OnStop();
    }

    #region P/Invoke

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, ref IntPtr phToken);

    [DllImport("kernel32.dll")]
    private static extern uint GetActiveConsoleSessionId();

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(
        IntPtr hToken,
        string lpApplicationName,
        string? lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(ref IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public int cb;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpDesktop;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    #endregion
}
