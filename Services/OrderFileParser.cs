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
                var root = doc.Root; // e.g. EXPEDIDO
                if (root == null)
                {
                    _logger.LogWarning("File {file} has no root element", filePath);
                    return null;
                }

                // navigate to PEDIDO element
                var pedido = root.Descendants("PEDIDO").FirstOrDefault();
                if (pedido == null)
                {
                    _logger.LogWarning("File {file} does not contain a PEDIDO element", filePath);
                    return null;
                }

                var order = new Order
                {
                    OrderNumber = (string?)pedido.Element("NUMERO_PEDIDO") ?? string.Empty,
                    SupplierCode = (string?)pedido.Element("CODIGO_PROVEEDOR") ?? string.Empty,
                    OrderDate = ParseDate((string?)pedido.Element("FECHA_PEDIDO")),
                    DeliveryDate = ParseDate((string?)pedido.Element("FECHA_ENTREGA")),
                    PaymentCondition = (string?)pedido.Element("CONDICION_PAGO") ?? string.Empty,
                    DeliveryLocationCode = (string?)pedido.Element("LUGAR_ENTREGA") ?? string.Empty,
                    InvoiceLocationCode = (string?)pedido.Element("LUGAR_FACTURA") ?? string.Empty,
                    BillingLocationCode = (string?)pedido.Element("LUGAR_COBRO") ?? string.Empty,
                    Notes = (string?)pedido.Element("NOTAS") ?? string.Empty
                };

                // center
                var centroElem = pedido.Element("CENTROS")?.Elements("CENTRO").FirstOrDefault();
                if (centroElem != null)
                {
                    order.Center = new Center
                    {
                        Code = (string?)centroElem.Element("CODIGO") ?? string.Empty,
                        Description = (string?)centroElem.Element("DESCRIPCION") ?? string.Empty,
                        Address = (string?)centroElem.Element("DIRECCION") ?? string.Empty,
                        City = (string?)centroElem.Element("CIUDAD") ?? string.Empty,
                        State = (string?)centroElem.Element("PROVINCIA") ?? string.Empty,
                        ZipCode = (string?)centroElem.Element("CP") ?? string.Empty,
                    };
                }

                // items
                var items = pedido.Element("PRODUTOS")?.Elements("PRODUTO");
                if (items != null)
                {
                    foreach (var p in items)
                    {
                        order.Items.Add(new OrderItem
                        {
                            MaterialCode = (string?)p.Element("CODIGO") ?? string.Empty,
                            Description = (string?)p.Element("DESCRIPCION") ?? string.Empty,
                            Uom = (string?)p.Element("UNIDAD") ?? string.Empty,
                            RequestedDate = ParseDate((string?)p.Element("FECHA_SOLICITADA")),
                            Quantity = ParseDecimal((string?)p.Element("CANTIDAD")),
                            UnitPrice = ParseDecimal((string?)p.Element("PRECIO_UNITARIO")),
                        });
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

        private static DateTime? ParseDate(string? text)
        {
            if (DateTime.TryParse(text, out var dt))
                return dt;
            return null;
        }

        private static decimal ParseDecimal(string? text)
        {
            if (decimal.TryParse(text, out var d))
                return d;
            return 0m;
        }
    }
}