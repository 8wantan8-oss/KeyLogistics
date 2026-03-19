namespace ProcesadorXmlPedidos.Models;

public class KeyLogisticsRequest
{
    public string Modulo { get; set; } = string.Empty;
    public object? Parametros { get; set; }
}