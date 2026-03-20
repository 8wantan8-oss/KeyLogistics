using System.Text.Json.Serialization;

namespace ProcesadorXmlPedidos.Models;

public class KeyLogisticsRequest
{
    [JsonPropertyName("Modulo")]
    public string Modulo { get; set; } = string.Empty;

    [JsonPropertyName("parametros")]
    public object? Parametros { get; set; }
}