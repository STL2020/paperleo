using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

namespace PaperlessAiCore.Core;

public static class LicenseCheck
{
    private const string PayHipSecretKey = "prod_sk_4auSd_c0786becb0b896035f871dcde5b063fd632dfee6";
    private const string PayHipApiUrl = "https://payhip.com/api/v2/license/verify";

    // DEV-ONLY: Lokaler Testkey – nie in Produktion deployen
    private const string DevTestKey = "PAPERLEO-DEV-TEST-0000";

    public static bool IsProMode(string? licenseKey)
    {
        if (string.IsNullOrWhiteSpace(licenseKey)) return false;
        if (licenseKey.Trim() == DevTestKey) return true;
        return IsKeyFormatValid(licenseKey);
    }

    public static bool IsKeyFormatValid(string key)
    {
        return !string.IsNullOrWhiteSpace(key) && key.Length >= 10;
    }

    private sealed record PayHipVerifyEnvelope(PayHipVerifyData? Data);
    private sealed record PayHipVerifyData(bool Enabled, string? BuyerEmail, string? ProductName, string? VariantName);

    public static async Task<(bool Success, string Message)> VerifyWithPayHipAsync(string licenseKey)
    {
        // DEV-Bypass: kein Netzwerkaufruf
        if (licenseKey.Trim() == DevTestKey)
            return (true, "✓ Dev-Testlizenz aktiviert (nur lokal gültig).");

        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("product-secret-key", PayHipSecretKey);

            var key = Uri.EscapeDataString(licenseKey.Trim());
            var url = $"{PayHipApiUrl}?license_key={key}";
            var response = await http.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var envelope = System.Text.Json.JsonSerializer.Deserialize<PayHipVerifyEnvelope>(
                    body, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (envelope?.Data?.Enabled == true)
                    return (true, "Lizenz erfolgreich aktiviert.");

                if (envelope?.Data is not null)
                    return (false, "Dieser Lizenzschlüssel ist nicht (mehr) aktiv.");

                // Antwort war OK aber leer / unbekanntes Format
                return (false, $"PayHip-Antwort nicht erkannt (HTTP {(int)response.StatusCode}): {body[..Math.Min(body.Length, 200)]}");
            }

            // HTTP-Fehler (401, 404, 422 …) – Antworttext anzeigen für Debugging
            return (false, $"PayHip HTTP {(int)response.StatusCode}: {body[..Math.Min(body.Length, 200)]}");
        }
        catch (Exception ex)
        {
            return (false, $"Verbindungsfehler: {ex.Message}");
        }
    }

    private static string Sha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}
