namespace ProcesadorXmlPedidos.Models
{
    public class OrderItem
    {
        public string MaterialCode { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Uom { get; set; } = string.Empty;
        public DateTime? RequestedDate { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }
}