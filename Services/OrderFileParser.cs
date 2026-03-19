using System.Globalization;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using ProcesadorXmlPedidos.Models;

namespace ProcesadorXmlPedidos.Services
{
    public class OrderFileParser
    {
        private readonly ILogger<OrderFileParser> _logger;

        public OrderFileParser(ILogger<OrderFileParser> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Tries to read an XML file and convert it to an Order model.
        /// Returns null if the document has an unexpected structure or an error occurs.
        /// </summary>
        public Order? Parse(string filePath)
        {
            try
            {
                var doc = XDocument.Load(filePath);
                var root = doc.Root; // EXPEDIDO
                if (root == null || root.Name != "EXPEDIDO")
                {
                    _logger.LogWarning("File {file} has no EXPEDIDO root element", filePath);
                    return null;
                }

                // Build product dictionary
                var products = new Dictionary<string, (string Description, string Uom)>();
                var produtos = root.Element("PRODUTOS")?.Elements("PRODUTO");
                if (produtos != null)
                {
                    foreach (var p in produtos)
                    {
                        var code = (string?)p.Element("CODIGO");
                        var desc = (string?)p.Element("DESCRICAO") ?? string.Empty;
                        var uom = (string?)p.Element("UNIDADEMEDIDA") ?? string.Empty;
                        if (!string.IsNullOrEmpty(code))
                        {
                            products[code] = (desc, uom);
                        }
                    }
                }

                // Navigate to first PEDIDO
                var pedido = root.Element("PEDIDOS")?.Elements("PEDIDO").FirstOrDefault();
                if (pedido == null)
                {
                    _logger.LogWarning("File {file} does not contain a PEDIDO element", filePath);
                    return null;
                }

                var order = new Order
                {
                    OrderNumber = (string?)pedido.Element("NUM_PEDIDO") ?? string.Empty,
                    // TODO: Other Order properties like SupplierCode, OrderDate, etc., are not present in this XML format
                };

                // Items
                var itens = pedido.Element("ITENS")?.Elements("ITEM");
                if (itens != null)
                {
                    foreach (var item in itens)
                    {
                        var code = (string?)item.Element("COD_MAT") ?? string.Empty;
                        var qtyText = (string?)item.Element("QTDE_PEDIDA");
                        var priceText = (string?)item.Element("VALOR_UNIT");
                        var dateText = (string?)item.Element("DATA_ENTREGA");

                        var quantity = ParseDecimal(qtyText, CultureInfo.InvariantCulture);
                        var unitPrice = ParseDecimal(priceText, CultureInfo.InvariantCulture);
                        var deliveryDate = ParseDate(dateText, "yyyyMMdd");

                        var orderItem = new OrderItem
                        {
                            MaterialCode = code,
                            Quantity = quantity,
                            UnitPrice = unitPrice,
                            RequestedDate = deliveryDate, // Assuming RequestedDate is for delivery
                        };

                        // Enrich from products dictionary
                        if (products.TryGetValue(code, out var productInfo))
                        {
                            orderItem.Description = productInfo.Description;
                            orderItem.Uom = productInfo.Uom;
                        }

                        order.Items.Add(orderItem);
                    }
                }

                return order;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception parsing order file {file}", filePath);
                return null;
            }
        }

        // ✅ Fix CS8625: allow null default, and keep parsing robust
        private static DateTime? ParseDate(string? text, string? format = null)
        {
            if (string.IsNullOrEmpty(text)) return null;

            if (format != null &&
                DateTime.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                return dt;
            }

            // Keep culture consistent (optional but recommended)
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            {
                return dt;
            }

            return null;
        }

        // ✅ Fix CS8625: allow null default
        private static decimal ParseDecimal(string? text, IFormatProvider? provider = null)
        {
            if (decimal.TryParse(text, NumberStyles.Any, provider ?? CultureInfo.CurrentCulture, out var d))
            {
                return d;
            }

            return 0m;
        }
    }
}