using System.Text.Json;

namespace ProcesadorXmlPedidos.Models;

public class KeyLogisticsResponse
{
    public string Respuesta { get; set; } = string.Empty;
    public bool Exito { get; set; }
    public JsonElement Data { get; set; }
    public string ErrorTrace { get; set; } = string.Empty;
}