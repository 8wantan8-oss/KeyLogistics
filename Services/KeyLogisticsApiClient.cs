using System.Net.Http.Json;
using System.Text.Json;
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

        if (string.IsNullOrEmpty(baseUrl))
        {
            throw new InvalidOperationException("KeyLogistics:BaseUrl is not configured.");
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("KeyLogistics:ApiKey is not configured.");
        }

        var request = new KeyLogisticsRequest
        {
            Modulo = "68073e0e748c6a204f601daa70f63f7c",
            Parametros = parametros
        };

        var jsonContent = JsonContent.Create(request, options: new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, baseUrl)
        {
            Content = jsonContent
        };
        requestMessage.Headers.Add("APIKEY", apiKey);

        _logger.LogInformation("Sending POST request to {Url}", baseUrl);

        var startTime = DateTime.UtcNow;
        var response = await _httpClient.SendAsync(requestMessage, ct);
        var duration = DateTime.UtcNow - startTime;

        _logger.LogInformation("Received response from {Url} with status {StatusCode} in {Duration}ms", baseUrl, response.StatusCode, duration.TotalMilliseconds);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"HTTP {response.StatusCode}: {body}");
        }

        var result = await response.Content.ReadFromJsonAsync<KeyLogisticsResponse>(cancellationToken: ct);
        if (result == null)
        {
            throw new InvalidOperationException("Failed to deserialize response.");
        }

        if (!result.Exito || result.Respuesta != "OK")
        {
            throw new InvalidOperationException($"API error: {result.ErrorTrace}");
        }

        return result;
    }
}