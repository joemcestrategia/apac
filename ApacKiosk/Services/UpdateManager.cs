using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
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

    public UpdateManager()
    {
        _client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ApacKiosk", "1.0"));
    }

    public async Task CheckAndApplyUpdateAsync(string? hardwareFingerprint = null)
    {
        try
        {
            var updateInfo = await CheckForUpdatesAsync();
            if (updateInfo == null || !updateInfo.UpdateAvailable) return;

            var (downloadOk, tempExe) = await DownloadUpdateFileAsync(updateInfo, null);
            if (downloadOk)
            {
                ApplyAtomicUpdate(tempExe);
            }
        }
        catch (Exception ex)
        {
            LogUpdateError($"Erro no CheckAndApplyUpdateAsync: {ex.Message}");
        }
    }

    public async Task<bool> CheckAndApplyStartupUpdateAsync(Action<string>? statusCallback = null, Action<int>? progressCallback = null)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var updateInfo = await CheckForUpdatesAsync(cts.Token);
            if (updateInfo == null || !updateInfo.UpdateAvailable) return false;

            statusCallback?.Invoke($"Nova versão v{updateInfo.LatestVersion} encontrada. Baixando atualização...");
            var (downloadOk, tempExeOrError) = await DownloadUpdateFileAsync(updateInfo, progressCallback);
            if (!downloadOk)
            {
                LogUpdateError($"Falha no download da atualização de inicialização: {tempExeOrError}");
                return false;
            }

            statusCallback?.Invoke("Aplicando atualização e reiniciando...");
            return ApplyAtomicUpdate(tempExeOrError);
        }
        catch (Exception ex)
        {
            LogUpdateError($"Erro na atualização de inicialização: {ex.Message}");
            return false;
        }
    }

    public async Task<UpdateCheckResult?> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var currentVersion = GetCurrentVersion();
            var apiUrl = $"https://api.github.com/repos/{GitHubRepo}/releases/latest";

            var response = await _client.GetAsync(apiUrl, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
            var versionString = tagName.TrimStart('v', 'V');
            if (string.IsNullOrWhiteSpace(versionString)) return null;

            if (!Version.TryParse(versionString, out var remoteVer)) return null;
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
                            catch { }
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(downloadUrl)) return null;

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
            LogUpdateError($"Erro ao verificar atualização no GitHub: {ex.Message}");
            return null;
        }
    }

    public async Task<(bool success, string message)> PerformManualUpdateAsync(Control? owner = null, Action<string>? progressCallback = null)
    {
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
            return (false, "Atualização cancelada pelo administrador.");
        }

        progressCallback?.Invoke("Baixando atualização...");
        var (downloadOk, tempExeOrError) = await DownloadUpdateFileAsync(updateInfo, pct =>
        {
            progressCallback?.Invoke($"Baixando atualização ({pct}%)...");
        });

        if (!downloadOk)
        {
            return (false, $"Falha ao baixar atualização:\n{tempExeOrError}");
        }

        progressCallback?.Invoke("Instalando atualização e reiniciando...");
        bool applied = ApplyAtomicUpdate(tempExeOrError);
        return applied ? (true, "Atualização aplicada com sucesso.") : (false, "Falha ao aplicar a atualização atômica.");
    }

    private async Task<(bool success, string result)> DownloadUpdateFileAsync(
        UpdateCheckResult updateInfo,
        Action<int>? progressCallback,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "ApacUpdates");
            if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

            string tempExe = Path.Combine(tempDir, $"ApacKiosk_v{updateInfo.LatestVersion}.exe");
            if (File.Exists(tempExe))
            {
                try { File.Delete(tempExe); } catch { }
            }

            using var response = await _client.GetAsync(updateInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(tempExe, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) != 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalRead += bytesRead;

                if (totalBytes > 0 && progressCallback != null)
                {
                    int pct = (int)((totalRead * 100) / totalBytes);
                    progressCallback(pct);
                }
            }

            await fileStream.FlushAsync(cancellationToken);
            fileStream.Close();

            if (!string.IsNullOrEmpty(updateInfo.Sha256))
            {
                string fileHash = ComputeSha256(tempExe);
                if (!fileHash.Equals(updateInfo.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(tempExe); } catch { }
                    return (false, "Falha na verificação de integridade (Hash SHA-256 não confere).");
                }
            }

            return (true, tempExe);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static bool ApplyAtomicUpdate(string newExePath)
    {
        try
        {
            string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (string.IsNullOrEmpty(currentExe)) return false;

            string currentDir = Path.GetDirectoryName(currentExe) ?? "";
            string backupExe = Path.Combine(currentDir, "ApacKiosk.old.exe");

            if (File.Exists(backupExe))
            {
                try { File.Delete(backupExe); } catch { }
            }

            File.Move(currentExe, backupExe);
            File.Copy(newExePath, currentExe, true);

            var psi = new ProcessStartInfo
            {
                FileName = currentExe,
                WorkingDirectory = currentDir,
                UseShellExecute = true
            };
            Process.Start(psi);

            Environment.Exit(0);
            return true;
        }
        catch (Exception ex)
        {
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "ApacKiosk_UpdateError.log"), $"[{DateTime.Now}] ApplyAtomicUpdate error: {ex.Message}\n");
            }
            catch { }
            return false;
        }
    }

    private Version GetCurrentVersion()
    {
        return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);
    }

    private string ComputeSha256(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private void LogUpdateError(string msg)
    {
        try
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "ApacKiosk_UpdateError.log"), $"[{DateTime.Now}] {msg}\n");
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
