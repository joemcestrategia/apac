using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ApacKiosk.Forms;

namespace ApacKiosk.Services;

public class UpdateManager
{
    private const string GitHubRepo = "joemcestrategia/apac";
    private readonly HttpClient _client;

    public string? LastFailureMessage { get; private set; }
    public bool HadUpdateAttempt { get; private set; }

    public UpdateManager()
    {
        _client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ApacKiosk", "1.0"));
    }

    public async Task CheckAndApplyUpdateAsync(string? hardwareFingerprint = null)
    {
        try
        {
            LastFailureMessage = null;
            HadUpdateAttempt = false;
            LogUpdate("CloudSync", "Iniciando verificacao periodica de atualizacao em segundo plano.");

            var updateInfo = await CheckForUpdatesAsync();
            if (updateInfo == null || !updateInfo.UpdateAvailable)
                return;

            string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            string currentDir = Path.GetDirectoryName(currentExe) ?? "";
            if (!CanWriteToDirectory(currentDir))
            {
                LogUpdate("CloudSync", $"Nova versao v{updateInfo.LatestVersion} disponivel, mas sem permissao de escrita em '{currentDir}' para atualizacao silenciosa em segundo plano.");
                return;
            }

            HadUpdateAttempt = true;
            var (downloadOk, tempExeOrError) = await DownloadUpdateFileAsync(updateInfo, null);
            if (!downloadOk)
            {
                LastFailureMessage = tempExeOrError;
                LogUpdateError("CloudSync", $"Falha no download v{updateInfo.LatestVersion}: {tempExeOrError}");
                return;
            }

            if (!ApplyAtomicUpdate(tempExeOrError, out var applyErr))
            {
                LastFailureMessage = applyErr;
                LogUpdateError("CloudSync", $"Falha ao aplicar atualizacao v{updateInfo.LatestVersion}: {applyErr}");
            }
        }
        catch (Exception ex)
        {
            LastFailureMessage = ex.Message;
            LogUpdateError("CloudSync", $"Erro no CheckAndApplyUpdateAsync: {ex}");
        }
    }

    public async Task<bool> CheckAndApplyStartupUpdateAsync(Action<string>? statusCallback = null, Action<int>? progressCallback = null)
    {
        try
        {
            LastFailureMessage = null;
            HadUpdateAttempt = false;
            LogUpdate("Startup", $"Iniciando checagem de atualizacao no boot. Versao atual=v{GetCurrentVersion()}, PID={Environment.ProcessId}, Admin={IsAdministrator()}, Exe='{Process.GetCurrentProcess().MainModule?.FileName}'");

            using var checkCts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var updateInfo = await CheckForUpdatesAsync(checkCts.Token);
            if (updateInfo == null)
            {
                LogUpdate("Startup", "CheckForUpdatesAsync retornou null (sem conexao, timeout ou resposta invalida).");
                return false;
            }

            if (!updateInfo.UpdateAvailable)
            {
                LogUpdate("Startup", $"Sistema ja atualizado (atual=v{updateInfo.CurrentVersion}, remota=v{updateInfo.LatestVersion}).");
                return false;
            }

            HadUpdateAttempt = true;
            LogUpdate("Startup", $"Nova versao detectada: v{updateInfo.LatestVersion}. Iniciando download de '{updateInfo.DownloadUrl}'.");
            statusCallback?.Invoke($"Nova versão v{updateInfo.LatestVersion} encontrada. Baixando atualização...");

            var (downloadOk, tempExeOrError) = await DownloadUpdateFileAsync(updateInfo, progressCallback);
            if (!downloadOk)
            {
                LastFailureMessage = tempExeOrError;
                LogUpdateError("Startup", $"Falha no download da atualizacao de inicializacao: {tempExeOrError}");
                return false;
            }

            statusCallback?.Invoke("Aplicando atualização e reiniciando...");
            LogUpdate("Startup", $"Download concluido e validado em '{tempExeOrError}'. Chamando ApplyAtomicUpdate...");

            if (!ApplyAtomicUpdate(tempExeOrError, out var applyError))
            {
                LastFailureMessage = applyError ?? "Não foi possível substituir o executável.";
                LogUpdateError("Startup", $"ApplyAtomicUpdate falhou: {LastFailureMessage}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            LastFailureMessage = ex.Message;
            LogUpdateError("Startup", $"Erro na atualizacao de inicializacao: {ex}");
            return false;
        }
    }

    public async Task<UpdateCheckResult?> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var currentVersion = GetCurrentVersion();
            var apiUrl = $"https://api.github.com/repos/{GitHubRepo}/releases/latest";
            LogUpdate("Check", $"Consultando GitHub Releases: {apiUrl} (versao local v{currentVersion})");

            var response = await _client.GetAsync(apiUrl, cancellationToken);
            LogUpdate("Check", $"Resposta GitHub API: HTTP {(int)response.StatusCode} ({response.StatusCode})");
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
            var versionString = tagName.TrimStart('v', 'V');
            if (string.IsNullOrWhiteSpace(versionString))
            {
                LogUpdate("Check", "tag_name vazio na release do GitHub.");
                return null;
            }

            if (!Version.TryParse(versionString, out var remoteVer))
            {
                LogUpdate("Check", $"tag_name '{tagName}' nao pode ser interpretado como Version.");
                return null;
            }

            if (remoteVer <= currentVersion)
            {
                return new UpdateCheckResult
                {
                    UpdateAvailable = false,
                    CurrentVersion = currentVersion.ToString(),
                    LatestVersion = remoteVer.ToString()
                };
            }

            string downloadUrl = "";
            string expectedSha256 = "";
            string releaseNotes = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";

            if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsProp.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                    if (name.Equals("ApacKiosk.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.TryGetProperty("browser_download_url", out var urlProp) ? urlProp.GetString() ?? "" : "";
                    }
                    else if (name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase) || name.Equals("sha256.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        var shaUrl = asset.TryGetProperty("browser_download_url", out var shaUrlProp) ? shaUrlProp.GetString() ?? "" : "";
                        if (!string.IsNullOrEmpty(shaUrl))
                        {
                            try
                            {
                                var shaText = await _client.GetStringAsync(shaUrl, cancellationToken);
                                expectedSha256 = shaText.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                            }
                            catch (Exception ex)
                            {
                                LogUpdate("Check", $"Aviso: falha ao baixar arquivo SHA-256 ({ex.Message}).");
                            }
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(downloadUrl))
            {
                LogUpdate("Check", $"Release {tagName} encontrada, mas asset ApacKiosk.exe ausente.");
                return null;
            }

            LogUpdate("Check", $"Update disponivel: v{remoteVer} | SHA256='{expectedSha256}' | URL='{downloadUrl}'");
            return new UpdateCheckResult
            {
                UpdateAvailable = true,
                CurrentVersion = currentVersion.ToString(),
                LatestVersion = remoteVer.ToString(),
                DownloadUrl = downloadUrl,
                Sha256 = expectedSha256,
                ReleaseNotes = releaseNotes
            };
        }
        catch (Exception ex)
        {
            LogUpdateError("Check", $"Erro ao verificar atualizacao no GitHub: {ex.Message}");
            return null;
        }
    }

    public async Task<(bool success, string message)> PerformManualUpdateAsync(Control? owner = null, Action<string>? progressCallback = null)
    {
        LastFailureMessage = null;
        HadUpdateAttempt = false;
        LogUpdate("Manual", $"Verificacao manual iniciada. Versao atual=v{GetCurrentVersion()}, Admin={IsAdministrator()}");

        progressCallback?.Invoke("Verificando se há novas atualizações disponíveis...");
        var updateInfo = await CheckForUpdatesAsync();

        if (updateInfo == null)
        {
            return (false, "Não foi possível verificar atualizações no momento. Verifique sua conexão com a internet.");
        }

        if (!updateInfo.UpdateAvailable)
        {
            return (true, $"O sistema já está na versão mais recente (v{updateInfo.CurrentVersion}). Nenhuma atualização necessária.");
        }

        var confirm = KioskDialog.ShowMessage(
            $"Nova versão encontrada: v{updateInfo.LatestVersion} (Versão atual: v{updateInfo.CurrentVersion}).\n\n" +
            "Seus dados configurados, usuários, senhas e logs serão mantidos intactos.\n\n" +
            "Deseja baixar e aplicar a atualização agora?",
            "Atualização de Sistema",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            owner);

        if (confirm != DialogResult.Yes)
        {
            LogUpdate("Manual", "Atualizacao cancelada pelo administrador na confirmacao.");
            return (false, "Atualização cancelada pelo administrador.");
        }

        HadUpdateAttempt = true;
        progressCallback?.Invoke("Baixando atualização...");
        var (downloadOk, tempExeOrError) = await DownloadUpdateFileAsync(updateInfo, pct =>
        {
            progressCallback?.Invoke($"Baixando atualização ({pct}%)...");
        });

        if (!downloadOk)
        {
            LastFailureMessage = tempExeOrError;
            LogUpdateError("Manual", $"Falha ao baixar atualizacao manual: {tempExeOrError}");
            return (false, $"Falha ao baixar atualização:\n{tempExeOrError}");
        }

        progressCallback?.Invoke("Instalando atualização e reiniciando...");
        bool applied = ApplyAtomicUpdate(tempExeOrError, out var applyErr);
        if (!applied)
        {
            LastFailureMessage = applyErr;
            LogUpdateError("Manual", $"Falha ao aplicar atualizacao manual: {applyErr}");
            return (false, $"Falha ao aplicar a atualização:\n{applyErr ?? "Erro desconhecido (consulte ApacKiosk_Update.log)."}");
        }

        return (true, "Atualização aplicada com sucesso.");
    }

    private async Task<(bool success, string result)> DownloadUpdateFileAsync(
        UpdateCheckResult updateInfo,
        Action<int>? progressCallback,
        CancellationToken cancellationToken = default)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "ApacUpdates");
        string tempExe = Path.Combine(tempDir, $"ApacKiosk_v{updateInfo.LatestVersion}.exe");
        string partExe = tempExe + ".part";

        try
        {
            if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

            CleanupFileQuietly(partExe);
            CleanupFileQuietly(tempExe);

            LogUpdate("Download", $"Baixando '{updateInfo.DownloadUrl}' -> '{partExe}'");
            using var response = await _client.GetAsync(updateInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            LogUpdate("Download", $"Tamanho informado pelo servidor: {totalBytes} bytes");

            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var fileStream = new FileStream(partExe, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long totalRead = 0;
                int lastPct = -1;
                int lastLoggedQuarter = -1;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) != 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    totalRead += bytesRead;

                    if (totalBytes > 0)
                    {
                        int pct = Math.Clamp((int)((totalRead * 100L) / totalBytes), 0, 100);
                        if (pct != lastPct)
                        {
                            lastPct = pct;
                            progressCallback?.Invoke(pct);
                        }

                        int quarter = pct / 25;
                        if (quarter > lastLoggedQuarter)
                        {
                            lastLoggedQuarter = quarter;
                            LogUpdate("Download", $"Progresso: {pct}% ({totalRead}/{totalBytes} bytes)");
                        }
                    }
                }

                await fileStream.FlushAsync(cancellationToken);
                LogUpdate("Download", $"Stream finalizado: {totalRead} bytes gravados (esperado={totalBytes}).");

                if (totalRead <= 0 || (totalBytes > 0 && totalRead != totalBytes))
                {
                    fileStream.Close();
                    CleanupFileQuietly(partExe);
                    return (false, $"Download incompleto ({totalRead} de {totalBytes} bytes).");
                }
            }

            if (!string.IsNullOrEmpty(updateInfo.Sha256))
            {
                string fileHash = ComputeSha256(partExe);
                LogUpdate("Download", $"Validando SHA-256: esperado={updateInfo.Sha256}, calculado={fileHash}");
                if (!fileHash.Equals(updateInfo.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    CleanupFileQuietly(partExe);
                    return (false, "Falha na verificação de integridade (Hash SHA-256 não confere).");
                }
            }

            if (File.Exists(tempExe))
            {
                tempExe = Path.Combine(tempDir, $"ApacKiosk_v{updateInfo.LatestVersion}_{DateTime.Now:HHmmss}.exe");
            }
            File.Move(partExe, tempExe, overwrite: true);
            LogUpdate("Download", $"Arquivo final validado e pronto em '{tempExe}'.");
            progressCallback?.Invoke(100);

            return (true, tempExe);
        }
        catch (Exception ex)
        {
            CleanupFileQuietly(partExe);
            LogUpdateError("Download", $"Excecao no download: {ex}");
            return (false, ex.Message);
        }
    }

    public static bool ApplyAtomicUpdate(string newExePath) => ApplyAtomicUpdate(newExePath, out _);

    public static bool ApplyAtomicUpdate(string newExePath, out string? errorMessage)
    {
        errorMessage = null;
        try
        {
            if (string.IsNullOrWhiteSpace(newExePath) || !File.Exists(newExePath))
            {
                errorMessage = "Arquivo de atualização não encontrado no disco.";
                LogUpdateError("Apply", errorMessage);
                return false;
            }

            string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? Application.ExecutablePath;
            if (string.IsNullOrWhiteSpace(currentExe))
            {
                errorMessage = "Não foi possível determinar o caminho do executável atual.";
                LogUpdateError("Apply", errorMessage);
                return false;
            }

            string currentDir = Path.GetDirectoryName(currentExe) ?? "";
            bool isAdmin = IsAdministrator();
            bool canWrite = CanWriteToDirectory(currentDir);
            int currentPid = Environment.ProcessId;

            LogUpdate("Apply", $"Preparando aplicacao da atualizacao. Alvo='{currentExe}', Novo='{newExePath}', PID={currentPid}, IsAdmin={isAdmin}, CanWriteDir={canWrite}");

            bool needsElevation = !canWrite;
            Program.PrepareForUpdateExit(needsElevation);

            var psi = new ProcessStartInfo
            {
                FileName = newExePath,
                Arguments = $"--apply-update \"{currentExe}\" --wait-pid={currentPid}",
                WorkingDirectory = Path.GetDirectoryName(newExePath) ?? Path.GetTempPath(),
                UseShellExecute = true,
                Verb = needsElevation ? "runas" : ""
            };

            LogUpdate("Apply", $"Iniciando processo updater em '%TEMP%': FileName='{psi.FileName}', Args='{psi.Arguments}', Verb='{psi.Verb}'");

            Process? updaterProc;
            try
            {
                updaterProc = Process.Start(psi);
            }
            catch (Win32Exception wex) when (wex.NativeErrorCode == 1223)
            {
                errorMessage = "Permissão de administrador negada ou cancelada no UAC.";
                LogUpdateError("Apply", $"{errorMessage} (Win32 1223)");
                Program.RestoreAfterFailedUpdateExit(needsElevation);
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                LogUpdateError("Apply", $"Falha ao iniciar updater externo: {ex}");
                Program.RestoreAfterFailedUpdateExit(needsElevation);
                return false;
            }

            if (updaterProc == null)
            {
                errorMessage = "Não foi possível iniciar o instalador de atualização.";
                LogUpdateError("Apply", errorMessage);
                Program.RestoreAfterFailedUpdateExit(needsElevation);
                return false;
            }

            LogUpdate("Apply", $"Updater externo iniciado com sucesso (UpdaterPID={updaterProc.Id}). Encerrando processo antigo PID={currentPid}...");
            Environment.Exit(0);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            LogUpdateError("Apply", $"ApplyAtomicUpdate error: {ex}");
            return false;
        }
    }

    public static int ExecuteApplyUpdateMode(string[] args)
    {
        try
        {
            string targetExe = "";
            int waitPid = 0;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].Equals("--apply-update", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                {
                    targetExe = args[i + 1].Trim('"');
                }
                else if (args[i].StartsWith("--target=", StringComparison.OrdinalIgnoreCase))
                {
                    targetExe = args[i].Substring("--target=".Length).Trim('"');
                }
                else if (args[i].StartsWith("--wait-pid=", StringComparison.OrdinalIgnoreCase))
                {
                    int.TryParse(args[i].Substring("--wait-pid=".Length), out waitPid);
                }
            }

            string selfExe = Process.GetCurrentProcess().MainModule?.FileName ?? Application.ExecutablePath;
            LogUpdate("Updater", $"Modo --apply-update iniciado. Self='{selfExe}', Target='{targetExe}', WaitPID={waitPid}, IsAdmin={IsAdministrator()}, UpdaterPID={Environment.ProcessId}");

            if (string.IsNullOrWhiteSpace(targetExe) || string.IsNullOrWhiteSpace(selfExe))
            {
                LogUpdateError("Updater", "Parametro --apply-update sem caminho de destino valido.");
                return 1;
            }

            if (waitPid > 0 && waitPid != Environment.ProcessId)
            {
                WaitForProcessExit(waitPid, 15000);
            }

            string targetDir = Path.GetDirectoryName(targetExe) ?? "";
            string backupExe = Path.Combine(targetDir, "ApacKiosk.old.exe");
            string selfSha = ComputeSha256Static(selfExe);

            Exception? lastEx = null;
            bool copied = false;

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
                        File.Move(targetExe, backupExe, overwrite: true);
                    }

                    File.Copy(selfExe, targetExe, overwrite: true);
                    string targetSha = ComputeSha256Static(targetExe);

                    if (!string.Equals(selfSha, targetSha, StringComparison.OrdinalIgnoreCase))
                    {
                        LogUpdateError("Updater", $"Tentativa {attempt}: SHA-256 do destino divergiu (esperado={selfSha}, destino={targetSha}). Restaurando backup...");
                        if (File.Exists(backupExe))
                        {
                            try { File.Copy(backupExe, targetExe, true); } catch { }
                        }
                        Thread.Sleep(300);
                        continue;
                    }

                    copied = true;
                    LogUpdate("Updater", $"Substituicao concluida na tentativa {attempt}. SHA-256 conferido: {targetSha}");
                    break;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    LogUpdate("Updater", $"Tentativa {attempt}/20 de substituir '{targetExe}' falhou: {ex.GetType().Name} - {ex.Message}");
                    Thread.Sleep(300);
                }
            }

            if (!copied)
            {
                LogUpdateError("Updater", $"Falha definitiva ao substituir '{targetExe}': {lastEx}");
                if (File.Exists(backupExe) && !File.Exists(targetExe))
                {
                    try { File.Move(backupExe, targetExe); } catch { }
                }
                return 2;
            }

            CleanupFileQuietly(backupExe);

            var restartPsi = new ProcessStartInfo
            {
                FileName = targetExe,
                Arguments = $"--cleanup-updates --wait-pid={Environment.ProcessId}",
                WorkingDirectory = targetDir,
                UseShellExecute = true
            };

            var started = Process.Start(restartPsi);
            LogUpdate("Updater", $"Nova versao iniciada: Target='{targetExe}', NewPID={started?.Id ?? 0}. Encerrando updater.");
            return 0;
        }
        catch (Exception ex)
        {
            LogUpdateError("Updater", $"Erro fatal em ExecuteApplyUpdateMode: {ex}");
            return 1;
        }
    }

    public static void HandlePostUpdateCleanup(string[] args)
    {
        try
        {
            int waitPid = 0;
            bool isCleanup = false;
            foreach (var arg in args)
            {
                if (arg.Equals("--cleanup-updates", StringComparison.OrdinalIgnoreCase))
                    isCleanup = true;
                else if (arg.StartsWith("--wait-pid=", StringComparison.OrdinalIgnoreCase))
                    int.TryParse(arg.Substring("--wait-pid=".Length), out waitPid);
            }

            if (waitPid > 0)
            {
                WaitForProcessExit(waitPid, 6000);
            }

            string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? Application.ExecutablePath;
            string currentDir = Path.GetDirectoryName(currentExe) ?? "";
            if (!string.IsNullOrEmpty(currentDir))
            {
                CleanupFileQuietly(Path.Combine(currentDir, "ApacKiosk.old.exe"));
            }

            if (isCleanup)
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "ApacUpdates");
                if (Directory.Exists(tempDir))
                {
                    foreach (var f in Directory.GetFiles(tempDir))
                    {
                        CleanupFileQuietly(f);
                    }
                }
                var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                LogUpdate("Boot", $"Pos-atualizacao concluido! Versao em execucao=v{ver}, Exe='{currentExe}', Admin={IsAdministrator()}, PID={Environment.ProcessId}");
            }
        }
        catch { }
    }

    private static void WaitForProcessExit(int pid, int timeoutMs)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            if (!proc.HasExited)
            {
                LogUpdate("Updater", $"Aguardando encerramento do processo PID={pid} (ate {timeoutMs}ms)...");
                proc.WaitForExit(timeoutMs);
            }
        }
        catch (ArgumentException)
        {
        }
        catch (Exception ex)
        {
            LogUpdate("Updater", $"Aviso ao aguardar PID={pid}: {ex.Message}");
        }
    }

    private static bool CanWriteToDirectory(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return false;

        try
        {
            string probe = Path.Combine(dir, $".apac_write_probe_{Environment.ProcessId}_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "probe");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static void CleanupFileQuietly(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    private Version GetCurrentVersion()
    {
        return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);
    }

    private string ComputeSha256(string filePath) => ComputeSha256Static(filePath);

    private static string ComputeSha256Static(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static void LogUpdate(string stage, string msg)
    {
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{stage}] {msg}\n";
        AppendLogSafe(Path.Combine(Path.GetTempPath(), "ApacKiosk_Update.log"), line);
        AppendLogSafe(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ApacKiosk", "ApacKiosk_Update.log"), line);
    }

    private static void LogUpdateError(string stage, string msg)
    {
        LogUpdate($"ERRO:{stage}", msg);
        AppendLogSafe(Path.Combine(Path.GetTempPath(), "ApacKiosk_UpdateError.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{stage}] {msg}\n");
    }

    private void LogUpdateError(string msg) => LogUpdateError("Update", msg);

    private static void AppendLogSafe(string filePath, string content)
    {
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.AppendAllText(filePath, content);
        }
        catch { }
    }

    public class UpdateCheckResult
    {
        public bool UpdateAvailable { get; set; }
        public string CurrentVersion { get; set; } = "";
        public string LatestVersion { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public string ReleaseNotes { get; set; } = "";
    }
}
