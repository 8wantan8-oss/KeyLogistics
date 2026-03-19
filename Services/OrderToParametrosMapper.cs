using ProcesadorXmlPedidos.Models;

namespace ProcesadorXmlPedidos.Services;

public static class OrderToParametrosMapper
{
    public static Dictionary<string, object?> Map(Order order)
    {
        if (order == null)
        {
            throw new ArgumentNullException(nameof(order));
        }

        var parametros = new Dictionary<string, object?>
        {
            ["orderNumber"] = order.OrderNumber,
            ["supplierCode"] = order.SupplierCode,
            ["orderDate"] = order.OrderDate?.ToString("yyyy-MM-dd"),
            ["deliveryDate"] = order.DeliveryDate?.ToString("yyyy-MM-dd"),
            ["paymentCondition"] = order.PaymentCondition,
            ["deliveryLocationCode"] = order.DeliveryLocationCode,
            ["invoiceLocationCode"] = order.InvoiceLocationCode,
            ["billingLocationCode"] = order.BillingLocationCode,
            ["notes"] = order.Notes,
            ["items"] = (order.Items ?? new List<OrderItem>()).Select(MapItem).ToList()
        };

        return parametros;
    }

    private static Dictionary<string, object?> MapItem(OrderItem item)
    {
        return new Dictionary<string, object?>
        {
            ["materialCode"] = item.MaterialCode,
            ["description"] = item.Description,
            ["uom"] = item.Uom,
            ["requestedDate"] = item.RequestedDate?.ToString("yyyy-MM-dd"),
            ["quantity"] = item.Quantity,
            ["unitPrice"] = item.UnitPrice
        };
    }
}