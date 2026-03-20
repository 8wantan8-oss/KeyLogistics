namespace ProcesadorXmlPedidos.Models
{
    /// <summary>
    /// Modelo de respuesta esperado por Worker.cs
    /// Alineado con las propiedades que el flujo actual consume.
    /// </summary>
    public class KeyLogisticsResponse
    {
        /// <summary>
        /// Indica si la operación fue exitosa.
        /// </summary>
        public bool Exito { get; set; }

        /// <summary>
        /// Mensaje de respuesta de la API.
        /// </summary>
        public string? Respuesta { get; set; }

        /// <summary>
        /// Información de error o traza devuelta por la API.
        /// </summary>
        public string? ErrorTrace { get; set; }

        /// <summary>
        /// Payload de datos devuelto por la API (estructura variable).
        /// </summary>
        public object? Data { get; set; }
    }
}