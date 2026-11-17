using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace ApacKiosk.Services;

public static class WindowsUpdaterService
{
    public const string ServiceName = "ApacKioskUpdater";
    public const string ServiceDisplayName = "APAC Kiosk Guardian Updater";
    public const string PipeName = "ApacKioskUpdaterPipe";
    public const string ServiceHostExeName = "ApacUpdaterHost.exe";
    public const string ServiceArg = "--update-service";
    private const string GitHubRepo = "joemcestrategia/apac";

    private static IntPtr _serviceStatusHandle = IntPtr.Zero;
    private static SERVICE_STATUS _serviceStatus;
    private static readonly ManualResetEventSlim _stopEvent = new(false);
    private static readonly object _applySyncLock = new();
    private static ServiceMainProc? _serviceMainDelegate;
    private static HandlerExProc? _handlerExDelegate;

    public static bool IsServiceInstalled()
    {
        IntPtr scm = OpenSCManagerW(null, null, SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) return false;
        try
        {
            IntPtr svc = OpenServiceW(scm, ServiceName, SERVICE_QUERY_STATUS);
            if (svc == IntPtr.Zero) return false;
            CloseServiceHandle(svc);
            return true;
        }
        finally
        {
            CloseServiceHandle(scm);
        }
    }

    public static bool IsServiceRunning()
    {
        IntPtr scm = OpenSCManagerW(null, null, SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) return false;
        try
        {
            IntPtr svc = OpenServiceW(scm, ServiceName, SERVICE_QUERY_STATUS);
            if (svc == IntPtr.Zero) return false;
            try
            {
                if (!QueryServiceState(svc, out uint state) || state != SERVICE_RUNNING)
                    return false;
            }
            finally
            {
                CloseServiceHandle(svc);
            }
        }
        finally
        {
            CloseServiceHandle(scm);
        }

        return PingService();
    }

    public static bool PingService(int timeoutMs = 1500)
    {
        try
        {
            var resp = SendPipeCommand(new PipeRequest { Action = "PING" }, timeoutMs);
            return resp != null && resp.Ok;
        }
        catch
        {
            return false;
        }
    }

    public static bool InstallOrUpdateService(string targetExePath, out string? errorMessage)
    {
        errorMessage = null;
        try
        {
            if (string.IsNullOrWhiteSpace(targetExePath) || !File.Exists(targetExePath))
            {
                errorMessage = "Executável principal não encontrado para registrar serviço.";
                return false;
            }

            string targetDir = Path.GetDirectoryName(Path.GetFullPath(targetExePath)) ?? "";
            string hostExePath = Path.Combine(targetDir, ServiceHostExeName);
            string binCommand = $"\"{hostExePath}\" {ServiceArg}";

            EnsureSharedDataDirectoryPermissions();
            EnsureProtectedStagingDirectory();

            IntPtr scm = OpenSCManagerW(null, null, SC_MANAGER_ALL_ACCESS);
            if (scm == IntPtr.Zero)
            {
                errorMessage = $"Não foi possível abrir o Service Control Manager (Win32 {Marshal.GetLastWin32Error()}).";
                return false;
            }

            try
            {
                IntPtr svc = OpenServiceW(scm, ServiceName, SERVICE_ALL_ACCESS);
                if (svc != IntPtr.Zero)
                {
                    try
                    {
                        StopServiceAndWait(svc, 10000);
                        TerminateServiceHostProcesses(hostExePath);
                        CopyFileWithRetries(targetExePath, hostExePath);

                        if (!ChangeServiceConfigW(
                            svc,
                            SERVICE_WIN32_OWN_PROCESS,
                            SERVICE_AUTO_START,
                            SERVICE_ERROR_NORMAL,
                            binCommand,
                            null,
                            IntPtr.Zero,
                            null,
                            "LocalSystem",
                            null,
                            ServiceDisplayName))
                        {
                            UpdateManager.LogUpdate("Service", $"Aviso ChangeServiceConfigW: Win32 {Marshal.GetLastWin32Error()}");
                        }

                        if (!StartServiceAndWait(svc, 12000, out var startErr))
                        {
                            errorMessage = startErr;
                            return false;
                        }

                        UpdateManager.LogUpdate("Service", $"Servico '{ServiceName}' atualizado e iniciado com sucesso.");
                        return true;
                    }
                    finally
                    {
                        CloseServiceHandle(svc);
                    }
                }
                else
                {
                    TerminateServiceHostProcesses(hostExePath);
                    CopyFileWithRetries(targetExePath, hostExePath);

                    IntPtr created = CreateServiceW(
                        scm,
                        ServiceName,
                        ServiceDisplayName,
                        SERVICE_ALL_ACCESS,
                        SERVICE_WIN32_OWN_PROCESS,
                        SERVICE_AUTO_START,
                        SERVICE_ERROR_NORMAL,
                        binCommand,
                        null,
                        IntPtr.Zero,
                        null,
                        null,
                        null);

                    if (created == IntPtr.Zero)
                    {
                        errorMessage = $"CreateServiceW falhou (Win32 {Marshal.GetLastWin32Error()}).";
                        return false;
                    }

                    try
                    {
                        SetServiceDescription(created, "Serviço de atualização silenciosa do APAC Kiosk Guardian.");
                        if (!StartServiceAndWait(created, 12000, out var startErr))
                        {
                            errorMessage = startErr;
                            return false;
                        }
                        UpdateManager.LogUpdate("Service", $"Servico '{ServiceName}' instalado e iniciado com sucesso.");
                        return true;
                    }
                    finally
                    {
                        CloseServiceHandle(created);
                    }
                }
            }
            finally
            {
                CloseServiceHandle(scm);
            }
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            UpdateManager.LogUpdate("Service", $"Erro em InstallOrUpdateService: {ex}");
            return false;
        }
    }

    public static void UninstallService()
    {
        try
        {
            IntPtr scm = OpenSCManagerW(null, null, SC_MANAGER_ALL_ACCESS);
            if (scm != IntPtr.Zero)
            {
                try
                {
                    IntPtr svc = OpenServiceW(scm, ServiceName, SERVICE_ALL_ACCESS);
                    if (svc != IntPtr.Zero)
                    {
                        try
                        {
                            StopServiceAndWait(svc, 8000);
                            DeleteService(svc);
                        }
                        finally
                        {
                            CloseServiceHandle(svc);
                        }
                    }
                }
                finally
                {
                    CloseServiceHandle(scm);
                }
            }
        }
        catch { }

        TerminateServiceHostProcesses(null);
    }

    public static PipeResponse? ApplyUpdateViaService(string targetExe, string sourceExe, string targetVersion, string expectedSha256, int waitPid)
    {
        return SendPipeCommand(new PipeRequest
        {
            Action = "APPLY",
            TargetExe = targetExe,
            SourceExe = sourceExe,
            TargetVersion = targetVersion,
            ExpectedSha256 = expectedSha256,
            WaitPid = waitPid
        }, 60000);
    }

    public static PipeResponse? RollbackViaService(string targetExe)
    {
        return SendPipeCommand(new PipeRequest
        {
            Action = "ROLLBACK",
            TargetExe = targetExe
        }, 15000);
    }

    public static PipeResponse? CleanupBackupViaService(string targetExe)
    {
        return SendPipeCommand(new PipeRequest
        {
            Action = "CLEANUP_BACKUP",
            TargetExe = targetExe
        }, 10000);
    }

    public static bool TryFixDataDirectoryAclViaService()
    {
        try
        {
            var resp = SendPipeCommand(new PipeRequest { Action = "FIX_DATA_ACL" }, 5000);
            return resp != null && resp.Ok;
        }
        catch
        {
            return false;
        }
    }

    public static bool SetCtrlAltDelLockViaService(bool enabled)
    {
        try
        {
            var resp = SendPipeCommand(new PipeRequest
            {
                Action = "SET_CTRL_ALT_DEL_LOCK",
                Enabled = enabled
            }, 2500);
            return resp != null && resp.Ok;
        }
        catch
        {
            return false;
        }
    }

    public static void SetFastUserSwitchingPolicy(bool lockEnabled)
    {
        try
        {
            const string hklmSysPolicies = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
            if (lockEnabled)
            {
                using var key = Registry.LocalMachine.CreateSubKey(hklmSysPolicies, true);
                key?.SetValue("HideFastUserSwitching", 1, RegistryValueKind.DWord);
            }
            else
            {
                using var key = Registry.LocalMachine.OpenSubKey(hklmSysPolicies, true);
                key?.DeleteValue("HideFastUserSwitching", throwOnMissingValue: false);
            }
        }
        catch { }
    }

    public static void SetCtrlAltDelPoliciesForUser(string? userSid, bool lockEnabled)
    {
        SetFastUserSwitchingPolicy(lockEnabled);

        if (string.IsNullOrWhiteSpace(userSid))
            return;

        try
        {
            _ = new SecurityIdentifier(userSid);
        }
        catch
        {
            return;
        }

        ApplyUserPolicyKey(Registry.Users, $@"{userSid}\Software\Microsoft\Windows\CurrentVersion\Policies\System",
            lockEnabled, "DisableTaskMgr", "DisableLockWorkstation", "DisableChangePassword");
        ApplyUserPolicyKey(Registry.Users, $@"{userSid}\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer",
            lockEnabled, "NoClose", "NoLogoff");
    }

    private static void ApplyUserPolicyKey(RegistryKey root, string subKeyPath, bool lockEnabled, params string[] valueNames)
    {
        try
        {
            if (lockEnabled)
            {
                using var key = root.CreateSubKey(subKeyPath, true);
                if (key == null) return;
                foreach (var name in valueNames)
                    key.SetValue(name, 1, RegistryValueKind.DWord);
            }
            else
            {
                using var key = root.OpenSubKey(subKeyPath, true);
                if (key == null) return;
                foreach (var name in valueNames)
                    key.DeleteValue(name, throwOnMissingValue: false);
            }
        }
        catch { }
    }

    public static PipeResponse? SendPipeCommand(PipeRequest req, int timeoutMs)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.InOut,
                PipeOptions.None,
                TokenImpersonationLevel.Impersonation);
            client.Connect(Math.Min(timeoutMs, 3000));
            client.ReadMode = PipeTransmissionMode.Byte;

            string jsonReq = JsonSerializer.Serialize(req) + "\n";
            byte[] reqBytes = Encoding.UTF8.GetBytes(jsonReq);
            client.Write(reqBytes, 0, reqBytes.Length);
            client.Flush();

            using var reader = new StreamReader(client, Encoding.UTF8);
            var readTask = reader.ReadLineAsync();
            if (!readTask.Wait(timeoutMs))
                return new PipeResponse { Ok = false, Error = "Tempo limite aguardando resposta do serviço de atualização." };

            string? line = readTask.Result;
            if (string.IsNullOrWhiteSpace(line))
                return new PipeResponse { Ok = false, Error = "Resposta vazia do serviço de atualização." };

            return JsonSerializer.Deserialize<PipeResponse>(line);
        }
        catch (Exception ex)
        {
            return new PipeResponse { Ok = false, Error = ex.Message };
        }
    }

    public static int RunServiceEntryPoint()
    {
        try
        {
            UpdateManager.LogUpdate("Service", $"Iniciando {ServiceName} em PID={Environment.ProcessId}...");
            _serviceMainDelegate = ServiceMain;
            IntPtr procPtr = Marshal.GetFunctionPointerForDelegate(_serviceMainDelegate);

            var table = new SERVICE_TABLE_ENTRY[2];
            table[0] = new SERVICE_TABLE_ENTRY
            {
                lpServiceName = Marshal.StringToHGlobalUni(ServiceName),
                lpServiceProc = procPtr
            };
            table[1] = new SERVICE_TABLE_ENTRY
            {
                lpServiceName = IntPtr.Zero,
                lpServiceProc = IntPtr.Zero
            };

            try
            {
                bool ok = StartServiceCtrlDispatcherW(table);
                if (!ok)
                {
                    int err = Marshal.GetLastWin32Error();
                    UpdateManager.LogUpdate("Service", $"StartServiceCtrlDispatcherW falhou: Win32 {err}");
                    return err;
                }
                return 0;
            }
            finally
            {
                if (table[0].lpServiceName != IntPtr.Zero)
                    Marshal.FreeHGlobal(table[0].lpServiceName);
            }
        }
        catch (Exception ex)
        {
            UpdateManager.LogUpdate("Service", $"Excecao no RunServiceEntryPoint: {ex}");
            return 1;
        }
    }

    private static void ServiceMain(uint dwArgc, IntPtr lpszArgv)
    {
        try
        {
            _handlerExDelegate = ServiceCtrlHandler;
            _serviceStatusHandle = RegisterServiceCtrlHandlerExW(ServiceName, _handlerExDelegate, IntPtr.Zero);
            if (_serviceStatusHandle == IntPtr.Zero)
                return;

            _serviceStatus = new SERVICE_STATUS
            {
                dwServiceType = SERVICE_WIN32_OWN_PROCESS,
                dwCurrentState = SERVICE_START_PENDING,
                dwControlsAccepted = SERVICE_ACCEPT_STOP | SERVICE_ACCEPT_SHUTDOWN
            };
            SetServiceStatus(_serviceStatusHandle, ref _serviceStatus);

            EnsureSharedDataDirectoryPermissions();
            EnsureProtectedStagingDirectory();

            _stopEvent.Reset();
            var serverThread = new Thread(RunPipeServerLoop)
            {
                IsBackground = true,
                Name = "ApacUpdaterPipeServer"
            };
            serverThread.Start();

            _serviceStatus.dwCurrentState = SERVICE_RUNNING;
            _serviceStatus.dwWaitHint = 0;
            SetServiceStatus(_serviceStatusHandle, ref _serviceStatus);

            UpdateManager.LogUpdate("Service", $"Pipe server '{PipeName}' ativo como SYSTEM/Admin.");
            _stopEvent.Wait();

            _serviceStatus.dwCurrentState = SERVICE_STOPPED;
            _serviceStatus.dwWin32ExitCode = 0;
            SetServiceStatus(_serviceStatusHandle, ref _serviceStatus);
        }
        catch (Exception ex)
        {
            UpdateManager.LogUpdate("Service", $"Erro fatal em ServiceMain: {ex}");
            if (_serviceStatusHandle != IntPtr.Zero)
            {
                _serviceStatus.dwCurrentState = SERVICE_STOPPED;
                _serviceStatus.dwWin32ExitCode = 1;
                SetServiceStatus(_serviceStatusHandle, ref _serviceStatus);
            }
        }
    }

    private static uint ServiceCtrlHandler(uint dwControl, uint dwEventType, IntPtr lpEventData, IntPtr lpContext)
    {
        if (dwControl == SERVICE_CONTROL_STOP || dwControl == SERVICE_CONTROL_SHUTDOWN)
        {
            _serviceStatus.dwCurrentState = SERVICE_STOP_PENDING;
            _serviceStatus.dwWaitHint = 3000;
            SetServiceStatus(_serviceStatusHandle, ref _serviceStatus);
            _stopEvent.Set();
        }
        return 0;
    }

    private static void RunPipeServerLoop()
    {
        var pipeSecurity = CreatePipeSecurity();

        while (!_stopEvent.IsSet)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = NamedPipeServerStreamAcl.Create(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    4096,
                    4096,
                    pipeSecurity);

                var waitTask = server.WaitForConnectionAsync();
                while (!waitTask.IsCompleted)
                {
                    if (_stopEvent.IsSet)
                    {
                        server.Dispose();
                        return;
                    }
                    Thread.Sleep(150);
                }
                waitTask.GetAwaiter().GetResult();

                var connectedPipe = server;
                server = null;
                Task.Run(() =>
                {
                    try
                    {
                        ProcessPipeClient(connectedPipe);
                    }
                    finally
                    {
                        try { connectedPipe.Dispose(); } catch { }
                    }
                });
            }
            catch (Exception ex)
            {
                if (!_stopEvent.IsSet)
                {
                    UpdateManager.LogUpdate("Service", $"Erro no loop do pipe: {ex.Message}");
                    Thread.Sleep(300);
                }
            }
            finally
            {
                if (server != null)
                {
                    try { server.Dispose(); } catch { }
                }
            }
        }
    }

    private static void ProcessPipeClient(NamedPipeServerStream server)
    {
        PipeResponse response;
        try
        {
            using var reader = new StreamReader(server, Encoding.UTF8, false, 4096, leaveOpen: true);
            string? line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
            {
                response = new PipeResponse { Ok = false, Error = "Comando vazio." };
            }
            else
            {
                string clientSid = "";
                try
                {
                    server.RunAsClient(() =>
                    {
                        using var identity = WindowsIdentity.GetCurrent();
                        clientSid = identity.User?.Value ?? "";
                    });
                }
                catch { }

                var req = JsonSerializer.Deserialize<PipeRequest>(line);
                response = req == null
                    ? new PipeResponse { Ok = false, Error = "Requisição inválida." }
                    : ExecutePipeAction(req, clientSid);
            }
        }
        catch (Exception ex)
        {
            response = new PipeResponse { Ok = false, Error = ex.Message };
        }

        try
        {
            byte[] respBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response) + "\n");
            server.Write(respBytes, 0, respBytes.Length);
            server.Flush();
            server.WaitForPipeDrain();
        }
        catch { }
    }

    private static PipeResponse ExecutePipeAction(PipeRequest req, string clientSid = "")
    {
        string action = (req.Action ?? "").Trim().ToUpperInvariant();
        if (action == "PING")
        {
            return new PipeResponse { Ok = true, Message = $"v{UpdateManager.GetCurrentVersion()}" };
        }

        if (action == "FIX_DATA_ACL")
        {
            EnsureSharedDataDirectoryPermissions();
            EnsureProtectedStagingDirectory();
            return new PipeResponse { Ok = true, Message = "Permissões de dados ajustadas." };
        }

        if (action == "SET_CTRL_ALT_DEL_LOCK")
        {
            SetCtrlAltDelPoliciesForUser(clientSid, req.Enabled ?? true);
            return new PipeResponse { Ok = true, Message = "Política Ctrl+Alt+Del atualizada." };
        }

        string selfExe = Process.GetCurrentProcess().MainModule?.FileName ?? Environment.ProcessPath ?? "";
        string serviceDir = Path.GetDirectoryName(Path.GetFullPath(selfExe)) ?? "";
        string allowedTargetExe = Path.GetFullPath(Path.Combine(serviceDir, "ApacKiosk.exe"));

        if (string.IsNullOrWhiteSpace(req.TargetExe) ||
            !string.Equals(Path.GetFullPath(req.TargetExe), allowedTargetExe, StringComparison.OrdinalIgnoreCase))
        {
            UpdateManager.LogUpdate("Service", $"Bloqueado: TargetExe nao autorizado '{req.TargetExe}' (esperado '{allowedTargetExe}').");
            return new PipeResponse { Ok = false, Error = "Caminho de destino não autorizado pelo serviço." };
        }

        if (action == "CLEANUP_BACKUP")
        {
            try
            {
                string backupExe = Path.Combine(serviceDir, "ApacKiosk.old.exe");
                if (File.Exists(backupExe)) File.Delete(backupExe);
                ScheduleHostSelfRefreshIfNeeded(allowedTargetExe);
                return new PipeResponse { Ok = true, Message = "Backup limpo." };
            }
            catch (Exception ex)
            {
                return new PipeResponse { Ok = false, Error = ex.Message };
            }
        }

        if (action == "ROLLBACK")
        {
            string backupExe = Path.Combine(serviceDir, "ApacKiosk.old.exe");
            bool dummy = true;
            bool rolled = UpdateManager.RestoreBackupFromService(backupExe, allowedTargetExe, ref dummy);
            if (rolled)
            {
                EnsureTargetBinaryReadableByUsers(allowedTargetExe);
            }
            return new PipeResponse
            {
                Ok = rolled,
                Message = rolled ? "Versão anterior restaurada." : "Não foi possível restaurar o backup."
            };
        }

        if (action == "APPLY")
        {
            lock (_applySyncLock)
            {
                return ExecuteServiceApply(req, allowedTargetExe, serviceDir);
            }
        }

        return new PipeResponse { Ok = false, Error = $"Ação desconhecida: {req.Action}" };
    }

    private static PipeResponse ExecuteServiceApply(PipeRequest req, string targetExe, string targetDir)
    {
        string stagingDir = EnsureProtectedStagingDirectory();
        string stagedExe = Path.Combine(stagingDir, $"pending_{Guid.NewGuid():N}.exe");
        string backupExe = Path.Combine(targetDir, "ApacKiosk.old.exe");
        bool movedToBackup = false;

        try
        {
            if (string.IsNullOrWhiteSpace(req.SourceExe) || !File.Exists(req.SourceExe))
                return new PipeResponse { Ok = false, Error = "Pacote de atualização não encontrado." };

            File.Copy(req.SourceExe, stagedExe, true);
            string stagedSha = ComputeSha256(stagedExe);

            if (!VerifyOfficialGitHubReleaseAsync(req.TargetVersion, stagedSha, targetExe).GetAwaiter().GetResult())
            {
                return new PipeResponse { Ok = false, Error = "Falha na verificação oficial de integridade da atualização." };
            }

            if (req.WaitPid > 0 && req.WaitPid != Environment.ProcessId)
            {
                WaitOrKillProcess(req.WaitPid, 6000);
            }
            UpdateManager.TerminateInstancesAtPathStatic(targetExe);

            for (int attempt = 1; attempt <= 20; attempt++)
            {
                try
                {
                    if (File.Exists(backupExe))
                    {
                        try { File.Delete(backupExe); } catch { }
                    }

                    if (File.Exists(targetExe))
                    {
                        File.Move(targetExe, backupExe, true);
                        movedToBackup = true;
                    }

                    File.Copy(stagedExe, targetExe, true);
                    string targetSha = ComputeSha256(targetExe);

                    if (!string.Equals(stagedSha, targetSha, StringComparison.OrdinalIgnoreCase))
                    {
                        UpdateManager.RestoreBackupFromService(backupExe, targetExe, ref movedToBackup);
                        Thread.Sleep(300);
                        continue;
                    }

                    UpdateManager.LogUpdate("Service", $"Atualizacao aplicada sem UAC na tentativa {attempt}. SHA-256={targetSha}");
                    EnsureTargetBinaryReadableByUsers(targetExe);
                    return new PipeResponse { Ok = true, Message = "Atualização aplicada com sucesso." };
                }
                catch (Exception ex)
                {
                    UpdateManager.LogUpdate("Service", $"Tentativa {attempt}/20 falhou no servico: {ex.Message}");
                    if (attempt == 5 || attempt == 12)
                    {
                        UpdateManager.TerminateInstancesAtPathStatic(targetExe);
                    }
                    Thread.Sleep(300);
                }
            }

            UpdateManager.RestoreBackupFromService(backupExe, targetExe, ref movedToBackup);
            return new PipeResponse { Ok = false, Error = "Não foi possível substituir o executável após 20 tentativas." };
        }
        catch (Exception ex)
        {
            UpdateManager.LogUpdate("Service", $"Erro em ExecuteServiceApply: {ex}");
            UpdateManager.RestoreBackupFromService(backupExe, targetExe, ref movedToBackup);
            return new PipeResponse { Ok = false, Error = ex.Message };
        }
        finally
        {
            try { if (File.Exists(stagedExe)) File.Delete(stagedExe); } catch { }
        }
    }

    private static async Task<bool> VerifyOfficialGitHubReleaseAsync(string requestedVersion, string stagedSha, string targetExe)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ApacKioskUpdater", "1.0"));

            string versionClean = (requestedVersion ?? "").Trim().TrimStart('v', 'V');
            Version? targetVer = Version.TryParse(versionClean, out var parsed) ? parsed : null;

            if (targetVer != null && File.Exists(targetExe))
            {
                var info = FileVersionInfo.GetVersionInfo(targetExe);
                if (Version.TryParse(info.FileVersion, out var localVer) && targetVer <= localVer)
                {
                    UpdateManager.LogUpdate("Service", $"Versao solicitada v{targetVer} nao e superior a instalada v{localVer}.");
                    return false;
                }
            }

            string officialSha = "";
            if (targetVer != null)
            {
                string directShaUrl = $"https://github.com/{GitHubRepo}/releases/download/v{targetVer}/ApacKiosk.exe.sha256";
                try
                {
                    var shaText = await client.GetStringAsync(directShaUrl);
                    officialSha = shaText.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                }
                catch (Exception ex)
                {
                    UpdateManager.LogUpdate("Service", $"Aviso ao consultar SHA direto '{directShaUrl}': {ex.Message}");
                }
            }

            if (string.IsNullOrEmpty(officialSha))
            {
                var response = await client.GetAsync($"https://api.github.com/repos/{GitHubRepo}/releases/latest");
                if (!response.IsSuccessStatusCode)
                {
                    UpdateManager.LogUpdate("Service", $"Falha ao validar release oficial: HTTP {(int)response.StatusCode}");
                    return false;
                }

                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var root = doc.RootElement;
                string tag = root.TryGetProperty("tag_name", out var tp) ? (tp.GetString() ?? "").TrimStart('v', 'V') : "";

                if (Version.TryParse(tag, out var remoteVer) && File.Exists(targetExe))
                {
                    var info = FileVersionInfo.GetVersionInfo(targetExe);
                    if (Version.TryParse(info.FileVersion, out var localVer) && remoteVer <= localVer)
                    {
                        UpdateManager.LogUpdate("Service", $"Versao remota v{remoteVer} nao e superior a instalada v{localVer}.");
                        return false;
                    }
                }

                if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assetsProp.EnumerateArray())
                    {
                        var name = asset.TryGetProperty("name", out var np) ? np.GetString() ?? "" : "";
                        if (name.Equals("ApacKiosk.exe.sha256", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("sha256.txt", StringComparison.OrdinalIgnoreCase))
                        {
                            var shaUrl = asset.TryGetProperty("browser_download_url", out var up) ? up.GetString() ?? "" : "";
                            if (!string.IsNullOrEmpty(shaUrl))
                            {
                                var shaText = await client.GetStringAsync(shaUrl);
                                officialSha = shaText.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                                break;
                            }
                        }
                    }
                }
            }

            bool match = !string.IsNullOrEmpty(officialSha) &&
                         officialSha.Equals(stagedSha, StringComparison.OrdinalIgnoreCase);

            UpdateManager.LogUpdate("Service", $"Validacao oficial GitHub SHA-256: match={match}, oficial={officialSha}, staging={stagedSha}");
            return match;
        }
        catch (Exception ex)
        {
            UpdateManager.LogUpdate("Service", $"Erro na validacao oficial GitHub: {ex.Message}");
            return false;
        }
    }

    private static void ScheduleHostSelfRefreshIfNeeded(string updatedTargetExe)
    {
        try
        {
            string dir = Path.GetDirectoryName(updatedTargetExe) ?? "";
            string hostExe = Path.Combine(dir, ServiceHostExeName);
            if (!File.Exists(updatedTargetExe) || !File.Exists(hostExe)) return;
            if (ComputeSha256(updatedTargetExe).Equals(ComputeSha256(hostExe), StringComparison.OrdinalIgnoreCase))
                return;

            string refreshDir = EnsureProtectedStagingDirectory();
            string tempRefresher = Path.Combine(refreshDir, "ApacHostRefresher.exe");
            File.Copy(updatedTargetExe, tempRefresher, true);

            Task.Run(() =>
            {
                Thread.Sleep(5000);
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = tempRefresher,
                        Arguments = $"--install-update-service \"{updatedTargetExe}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                }
                catch { }
            });
        }
        catch { }
    }

    public static void EnsureSharedDataDirectoryPermissions()
    {
        string baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ApacKiosk");
        try
        {
            Directory.CreateDirectory(baseDir);

            var authUsers = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
            var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var adminsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

            var dirInfo = new DirectoryInfo(baseDir);
            var dirSec = dirInfo.GetAccessControl();
            dirSec.AddAccessRule(new FileSystemAccessRule(
                authUsers,
                FileSystemRights.Modify,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            dirSec.AddAccessRule(new FileSystemAccessRule(
                adminsSid,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            dirSec.AddAccessRule(new FileSystemAccessRule(
                systemSid,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            dirInfo.SetAccessControl(dirSec);

            foreach (string file in Directory.GetFiles(baseDir))
            {
                GrantModifyOnFile(file, authUsers);
            }

            foreach (string subDir in Directory.GetDirectories(baseDir))
            {
                if (Path.GetFileName(subDir).Equals("ServiceStaging", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var subInfo = new DirectoryInfo(subDir);
                    var subSec = subInfo.GetAccessControl();
                    subSec.AddAccessRule(new FileSystemAccessRule(
                        authUsers,
                        FileSystemRights.Modify,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None,
                        AccessControlType.Allow));
                    subInfo.SetAccessControl(subSec);

                    foreach (string file in Directory.GetFiles(subDir, "*", SearchOption.AllDirectories))
                    {
                        GrantModifyOnFile(file, authUsers);
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            UpdateManager.LogUpdate("Service", $"Aviso ao ajustar ACL de ProgramData: {ex.Message}");
        }
    }

    private static void GrantModifyOnFile(string filePath, IdentityReference identity)
    {
        try
        {
            var fi = new FileInfo(filePath);
            var fs = fi.GetAccessControl();
            fs.SetAccessRuleProtection(isProtected: false, preserveInheritance: true);
            fs.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.Modify, AccessControlType.Allow));
            fi.SetAccessControl(fs);
        }
        catch { }
    }

    private static void EnsureTargetBinaryReadableByUsers(string targetExe)
    {
        try
        {
            if (!File.Exists(targetExe)) return;
            var fi = new FileInfo(targetExe);
            var fs = fi.GetAccessControl();
            fs.SetAccessRuleProtection(isProtected: false, preserveInheritance: true);
            fs.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                FileSystemRights.ReadAndExecute,
                AccessControlType.Allow));
            fi.SetAccessControl(fs);
        }
        catch { }
    }

    private static string EnsureProtectedStagingDirectory()
    {
        string stagingDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ApacKiosk", "ServiceStaging");
        Directory.CreateDirectory(stagingDir);
        try
        {
            var ds = new DirectorySecurity();
            ds.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            ds.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            ds.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            new DirectoryInfo(stagingDir).SetAccessControl(ds);
        }
        catch { }
        return stagingDir;
    }

    private static PipeSecurity CreatePipeSecurity()
    {
        var ps = new PipeSecurity();
        ps.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        ps.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        ps.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));
        return ps;
    }

    private static void CopyFileWithRetries(string src, string dst)
    {
        for (int attempt = 1; attempt <= 10; attempt++)
        {
            try
            {
                File.Copy(src, dst, true);
                return;
            }
            catch when (attempt < 10)
            {
                Thread.Sleep(300);
            }
        }
    }

    private static void TerminateServiceHostProcesses(string? hostExePath)
    {
        try
        {
            int selfPid = Environment.ProcessId;
            foreach (var proc in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ServiceHostExeName)))
            {
                try
                {
                    if (proc.Id == selfPid) continue;
                    if (!string.IsNullOrEmpty(hostExePath))
                    {
                        string? p = null;
                        try { p = proc.MainModule?.FileName; } catch { }
                        if (!string.IsNullOrEmpty(p) && !string.Equals(Path.GetFullPath(p), Path.GetFullPath(hostExePath), StringComparison.OrdinalIgnoreCase))
                            continue;
                    }
                    proc.Kill();
                    proc.WaitForExit(3000);
                }
                catch { }
                finally { proc.Dispose(); }
            }
        }
        catch { }
    }

    private static void WaitOrKillProcess(int pid, int timeoutMs)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            if (!proc.HasExited)
            {
                if (!proc.WaitForExit(timeoutMs))
                {
                    proc.Kill();
                    proc.WaitForExit(3000);
                }
            }
        }
        catch { }
    }

    private static string ComputeSha256(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }

    private static void StopServiceAndWait(IntPtr svc, int timeoutMs)
    {
        if (!QueryServiceState(svc, out uint state) || state == SERVICE_STOPPED)
            return;

        var ss = new SERVICE_STATUS();
        ControlService(svc, SERVICE_CONTROL_STOP, ref ss);

        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (QueryServiceState(svc, out uint cur) && cur == SERVICE_STOPPED)
                return;
            Thread.Sleep(200);
        }
    }

    private static bool StartServiceAndWait(IntPtr svc, int timeoutMs, out string? error)
    {
        error = null;
        if (QueryServiceState(svc, out uint state) && state == SERVICE_RUNNING)
            return true;

        if (!StartServiceW(svc, 0, IntPtr.Zero))
        {
            int err = Marshal.GetLastWin32Error();
            if (err != 1056)
            {
                error = $"StartServiceW falhou (Win32 {err}).";
                return false;
            }
        }

        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (QueryServiceState(svc, out uint cur) && cur == SERVICE_RUNNING)
                return true;
            Thread.Sleep(200);
        }

        error = "Tempo limite aguardando o serviço de atualização iniciar.";
        return false;
    }

    private static bool QueryServiceState(IntPtr svc, out uint state)
    {
        state = 0;
        int size = Marshal.SizeOf<SERVICE_STATUS_PROCESS>();
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!QueryServiceStatusEx(svc, SC_STATUS_PROCESS_INFO, buffer, size, out _))
                return false;
            var info = Marshal.PtrToStructure<SERVICE_STATUS_PROCESS>(buffer);
            state = info.dwCurrentState;
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void SetServiceDescription(IntPtr svc, string description)
    {
        try
        {
            var desc = new SERVICE_DESCRIPTION { lpDescription = Marshal.StringToHGlobalUni(description) };
            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf<SERVICE_DESCRIPTION>());
            try
            {
                Marshal.StructureToPtr(desc, ptr, false);
                ChangeServiceConfig2W(svc, SERVICE_CONFIG_DESCRIPTION, ptr);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
                Marshal.FreeHGlobal(desc.lpDescription);
            }
        }
        catch { }
    }

    public sealed class PipeRequest
    {
        public string Action { get; set; } = "";
        public string TargetExe { get; set; } = "";
        public string SourceExe { get; set; } = "";
        public string TargetVersion { get; set; } = "";
        public string ExpectedSha256 { get; set; } = "";
        public int WaitPid { get; set; }
        public bool? Enabled { get; set; }
    }

    public sealed class PipeResponse
    {
        public bool Ok { get; set; }
        public string Message { get; set; } = "";
        public string Error { get; set; } = "";
    }

    #region Win32 Service P/Invoke

    private delegate void ServiceMainProc(uint dwArgc, IntPtr lpszArgv);
    private delegate uint HandlerExProc(uint dwControl, uint dwEventType, IntPtr lpEventData, IntPtr lpContext);

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_TABLE_ENTRY
    {
        public IntPtr lpServiceName;
        public IntPtr lpServiceProc;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS_PROCESS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
        public uint dwProcessId;
        public uint dwServiceFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_DESCRIPTION
    {
        public IntPtr lpDescription;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManagerW(string? lpMachineName, string? lpDatabaseName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateServiceW(
        IntPtr hSCManager, string lpServiceName, string lpDisplayName, uint dwDesiredAccess,
        uint dwServiceType, uint dwStartType, uint dwErrorControl, string lpBinaryPathName,
        string? lpLoadOrderGroup, IntPtr lpdwTagId, string? lpDependencies, string? lpServiceStartName, string? lpPassword);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenServiceW(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ChangeServiceConfigW(
        IntPtr hService, uint dwServiceType, uint dwStartType, uint dwErrorControl,
        string? lpBinaryPathName, string? lpLoadOrderGroup, IntPtr lpdwTagId,
        string? lpDependencies, string? lpServiceStartName, string? lpPassword, string? lpDisplayName);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ChangeServiceConfig2W(IntPtr hService, uint dwInfoLevel, IntPtr lpInfo);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool StartServiceW(IntPtr hService, uint dwNumServiceArgs, IntPtr lpServiceArgVectors);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool ControlService(IntPtr hService, uint dwControl, ref SERVICE_STATUS lpServiceStatus);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DeleteService(IntPtr hService);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr hSCObject);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatusEx(IntPtr hService, int InfoLevel, IntPtr lpBuffer, int cbBufSize, out int pcbBytesNeeded);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool StartServiceCtrlDispatcherW([In] SERVICE_TABLE_ENTRY[] lpServiceStartTable);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr RegisterServiceCtrlHandlerExW(string lpServiceName, HandlerExProc lpHandlerProc, IntPtr lpContext);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetServiceStatus(IntPtr hServiceStatus, ref SERVICE_STATUS lpServiceStatus);

    private const uint SC_MANAGER_CONNECT = 0x0001;
    private const uint SC_MANAGER_ALL_ACCESS = 0xF003F;
    private const uint SERVICE_QUERY_STATUS = 0x0004;
    private const uint SERVICE_ALL_ACCESS = 0xF01FF;
    private const uint SERVICE_WIN32_OWN_PROCESS = 0x00000010;
    private const uint SERVICE_AUTO_START = 0x00000002;
    private const uint SERVICE_ERROR_NORMAL = 0x00000001;
    private const uint SERVICE_STOPPED = 0x00000001;
    private const uint SERVICE_START_PENDING = 0x00000002;
    private const uint SERVICE_RUNNING = 0x00000004;
    private const uint SERVICE_STOP_PENDING = 0x00000003;
    private const uint SERVICE_ACCEPT_STOP = 0x00000001;
    private const uint SERVICE_ACCEPT_SHUTDOWN = 0x00000004;
    private const uint SERVICE_CONTROL_STOP = 0x00000001;
    private const uint SERVICE_CONTROL_SHUTDOWN = 0x00000005;
    private const uint SERVICE_CONFIG_DESCRIPTION = 1;
    private const int SC_STATUS_PROCESS_INFO = 0;

    #endregion
}
