using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ProcesadorXmlPedidos.Models;

namespace ProcesadorXmlPedidos.Services;

public class KeyLogisticsApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<KeyLogisticsApiClient> _logger;

    public KeyLogisticsApiClient(HttpClient httpClient, IConfiguration configuration, ILogger<KeyLogisticsApiClient> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<KeyLogisticsResponse> PostIntegracionAsync(object parametros, CancellationToken ct)
    {
        var baseUrl = _configuration["KeyLogistics:BaseUrl"];
        var apiKey = _configuration["KeyLogistics:ApiKey"];

        // CHECK: ¿la variable de entorno existe en ESTE proceso?
        var envApiKey = Environment.GetEnvironmentVariable("KeyLogistics__ApiKey");

        _logger.LogDebug(
        "CHECK CONFIG SOURCE: env(KeyLogistics__ApiKey) present={EnvPresent}, len={EnvLen}, sha256_8={EnvHash8} | config(KeyLogistics:ApiKey) len={CfgLen}, sha256_8={CfgHash8}",
        !string.IsNullOrWhiteSpace(envApiKey),
        envApiKey?.Length ?? 0,
        ShortSha256(envApiKey),
        apiKey?.Length ?? 0,
        ShortSha256(apiKey)
        );

        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("KeyLogistics:BaseUrl is not configured.");

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("KeyLogistics:ApiKey is not configured.");

        var correlationId = Guid.NewGuid().ToString();

        // Extraer OrderNumber para nombre de archivo
        var orderNumber = TryGetOrderNumber(parametros) ?? "UNKNOWN";

        var request = new
        {
            Modulo = "68073e0e748c6a204f601daa70f63f7c",
            parametros = parametros
        };

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        var jsonPayload = JsonSerializer.Serialize(request, jsonOptions);

        _logger.LogDebug("SENDING JSON PAYLOAD: {Payload}", jsonPayload);

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, baseUrl);
        requestMessage.Headers.Add("keyApiVal", apiKey);
        requestMessage.Headers.Add("X-Correlation-ID", correlationId);
        requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var bodyBytes = Encoding.UTF8.GetBytes(jsonPayload);
        var content = new ByteArrayContent(bodyBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json"); // sin charset
        requestMessage.Content = content;

        // Validación segura: confirmar que el header keyApiVal realmente va en el request
        var hasApiKeyHeader = requestMessage.Headers.TryGetValues("keyApiVal", out var apiKeyHeaderValues);
        var apiKeyHeaderValue = apiKeyHeaderValues?.FirstOrDefault();

        _logger.LogDebug(
        "CHECK AUTH: keyApiVal header present={Present}, valueNonEmpty={NonEmpty}, length={Length}, sha256_8={Hash8}, CorrelationId={CorrelationId}",
        hasApiKeyHeader,
        !string.IsNullOrWhiteSpace(apiKeyHeaderValue),
        apiKeyHeaderValue?.Length ?? 0,
        ShortSha256(apiKeyHeaderValue),
        correlationId
        );

        _logger.LogInformation(
            "Sending POST request to {Url} CorrelationId={CorrelationId} OrderNumber={OrderNumber}",
            baseUrl, correlationId, orderNumber
        );

        var start = DateTime.UtcNow;
        using var response = await _httpClient.SendAsync(requestMessage, ct);
        var elapsedMs = (DateTime.UtcNow - start).TotalMilliseconds;

        var responseBody = await response.Content.ReadAsStringAsync(ct);

        _logger.LogInformation(
            "Received response from {Url} with status {StatusCode} in {Elapsed}ms CorrelationId={CorrelationId} OrderNumber={OrderNumber}",
            baseUrl, response.StatusCode, elapsedMs, correlationId, orderNumber
        );

        // ✅ Log por ejecución (siempre): archivo único por request
        await WriteExecutionLogAsync(baseUrl, correlationId, orderNumber, jsonPayload, response, responseBody, ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"[{correlationId}] HTTP {(int)response.StatusCode}: {responseBody}");
        }

        var result = JsonSerializer.Deserialize<KeyLogisticsResponse>(
            responseBody,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (result is null)
            throw new InvalidOperationException("Failed to deserialize response.");

        return result;
    }

    public Task<KeyLogisticsResponse> CrearOrdenAsync(Dictionary<string, object?> parametros, CancellationToken ct)
        => PostIntegracionAsync(parametros, ct);

    // -----------------------------
    // Helper: log único por ejecución (SUCCESS o ERROR)
    // Nombre: keylogistics-<OrderNumber>-<timestamp Chile>.log
    // -----------------------------
    private static async Task WriteExecutionLogAsync(
        string url,
        string correlationId,
        string orderNumber,
        string jsonPayload,
        HttpResponseMessage response,
        string responseBody,
        CancellationToken ct)
    {
        var logsDir = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logsDir);

        // Timestamp local Chile (Santiago). Compatible con Windows.
        // Si por alguna razón no existe el timezone ID, cae a hora local del servidor.
        var chileNow = GetChileLocalNow();
        var ts = chileNow.ToString("yyyyMMdd-HHmmss-fff"); // para nombre de archivo

        var safeOrder = SanitizeFilePart(orderNumber);
        var filePath = Path.Combine(logsDir, $"keylogistics-{safeOrder}-{ts}.log");

        var sb = new StringBuilder();
        sb.AppendLine($"TimestampChile: {chileNow:O}");
        sb.AppendLine($"TimestampUtc: {DateTime.UtcNow:O}");
        sb.AppendLine($"OrderNumber: {orderNumber}");
        sb.AppendLine($"CorrelationId: {correlationId}");
        sb.AppendLine($"Request URL: {url}");
        sb.AppendLine("Request Headers:");
        sb.AppendLine("  keyApiVal: [REDACTED]");
        sb.AppendLine($"  X-Correlation-ID: {correlationId}");
        sb.AppendLine("Payload:");
        sb.AppendLine(jsonPayload);
        sb.AppendLine();

        sb.AppendLine($"Response Status: {(int)response.StatusCode} {response.ReasonPhrase}");
        sb.AppendLine("Response Headers:");
        foreach (var h in response.Headers)
            sb.AppendLine($"  {h.Key}: {string.Join(", ", h.Value)}");

        sb.AppendLine("Response Content Headers:");
        foreach (var h in response.Content.Headers)
            sb.AppendLine($"  {h.Key}: {string.Join(", ", h.Value)}");

        sb.AppendLine("Response Body:");
        sb.AppendLine(responseBody);
        sb.AppendLine();

        sb.AppendLine("Result: " + (response.IsSuccessStatusCode ? "SUCCESS" : "ERROR"));

        // 1 archivo por ejecución -> write completo
        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8, ct);
    }

    private static DateTime GetChileLocalNow()
    {
        try
        {
            // Windows time zone id para Chile continental (Santiago)
            var tz = TimeZoneInfo.FindSystemTimeZoneById("Pacific SA Standard Time");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        }
        catch
        {
            // Fallback: hora local del servidor si no se encuentra el TZ
            return DateTime.Now;
        }
    }

    private static string SanitizeFilePart(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "UNKNOWN";
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return cleaned.Trim();
    }

    // -----------------------------
    // Helper: extraer OrderNumber para nombrar archivo
    // -----------------------------
    private static string? TryGetOrderNumber(object parametros)
    {
        if (parametros is Dictionary<string, object?> dict)
        {
            if (TryGetFromDictionary(dict, "orderNumber", out var val)) return val;
            if (TryGetFromDictionary(dict, "OrderNumber", out val)) return val;
            return null;
        }

        if (parametros is IDictionary<string, object?> idict)
        {
            if (TryGetFromDictionary(idict, "orderNumber", out var val)) return val;
            if (TryGetFromDictionary(idict, "OrderNumber", out val)) return val;
            return null;
        }

        if (parametros is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            if (je.TryGetProperty("orderNumber", out var p1)) return p1.GetString();
            if (je.TryGetProperty("OrderNumber", out var p2)) return p2.GetString();
        }

        return null;
    }

    private static bool TryGetFromDictionary(IDictionary<string, object?> dict, string key, out string? value)
    {
        value = null;
        if (!dict.TryGetValue(key, out var obj) || obj is null) return false;
        value = obj.ToString();
        return !string.IsNullOrWhiteSpace(value);
    }

    // -----------------------------
    // Helper: hash corto para validar “mismo APIKEY” sin exponerlo
    // -----------------------------
    private static string ShortSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).Substring(0, 8);
    }
}