using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using ApacKiosk.Utils;

namespace ApacKiosk.Services;

public class CloudSyncService : IDisposable
{
    private readonly ConfigManager _config;
    private readonly string _hardwareFingerprint;
    private CancellationTokenSource _cts;
    private Task _syncTask;
    private const string ApiBaseUrl = "https://apac-kiosk-cloud.sistema-apac-nuvemapac.workers.dev";

    public CloudSyncService(ConfigManager config)
    {
        _config = config;
        _hardwareFingerprint = HardwareFingerprint.Generate();
        _cts = new CancellationTokenSource();
    }

    public void Start()
    {
        _syncTask = Task.Run(SyncLoop, _cts.Token);
    }

    private async Task SyncLoop()
    {
        // Roda a cada 6 horas
        var delay = TimeSpan.FromHours(6);
        var updateManager = new UpdateManager();
        
        while (!_cts.Token.IsCancellationRequested)
        {
            await CheckLicenseStatusAsync();
            await updateManager.CheckAndApplyUpdateAsync(_hardwareFingerprint);

            try
            {
                await Task.Delay(delay, _cts.Token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task CheckLicenseStatusAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var response = await client.GetAsync($"{ApiBaseUrl}/license/status?device_id={_hardwareFingerprint}");
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<StatusResponse>();
                
                // Grava a última vez que checou com sucesso
                _config.LastCloudCheckin = DateTime.Now.ToString("O");

                if (result?.status == "revoked" || result?.status == "suspended")
                {
                    // Licença foi suspensa ou revogada remotamente!
                    if (!string.IsNullOrEmpty(_config.LicenseSignature))
                    {
                        _config.LicenseSignature = string.Empty;
                        Environment.Exit(0);
                    }
                }
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Dispositivo não existe no banco!
                if (!string.IsNullOrEmpty(_config.LicenseSignature))
                {
                    _config.LicenseSignature = string.Empty;
                    Environment.Exit(0);
                }
            }
        }
        catch
        {
            // Falha na comunicação (sem internet ou servidor fora)
            // Lógica de fail-open: Permitir funcionar sem internet por até 30 dias
            if (DateTime.TryParse(_config.LastCloudCheckin, out DateTime lastCheckin))
            {
                if (DateTime.Now > lastCheckin.AddDays(30))
                {
                    // Expirou o prazo de fail-open de 30 dias sem internet
                    _config.LicenseSignature = string.Empty;
                    Environment.Exit(0);
                }
            }
            else
            {
                // Se nunca fez check-in (ex: ativou offline e nunca subiu), inicia o contador de 30 dias a partir de agora
                _config.LastCloudCheckin = DateTime.Now.ToString("O");
            }
        }
    }

    public void Stop()
    {
        _cts.Cancel();
        _syncTask?.Wait();
    }

    public void Dispose()
    {
        Stop();
        _cts.Dispose();
    }

    private class StatusResponse
    {
        public string? status { get; set; }
        public string? expiry_date { get; set; }
        public string? message { get; set; }
    }
}
