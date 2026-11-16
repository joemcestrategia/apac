using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ApacKiosk.Forms;

namespace ApacKiosk.Services;

public sealed class UpdateProgressInfo
{
    public string Stage { get; init; } = "Info";
    public string Title { get; init; } = "";
    public int Percent { get; init; }
    public long BytesReceived { get; init; }
    public long TotalBytes { get; init; }
    public double BytesPerSecond { get; init; }
}

public class UpdateManager
{
    private const string GitHubRepo = "joemcestrategia/apac";
    private static readonly TimeSpan DownloadStallTimeout = TimeSpan.FromSeconds(20);
    private readonly HttpClient _client;

    public static event Action<string>? OnUpdateLog;

    public string? LastFailureMessage { get; private set; }
    public string? LastFailureStage { get; private set; }
    public string? LastCheckError { get; private set; }
    public bool HadUpdateAttempt { get; private set; }
    public UpdateCheckResult? LastCheckResult { get; private set; }

    public UpdateManager()
    {
        _client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ApacKiosk", "1.0"));
    }

    public async Task CheckAndApplyUpdateAsync(string? hardwareFingerprint = null)
    {
        try
        {
            ResetRunState();
            LogUpdate("CloudSync", "Iniciando verificacao periodica de atualizacao em segundo plano.");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var updateInfo = await CheckForUpdatesAsync(cts.Token);
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
                LastFailureStage = "Download";
                LastFailureMessage = tempExeOrError;
                LogUpdateError("CloudSync", $"Falha no download v{updateInfo.LatestVersion}: {tempExeOrError}");
                return;
            }

            if (!ApplyAtomicUpdate(tempExeOrError, out var applyErr))
            {
                LastFailureStage = "Apply";
                LastFailureMessage = applyErr;
                LogUpdateError("CloudSync", $"Falha ao aplicar atualizacao v{updateInfo.LatestVersion}: {applyErr}");
            }
        }
        catch (Exception ex)
        {
            LastFailureStage = "CloudSync";
            LastFailureMessage = ex.Message;
            LogUpdateError("CloudSync", $"Erro no CheckAndApplyUpdateAsync: {ex}");
        }
    }

    public Task<bool> CheckAndApplyStartupUpdateAsync(Action<string>? statusCallback = null, Action<int>? progressCallback = null)
    {
        return CheckAndApplyStartupUpdateAsync(
            info =>
            {
                statusCallback?.Invoke(info.Title);
                if (info.Percent >= 0) progressCallback?.Invoke(info.Percent);
            },
            null);
    }

    public async Task<bool> CheckAndApplyStartupUpdateAsync(
        Action<UpdateProgressInfo>? progressCallback,
        Action<bool>? beforeElevationPrompt = null)
    {
        try
        {
            ResetRunState();
            string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? Application.ExecutablePath;
            LogUpdate("Startup", $"Iniciando checagem de atualizacao no boot. Versao atual=v{GetCurrentVersion()}, PID={Environment.ProcessId}, Admin={IsAdministrator()}, Exe='{exePath}'");

            Report(progressCallback, "Check", "Consultando atualizações disponíveis...", 5);
            using var checkCts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var updateInfo = await CheckForUpdatesAsync(checkCts.Token);
            if (updateInfo == null)
            {
                string err = LastCheckError ?? "Sem resposta do servidor de atualizações.";
                LogUpdate("Startup", $"CheckForUpdatesAsync nao retornou versao: {err}");
                return false;
            }

            if (!updateInfo.UpdateAvailable)
            {
                LogUpdate("Startup", $"Sistema ja atualizado (atual=v{updateInfo.CurrentVersion}, remota=v{updateInfo.LatestVersion}).");
                return false;
            }

            HadUpdateAttempt = true;
            LogUpdate("Startup", $"Nova versao detectada: v{updateInfo.LatestVersion}. Preparando download...");
            Report(progressCallback, "Download", $"Nova versão v{updateInfo.LatestVersion} encontrada. Iniciando download...", 0);

            var (downloadOk, tempExeOrError) = await DownloadUpdateFileAsync(updateInfo, progressCallback);
            if (!downloadOk)
            {
                LastFailureStage = "Download";
                LastFailureMessage = tempExeOrError;
                LogUpdateError("Startup", $"Falha no download da atualizacao de inicializacao: {tempExeOrError}");
                Report(progressCallback, "Error", $"Falha no download: {tempExeOrError}", -1);
                return false;
            }

            Report(progressCallback, "Apply", "Aplicando atualização e reiniciando...", 100);
            LogUpdate("Startup", $"Download concluido e validado em '{tempExeOrError}'. Chamando ApplyAtomicUpdate...");

            if (!ApplyAtomicUpdate(tempExeOrError, out var applyError, beforeElevationPrompt))
            {
                LastFailureStage = "Apply";
                LastFailureMessage = applyError ?? "Não foi possível iniciar o instalador da atualização.";
                LogUpdateError("Startup", $"ApplyAtomicUpdate falhou: {LastFailureMessage}");
                Report(progressCallback, "Error", LastFailureMessage, -1);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            LastFailureStage = "Startup";
            LastFailureMessage = ex.Message;
            LogUpdateError("Startup", $"Erro na atualizacao de inicializacao: {ex}");
            Report(progressCallback, "Error", ex.Message, -1);
            return false;
        }
    }

    public async Task<UpdateCheckResult?> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        LastCheckError = null;
        try
        {
            var currentVersion = GetCurrentVersion();
            var apiUrl = $"https://api.github.com/repos/{GitHubRepo}/releases/latest";
            LogUpdate("Check", $"Consultando GitHub Releases: {apiUrl} (versao local v{currentVersion})");

            var response = await _client.GetAsync(apiUrl, cancellationToken);
            LogUpdate("Check", $"Resposta GitHub API: HTTP {(int)response.StatusCode} ({response.StatusCode})");
            if (!response.IsSuccessStatusCode)
            {
                LastCheckError = $"Servidor retornou HTTP {(int)response.StatusCode} ({response.StatusCode}).";
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
            var versionString = tagName.TrimStart('v', 'V');
            if (string.IsNullOrWhiteSpace(versionString))
            {
                LastCheckError = "A release no GitHub não informou tag de versão.";
                LogUpdate("Check", "tag_name vazio na release do GitHub.");
                return null;
            }

            if (!Version.TryParse(versionString, out var remoteVer))
            {
                LastCheckError = $"Formato de versão inválido na release ('{tagName}').";
                LogUpdate("Check", $"tag_name '{tagName}' nao pode ser interpretado como Version.");
                return null;
            }

            if (remoteVer <= currentVersion)
            {
                var sameResult = new UpdateCheckResult
                {
                    UpdateAvailable = false,
                    CurrentVersion = currentVersion.ToString(),
                    LatestVersion = remoteVer.ToString()
                };
                LastCheckResult = sameResult;
                return sameResult;
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
                    else if (name.Equals("ApacKiosk.exe.sha256", StringComparison.OrdinalIgnoreCase) ||
                             name.Equals("sha256.txt", StringComparison.OrdinalIgnoreCase) ||
                             (string.IsNullOrEmpty(expectedSha256) &&
                              name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase) &&
                              !name.Contains("Instalador", StringComparison.OrdinalIgnoreCase)))
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
                LastCheckError = $"Release {tagName} encontrada, mas sem o executável ApacKiosk.exe anexado.";
                LogUpdate("Check", $"Release {tagName} encontrada, mas asset ApacKiosk.exe ausente.");
                return null;
            }

            LogUpdate("Check", $"Update disponivel: v{remoteVer} | SHA256='{expectedSha256}' | URL='{downloadUrl}'");
            var result = new UpdateCheckResult
            {
                UpdateAvailable = true,
                CurrentVersion = currentVersion.ToString(),
                LatestVersion = remoteVer.ToString(),
                DownloadUrl = downloadUrl,
                Sha256 = expectedSha256,
                ReleaseNotes = releaseNotes
            };
            LastCheckResult = result;
            return result;
        }
        catch (OperationCanceledException)
        {
            LastCheckError = "Tempo limite esgotado ao consultar o servidor de atualizações (12s). Verifique a internet.";
            LogUpdateError("Check", LastCheckError);
            return null;
        }
        catch (HttpRequestException ex)
        {
            LastCheckError = $"Falha de conexão ao verificar atualização: {ex.Message}";
            LogUpdateError("Check", $"Erro HTTP ao verificar atualizacao no GitHub: {ex}");
            return null;
        }
        catch (Exception ex)
        {
            LastCheckError = $"Erro ao verificar atualização: {ex.Message}";
            LogUpdateError("Check", $"Erro ao verificar atualizacao no GitHub: {ex}");
            return null;
        }
    }

    public async Task<(bool success, string message)> PerformManualUpdateAsync(Control? owner = null, Action<string>? progressCallback = null)
    {
        ResetRunState();
        LogUpdate("Manual", $"Verificacao manual iniciada. Versao atual=v{GetCurrentVersion()}, Admin={IsAdministrator()}");

        progressCallback?.Invoke("Verificando se há novas atualizações disponíveis...");
        using var checkCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var updateInfo = await CheckForUpdatesAsync(checkCts.Token);

        if (updateInfo == null)
        {
            return (false, LastCheckError ?? "Não foi possível verificar atualizações no momento. Verifique sua conexão com a internet.");
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

        return await DownloadAndApplyConfirmedUpdateAsync(
            updateInfo,
            info => progressCallback?.Invoke(info.Title),
            null);
    }

    public async Task<(bool success, string message)> DownloadAndApplyConfirmedUpdateAsync(
        UpdateCheckResult updateInfo,
        Action<UpdateProgressInfo>? progressCallback = null,
        Action<bool>? beforeElevationPrompt = null,
        CancellationToken cancellationToken = default)
    {
        HadUpdateAttempt = true;
        LastFailureStage = null;
        LastFailureMessage = null;

        Report(progressCallback, "Download", $"Iniciando download da versão v{updateInfo.LatestVersion}...", 0);
        var (downloadOk, tempExeOrError) = await DownloadUpdateFileAsync(updateInfo, progressCallback, cancellationToken);

        if (!downloadOk)
        {
            LastFailureStage = "Download";
            LastFailureMessage = tempExeOrError;
            LogUpdateError("Manual", $"Falha ao baixar atualizacao: {tempExeOrError}");
            return (false, $"Falha ao baixar atualização:\n{tempExeOrError}");
        }

        Report(progressCallback, "Apply", "Iniciando instalador de atualização...", 100);
        bool applied = ApplyAtomicUpdate(tempExeOrError, out var applyErr, beforeElevationPrompt);
        if (!applied)
        {
            LastFailureStage = "Apply";
            LastFailureMessage = applyErr;
            LogUpdateError("Manual", $"Falha ao aplicar atualizacao: {applyErr}");
            return (false, $"Falha ao aplicar a atualização:\n{applyErr ?? "Erro desconhecido (consulte ApacKiosk_Update.log)."}");
        }

        return (true, "Atualização iniciada com sucesso.");
    }

    private async Task<(bool success, string result)> DownloadUpdateFileAsync(
        UpdateCheckResult updateInfo,
        Action<UpdateProgressInfo>? progressCallback,
        CancellationToken cancellationToken = default)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "ApacUpdates");
        string tempExe = Path.Combine(tempDir, $"ApacKiosk_v{updateInfo.LatestVersion}.exe");
        string partExe = tempExe + ".part";

        try
        {
            if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

            if (!string.IsNullOrEmpty(updateInfo.Sha256) && File.Exists(tempExe))
            {
                try
                {
                    Report(progressCallback, "Verify", "Verificando pacote já baixado em cache...", 95);
                    string cachedHash = ComputeSha256(tempExe);
                    if (cachedHash.Equals(updateInfo.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        long cachedLen = new FileInfo(tempExe).Length;
                        LogUpdate("Download", $"Pacote v{updateInfo.LatestVersion} ja validado em cache local ({cachedLen} bytes, SHA-256={cachedHash}). Pulando novo download.");
                        Report(progressCallback, "Verify", $"Pacote v{updateInfo.LatestVersion} validado em cache ({FormatBytes(cachedLen)}).", 100, cachedLen, cachedLen, 0);
                        return (true, tempExe);
                    }
                    LogUpdate("Download", $"Cache local '{tempExe}' com SHA-256 divergente ({cachedHash}). Baixando novamente.");
                }
                catch { }
            }

            CleanupFileQuietly(partExe);
            CleanupFileQuietly(tempExe);

            LogUpdate("Download", $"Baixando '{updateInfo.DownloadUrl}' -> '{partExe}'");
            using var response = await _client.GetAsync(updateInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            LogUpdate("Download", $"Tamanho informado pelo servidor: {totalBytes} bytes");

            if (totalBytes > 0)
            {
                EnsureEnoughDiskSpace(tempDir, totalBytes + 30L * 1024 * 1024);
            }

            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var speedTimer = Stopwatch.StartNew();
            var uiTimer = Stopwatch.StartNew();

            await using (var fileStream = new FileStream(partExe, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long totalRead = 0;
                int lastPct = -1;
                int lastLoggedQuarter = -1;

                while (true)
                {
                    int bytesRead;
                    using var stallCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    stallCts.CancelAfter(DownloadStallTimeout);
                    try
                    {
                        bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), stallCts.Token);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new TimeoutException($"Download estagnado: nenhum dado recebido por {(int)DownloadStallTimeout.TotalSeconds} segundos ({FormatBytes(totalRead)} baixados).");
                    }

                    if (bytesRead == 0)
                        break;

                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    totalRead += bytesRead;

                    double elapsedSec = Math.Max(0.2, speedTimer.Elapsed.TotalSeconds);
                    double speed = totalRead / elapsedSec;

                    if (totalBytes > 0)
                    {
                        int pct = Math.Clamp((int)((totalRead * 100L) / totalBytes), 0, 100);
                        if (pct != lastPct || uiTimer.ElapsedMilliseconds >= 400)
                        {
                            lastPct = pct;
                            uiTimer.Restart();
                            Report(
                                progressCallback,
                                "Download",
                                $"Baixando v{updateInfo.LatestVersion}: {pct}% ({FormatBytes(totalRead)} / {FormatBytes(totalBytes)} • {FormatBytes((long)speed)}/s)",
                                pct,
                                totalRead,
                                totalBytes,
                                speed);
                        }

                        int quarter = pct / 25;
                        if (quarter > lastLoggedQuarter)
                        {
                            lastLoggedQuarter = quarter;
                            LogUpdate("Download", $"Progresso: {pct}% ({totalRead}/{totalBytes} bytes, {FormatBytes((long)speed)}/s)");
                        }
                    }
                    else if (uiTimer.ElapsedMilliseconds >= 400)
                    {
                        uiTimer.Restart();
                        Report(
                            progressCallback,
                            "Download",
                            $"Baixando v{updateInfo.LatestVersion}: {FormatBytes(totalRead)} ({FormatBytes((long)speed)}/s)",
                            50,
                            totalRead,
                            -1,
                            speed);
                    }
                }

                await fileStream.FlushAsync(cancellationToken);
                LogUpdate("Download", $"Stream finalizado: {totalRead} bytes gravados (esperado={totalBytes}).");

                if (totalRead <= 0 || (totalBytes > 0 && totalRead != totalBytes))
                {
                    fileStream.Close();
                    CleanupFileQuietly(partExe);
                    return (false, $"Download incompleto ({FormatBytes(totalRead)} de {FormatBytes(totalBytes)}).");
                }
            }

            if (!string.IsNullOrEmpty(updateInfo.Sha256))
            {
                Report(progressCallback, "Verify", "Validando integridade do arquivo (SHA-256)...", 99);
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
            Report(progressCallback, "Download", $"Download concluído ({FormatBytes(new FileInfo(tempExe).Length)}).", 100);

            return (true, tempExe);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CleanupFileQuietly(partExe);
            LogUpdate("Download", "Download cancelado.");
            return (false, "Download cancelado.");
        }
        catch (Exception ex)
        {
            CleanupFileQuietly(partExe);
            LogUpdateError("Download", $"Excecao no download: {ex}");
            return (false, ex.Message);
        }
    }

    public static bool ApplyAtomicUpdate(string newExePath) => ApplyAtomicUpdate(newExePath, out _, null);

    public static bool ApplyAtomicUpdate(string newExePath, out string? errorMessage) =>
        ApplyAtomicUpdate(newExePath, out errorMessage, null);

    public static bool ApplyAtomicUpdate(string newExePath, out string? errorMessage, Action<bool>? beforeElevationPrompt)
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
            if (needsElevation)
            {
                LogUpdate("Apply", "Pasta de instalacao protegida; solicitando elevacao (UAC) antes de iniciar updater.");
                beforeElevationPrompt?.Invoke(true);
            }

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
                beforeElevationPrompt?.Invoke(false);
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                LogUpdateError("Apply", $"Falha ao iniciar updater externo: {ex}");
                Program.RestoreAfterFailedUpdateExit(needsElevation);
                beforeElevationPrompt?.Invoke(false);
                return false;
            }

            if (updaterProc == null)
            {
                errorMessage = "Não foi possível iniciar o instalador de atualização.";
                LogUpdateError("Apply", errorMessage);
                Program.RestoreAfterFailedUpdateExit(needsElevation);
                beforeElevationPrompt?.Invoke(false);
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
            bool noUi = args.Any(a => a.Equals("--no-ui", StringComparison.OrdinalIgnoreCase));
            bool noBootWait = args.Any(a => a.Equals("--no-boot-wait", StringComparison.OrdinalIgnoreCase));

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

            if (noUi)
            {
                var result = RunApplyUpdateCore(targetExe, waitPid, !noBootWait, null, CancellationToken.None);
                return result.Success ? 0 : result.ErrorCode;
            }

            ApplicationConfiguration.Initialize();
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            using var statusForm = new UpdaterStatusForm(targetExe, waitPid, !noBootWait);
            Application.Run(statusForm);
            return statusForm.ExitCode;
        }
        catch (Exception ex)
        {
            LogUpdateError("Updater", $"Erro fatal em ExecuteApplyUpdateMode: {ex}");
            return 1;
        }
    }

    public sealed record ApplyUpdateResult(bool Success, int ErrorCode, string Summary, bool RolledBack = false);

    public static ApplyUpdateResult RunApplyUpdateCore(
        string targetExe,
        int waitPid,
        bool waitForBootConfirmation,
        Action<UpdateProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        string backupExe = "";
        bool movedToBackup = false;

        try
        {
            string selfExe = Process.GetCurrentProcess().MainModule?.FileName ?? Application.ExecutablePath;
            LogUpdate("Updater", $"Modo --apply-update iniciado. Self='{selfExe}', Target='{targetExe}', WaitPID={waitPid}, IsAdmin={IsAdministrator()}, UpdaterPID={Environment.ProcessId}");
            Report(progress, "Updater", "Preparando instalação da atualização...", 5);

            if (string.IsNullOrWhiteSpace(targetExe) || string.IsNullOrWhiteSpace(selfExe))
            {
                string msg = "Parâmetro --apply-update sem caminho de destino válido.";
                LogUpdateError("Updater", msg);
                return new ApplyUpdateResult(false, 1, msg);
            }

            string targetDir = Path.GetDirectoryName(targetExe) ?? "";
            if (string.IsNullOrEmpty(targetDir))
            {
                return new ApplyUpdateResult(false, 1, "Diretório de destino inválido.");
            }

            Directory.CreateDirectory(targetDir);
            if (!CanWriteToDirectory(targetDir))
            {
                string msg = $"Sem permissão de escrita em '{targetDir}'. É necessário autorizar o acesso de Administrador.";
                LogUpdateError("Updater", msg);
                return new ApplyUpdateResult(false, 3, msg);
            }

            if (waitPid > 0 && waitPid != Environment.ProcessId)
            {
                Report(progress, "Updater", $"Etapa 1/5: Aguardando encerramento da versão anterior (PID {waitPid})...", 15);
                bool exited = WaitForProcessExit(waitPid, 7000);
                if (!exited)
                {
                    LogUpdate("Updater", $"PID {waitPid} nao encerrou em 7s. Solicitando encerramento forçado para liberar arquivo...");
                    TerminateBlockingProcess(waitPid);
                }
            }

            TerminateOtherInstancesAtPath(targetExe);

            Report(progress, "Updater", "Etapa 2/5: Validando pacote de origem (SHA-256)...", 30);
            backupExe = Path.Combine(targetDir, "ApacKiosk.old.exe");
            string selfSha = ComputeSha256Static(selfExe);
            LogUpdate("Updater", $"SHA-256 de origem calculado: {selfSha}");

            Exception? lastEx = null;
            bool copied = false;

            Report(progress, "Updater", "Etapa 3/5: Substituindo executável instalado...", 45);
            for (int attempt = 1; attempt <= 20; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    int pct = Math.Min(75, 45 + attempt);
                    Report(progress, "Updater", $"Etapa 3/5: Instalando arquivo (tentativa {attempt}/20)...", pct);

                    if (File.Exists(backupExe))
                    {
                        try { File.Delete(backupExe); } catch { }
                    }

                    if (File.Exists(targetExe))
                    {
                        File.Move(targetExe, backupExe, overwrite: true);
                        movedToBackup = true;
                    }

                    File.Copy(selfExe, targetExe, overwrite: true);

                    Report(progress, "Updater", "Etapa 4/5: Conferindo integridade SHA-256 no destino...", 80);
                    string targetSha = ComputeSha256Static(targetExe);

                    if (!string.Equals(selfSha, targetSha, StringComparison.OrdinalIgnoreCase))
                    {
                        LogUpdateError("Updater", $"Tentativa {attempt}: SHA-256 do destino divergiu (esperado={selfSha}, destino={targetSha}). Restaurando backup...");
                        RestoreBackupIfNeeded(backupExe, targetExe, ref movedToBackup);
                        Thread.Sleep(350);
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
                    if (attempt == 5 || attempt == 12)
                    {
                        TerminateOtherInstancesAtPath(targetExe);
                    }
                    Thread.Sleep(350);
                }
            }

            if (!copied)
            {
                string errDetail = lastEx != null ? $"{lastEx.GetType().Name}: {lastEx.Message}" : "Arquivo bloqueado ou hash divergente.";
                LogUpdateError("Updater", $"Falha definitiva ao substituir '{targetExe}': {lastEx}");
                bool rolledBack = RestoreBackupIfNeeded(backupExe, targetExe, ref movedToBackup);
                return new ApplyUpdateResult(false, 2, $"Não foi possível substituir '{targetExe}': {errDetail}", rolledBack);
            }

            string bootToken = Guid.NewGuid().ToString("N");
            string bootFlagPath = GetBootFlagPath(bootToken);
            CleanupFileQuietly(bootFlagPath);

            Report(progress, "Updater", "Etapa 5/5: Iniciando a nova versão e aguardando confirmação...", 90);
            var restartPsi = new ProcessStartInfo
            {
                FileName = targetExe,
                Arguments = $"--cleanup-updates --wait-pid={Environment.ProcessId} --boot-token={bootToken}",
                WorkingDirectory = targetDir,
                UseShellExecute = true
            };

            var started = Process.Start(restartPsi);
            if (started == null)
            {
                LogUpdateError("Updater", "Process.Start retornou null ao reiniciar nova versao.");
                bool rolledBack = RestoreBackupIfNeeded(backupExe, targetExe, ref movedToBackup);
                return new ApplyUpdateResult(false, 4, "A nova versão foi copiada, mas falhou ao iniciar.", rolledBack);
            }

            LogUpdate("Updater", $"Nova versao iniciada: Target='{targetExe}', NewPID={started.Id}, BootToken={bootToken}.");

            if (!waitForBootConfirmation)
            {
                CleanupFileQuietly(backupExe);
                Report(progress, "Done", "Atualização concluída com sucesso!", 100);
                return new ApplyUpdateResult(true, 0, $"Atualização aplicada e processo PID={started.Id} iniciado.");
            }

            var waitSw = Stopwatch.StartNew();
            while (waitSw.Elapsed.TotalSeconds < 25)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (File.Exists(bootFlagPath))
                {
                    string flagContent = "";
                    try { flagContent = File.ReadAllText(bootFlagPath).Trim(); } catch { }
                    CleanupFileQuietly(bootFlagPath);
                    CleanupFileQuietly(backupExe);
                    LogUpdate("Updater", $"Confirmacao de inicializacao recebida ({flagContent}). Atualizacao finalizada!");
                    Report(progress, "Done", "Atualização instalada e inicialização confirmada!", 100);
                    return new ApplyUpdateResult(true, 0, $"Nova versão instalada e confirmada ({flagContent}).");
                }

                try
                {
                    started.Refresh();
                    if (started.HasExited)
                    {
                        if (File.Exists(bootFlagPath))
                        {
                            CleanupFileQuietly(bootFlagPath);
                            LogUpdate("Updater", "Nova versao confirmou boot antes de concluir execucao.");
                            Report(progress, "Done", "Atualização instalada e inicialização confirmada!", 100);
                            return new ApplyUpdateResult(true, 0, "Nova versão executada com confirmação de inicialização.");
                        }

                        int childExit = started.ExitCode;
                        LogUpdateError("Updater", $"A nova versao iniciou (PID={started.Id}) e encerrou inesperadamente antes da confirmacao de boot (ExitCode={childExit}).");
                        bool rolledBack = false;
                        if (childExit != 0)
                        {
                            rolledBack = RestoreBackupIfNeeded(backupExe, targetExe, ref movedToBackup);
                        }
                        return new ApplyUpdateResult(false, 5, $"A nova versão encerrou prematuramente (código {childExit}) antes de confirmar inicialização.", rolledBack);
                    }
                }
                catch { }

                int bootPct = Math.Min(98, 90 + (int)(waitSw.Elapsed.TotalSeconds * 0.35));
                Report(progress, "Updater", $"Etapa 5/5: Aguardando abertura da nova versão ({(int)waitSw.Elapsed.TotalSeconds}s)...", bootPct);
                Thread.Sleep(250);
            }

            LogUpdate("Updater", $"Tempo limite (25s) aguardando confirmacao explicita, mas PID={started.Id} permanece ativo.");
            Report(progress, "Done", $"Nova versão iniciada (PID={started.Id}). Aguardando carregamento completo do Windows.", 100);
            return new ApplyUpdateResult(true, 0, $"Executável substituído e processo PID={started.Id} segue em execução.");
        }
        catch (Exception ex)
        {
            LogUpdateError("Updater", $"Erro em RunApplyUpdateCore: {ex}");
            bool rolledBack = RestoreBackupIfNeeded(backupExe, targetExe, ref movedToBackup);
            return new ApplyUpdateResult(false, 1, ex.Message, rolledBack);
        }
    }

    public static void SignalBootSuccess(string[] args)
    {
        try
        {
            string? bootToken = null;
            foreach (var arg in args)
            {
                if (arg.StartsWith("--boot-token=", StringComparison.OrdinalIgnoreCase))
                {
                    bootToken = arg.Substring("--boot-token=".Length).Trim('"');
                }
            }

            if (string.IsNullOrWhiteSpace(bootToken))
                return;

            string flagPath = GetBootFlagPath(bootToken);
            var dir = Path.GetDirectoryName(flagPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(flagPath, $"OK PID={Environment.ProcessId} Time={DateTime.Now:O}");
            LogUpdate("Boot", $"Sinal de boot saudavel gravado para token={bootToken}, PID={Environment.ProcessId}");
        }
        catch (Exception ex)
        {
            LogUpdateError("Boot", $"Falha ao gravar sinal de boot: {ex.Message}");
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

            if (waitPid > 0 && waitPid != Environment.ProcessId)
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
                        if (Path.GetFileName(f).StartsWith("boot_ok_", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(f)).TotalMinutes > 5)
                                    CleanupFileQuietly(f);
                            }
                            catch { }
                            continue;
                        }
                        CleanupFileQuietly(f);
                    }
                }
                var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                LogUpdate("Boot", $"Pos-atualizacao concluido! Versao em execucao=v{ver}, Exe='{currentExe}', Admin={IsAdministrator()}, PID={Environment.ProcessId}");
            }
        }
        catch { }
    }

    public static string GetDiagnosticsReport()
    {
        var sb = new StringBuilder();
        string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? Application.ExecutablePath;
        string currentDir = Path.GetDirectoryName(currentExe) ?? "";
        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;

        sb.AppendLine($"Versão em execução : v{ver}");
        sb.AppendLine($"Executável atual   : {currentExe}");
        sb.AppendLine($"Executando c/ Admin: {(IsAdministrator() ? "Sim" : "Não")}");
        sb.AppendLine($"Pasta gravável     : {(CanWriteToDirectory(currentDir) ? "Sim" : "Não (requer elevação UAC)")}");
        sb.AppendLine();

        string progDataLog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ApacKiosk", "ApacKiosk_Update.log");
        string tempLog = Path.Combine(Path.GetTempPath(), "ApacKiosk_Update.log");
        string errLog = Path.Combine(Path.GetTempPath(), "ApacKiosk_UpdateError.log");

        AppendLogSection(sb, "Log ProgramData (últimas 60 linhas)", progDataLog, 60);
        AppendLogSection(sb, "Log Temp (últimas 60 linhas)", tempLog, 60);
        AppendLogSection(sb, "Log de Erros (últimas 30 linhas)", errLog, 30);

        return sb.ToString();
    }

    private static void AppendLogSection(StringBuilder sb, string title, string path, int maxLines)
    {
        sb.AppendLine($"=== {title}: {path} ===");
        try
        {
            if (!File.Exists(path))
            {
                sb.AppendLine("(arquivo ainda não gerado)");
                sb.AppendLine();
                return;
            }

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs, Encoding.UTF8);
            var lines = new List<string>();
            while (!sr.EndOfStream)
            {
                var line = sr.ReadLine();
                if (line != null) lines.Add(line);
            }

            int start = Math.Max(0, lines.Count - maxLines);
            for (int i = start; i < lines.Count; i++)
            {
                sb.AppendLine(lines[i]);
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"(erro ao ler log: {ex.Message})");
        }
        sb.AppendLine();
    }

    private static bool RestoreBackupIfNeeded(string backupExe, string targetExe, ref bool movedToBackup)
    {
        if (string.IsNullOrEmpty(backupExe) || !File.Exists(backupExe))
            return false;

        if (!File.Exists(targetExe) || movedToBackup)
        {
            for (int attempt = 1; attempt <= 10; attempt++)
            {
                try
                {
                    File.Copy(backupExe, targetExe, true);
                    LogUpdate("Updater", $"Rollback concluido na tentativa {attempt}: '{backupExe}' restaurado para '{targetExe}'.");
                    movedToBackup = false;
                    return true;
                }
                catch (Exception ex)
                {
                    if (attempt == 10)
                    {
                        LogUpdateError("Updater", $"Falha no rollback de backup apos {attempt} tentativas: {ex.Message}");
                        return false;
                    }
                    Thread.Sleep(250);
                }
            }
        }
        return false;
    }

    private static string GetBootFlagPath(string bootToken)
    {
        return Path.Combine(Path.GetTempPath(), "ApacUpdates", $"boot_ok_{bootToken}.flag");
    }

    private static void TerminateBlockingProcess(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            if (!proc.HasExited)
            {
                proc.Kill();
                proc.WaitForExit(3000);
                LogUpdate("Updater", $"Processo bloqueador PID={pid} encerrado.");
            }
        }
        catch (Exception ex)
        {
            LogUpdate("Updater", $"Aviso ao encerrar PID={pid}: {ex.Message}");
        }
    }

    private static void TerminateOtherInstancesAtPath(string targetExe)
    {
        try
        {
            string normalizedTarget = Path.GetFullPath(targetExe);
            string procName = Path.GetFileNameWithoutExtension(targetExe);
            foreach (var proc in Process.GetProcessesByName(procName))
            {
                try
                {
                    if (proc.Id == Environment.ProcessId) continue;
                    string? path = null;
                    try { path = proc.MainModule?.FileName; } catch { }
                    if (string.IsNullOrEmpty(path) || string.Equals(Path.GetFullPath(path), normalizedTarget, StringComparison.OrdinalIgnoreCase))
                    {
                        LogUpdate("Updater", $"Encerrando instancia em execucao no destino: PID={proc.Id}");
                        proc.Kill();
                        proc.WaitForExit(3000);
                    }
                }
                catch { }
                finally { proc.Dispose(); }
            }
        }
        catch { }
    }

    private static bool WaitForProcessExit(int pid, int timeoutMs)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            if (!proc.HasExited)
            {
                LogUpdate("Updater", $"Aguardando encerramento do processo PID={pid} (ate {timeoutMs}ms)...");
                return proc.WaitForExit(timeoutMs);
            }
            return true;
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (Exception ex)
        {
            LogUpdate("Updater", $"Aviso ao aguardar PID={pid}: {ex.Message}");
            return false;
        }
    }

    private static void EnsureEnoughDiskSpace(string path, long requiredBytes)
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root)) return;
            var drive = new DriveInfo(root);
            if (drive.IsReady && drive.AvailableFreeSpace < requiredBytes)
            {
                throw new IOException($"Espaço em disco insuficiente em {root} (necessário {FormatBytes(requiredBytes)}, disponível {FormatBytes(drive.AvailableFreeSpace)}).");
            }
        }
        catch (IOException)
        {
            throw;
        }
        catch { }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "?";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }

    private void ResetRunState()
    {
        LastFailureMessage = null;
        LastFailureStage = null;
        LastCheckError = null;
        HadUpdateAttempt = false;
        LastCheckResult = null;
    }

    private static void Report(
        Action<UpdateProgressInfo>? callback,
        string stage,
        string title,
        int percent,
        long bytesReceived = 0,
        long totalBytes = 0,
        double bytesPerSecond = 0)
    {
        try
        {
            callback?.Invoke(new UpdateProgressInfo
            {
                Stage = stage,
                Title = title,
                Percent = percent,
                BytesReceived = bytesReceived,
                TotalBytes = totalBytes,
                BytesPerSecond = bytesPerSecond
            });
        }
        catch { }
    }

    public static bool CanWriteToDirectory(string dir)
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

    public static bool IsAdministrator()
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

    public static Version GetCurrentVersion()
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
        string formatted = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{stage}] {msg}";
        string line = formatted + "\n";
        AppendLogSafe(Path.Combine(Path.GetTempPath(), "ApacKiosk_Update.log"), line);
        AppendLogSafe(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ApacKiosk", "ApacKiosk_Update.log"), line);
        try { OnUpdateLog?.Invoke(formatted); } catch { }
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
