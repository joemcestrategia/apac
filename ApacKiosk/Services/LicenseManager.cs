using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using ApacKiosk.Utils;

namespace ApacKiosk.Services;

public class LicenseManager
{
    private readonly ConfigManager _config;
    private const string ApiBaseUrl = "https://apac-kiosk-cloud.sistema-apac-nuvemapac.workers.dev";

    public LicenseManager(ConfigManager config)
    {
        _config = config;
    }

    public bool IsActivated()
    {
        CheckInstallerRegistry();
        var serial = _config.LicenseSignature;
        return !string.IsNullOrWhiteSpace(serial) && serial.StartsWith("APAC-") && serial.Length > 10;
    }

    private void CheckInstallerRegistry()
    {
        var views = new[] { Microsoft.Win32.RegistryView.Registry64, Microsoft.Win32.RegistryView.Registry32 };
        foreach (var view in views)
        {
            try
            {
                using var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, view);
                using var key = baseKey.OpenSubKey(@"Software\APAC\Kiosk", true);
                if (key != null)
                {
                    var instTrial = key.GetValue("InstallerTrial");
                    if (instTrial != null && instTrial.ToString() == "1")
                        key.DeleteValue("InstallerTrial", false);
                }
            }
            catch { }
        }
    }

    public bool IsInTrialMode()
    {
        if (string.IsNullOrEmpty(_config.TrialStartDate)) return false;
        if (DateTime.TryParse(_config.TrialStartDate, out DateTime startDate))
            return DateTime.Now <= startDate.AddDays(7);
        return false;
    }

    public bool HasTrialExpired()
    {
        if (string.IsNullOrEmpty(_config.TrialStartDate)) return false;
        if (DateTime.TryParse(_config.TrialStartDate, out DateTime startDate))
            return DateTime.Now > startDate.AddDays(7);
        return true;
    }

    public void StartTrial()
    {
        if (string.IsNullOrEmpty(_config.TrialStartDate))
            _config.TrialStartDate = DateTime.Now.ToString("o");
    }

    public record SerialValidationResult(bool Valid, string ClientName, string Plan, string Status, bool AlreadyBound, string Reason);

    public async Task<SerialValidationResult> ValidateSerialOnlineAsync(string serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
            return new SerialValidationResult(false, "", "", "", false, "Digite o serial antes de verificar.");

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var url = $"{ApiBaseUrl}/serial/validate";
            var response = await client.PostAsJsonAsync(url, new { serial });

            var body = await response.Content.ReadAsStringAsync();
            var bodyTrimmed = body.Length > 200 ? body.Substring(0, 200) + "..." : body;

            if (!response.IsSuccessStatusCode)
            {
                // Mostra a URL e o corpo para debug
                return new SerialValidationResult(false, "", "", "", false, 
                    $"Erro HTTP {(int)response.StatusCode}. URL: {url}\nDetalhes: {bodyTrimmed}");
            }

            SerialValidateResponse? data;
            try
            {
                data = JsonSerializer.Deserialize<SerialValidateResponse>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                return new SerialValidationResult(false, "", "", "", false, 
                    $"Resposta inválida do servidor (HTTP {(int)response.StatusCode}). Detalhes: {bodyTrimmed}");
            }

            if (data == null)
                return new SerialValidationResult(false, "", "", "", false, "Resposta inválida do servidor.");

            if (!data.valid)
            {
                var reason = data.reason switch
                {
                    "not_found" => "Serial não encontrado. Verifique o código e tente novamente.",
                    "revoked" => "Este serial foi revogado. Entre em contato com o suporte.",
                    "no_serial" => "Nenhum serial informado.",
                    _ => "Serial inválido."
                };
                return new SerialValidationResult(false, "", "", "", false, reason);
            }

            return new SerialValidationResult(true, data.client_name ?? "", data.plan ?? "", data.status ?? "", data.already_bound, "");
        }
        catch (HttpRequestException)
        {
            return new SerialValidationResult(false, "", "", "", false, "Sem conexão com o servidor. Verifique sua internet e tente novamente.");
        }
        catch (Exception ex)
        {
            return new SerialValidationResult(false, "", "", "", false, $"Erro na comunicação: {ex.Message}");
        }
    }

    public async Task<(bool success, string error)> ActivateOnlineAsync(string serial, string state, string city, string gender, string email, string name)
    {
        var hardwareFingerprint = HardwareFingerprint.Generate();
        var appVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var payload = new
            {
                serial,
                hardware_fingerprint = hardwareFingerprint,
                unit_state = state,
                unit_city = city,
                unit_gender = gender,
                contact_email = email,
                contact_name = name,
                app_version = appVersion
            };

            var response = await client.PostAsJsonAsync($"{ApiBaseUrl}/device/register", payload);

            if (response.IsSuccessStatusCode)
            {
                _config.LicenseSignature = serial;
                _config.UnitState = state;
                _config.UnitCity = city;
                _config.UnitGender = gender;
                _config.ContactEmail = email;
                _config.ContactName = name;
                return (true, string.Empty);
            }

            var body = await response.Content.ReadAsStringAsync();
            var bodyTrimmed = body.Length > 200 ? body.Substring(0, 200) + "..." : body;

            if (!response.IsSuccessStatusCode)
            {
                return (false, $"Erro HTTP {(int)response.StatusCode}. Detalhes: {bodyTrimmed}");
            }

            ErrorResponse? err = null;
            try
            {
                err = JsonSerializer.Deserialize<ErrorResponse>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                return (false, $"Resposta inesperada do servidor. Detalhes: {bodyTrimmed}");
            }

            return (false, err?.error ?? "Erro desconhecido no servidor.");
        }
        catch (HttpRequestException)
        {
            return (false, "Sem conexão com o servidor. Verifique sua internet e tente novamente.");
        }
        catch (Exception ex)
        {
            return (false, $"Erro na comunicação: {ex.Message}");
        }
    }

    private class SerialValidateResponse
    {
        public bool valid { get; set; }
        public string? client_name { get; set; }
        public string? plan { get; set; }
        public string? status { get; set; }
        public bool already_bound { get; set; }
        public string? reason { get; set; }
    }

    private class ErrorResponse
    {
        public string? error { get; set; }
    }
}
