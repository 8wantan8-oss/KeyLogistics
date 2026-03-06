namespace ProcesadorXmlPedidos.Models
{
    public class Order
    {
        public string OrderNumber { get; set; } = string.Empty;
        public string SupplierCode { get; set; } = string.Empty;
        public DateTime? OrderDate { get; set; }
        public DateTime? DeliveryDate { get; set; }
        public string PaymentCondition { get; set; } = string.Empty;
        public string DeliveryLocationCode { get; set; } = string.Empty;
        public string InvoiceLocationCode { get; set; } = string.Empty;
        public string BillingLocationCode { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public Center? Center { get; set; }
        public List<OrderItem> Items { get; set; } = new List<OrderItem>();
    }
}